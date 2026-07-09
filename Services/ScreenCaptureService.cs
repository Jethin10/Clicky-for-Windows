using Clicky.Windows.Native;
using System.IO;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Clicky.Windows.Services;

public sealed class ScreenCaptureService
{
    public CapturedScreen? CaptureCursorScreen()
    {
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            return null;
        }

        return CaptureScreen(Forms.Screen.FromPoint(new Drawing.Point(cursor.X, cursor.Y)));
    }

    public IReadOnlyList<CapturedScreen> CaptureAllScreens()
    {
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            return [];
        }

        return Forms.Screen.AllScreens
            .Select(CaptureScreen)
            .Where(capture => capture is not null)
            .Cast<CapturedScreen>()
            .OrderByDescending(capture => capture.Bounds.Contains(cursor.X, cursor.Y))
            .ToList();
    }

    private static CapturedScreen? CaptureScreen(Forms.Screen screen)
    {
        var bounds = screen.Bounds;

        try
        {
            using var desktop = new Drawing.Bitmap(bounds.Width, bounds.Height, Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var graphics = Drawing.Graphics.FromImage(desktop))
            {
                graphics.CopyFromScreen(bounds.Location, Drawing.Point.Empty, bounds.Size, Drawing.CopyPixelOperation.SourceCopy);
            }

            using var thumbnail = CreateThumbnail(desktop, 1280);
            using var stream = new MemoryStream();
            thumbnail.Save(stream, Drawing.Imaging.ImageFormat.Jpeg);

            return new CapturedScreen(
                bounds,
                thumbnail.Width,
                thumbnail.Height,
                Convert.ToBase64String(stream.ToArray()));
        }
        catch (Exception)
        {
            // Protected surfaces and secure desktops intentionally fail capture.
            return null;
        }
    }

    private static Drawing.Bitmap CreateThumbnail(Drawing.Bitmap source, int maximumLongEdge)
    {
        var scale = Math.Min(1d, maximumLongEdge / (double)Math.Max(source.Width, source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var thumbnail = new Drawing.Bitmap(width, height, Drawing.Imaging.PixelFormat.Format32bppPArgb);

        using var graphics = Drawing.Graphics.FromImage(thumbnail);
        graphics.InterpolationMode = Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(source, new Drawing.Rectangle(0, 0, width, height));
        return thumbnail;
    }
}

public sealed record CapturedScreen(
    Drawing.Rectangle Bounds,
    int Width,
    int Height,
    string Base64)
{
    public string DataUrl => $"data:image/jpeg;base64,{Base64}";
}
