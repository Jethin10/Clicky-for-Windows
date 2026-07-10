using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Clicky.Windows.Services;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Drawing.Icon _icon;

    public event Action? OpenRequested;
    public event Action? SettingsRequested;
    public event Action? OverlayToggleRequested;
    public event Action? QuitRequested;

    public TrayService()
    {
        _icon = CreateIcon();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Clicky", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add("AI provider settings", null, (_, _) => SettingsRequested?.Invoke());
        menu.Items.Add("Show / hide Clicky", null, (_, _) => OverlayToggleRequested?.Invoke());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => QuitRequested?.Invoke());

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "Clicky for Windows",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.MouseClick += (_, eventArgs) =>
        {
            if (eventArgs.Button == Forms.MouseButtons.Left)
            {
                OpenRequested?.Invoke();
            }
        };
    }

    private static Drawing.Icon CreateIcon()
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        {
            graphics.Clear(Drawing.Color.Transparent);
            graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new Drawing.SolidBrush(Drawing.Color.FromArgb(16, 18, 17));
            using var outline = new Drawing.Pen(Drawing.Color.FromArgb(51, 128, 255), 1.5f);
            var triangle = new[]
            {
                new Drawing.PointF(7, 4),
                new Drawing.PointF(27, 14),
                new Drawing.PointF(17, 18),
                new Drawing.PointF(13, 28)
            };
            graphics.FillPolygon(brush, triangle);
            graphics.DrawPolygon(outline, triangle);
        }

        return Drawing.Icon.FromHandle(bitmap.GetHicon());
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon.Dispose();
    }
}
