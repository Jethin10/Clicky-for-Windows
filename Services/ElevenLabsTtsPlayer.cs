using NAudio.Wave;
using System.Text;
using System.Text.Json;

namespace Clicky.Windows.Services;

public sealed class ElevenLabsTtsPlayer : IDisposable
{
    private readonly ClickyWorkerConfiguration _worker;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(60) };
    private WaveOutEvent? _waveOut;
    private Mp3FileReader? _reader;
    private MemoryStream? _audioStream;

    public ElevenLabsTtsPlayer(ClickyWorkerConfiguration worker)
    {
        _worker = worker;
    }

    public bool IsPlaying => _waveOut?.PlaybackState == PlaybackState.Playing;

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
            text,
            model_id = "eleven_flash_v2_5",
            voice_settings = new { stability = 0.5, similarity_boost = 0.75 }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, _worker.Endpoint("/tts"));
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        request.Headers.Accept.ParseAdd("audio/mpeg");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

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

    public void Dispose()
    {
        Stop();
        _httpClient.Dispose();
    }
}
