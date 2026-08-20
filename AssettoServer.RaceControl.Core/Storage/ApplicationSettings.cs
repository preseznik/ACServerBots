using System.Text.Json;
using System.Text.Json.Serialization;
using AssettoServer.RaceControl.Core.Infrastructure;

namespace AssettoServer.RaceControl.Core.Storage;

public enum AppThemeMode
{
    System,
    Light,
    Dark,
}

public sealed class ApplicationSettings
{
    public int SchemaVersion { get; set; } = 1;
    public AppThemeMode Theme { get; set; } = AppThemeMode.Dark;
    public bool LoadMostRecentPresetOnStartup { get; set; }
    public bool RememberLastPage { get; set; } = true;
    public int LastPageIndex { get; set; }
    public bool ConfirmBeforeStoppingServerOnExit { get; set; } = true;
    public bool CompactGridRows { get; set; }

    public ApplicationSettings Copy() => new()
    {
        SchemaVersion = SchemaVersion,
        Theme = Theme,
        LoadMostRecentPresetOnStartup = LoadMostRecentPresetOnStartup,
        RememberLastPage = RememberLastPage,
        LastPageIndex = LastPageIndex,
        ConfirmBeforeStoppingServerOnExit = ConfirmBeforeStoppingServerOnExit,
        CompactGridRows = CompactGridRows,
    };
}

public sealed class ApplicationSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly RaceControlPaths _paths;

    public ApplicationSettingsStore(RaceControlPaths paths) => _paths = paths;

    public string SettingsPath => Path.Combine(_paths.DataRoot, "settings.json");

    public ApplicationSettings Load()
    {
        _paths.EnsureCreated();
        if (!File.Exists(SettingsPath))
        {
            return new ApplicationSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<ApplicationSettings>(File.ReadAllText(SettingsPath), JsonOptions)
                ?? new ApplicationSettings();
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return new ApplicationSettings();
        }
    }

    public void Save(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _paths.EnsureCreated();
        var temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporary, SettingsPath, true);
    }
}
