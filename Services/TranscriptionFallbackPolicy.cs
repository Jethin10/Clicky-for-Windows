namespace Clicky.Windows.Services;

public static class TranscriptionFallbackPolicy
{
    public static async Task<string> ExecuteAsync(
        Func<CancellationToken, Task<string>> primary,
        Func<CancellationToken, Task<string>> fallback,
        bool fallbackEnabled,
        CancellationToken cancellationToken)
    {
        Exception? primaryFailure = null;
        string transcript = string.Empty;
        try
        {
            transcript = await primary(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
        }

        if (string.IsNullOrWhiteSpace(transcript) && fallbackEnabled)
        {
            transcript = await fallback(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(transcript) && primaryFailure is not null)
        {
            throw new InvalidOperationException($"Cloud and local transcription failed: {primaryFailure.Message}", primaryFailure);
        }
        return transcript;
    }
}
