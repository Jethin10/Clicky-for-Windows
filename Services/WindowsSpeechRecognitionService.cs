using System.Globalization;
using System.Speech.Recognition;

namespace Clicky.Windows.Services;

public sealed class WindowsSpeechRecognitionService
{
    public const float MinimumConfidence = 0.20f;

    public static IReadOnlyList<WindowsSpeechRecognizer> InstalledRecognizers()
    {
        try
        {
            return SpeechRecognitionEngine.InstalledRecognizers()
                .Select(info => new WindowsSpeechRecognizer(info.Id, info.Culture.Name, info.Description))
                .ToList();
        }
        catch (PlatformNotSupportedException)
        {
            return [];
        }
    }

    public static bool IsAvailable => InstalledRecognizers().Count > 0;

    public Task<string> RecognizePcm16Async(
        byte[] pcm16,
        string? preferredCulture,
        CancellationToken cancellationToken) =>
        RecognizeWaveAsync(PcmWaveEncoder.Encode16KhzMono(pcm16), preferredCulture, cancellationToken);

    public Task<string> RecognizeWaveAsync(
        byte[] wave,
        string? preferredCulture,
        CancellationToken cancellationToken)
    {
        if (wave.Length <= 44)
        {
            return Task.FromResult(string.Empty);
        }

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var recognizer = SelectRecognizer(preferredCulture)
                ?? throw new InvalidOperationException("Install a Windows speech recognition language to use local transcription.");
            using var engine = new SpeechRecognitionEngine(recognizer.Id);
            engine.LoadGrammar(new DictationGrammar());
            engine.InitialSilenceTimeout = TimeSpan.FromSeconds(4);
            engine.BabbleTimeout = TimeSpan.FromSeconds(8);
            engine.EndSilenceTimeout = TimeSpan.FromMilliseconds(500);
            engine.EndSilenceTimeoutAmbiguous = TimeSpan.FromMilliseconds(900);
            using var stream = new MemoryStream(wave, writable: false);
            engine.SetInputToWaveStream(stream);
            var result = engine.Recognize();
            cancellationToken.ThrowIfCancellationRequested();
            return result is not null && result.Confidence >= MinimumConfidence
                ? result.Text.Trim()
                : string.Empty;
        }, cancellationToken);
    }

    private static WindowsSpeechRecognizer? SelectRecognizer(string? preferredCulture)
    {
        var recognizers = InstalledRecognizers();
        if (recognizers.Count == 0)
        {
            return null;
        }
        if (!string.IsNullOrWhiteSpace(preferredCulture))
        {
            var exact = recognizers.FirstOrDefault(item =>
                string.Equals(item.Culture, preferredCulture.Trim(), StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                return exact;
            }
        }

        var current = CultureInfo.CurrentUICulture;
        return recognizers.FirstOrDefault(item => string.Equals(item.Culture, current.Name, StringComparison.OrdinalIgnoreCase))
            ?? recognizers.FirstOrDefault(item => item.Culture.StartsWith(current.TwoLetterISOLanguageName + "-", StringComparison.OrdinalIgnoreCase))
            ?? recognizers[0];
    }
}

public sealed record WindowsSpeechRecognizer(string Id, string Culture, string Description);
