using System.Net.Http.Headers;

namespace Clicky.Windows.Services;

/// <summary>
/// Streams a multimodal Claude response through an owner-operated Worker. The
/// Worker holds provider secrets; this desktop client only ever receives the
/// configured Worker URL.
/// </summary>
public sealed class ClaudeWorkerClient : IDisposable
{
    private const string SystemPrompt = """
        you're clicky, a friendly always-on companion that lives next to the user's cursor. the user just spoke to you via push-to-talk and you can see their screen(s). your reply will be spoken aloud via text-to-speech, so write the way you'd actually talk. this is an ongoing conversation — you remember everything they've said before.

        rules:
        - default to one or two sentences. be direct and dense. if the user asks you to explain more, go deeper and be thorough.
        - all lowercase, casual, warm. no emojis.
        - write for the ear, not the eye. short sentences. no lists, bullet points, markdown, or formatting — just natural speech.
        - don't use abbreviations or symbols that sound weird read aloud. write "for example" not "e.g." and spell out small numbers.
        - if the user's question relates to what's on their screen, reference specific things you see. otherwise answer directly.
        - never say "simply" or "just". don't read code verbatim; describe what it does conversationally.
        - the first image is primary focus, the screen where the cursor was when the user released the hotkey.

        element pointing:
        you have a small blue triangle cursor that can fly to and point at things on screen. use it whenever pointing would genuinely help. append a coordinate tag at the very end of every response: [POINT:x,y:label] for the first screen, [POINT:x,y:label:screenN] for another screen, or [POINT:none] when pointing would not help. coordinates are integer pixels in each supplied screenshot with origin at the top left.
        """;

    private readonly ClickyWorkerConfiguration _worker;
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(3)
    };

    public ClaudeWorkerClient(ClickyWorkerConfiguration worker)
    {
        _worker = worker;
    }

    public async Task<string> AnalyzeAsync(
        IReadOnlyList<CapturedScreen> screenshots,
        string transcript,
        string model,
        IReadOnlyList<ConversationTurn> history,
        Action<string>? onTextChunk,
        CancellationToken cancellationToken)
    {
        if (!_worker.IsConfigured)
        {
            throw new InvalidOperationException("Set CLICKY_WORKER_URL to an owner-operated Clicky Worker before enabling AI voice responses.");
        }

        var messages = new List<object>();
        foreach (var turn in history)
        {
            messages.Add(new { role = "user", content = turn.UserTranscript });
            messages.Add(new { role = "assistant", content = turn.AssistantResponse });
        }

        var blocks = new List<object>();
        for (var index = 0; index < screenshots.Count; index++)
        {
            var screenshot = screenshots[index];
            blocks.Add(new
            {
                type = "image",
                source = new
                {
                    type = "base64",
                    media_type = "image/jpeg",
                    data = screenshot.Base64
                }
            });
            var focus = index == 0 ? "primary focus" : $"screen {index + 1}";
            blocks.Add(new
            {
                type = "text",
                text = $"{focus} (image dimensions: {screenshot.Width}x{screenshot.Height} pixels)"
            });
        }

        blocks.Add(new { type = "text", text = transcript });
        messages.Add(new { role = "user", content = blocks });

        var requestBody = new
        {
            model,
            max_tokens = 1024,
            stream = true,
            system = SystemPrompt,
            messages
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _worker.Endpoint("/chat"));
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Clicky Worker /chat returned {(int)response.StatusCode}: {responseText}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var accumulatedText = new StringBuilder();

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }

            var payload = line[6..].Trim();
            if (payload == "[DONE]")
            {
                break;
            }

            TryAppendTextDelta(payload, accumulatedText, onTextChunk);
        }

        return accumulatedText.ToString().Trim();
    }

    private static void TryAppendTextDelta(string json, StringBuilder accumulatedText, Action<string>? onTextChunk)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "content_block_delta")
            {
                return;
            }

            if (!root.TryGetProperty("delta", out var delta)
                || !delta.TryGetProperty("type", out var deltaType)
                || deltaType.GetString() != "text_delta"
                || !delta.TryGetProperty("text", out var text))
            {
                return;
            }

            var chunk = text.GetString();
            if (string.IsNullOrEmpty(chunk))
            {
                return;
            }

            accumulatedText.Append(chunk);
            onTextChunk?.Invoke(accumulatedText.ToString());
        }
        catch (JsonException)
        {
            // Ignore a malformed SSE event and keep the voice interaction alive.
        }
    }

    public void Dispose() => _httpClient.Dispose();
}

public sealed record ConversationTurn(string UserTranscript, string AssistantResponse);
