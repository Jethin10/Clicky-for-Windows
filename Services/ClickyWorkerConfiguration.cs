namespace Clicky.Windows.Services;

/// <summary>
/// The app never accepts raw provider keys. Set CLICKY_WORKER_URL to a Worker
/// you own that implements the public Clicky /chat, /tts and /transcribe-token
/// endpoints. Without it, the desktop shell remains local-only.
/// </summary>
public sealed class ClickyWorkerConfiguration
{
    private ClickyWorkerConfiguration(Uri? baseUri)
    {
        BaseUri = baseUri;
    }

    public Uri? BaseUri { get; }
    public bool IsConfigured => BaseUri is not null;

    public Uri Endpoint(string relativePath)
    {
        if (BaseUri is null)
        {
            throw new InvalidOperationException("Set CLICKY_WORKER_URL before enabling AI voice responses.");
        }

        return new Uri(BaseUri, relativePath.TrimStart('/'));
    }

    public static ClickyWorkerConfiguration FromEnvironment()
    {
        var rawUrl = Environment.GetEnvironmentVariable("CLICKY_WORKER_URL")?.Trim();
        if (string.IsNullOrWhiteSpace(rawUrl) || !Uri.TryCreate(rawUrl.EndsWith('/') ? rawUrl : rawUrl + '/', UriKind.Absolute, out var baseUri))
        {
            return new ClickyWorkerConfiguration(null);
        }

        return new ClickyWorkerConfiguration(baseUri);
    }
}
