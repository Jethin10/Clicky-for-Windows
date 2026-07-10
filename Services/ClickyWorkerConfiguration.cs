namespace Clicky.Windows.Services;

/// <summary>
/// Voice services use an owner-operated Worker configured through
/// CLICKY_WORKER_URL. Direct model provider keys are handled separately by
/// SecureCredentialStore and never pass through this configuration object.
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
