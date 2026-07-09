using Clicky.Windows.Services;
using Clicky.Windows.Native;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace Clicky.Windows.Views;

public partial class CompanionPanelWindow : Window
{
    private bool _microphoneGranted;
    private bool _accessibilityGranted;
    private bool _screenGranted;
    private bool _contentGranted;
    private bool _emailSubmitted;
    private bool _onboarded;

    public bool IsOnboarded => _onboarded;
    public string SelectedModel { get; private set; } = "claude-sonnet-4-6";

    public CompanionPanelWindow()
    {
        InitializeComponent();
        SourceInitialized += ExcludePanelFromCapture;
        Loaded += async (_, _) =>
        {
            if (!NativeMethods.IsVisualTest)
            {
                return;
            }

            await Task.Delay(350);
            WriteVisualTestSnapshot();
        };
        SetVoiceState(InteractionState.Idle);
        RefreshLayout();
    }

    public event Action? StartRequested;
    public event Action? ReplayRequested;
    public event Action? ScreenRecordingRequested;
    public event Action? QuitRequested;
    public event Action<string>? ModelChanged;

    public void SetWorkerConfigured(bool isConfigured)
    {
        WorkerStatus.Text = isConfigured
            ? "your private worker is configured"
            : "local-only until CLICKY_WORKER_URL is configured";
        WorkerStatus.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            isConfigured ? "#6B736F" : "#E59A40"));
    }

    public void EnableVisualTestReadyState()
    {
        if (!NativeMethods.IsVisualTest)
        {
            return;
        }

        _microphoneGranted = true;
        _accessibilityGranted = true;
        _screenGranted = true;
        _contentGranted = true;
        _emailSubmitted = true;
        _onboarded = true;
        RefreshLayout();
    }

    public void ShowPanel()
    {
        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        var workingArea = screen.WorkingArea;
        Left = workingArea.Right - ActualWidth - 18;
        Top = workingArea.Bottom - ActualHeight - 18;
        Activate();
    }

    public void SetVoiceState(InteractionState state)
    {
        var (label, color) = state switch
        {
            InteractionState.Listening => ("Listening", "#3380FF"),
            InteractionState.Processing => ("Processing", "#3380FF"),
            InteractionState.Responding => ("Responding", "#34D399"),
            _ when _onboarded => ("Active", "#34D399"),
            _ => ("Ready", "#6B736F")
        };

        StateText.Text = label;
        StatusDot.Fill = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
    }

    private void HidePanel(object? sender, RoutedEventArgs eventArgs) => Hide();

    private void GrantMicrophone(object? sender, RoutedEventArgs eventArgs)
    {
        OpenWindowsSettings("ms-settings:privacy-microphone");
        _microphoneGranted = true;
        UpdatePermission(MicrophoneButton, MicrophoneStatus);
        RefreshLayout();
    }

    private void GrantAccessibility(object? sender, RoutedEventArgs eventArgs)
    {
        _accessibilityGranted = true;
        UpdatePermission(AccessibilityButton, AccessibilityStatus);
        RefreshLayout();
    }

    private void GrantScreen(object? sender, RoutedEventArgs eventArgs)
    {
        ScreenRecordingRequested?.Invoke();
        _screenGranted = true;
        UpdatePermission(ScreenButton, ScreenStatus);
        RefreshLayout();
    }

    private void GrantContent(object? sender, RoutedEventArgs eventArgs)
    {
        _contentGranted = true;
        UpdatePermission(ContentButton, ContentStatus);
        RefreshLayout();
    }

    private void EmailChanged(object? sender, System.Windows.Controls.TextChangedEventArgs eventArgs)
    {
        SubmitButton.IsEnabled = !string.IsNullOrWhiteSpace(EmailInput.Text);
    }

    private void SubmitEmail(object? sender, RoutedEventArgs eventArgs)
    {
        if (string.IsNullOrWhiteSpace(EmailInput.Text))
        {
            return;
        }

        _emailSubmitted = true;
        RefreshLayout();
    }

    private void StartClicky(object? sender, RoutedEventArgs eventArgs)
    {
        _onboarded = true;
        RefreshLayout();
        StartRequested?.Invoke();
    }

    private void SelectSonnet(object? sender, RoutedEventArgs eventArgs)
    {
        SelectedModel = "claude-sonnet-4-6";
        ModelChanged?.Invoke(SelectedModel);
        SonnetButton.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2563EB"));
        SonnetButton.Foreground = System.Windows.Media.Brushes.White;
        OpusButton.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#202221"));
        OpusButton.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#ADB5B2"));
    }

    private void SelectOpus(object? sender, RoutedEventArgs eventArgs)
    {
        SelectedModel = "claude-opus-4-6";
        ModelChanged?.Invoke(SelectedModel);
        OpusButton.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2563EB"));
        OpusButton.Foreground = System.Windows.Media.Brushes.White;
        SonnetButton.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#202221"));
        SonnetButton.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#ADB5B2"));
    }

    private void OpenFeedback(object? sender, RoutedEventArgs eventArgs)
    {
        Process.Start(new ProcessStartInfo("https://x.com/FarzaTV") { UseShellExecute = true });
    }

    private void ReplayOnboarding(object? sender, RoutedEventArgs eventArgs)
    {
        ReplayRequested?.Invoke();
    }

    private void QuitClicky(object? sender, RoutedEventArgs eventArgs) => QuitRequested?.Invoke();

    private void RefreshLayout()
    {
        var allGranted = _microphoneGranted && _accessibilityGranted && _screenGranted && _contentGranted;
        SetupCopyPanel.Visibility = allGranted || _onboarded ? Visibility.Collapsed : Visibility.Visible;
        PermissionHeading.Visibility = allGranted || _onboarded ? Visibility.Collapsed : Visibility.Visible;
        PermissionPanel.Visibility = allGranted || _onboarded ? Visibility.Collapsed : Visibility.Visible;
        EmailPanel.Visibility = allGranted && !_emailSubmitted && !_onboarded ? Visibility.Visible : Visibility.Collapsed;
        StartPanel.Visibility = allGranted && _emailSubmitted && !_onboarded ? Visibility.Visible : Visibility.Collapsed;
        ReadyPanel.Visibility = _onboarded ? Visibility.Visible : Visibility.Collapsed;
        SetVoiceState(InteractionState.Idle);
    }

    private static void UpdatePermission(System.Windows.Controls.Button button, System.Windows.Controls.TextBlock status)
    {
        button.Visibility = Visibility.Collapsed;
        status.Visibility = Visibility.Visible;
    }

    private static void OpenWindowsSettings(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Windows may disable protocol launches in constrained environments.
        }
    }

    private void ExcludePanelFromCapture(object? sender, EventArgs eventArgs)
    {
        if (NativeMethods.IsVisualTest)
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        _ = NativeMethods.SetWindowDisplayAffinity(handle, NativeMethods.WdaExcludeFromCapture);
    }

    private void WriteVisualTestSnapshot()
    {
        if (!NativeMethods.IsVisualTest || Content is not Visual visual)
        {
            return;
        }

        UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(Path.GetTempPath(), "clicky-panel-render.png");
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
