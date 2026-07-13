using Clicky.Windows.Native;
using Clicky.Windows.Services;
using Microsoft.Web.WebView2.Core;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Clicky.Windows.Views;

public partial class OnboardingVideoWindow : Window
{
    private readonly OnboardingMediaService _media = new();
    private readonly DispatcherTimer _cursorTimer;
    private readonly DispatcherTimer _endTimer;
    private bool _ended;

    public OnboardingVideoWindow()
    {
        InitializeComponent();
        SourceInitialized += ConfigureNativeWindow;
        Loaded += OnLoaded;
        _cursorTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _cursorTimer.Tick += (_, _) => PositionNearCursor();
        _endTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(72) };
        _endTimer.Tick += (_, _) => Finish();
        Closed += (_, _) =>
        {
            _cursorTimer.Stop();
            _endTimer.Stop();
            Player.Dispose();
        };
    }

    public event Action? PlaybackFinished;

    public void StartFollowingCursor()
    {
        PositionNearCursor();
        _cursorTimer.Start();
        _endTimer.Start();
        Show();
    }

    public void Finish()
    {
        if (_ended)
        {
            return;
        }
        _ended = true;
        _cursorTimer.Stop();
        _endTimer.Stop();
        PlaybackFinished?.Invoke();
        Close();
    }

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (NativeMethods.IsVisualTest
            && string.Equals(Environment.GetEnvironmentVariable("CLICKY_VISUAL_TEST_ONBOARDING_FALLBACK"), "1", StringComparison.Ordinal))
        {
            ShowFallback("Video unavailable — the local quick-start remains available.");
            await Task.Delay(350);
            WriteFallbackSnapshot();
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            if (!await _media.IsRemoteVideoAvailableAsync(timeout.Token))
            {
                ShowFallback("Video unavailable — the local quick-start remains available.");
                if (NativeMethods.IsVisualTest)
                {
                    await Task.Delay(350);
                    WriteFallbackSnapshot();
                }
                return;
            }
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Clicky",
                "webview2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await Player.EnsureCoreWebView2Async(environment);
            var settings = Player.CoreWebView2.Settings;
            settings.AreDevToolsEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            Player.NavigationCompleted += NavigationCompleted;
            Player.NavigateToString(OnboardingMediaService.BuildEmbedHtml());
        }
        catch (Exception exception)
        {
            ShowFallback($"Video unavailable — {ShortReason(exception.Message)}");
            if (NativeMethods.IsVisualTest)
            {
                await Task.Delay(350);
                WriteFallbackSnapshot();
            }
        }
    }

    private async void NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs eventArgs)
    {
        if (!eventArgs.IsSuccess)
        {
            ShowFallback($"Video unavailable — WebView error {eventArgs.WebErrorStatus}.");
            if (NativeMethods.IsVisualTest)
            {
                await Task.Delay(350);
                WriteFallbackSnapshot();
            }
            return;
        }

        FallbackPanel.Visibility = Visibility.Collapsed;
        if (NativeMethods.IsVisualTest)
        {
            await Task.Delay(10_000);
            await using var stream = File.Create(Path.Combine(Path.GetTempPath(), "clicky-onboarding-video-render.png"));
            await Player.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
        }
    }

    private void ShowFallback(string reason)
    {
        _endTimer.Interval = TimeSpan.FromSeconds(12);
        FallbackPanel.Visibility = Visibility.Visible;
        FallbackReasonText.Text = reason;
    }

    private void PositionNearCursor()
    {
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            return;
        }
        var screen = Forms.Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y));
        var position = OnboardingMediaService.PositionNearCursor(
            new System.Drawing.Point(cursor.X, cursor.Y),
            screen.WorkingArea,
            Width,
            Height);
        Left = position.Left;
        Top = position.Top;
    }

    private void ConfigureNativeWindow(object? sender, EventArgs eventArgs)
    {
        var handle = new WindowInteropHelper(this).Handle;
        NativeMethods.ConfigureCompanionOverlay(handle);
        if (!NativeMethods.IsVisualTest)
        {
            _ = NativeMethods.SetWindowDisplayAffinity(handle, NativeMethods.WdaExcludeFromCapture);
        }
    }

    private void WriteFallbackSnapshot()
    {
        if (Content is not Visual visual)
        {
            return;
        }
        UpdateLayout();
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(ActualWidth)),
            Math.Max(1, (int)Math.Ceiling(ActualHeight)),
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(Path.GetTempPath(), "clicky-onboarding-fallback-render.png"));
        encoder.Save(stream);
    }

    private static string ShortReason(string message) => message.Length <= 90 ? message : message[..87] + "...";
}
