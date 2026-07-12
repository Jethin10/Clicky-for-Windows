namespace Clicky.Windows.Services;

public sealed class OnboardingMediaService
{
    public const string PlaybackId = "e5jB8UuSrtFABVnTHCR7k3sIsmcUHCyhtLu1tzqLlfs";
    public static readonly Uri ManifestUri = new($"https://stream.mux.com/{PlaybackId}.m3u8");
    public static readonly Uri PlayerUri = new(
        $"https://player.mux.com/{PlaybackId}?autoplay=any&disable-tracking=true&disable-cookies=true&title=Meet%20Clicky");

    public async Task<bool> IsRemoteVideoAvailableAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        using var request = new HttpRequestMessage(HttpMethod.Head, ManifestUri);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public static string BuildEmbedHtml() => $$"""
        <!doctype html>
        <html><head><meta name="viewport" content="width=device-width,initial-scale=1">
        <style>html,body,iframe{width:100%;height:100%;margin:0;border:0;overflow:hidden;background:#000}</style>
        </head><body>
        <iframe src="{{PlayerUri}}" allow="autoplay; encrypted-media; picture-in-picture"></iframe>
        </body></html>
        """;

    public static OnboardingWindowPosition PositionNearCursor(
        System.Drawing.Point cursor,
        System.Drawing.Rectangle workingArea,
        double windowWidth,
        double windowHeight)
    {
        var left = (double)cursor.X + 24;
        var top = (double)cursor.Y + 10;
        if (left + windowWidth > workingArea.Right)
        {
            left = cursor.X - windowWidth - 24;
        }
        if (top + windowHeight > workingArea.Bottom)
        {
            top = cursor.Y - windowHeight - 10;
        }
        return new OnboardingWindowPosition(
            Math.Clamp(left, workingArea.Left, Math.Max(workingArea.Left, workingArea.Right - windowWidth)),
            Math.Clamp(top, workingArea.Top, Math.Max(workingArea.Top, workingArea.Bottom - windowHeight)));
    }
}

public sealed record OnboardingWindowPosition(double Left, double Top);
