using Clicky.Windows.Native;
using Clicky.Windows.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
        var currentProvider = _settingsService.Load().Provider;
        _safetyIdentifier = currentProvider.SafetyIdentifier;
        Apply(currentProvider);
        CredentialHint.Text = string.IsNullOrWhiteSpace(_credentialStore.ReadApiKey())
            ? "No key is saved. Local endpoints may not require one."
            : "A key is saved. Leave this blank to keep it unchanged.";
        _loading = false;
        Loaded += async (_, _) =>
        {
            if (!NativeMethods.IsVisualTest
                || !string.Equals(Environment.GetEnvironmentVariable("CLICKY_VISUAL_TEST_SETTINGS"), "1", StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(350);
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

        var settings = new ClickySettings { Provider = provider };
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
