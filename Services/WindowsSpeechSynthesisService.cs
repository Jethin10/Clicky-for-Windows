using System.Globalization;
using System.Speech.Synthesis;

namespace Clicky.Windows.Services;

public sealed class WindowsSpeechSynthesisService
{
    public static IReadOnlyList<WindowsSpeechVoice> InstalledVoices()
    {
        try
        {
            using var synthesizer = new SpeechSynthesizer();
            return synthesizer.GetInstalledVoices()
                .Where(voice => voice.Enabled)
                .Select(voice => new WindowsSpeechVoice(
                    voice.VoiceInfo.Name,
                    voice.VoiceInfo.Culture.Name,
                    voice.VoiceInfo.Description))
                .ToList();
        }
        catch (PlatformNotSupportedException)
        {
            return [];
        }
    }

    public static bool IsAvailable => InstalledVoices().Count > 0;

    public async Task SpeakAsync(string text, string? preferredCulture, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        using var synthesizer = new SpeechSynthesizer();
        var voice = SelectVoice(preferredCulture);
        if (voice is null)
        {
            throw new InvalidOperationException("Install a Windows text-to-speech language to use local voice output.");
        }
        synthesizer.SelectVoice(voice.Name);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        synthesizer.SpeakCompleted += (_, eventArgs) =>
        {
            if (eventArgs.Cancelled)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            else if (eventArgs.Error is not null)
            {
                completion.TrySetException(eventArgs.Error);
            }
            else
            {
                completion.TrySetResult();
            }
        };
        using var registration = cancellationToken.Register(synthesizer.SpeakAsyncCancelAll);
        synthesizer.SpeakAsync(text.Trim());
        await completion.Task;
    }

    private static WindowsSpeechVoice? SelectVoice(string? preferredCulture)
    {
        var voices = InstalledVoices();
        if (voices.Count == 0)
        {
            return null;
        }
        if (!string.IsNullOrWhiteSpace(preferredCulture))
        {
            var exact = voices.FirstOrDefault(item =>
                string.Equals(item.Culture, preferredCulture.Trim(), StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                return exact;
            }
        }
        var current = CultureInfo.CurrentUICulture;
        return voices.FirstOrDefault(item => string.Equals(item.Culture, current.Name, StringComparison.OrdinalIgnoreCase))
            ?? voices.FirstOrDefault(item => item.Culture.StartsWith(current.TwoLetterISOLanguageName + "-", StringComparison.OrdinalIgnoreCase))
            ?? voices[0];
    }
}

public sealed record WindowsSpeechVoice(string Name, string Culture, string Description);
