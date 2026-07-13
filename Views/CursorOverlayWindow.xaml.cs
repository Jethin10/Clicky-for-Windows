using Clicky.Windows.Native;
using Clicky.Windows.Services;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Clicky.Windows.Views;

public partial class CursorOverlayWindow : Window
{
    private readonly DispatcherTimer _renderTimer;
    private readonly DispatcherTimer _flightTimer;
    private Forms.Screen? _screen;
    private System.Windows.Point _currentPosition;
    private System.Windows.Point _targetPosition;
    private System.Windows.Point _flightStart;
    private System.Windows.Point _flightEnd;
    private DateTime _flightStarted;
    private double _flightDurationMilliseconds = 780;
    private TaskCompletionSource<bool>? _flightCompletion;
    private CancellationTokenSource? _pointingCancellation;
    private InteractionState _state;
    private bool _isFlying;
    private bool _isPointing;
    private double _audioLevel;
    private double _phase;

    public CursorOverlayWindow()
    {
        InitializeComponent();
        _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _renderTimer.Tick += (_, _) => RenderFrame();
        _flightTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _flightTimer.Tick += (_, _) => RenderFlightFrame();
        SourceInitialized += OnSourceInitialized;
    }

    public void Configure(Forms.Screen screen)
    {
        _screen = screen;
        var bounds = screen.Bounds;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
    }

    public void Start()
    {
        if (!IsVisible)
        {
            Show();
        }

        ReassertTopmost();

        _renderTimer.Start();
    }

    public void Stop()
    {
        _renderTimer.Stop();
        _flightTimer.Stop();
        _pointingCancellation?.Cancel();
        _flightCompletion?.TrySetCanceled();
        _isFlying = false;
        _isPointing = false;
        Bubble.Visibility = Visibility.Collapsed;
        Hide();
    }

    public bool Contains(Drawing.Point point) => _screen?.Bounds.Contains(point) == true;

    public void SetState(InteractionState state)
    {
        if (state == InteractionState.Listening)
        {
            _pointingCancellation?.Cancel();
            _flightTimer.Stop();
            _flightCompletion?.TrySetCanceled();
            _isFlying = false;
            _isPointing = false;
            Bubble.Visibility = Visibility.Collapsed;
        }
        _state = state;
        CursorTriangle.Visibility = state is InteractionState.Listening or InteractionState.Processing
            ? Visibility.Collapsed
            : Visibility.Visible;
        Waveform.Visibility = state == InteractionState.Listening ? Visibility.Visible : Visibility.Collapsed;
        Spinner.Visibility = state == InteractionState.Processing ? Visibility.Visible : Visibility.Collapsed;
        ScheduleVisualTestSnapshot();
    }

    public void SetAudioLevel(float level)
    {
        _audioLevel = Math.Clamp(level, 0, 1);
    }

    public void FollowCursor(Drawing.Point position)
    {
        if (_isFlying || _isPointing || _screen is null)
        {
            return;
        }

        var local = PointFromScreen(new System.Windows.Point(position.X, position.Y));
        _targetPosition = new System.Windows.Point(local.X + 34, local.Y + 22);
        if (_currentPosition == default)
        {
            _currentPosition = _targetPosition;
        }
    }

    public void PointAt(Drawing.Point position, string phrase)
    {
        if (_screen is null)
        {
            return;
        }

        _pointingCancellation?.Cancel();
        _pointingCancellation = new CancellationTokenSource();
        _ = RunPointingSequenceAsync(position, phrase, _pointingCancellation.Token);
    }

