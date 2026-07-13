using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Clicky.Windows.Services;

public sealed class AssemblyAiTranscriptionSession : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly CancellationTokenSource _receiveCancellation = new();
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string> _finalTranscript = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _receiveLoop;
    private string _latestTranscript = string.Empty;
    private bool _finalizing;

    private AssemblyAiTranscriptionSession()
    {
    }

    public event Action<float>? AudioLevelChanged;

    public static async Task<AssemblyAiTranscriptionSession> ConnectAsync(
        ClickyWorkerConfiguration worker,
        CancellationToken cancellationToken)
    {
        var token = await FetchTokenAsync(worker, cancellationToken);
        var session = new AssemblyAiTranscriptionSession();
        var socketUri = BuildWebSocketUri(token);
        await session._socket.ConnectAsync(socketUri, cancellationToken);
        session._receiveLoop = session.ReceiveLoopAsync(session._receiveCancellation.Token);
        await session._ready.Task.WaitAsync(TimeSpan.FromSeconds(12), cancellationToken);
        return session;
    }

    public async Task SendAudioAsync(ReadOnlyMemory<byte> pcm16, float audioLevel, CancellationToken cancellationToken)
    {
        AudioLevelChanged?.Invoke(audioLevel);
        if (_socket.State != WebSocketState.Open || pcm16.IsEmpty)
        {
            return;
        }

        await _socket.SendAsync(pcm16, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken);
    }

    public async Task<string> FinalizeAsync(CancellationToken cancellationToken)
    {
        _finalizing = true;
        await SendJsonAsync("{\"type\":\"ForceEndpoint\"}", cancellationToken);

        try
        {
            return await _finalTranscript.Task.WaitAsync(TimeSpan.FromSeconds(4), cancellationToken);
        }
        catch (TimeoutException)
        {
            return _latestTranscript;
        }
        finally
        {
            await SendJsonAsync("{\"type\":\"Terminate\"}", CancellationToken.None);
        }
    }

    internal static async Task<string> FetchTokenAsync(ClickyWorkerConfiguration worker, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        using var response = await client.PostAsync(worker.Endpoint("/transcribe-token"), content: null, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("token").GetString()
            ?? throw new InvalidOperationException("The transcription token response did not contain a token.");
    }

    internal static Uri BuildWebSocketUri(string token)
    {
        var query = string.Join('&', new[]
        {
            "sample_rate=16000",
            "encoding=pcm_s16le",
            "format_turns=true",
            "speech_model=u3-rt-pro",
            $"token={Uri.EscapeDataString(token)}"
        });
        return new Uri($"wss://streaming.assemblyai.com/v3/ws?{query}");
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[16_384];
        var message = new StringBuilder();

        try
        {
            while (_socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (!result.EndOfMessage)
                {
                    continue;
                }

                HandleServerMessage(message.ToString());
                message.Clear();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _ready.TrySetException(exception);
            if (_finalizing)
            {
                _finalTranscript.TrySetResult(_latestTranscript);
            }
        }
    }

    private void HandleServerMessage(string json)
    {
        var message = ParseServerMessage(json);
        switch (message.Type)
        {
            case "begin":
                _ready.TrySetResult(true);
                break;
            case "turn":
                var transcript = message.Transcript;
                if (!string.IsNullOrEmpty(transcript))
                {
                    _latestTranscript = transcript;
                }

                if (_finalizing && message.IsFinal)
                {
                    _finalTranscript.TrySetResult(_latestTranscript);
                }
                break;
            case "termination":
                _ready.TrySetResult(true);
                if (_finalizing)
                {
                    _finalTranscript.TrySetResult(_latestTranscript);
                }
                break;
            case "error":
                _ready.TrySetException(new InvalidOperationException(message.Error ?? "AssemblyAI returned an error."));
                if (_finalizing)
                {
                    _finalTranscript.TrySetResult(_latestTranscript);
                }
                break;
        }
    }

    internal static AssemblyAiServerMessage ParseServerMessage(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var type = root.TryGetProperty("type", out var typeElement)
            ? typeElement.GetString()?.ToLowerInvariant() ?? string.Empty
            : string.Empty;
        var transcript = root.TryGetProperty("transcript", out var transcriptElement)
            ? transcriptElement.GetString()?.Trim() ?? string.Empty
            : string.Empty;
        var isFinal = (root.TryGetProperty("end_of_turn", out var endElement) && endElement.ValueKind == JsonValueKind.True)
            || (root.TryGetProperty("turn_is_formatted", out var formattedElement) && formattedElement.ValueKind == JsonValueKind.True);
        var error = root.TryGetProperty("error", out var errorElement)
            ? errorElement.GetString()
            : null;
        return new AssemblyAiServerMessage(type, transcript, isFinal, error);
    }

    private async Task SendJsonAsync(string json, CancellationToken cancellationToken)
    {
        if (_socket.State != WebSocketState.Open)
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(json);
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _receiveCancellation.Cancel();
        if (_socket.State == WebSocketState.Open)
        {
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
            }
            catch (WebSocketException)
            {
            }
        }

        if (_receiveLoop is not null)
        {
            try
            {
                await _receiveLoop;
            }
            catch (Exception)
            {
            }
        }

        _socket.Dispose();
        _receiveCancellation.Dispose();
    }
}

internal sealed record AssemblyAiServerMessage(string Type, string Transcript, bool IsFinal, string? Error);
