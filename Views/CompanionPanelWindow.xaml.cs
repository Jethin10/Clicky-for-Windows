using Clicky.Windows.Services;
using Clicky.Windows.Native;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace Clicky.Windows.Views;

public partial class CompanionPanelWindow : Window
{
    private bool _onboarded;

    public bool IsOnboarded => _onboarded;
    public string SelectedModel { get; private set; } = "claude-sonnet-4-6";

    public CompanionPanelWindow()
    {
        InitializeComponent();
        UpdatePermission(AccessibilityButton, AccessibilityStatus);
        UpdatePermission(ScreenButton, ScreenStatus);
        UpdatePermission(ContentButton, ContentStatus);
        SourceInitialized += ExcludePanelFromCapture;
        Deactivated += DismissWhenFocusLeavesClicky;
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

    private void DismissWhenFocusLeavesClicky(object? sender, EventArgs eventArgs)
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (!IsVisible || IsActive || OwnedWindows.Cast<Window>().Any(window => window.IsVisible))
            {
                return;
            }

            Hide();
            if (NativeMethods.IsVisualTest
                && string.Equals(Environment.GetEnvironmentVariable("CLICKY_VISUAL_TEST_PANEL_DISMISS"), "1", StringComparison.Ordinal))
            {
                File.WriteAllText(
                    Path.Combine(Path.GetTempPath(), "clicky-panel-dismissed.txt"),
                    "dismissed after focus left the panel");
            }
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    public event Action? StartRequested;
    public event Action? OnboardingCompleted;
    public event Action? ReplayRequested;
    public event Action? ScreenRecordingRequested;
    public event Action? SettingsRequested;
    public event Action? QuitRequested;
    public event Action<string>? ModelChanged;
    public event Action<string, bool>? PromptSubmitted;
    public event Action? AttachDocumentRequested;
    public event Action? RemoveDocumentRequested;
    public event Action? ViewAgentResultRequested;

    public void SetWorkerConfigured(bool isConfigured)
    {
        WorkerStatus.Text = isConfigured
            ? "your private worker is configured"
            : "local-only until CLICKY_WORKER_URL is configured";
        WorkerStatus.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            isConfigured ? "#6B736F" : "#E59A40"));
    }

    public void SetProviderConfiguration(
        ProviderSettings provider,
        bool directProviderReady,
        bool workerConfigured,
        bool directAudioReady,
        bool windowsSpeechReady)
    {
        if (directProviderReady)
        {
            ProviderNameText.Text = provider.DisplayName;
            ProviderModelText.Text = provider.Model;
            SelectedModel = provider.Model;
            WorkerModelButtons.Visibility = Visibility.Collapsed;
            WorkerStatus.Text = directAudioReady
                ? "direct AI, transcription, and speech are ready"
                : workerConfigured
                    ? "direct AI is ready; your private worker handles voice"
                    : windowsSpeechReady
                        ? "direct AI is ready; Windows offline voice is ready"
                        : "direct AI is ready; enable compatible voice endpoints";
            WorkerStatus.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                directAudioReady || workerConfigured || windowsSpeechReady ? "#6B736F" : "#E59A40"));
            return;
        }

        ProviderNameText.Text = workerConfigured ? "Private Worker" : "No AI provider";
        ProviderModelText.Text = workerConfigured ? SelectedModel : "Open Configure to connect one";
        WorkerModelButtons.Visibility = workerConfigured ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetAgentStatus(string status, bool active)
    {
        AgentStatusText.Text = status;
        AgentStatusText.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            active ? "#60A5FA" : "#6B736F"));
    }

    public void SetAgentResult(string providerName, string preview)
    {
        AgentResultPanel.Visibility = Visibility.Visible;
        AgentResultTitleText.Text = $"Latest result · {providerName}";
        AgentResultPreviewText.Text = preview.ReplaceLineEndings(" ").Trim();
    }

    public void SetAttachmentStatus(string fileName, string status, bool visible)
    {
        AttachmentPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        AttachmentNameText.Text = fileName;
        AttachmentStatusText.Text = status;
    }

    public void EnableVisualTestReadyState()
    {
        if (!NativeMethods.IsVisualTest)
        {
            return;
        }

        _onboarded = true;
        RefreshLayout();
        if (string.Equals(Environment.GetEnvironmentVariable("CLICKY_VISUAL_TEST_ATTACHMENT"), "1", StringComparison.Ordinal))
        {
            var ocr = string.Equals(Environment.GetEnvironmentVariable("CLICKY_VISUAL_TEST_OCR_ATTACHMENT"), "1", StringComparison.Ordinal);
            SetAttachmentStatus(
                ocr ? "scanned-project-brief.pdf" : "clicky-verification.pdf",
                ocr ? "3 pages, 1,842 characters (local OCR)" : "2 pages, 314 characters",
                visible: true);
        }
    }

    public void SetOnboardingCompleted(bool completed)
    {
        _onboarded = completed;
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
        UpdatePermission(MicrophoneButton, MicrophoneStatus);
        RefreshLayout();
    }

    private void GrantAccessibility(object? sender, RoutedEventArgs eventArgs)
    {
        UpdatePermission(AccessibilityButton, AccessibilityStatus);
        RefreshLayout();
    }

    private void GrantScreen(object? sender, RoutedEventArgs eventArgs)
    {
        ScreenRecordingRequested?.Invoke();
        UpdatePermission(ScreenButton, ScreenStatus);
        RefreshLayout();
    }

    private void GrantContent(object? sender, RoutedEventArgs eventArgs)
    {
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

        StartClicky(sender, eventArgs);
    }

    private void StartClicky(object? sender, RoutedEventArgs eventArgs)
    {
        _onboarded = true;
        RefreshLayout();
        OnboardingCompleted?.Invoke();
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

    private void OpenSettings(object? sender, RoutedEventArgs eventArgs) => SettingsRequested?.Invoke();

    private void SubmitPrompt(object? sender, RoutedEventArgs eventArgs) => DispatchPrompt(agentMode: false);

    private void SubmitAgentPrompt(object? sender, RoutedEventArgs eventArgs) => DispatchPrompt(agentMode: true);

    private void AttachDocument(object? sender, RoutedEventArgs eventArgs) => AttachDocumentRequested?.Invoke();

    private void RemoveDocument(object? sender, RoutedEventArgs eventArgs) => RemoveDocumentRequested?.Invoke();

    private void ViewAgentResult(object? sender, RoutedEventArgs eventArgs) => ViewAgentResultRequested?.Invoke();

    private void PromptKeyDown(object? sender, System.Windows.Input.KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            eventArgs.Handled = true;
            DispatchPrompt(agentMode: false);
        }
    }

    private void DispatchPrompt(bool agentMode)
    {
        var prompt = PromptInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        PromptInput.Clear();
        PromptSubmitted?.Invoke(prompt, agentMode);
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
        SetupCopyPanel.Visibility = _onboarded ? Visibility.Collapsed : Visibility.Visible;
        PermissionHeading.Visibility = _onboarded ? Visibility.Collapsed : Visibility.Visible;
        PermissionPanel.Visibility = _onboarded ? Visibility.Collapsed : Visibility.Visible;
        EmailPanel.Visibility = Visibility.Collapsed;
        StartPanel.Visibility = _onboarded ? Visibility.Collapsed : Visibility.Visible;
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
