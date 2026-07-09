using System.Text.RegularExpressions;
using Drawing = System.Drawing;

namespace Clicky.Windows.Services;

public static partial class PointerTagParser
{
    [GeneratedRegex(@"\[POINT:(?:none|(\d+)\s*,\s*(\d+)(?::([^\]:\s][^\]:]*?))?(?::screen(\d+))?)\]\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PointTagRegex();

    public static PointerTagResult Parse(string response)
    {
        var match = PointTagRegex().Match(response);
        if (!match.Success)
        {
            return new PointerTagResult(response.Trim(), null, null, null);
        }

        var spokenText = response[..match.Index].Trim();
        if (!int.TryParse(match.Groups[1].Value, out var x) || !int.TryParse(match.Groups[2].Value, out var y))
        {
            return new PointerTagResult(spokenText, null, "none", null);
        }

        var label = match.Groups[3].Success ? match.Groups[3].Value.Trim() : null;
        int? screenNumber = match.Groups[4].Success && int.TryParse(match.Groups[4].Value, out var screen) ? screen : null;
        return new PointerTagResult(spokenText, new Drawing.Point(x, y), label, screenNumber);
    }

    public static Drawing.Point? MapToScreen(PointerTagResult tag, IReadOnlyList<CapturedScreen> captures)
    {
        if (tag.Pixel is null || captures.Count == 0)
        {
            return null;
        }

        var index = tag.ScreenNumber is { } screenNumber
            ? Math.Clamp(screenNumber - 1, 0, captures.Count - 1)
            : 0;
        var capture = captures[index];
        var x = Math.Clamp(tag.Pixel.Value.X, 0, capture.Width);
        var y = Math.Clamp(tag.Pixel.Value.Y, 0, capture.Height);
        return new Drawing.Point(
            capture.Bounds.Left + (int)Math.Round(x * capture.Bounds.Width / (double)capture.Width),
            capture.Bounds.Top + (int)Math.Round(y * capture.Bounds.Height / (double)capture.Height));
    }
}

public sealed record PointerTagResult(string SpokenText, Drawing.Point? Pixel, string? Label, int? ScreenNumber);
