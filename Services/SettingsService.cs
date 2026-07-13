namespace Clicky.Windows.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath;

    public SettingsService()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Clicky");
        _settingsPath = Path.Combine(directory, "settings.json");
    }

    public ClickySettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new ClickySettings();
            }

            return JsonSerializer.Deserialize<ClickySettings>(File.ReadAllText(_settingsPath), JsonOptions)
                ?? new ClickySettings();
        }
        catch (Exception)
        {
            return new ClickySettings();
        }
    }

    public void Save(ClickySettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _settingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, _settingsPath, overwrite: true);
    }
}
