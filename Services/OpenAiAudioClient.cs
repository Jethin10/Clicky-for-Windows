using NAudio.Wave;
using System.Net.Http.Headers;

namespace Clicky.Windows.Services;

/// <summary>
/// Implements the OpenAI-compatible audio/transcriptions and audio/speech
/// endpoints. It can point at OpenAI itself or another compatible base URL.
/// </summary>
public sealed class OpenAiAudioClient : IDisposable
{
    private readonly AudioSettings _audio;
    private readonly ProviderSettings _provider;
    private readonly HttpClient _httpClient;
    private WaveOutEvent? _waveOut;
    private Mp3FileReader? _reader;
    private MemoryStream? _audioStream;

    public OpenAiAudioClient(AudioSettings audio, ProviderSettings provider, string? apiKey)
    {
        _audio = audio;
        _provider = provider;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        }
    }

    public bool IsPlaying => _waveOut?.PlaybackState == PlaybackState.Playing;

    public async Task<string> TranscribeAsync(byte[] pcm16, CancellationToken cancellationToken)
    {
        if (pcm16.Length == 0)
        {
            return string.Empty;
        }

        var wav = PcmWaveEncoder.Encode16KhzMono(pcm16);
        using var content = new MultipartFormDataContent();
        using var file = new ByteArrayContent(wav);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(file, "file", "clicky.wav");
        content.Add(new StringContent(_audio.TranscriptionModel.Trim()), "model");
        content.Add(new StringContent("json"), "response_format");

        using var response = await _httpClient.PostAsync(Endpoint("audio/transcriptions"), content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Audio transcription returned {(int)response.StatusCode}: {TrimError(body)}");
        }

        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("text", out var text)
            ? text.GetString()?.Trim() ?? string.Empty
            : string.Empty;
    }

    public async Task SpeakAsync(string text, CancellationToken cancellationToken)
    {
        Stop();
        _audioStream = new MemoryStream(await SynthesizeAsync(text, cancellationToken));
        _reader = new Mp3FileReader(_audioStream);
        _waveOut = new WaveOutEvent();
        _waveOut.Init(_reader);
        _waveOut.Play();
    }

    public async Task<byte[]> SynthesizeAsync(string text, CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            model = _audio.SpeechModel.Trim(),
            input = text,
            voice = _audio.Voice.Trim(),
            response_format = "mp3"
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint("audio/speech"));
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        request.Headers.Accept.ParseAdd("audio/mpeg");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Audio speech returned {(int)response.StatusCode}: {TrimError(error)}");
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public void Stop()
    {
        _waveOut?.Stop();
        _waveOut?.Dispose();
        _reader?.Dispose();
        _audioStream?.Dispose();
        _waveOut = null;
        _reader = null;
        _audioStream = null;
    }

    private Uri Endpoint(string relativePath)
    {
        var normalizedBase = _audio.EffectiveBaseUrl(_provider).TrimEnd('/') + "/";
        return new Uri(new Uri(normalizedBase, UriKind.Absolute), relativePath.TrimStart('/'));
    }

    private static string TrimError(string text) => text.Length <= 500 ? text : text[..500] + "...";

    public void Dispose()
    {
        Stop();
        _httpClient.Dispose();
    }
}
