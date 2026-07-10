using System.Text.Json.Serialization;

namespace Clicky.Windows.Services;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProviderProtocol
{
    OpenAiResponses,
    OpenAiChatCompletions
}

public sealed class ProviderSettings
{
    public string Preset { get; set; } = "OpenAI";
    public string DisplayName { get; set; } = "OpenAI";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "gpt-5.6-luna";
    public string SafetyIdentifier { get; set; } = Guid.NewGuid().ToString("N");
    public ProviderProtocol Protocol { get; set; } = ProviderProtocol.OpenAiResponses;
    public bool SendScreenshots { get; set; } = true;
    public bool EnableWebSearch { get; set; }

    [JsonIgnore]
    public bool IsConfigured => Uri.TryCreate(BaseUrl, UriKind.Absolute, out _)
        && !string.IsNullOrWhiteSpace(Model);

    public static ProviderSettings FromPreset(string preset) => preset switch
    {
        "OpenRouter" => new ProviderSettings
        {
            Preset = "OpenRouter",
            DisplayName = "OpenRouter",
            BaseUrl = "https://openrouter.ai/api/v1",
            Model = "openai/gpt-4.1-mini",
            Protocol = ProviderProtocol.OpenAiChatCompletions
        },
        "MiMo" => new ProviderSettings
        {
            Preset = "MiMo",
            DisplayName = "Xiaomi MiMo",
            BaseUrl = "https://api.xiaomimimo.com/v1",
            Model = "mimo-v2.5-pro",
            Protocol = ProviderProtocol.OpenAiChatCompletions
        },
        "Local" => new ProviderSettings
        {
            Preset = "Local",
            DisplayName = "Local model",
            BaseUrl = "http://localhost:1234/v1",
            Model = "local-model",
            Protocol = ProviderProtocol.OpenAiChatCompletions
        },
        "Custom" => new ProviderSettings
        {
            Preset = "Custom",
            DisplayName = "Custom endpoint",
            BaseUrl = "https://example.com/v1",
            Model = "model-id",
            Protocol = ProviderProtocol.OpenAiChatCompletions
        },
        _ => new ProviderSettings()
    };
}

public sealed class ClickySettings
{
    public int Version { get; set; } = 1;
    public ProviderSettings Provider { get; set; } = new();
    public AudioSettings Audio { get; set; } = new();
    public AgentSettings Agent { get; set; } = new();
}

public sealed class AgentSettings
{
    public string WorkspacePath { get; set; } = string.Empty;
}

public sealed class AudioSettings
{
    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = string.Empty;
    public string TranscriptionModel { get; set; } = "gpt-4o-mini-transcribe";
    public string SpeechModel { get; set; } = "tts-1";
    public string Voice { get; set; } = "alloy";

    public string EffectiveBaseUrl(ProviderSettings provider) =>
        string.IsNullOrWhiteSpace(BaseUrl) ? provider.BaseUrl : BaseUrl.Trim();

    public bool IsConfigured(ProviderSettings provider) => Enabled
        && Uri.TryCreate(EffectiveBaseUrl(provider), UriKind.Absolute, out _)
        && !string.IsNullOrWhiteSpace(TranscriptionModel)
        && !string.IsNullOrWhiteSpace(SpeechModel)
        && !string.IsNullOrWhiteSpace(Voice);
}