    private async Task RunPointingSequenceAsync(Drawing.Point position, string phrase, CancellationToken cancellationToken)
    {
        try
        {
            _isPointing = true;
            var local = PointFromScreen(new System.Windows.Point(position.X, position.Y));
            ConfigureBubblePlacement(local);
            var destination = ClampToOverlay(new System.Windows.Point(local.X + 10, local.Y + 10));
            await FlyToAsync(destination, cancellationToken);
            await TypePointerBubbleAsync(phrase, cancellationToken);

            if (NativeMethods.GetCursorPos(out var cursor))
            {
                var cursorLocal = PointFromScreen(new System.Windows.Point(cursor.X, cursor.Y));
                await FlyToAsync(ClampToOverlay(new System.Windows.Point(cursorLocal.X + 34, cursorLocal.Y + 22)), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // A new response takes over immediately.
        }
        finally
        {
            _isPointing = false;
            Bubble.Visibility = Visibility.Collapsed;
        }
    }

    private Task FlyToAsync(System.Windows.Point destination, CancellationToken cancellationToken)
    {
        _flightCompletion?.TrySetCanceled();
        _flightCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _flightStart = _currentPosition == default ? _targetPosition : _currentPosition;
        _flightEnd = destination;
        var distance = (_flightEnd - _flightStart).Length;
        _flightDurationMilliseconds = Math.Clamp(distance / 800d * 1000d, 600d, 1400d);
        _flightStarted = DateTime.UtcNow;
        _isFlying = true;
        _flightTimer.Start();
        return _flightCompletion.Task.WaitAsync(cancellationToken);
    }

    private void OnSourceInitialized(object? sender, EventArgs eventArgs)
    {
        var handle = new WindowInteropHelper(this).Handle;
        NativeMethods.ConfigureCompanionOverlay(handle);
        ReassertTopmost();
    }

    private void RenderFrame()
    {
        _phase += 0.25;
        if (!_isFlying && !_isPointing)
        {
            _currentPosition = new System.Windows.Point(
                _currentPosition.X + (_targetPosition.X - _currentPosition.X) * 0.34,
                _currentPosition.Y + (_targetPosition.Y - _currentPosition.Y) * 0.34);
            System.Windows.Controls.Canvas.SetLeft(Buddy, _currentPosition.X);
            System.Windows.Controls.Canvas.SetTop(Buddy, _currentPosition.Y);
        }

        SpinnerRotation.Angle = (SpinnerRotation.Angle + 12) % 360;
        var waveHeights = new[] { 7d, 12d, 18d, 12d, 7d };
        var waves = new[] { Wave1, Wave2, Wave3, Wave4, Wave5 };
        var amplitude = 0.36 + Math.Min(0.64, _audioLevel * 1.6);
        for (var index = 0; index < waves.Length; index++)
        {
            var pulse = 0.62 + 0.38 * Math.Sin(_phase + index * 0.7);
            waves[index].Height = Math.Max(3, waveHeights[index] * amplitude * pulse);
        }
    }

    private void RenderFlightFrame()
    {
        var linear = Math.Clamp((DateTime.UtcNow - _flightStarted).TotalMilliseconds / _flightDurationMilliseconds, 0, 1);
        var t = linear * linear * (3 - 2 * linear);
        var control = new System.Windows.Point(
            (_flightStart.X + _flightEnd.X) / 2,
            Math.Min(_flightStart.Y, _flightEnd.Y) - Math.Min(82, Math.Abs(_flightEnd.X - _flightStart.X) * 0.18));
        var oneMinusT = 1 - t;
        _currentPosition = new System.Windows.Point(
            oneMinusT * oneMinusT * _flightStart.X + 2 * oneMinusT * t * control.X + t * t * _flightEnd.X,
            oneMinusT * oneMinusT * _flightStart.Y + 2 * oneMinusT * t * control.Y + t * t * _flightEnd.Y);
        var tangentX = 2 * oneMinusT * (control.X - _flightStart.X) + 2 * t * (_flightEnd.X - control.X);
        var tangentY = 2 * oneMinusT * (control.Y - _flightStart.Y) + 2 * t * (_flightEnd.Y - control.Y);
        CursorRotation.Angle = Math.Atan2(tangentY, tangentX) * 180 / Math.PI + 90;
        System.Windows.Controls.Canvas.SetLeft(Buddy, _currentPosition.X);
        System.Windows.Controls.Canvas.SetTop(Buddy, _currentPosition.Y);
        var scale = 1 + Math.Sin(linear * Math.PI) * 0.30;
        BuddyScale.ScaleX = scale;
        BuddyScale.ScaleY = scale;

        if (linear < 1)
        {
            return;
        }

        _flightTimer.Stop();
        _isFlying = false;
        BuddyScale.ScaleX = 1;
        BuddyScale.ScaleY = 1;
        CursorRotation.Angle = -35;
        _flightCompletion?.TrySetResult(true);
    }

    private async Task TypePointerBubbleAsync(string phrase, CancellationToken cancellationToken)
    {
        Bubble.Visibility = Visibility.Visible;
        BubbleText.Text = string.Empty;
        foreach (var character in phrase)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BubbleText.Text += character;
            await Task.Delay(38, cancellationToken);
        }

        await Task.Delay(3000, cancellationToken);
        Bubble.Visibility = Visibility.Collapsed;
        WriteVisualTestSnapshot();
    }

    private System.Windows.Point ClampToOverlay(System.Windows.Point position)
    {
        return new System.Windows.Point(
            Math.Clamp(position.X, 20, Math.Max(20, ActualWidth - 32)),
            Math.Clamp(position.Y, 20, Math.Max(20, ActualHeight - 32)));
    }

    private void ConfigureBubblePlacement(System.Windows.Point target)
    {
        BubbleOffset.X = target.X > ActualWidth - 210 ? -202 : 0;
        BubbleOffset.Y = target.Y > ActualHeight - 130 ? -104 : 0;
    }

    private void ReassertTopmost()
    {
        if (_screen is null || !IsLoaded)
        {
            return;
        }

        var bounds = _screen.Bounds;
        var handle = new WindowInteropHelper(this).Handle;
        _ = NativeMethods.SetWindowPos(handle, NativeMethods.HwndTopmost, bounds.Left, bounds.Top, bounds.Width, bounds.Height, NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
    }

    private void ScheduleVisualTestSnapshot()
    {
        if (!NativeMethods.IsVisualTest)
        {
            return;
        }

        _ = Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(220);
            WriteVisualTestSnapshot();
        });
    }

    private void WriteVisualTestSnapshot()
    {
        if (!NativeMethods.IsVisualTest || Buddy.ActualWidth < 1 || Buddy.ActualHeight < 1)
        {
            return;
        }

        Buddy.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(Buddy.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(Buddy.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var frame = new DrawingVisual();
        using (var drawingContext = frame.RenderOpen())
        {
            drawingContext.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 18, 17)), null, new Rect(0, 0, width, height));
            drawingContext.DrawRectangle(new VisualBrush(Buddy), null, new Rect(0, 0, width, height));
        }
        bitmap.Render(frame);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(Path.GetTempPath(), "clicky-overlay-render.png");
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
