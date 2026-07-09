using Clicky.Windows.Native;
using Clicky.Windows.Views;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Clicky.Windows.Services;

public sealed class OverlayHost : IDisposable
{
    private readonly List<CursorOverlayWindow> _overlays = [];
    private readonly DispatcherTimer _cursorTimer;
    private InteractionState _state = InteractionState.Idle;

    public OverlayHost()
    {
        _cursorTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _cursorTimer.Tick += (_, _) => UpdateCursor();
    }

    public bool IsVisible => _overlays.Any(window => window.IsVisible);

    public void Show()
    {
        RefreshTopology();
        foreach (var overlay in _overlays)
        {
            overlay.Start();
            overlay.SetState(_state);
        }

        _cursorTimer.Start();
    }

    public void Hide()
    {
        _cursorTimer.Stop();
        foreach (var overlay in _overlays)
        {
            overlay.Stop();
        }
    }

    public void SetState(InteractionState state)
    {
        _state = state;
        foreach (var overlay in _overlays)
        {
            overlay.SetState(state);
        }
    }

    public void SetAudioLevel(float level)
    {
        foreach (var overlay in _overlays)
        {
            overlay.SetAudioLevel(level);
        }
    }

    public void PointAt(Drawing.Point target, string phrase)
    {
        var overlay = _overlays.FirstOrDefault(window => window.Contains(target));
        overlay?.PointAt(target, phrase);
    }

    private void RefreshTopology()
    {
        var currentBounds = Forms.Screen.AllScreens.Select(screen => screen.Bounds).ToArray();
        var existingBounds = _overlays.Select(window => window.Tag as Drawing.Rectangle?).Where(bounds => bounds.HasValue).Select(bounds => bounds!.Value).ToArray();
        if (_overlays.Count == currentBounds.Length && existingBounds.SequenceEqual(currentBounds))
        {
            return;
        }

        foreach (var overlay in _overlays)
        {
            overlay.Close();
        }
        _overlays.Clear();

        foreach (var screen in Forms.Screen.AllScreens)
        {
            var overlay = new CursorOverlayWindow
            {
                Tag = screen.Bounds
            };
            overlay.Configure(screen);
            _overlays.Add(overlay);
        }
    }

    private void UpdateCursor()
    {
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            return;
        }

        var point = new Drawing.Point(cursor.X, cursor.Y);
        foreach (var overlay in _overlays)
        {
            if (overlay.Contains(point))
            {
                overlay.FollowCursor(point);
            }
        }
    }

    public void Dispose()
    {
        Hide();
        foreach (var overlay in _overlays)
        {
            overlay.Close();
        }
        _overlays.Clear();
    }
}
