using Clicky.Windows.Native;
using Clicky.Windows.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace Clicky.Windows.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly SecureCredentialStore _credentialStore;
    private readonly string _safetyIdentifier;
    private bool _loading;

    public SettingsWindow(SettingsService settingsService, SecureCredentialStore credentialStore)
    {
        _settingsService = settingsService;
        _credentialStore = credentialStore;
        InitializeComponent();
        SourceInitialized += ExcludeFromCapture;

        _loading = true;
        PresetBox.ItemsSource = new[] { "OpenAI", "OpenRouter", "MiMo", "Local", "Custom" };
        ProtocolBox.ItemsSource = Enum.GetValues<ProviderProtocol>();
        SmtpSecurityBox.ItemsSource = Enum.GetValues<SmtpSecurityMode>();
        var localRecognizers = WindowsSpeechRecognitionService.InstalledRecognizers();
        var localVoices = WindowsSpeechSynthesisService.InstalledVoices();
        LocalSpeechCultureBox.ItemsSource = new[] { "Automatic" }
            .Concat(localRecognizers.Select(item => item.Culture))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        LocalSpeechStatus.Text = localRecognizers.Count == 0
            ? $"No offline recognizer; {localVoices.Count} local voice{(localVoices.Count == 1 ? string.Empty : "s")}."
            : $"{localRecognizers.Count} offline recognizer{(localRecognizers.Count == 1 ? string.Empty : "s")}; {localVoices.Count} local voice{(localVoices.Count == 1 ? string.Empty : "s")}.";
        var currentSettings = _settingsService.Load();
        var currentProvider = currentSettings.Provider;
        _safetyIdentifier = currentProvider.SafetyIdentifier;
        Apply(currentProvider);
        ApplyAudio(currentSettings.Audio);
        AgentWorkspaceInput.Text = currentSettings.Agent.WorkspacePath;
        ApplyEmail(currentSettings.Email);
        CredentialHint.Text = string.IsNullOrWhiteSpace(_credentialStore.ReadApiKey())
            ? "No key is saved. Local endpoints may not require one."
            : "A key is saved. Leave this blank to keep it unchanged.";
        AudioCredentialHint.Text = string.IsNullOrWhiteSpace(_credentialStore.ReadAudioApiKey())
            ? "Uses the main provider key when blank."
            : "A separate audio key is saved.";
        EmailCredentialHint.Text = string.IsNullOrWhiteSpace(_credentialStore.ReadEmailPassword())
            ? "No SMTP password is saved."
            : "An SMTP password or app password is saved.";
        _loading = false;
        Loaded += async (_, _) =>
        {
            if (!NativeMethods.IsVisualTest
                || !string.Equals(Environment.GetEnvironmentVariable("CLICKY_VISUAL_TEST_SETTINGS"), "1", StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(350);
            if (string.Equals(Environment.GetEnvironmentVariable("CLICKY_VISUAL_TEST_SETTINGS_AUDIO"), "1", StringComparison.Ordinal))
            {
                SettingsScroll.ScrollToEnd();
                UpdateLayout();
            }
            else if (string.Equals(Environment.GetEnvironmentVariable("CLICKY_VISUAL_TEST_SETTINGS_LOCAL_SPEECH"), "1", StringComparison.Ordinal))
            {
                SettingsScroll.ScrollToVerticalOffset(440);
                UpdateLayout();
            }
            WriteVisualTestSnapshot();
        };
    }

    public event Action<ClickySettings>? SettingsSaved;

    private void Apply(ProviderSettings provider)
    {
        PresetBox.SelectedItem = provider.Preset;
        DisplayNameInput.Text = provider.DisplayName;
        BaseUrlInput.Text = provider.BaseUrl;
        ProtocolBox.SelectedItem = provider.Protocol;
        ModelBox.Text = provider.Model;
        SendScreenshotsCheck.IsChecked = provider.SendScreenshots;
        WebSearchCheck.IsChecked = provider.EnableWebSearch;
    }

    private void ApplyAudio(AudioSettings audio)
    {
        DirectAudioCheck.IsChecked = audio.Enabled;
        LocalSpeechCheck.IsChecked = audio.EnableWindowsSpeechFallback;
        LocalSpeechCultureBox.SelectedItem = string.IsNullOrWhiteSpace(audio.WindowsSpeechCulture)
            ? "Automatic"
            : audio.WindowsSpeechCulture;
        if (LocalSpeechCultureBox.SelectedIndex < 0)
        {
            LocalSpeechCultureBox.SelectedIndex = 0;
        }
        AudioBaseUrlInput.Text = audio.BaseUrl;
        TranscriptionModelInput.Text = audio.TranscriptionModel;
        SpeechModelInput.Text = audio.SpeechModel;
        VoiceInput.Text = audio.Voice;
    }

    private void ApplyEmail(EmailSettings email)
    {
        EmailEnabledCheck.IsChecked = email.Enabled;
        SmtpHostInput.Text = email.SmtpHost;
        SmtpPortInput.Text = email.SmtpPort.ToString();
        SmtpSecurityBox.SelectedItem = email.Security;
        SmtpUsernameInput.Text = email.Username;
        FromAddressInput.Text = email.FromAddress;
        FromNameInput.Text = email.FromName;
    }

    private void PresetChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (_loading || PresetBox.SelectedItem is not string preset)
        {
            return;
        }

        Apply(ProviderSettings.FromPreset(preset));
    }

    private ProviderSettings ReadProvider() => new()
    {
        Preset = PresetBox.SelectedItem as string ?? "Custom",
        DisplayName = string.IsNullOrWhiteSpace(DisplayNameInput.Text) ? "Custom endpoint" : DisplayNameInput.Text.Trim(),
        BaseUrl = BaseUrlInput.Text.Trim(),
        Protocol = ProtocolBox.SelectedItem is ProviderProtocol protocol ? protocol : ProviderProtocol.OpenAiChatCompletions,
        Model = ModelBox.Text.Trim(),
        SafetyIdentifier = _safetyIdentifier,
        SendScreenshots = SendScreenshotsCheck.IsChecked == true,
        EnableWebSearch = WebSearchCheck.IsChecked == true
    };

    private AudioSettings ReadAudio() => new()
    {
        Enabled = DirectAudioCheck.IsChecked == true,
        EnableWindowsSpeechFallback = LocalSpeechCheck.IsChecked == true,
        WindowsSpeechCulture = LocalSpeechCultureBox.SelectedItem is string culture && culture != "Automatic"
            ? culture
            : string.Empty,
        BaseUrl = AudioBaseUrlInput.Text.Trim(),
        TranscriptionModel = TranscriptionModelInput.Text.Trim(),
        SpeechModel = SpeechModelInput.Text.Trim(),
        Voice = VoiceInput.Text.Trim()
    };

    private AgentSettings ReadAgent() => new()
    {
        WorkspacePath = AgentWorkspaceInput.Text.Trim()
    };

    private EmailSettings ReadEmail() => new()
    {
        Enabled = EmailEnabledCheck.IsChecked == true,
        SmtpHost = SmtpHostInput.Text.Trim(),
        SmtpPort = int.TryParse(SmtpPortInput.Text, out var port) ? port : 0,
        Security = SmtpSecurityBox.SelectedItem is SmtpSecurityMode security ? security : SmtpSecurityMode.Auto,
        Username = SmtpUsernameInput.Text.Trim(),
        FromAddress = FromAddressInput.Text.Trim(),
        FromName = FromNameInput.Text.Trim()
    };

    private async void DiscoverModels(object sender, RoutedEventArgs eventArgs)
    {
        StatusText.Text = "Discovering models...";
        try
        {
            var provider = ReadProvider();
            using var client = new UniversalModelClient(provider, ApiKeyInput.Password.Trim() is { Length: > 0 } entered
                ? entered
                : _credentialStore.ReadApiKey());
            var models = await client.ListModelsAsync(CancellationToken.None);
            ModelBox.ItemsSource = models;
            StatusText.Text = models.Count == 0
                ? "The endpoint responded but returned no model IDs. You can enter one manually."
                : $"Found {models.Count:N0} models. Start typing to filter, then choose one.";
            ModelBox.IsDropDownOpen = models.Count > 0;
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Model discovery failed: {exception.Message}";
        }
    }

    private void RemoveSavedKey(object sender, RoutedEventArgs eventArgs)
    {
        _credentialStore.DeleteApiKey();
        ApiKeyInput.Clear();
        CredentialHint.Text = "No key is saved.";
        StatusText.Text = "Saved API key removed from Windows Credential Manager.";
    }

    private void RemoveAudioKey(object sender, RoutedEventArgs eventArgs)
    {
        _credentialStore.DeleteAudioApiKey();
        AudioApiKeyInput.Clear();
        AudioCredentialHint.Text = "Uses the main provider key when blank.";
        StatusText.Text = "Saved audio API key removed from Windows Credential Manager.";
    }

    private void BrowseWorkspace(object sender, RoutedEventArgs eventArgs)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Choose where Clicky agents may create approved files",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            InitialDirectory = Directory.Exists(AgentWorkspaceInput.Text) ? AgentWorkspaceInput.Text : string.Empty
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            AgentWorkspaceInput.Text = dialog.SelectedPath;
        }
    }

    private void RemoveEmailPassword(object sender, RoutedEventArgs eventArgs)
    {
        _credentialStore.DeleteEmailPassword();
        EmailPasswordInput.Clear();
        EmailCredentialHint.Text = "No SMTP password is saved.";
        StatusText.Text = "Saved SMTP password removed from Windows Credential Manager.";
    }

    private void Save(object sender, RoutedEventArgs eventArgs)
    {
        var provider = ReadProvider();
        if (!provider.IsConfigured)
        {
            StatusText.Text = "Enter a valid absolute base URL and model ID.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(ApiKeyInput.Password))
        {
            _credentialStore.WriteApiKey(ApiKeyInput.Password);
        }

        if (!string.IsNullOrWhiteSpace(AudioApiKeyInput.Password))
        {
            _credentialStore.WriteAudioApiKey(AudioApiKeyInput.Password);
        }
        if (!string.IsNullOrWhiteSpace(EmailPasswordInput.Password))
        {
            _credentialStore.WriteEmailPassword(EmailPasswordInput.Password);
        }

        var audio = ReadAudio();
        if (audio.Enabled && !audio.IsConfigured(provider))
        {
            StatusText.Text = "Voice needs a valid audio base URL, transcription model, speech model, and voice ID.";
            return;
        }

        var email = ReadEmail();
        if (email.Enabled && !email.IsConfigured)
        {
            StatusText.Text = "Email delivery needs a valid SMTP host, port, and From address.";
            return;
        }

        var settings = new ClickySettings { Provider = provider, Audio = audio, Agent = ReadAgent(), Email = email };
        _settingsService.Save(settings);
        SettingsSaved?.Invoke(settings);
        DialogResult = true;
        Close();
    }

    private void Cancel(object sender, RoutedEventArgs eventArgs) => Close();

    private void ExcludeFromCapture(object? sender, EventArgs eventArgs)
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
        using var stream = File.Create(Path.Combine(Path.GetTempPath(), "clicky-settings-render.png"));
        encoder.Save(stream);
    }
}
