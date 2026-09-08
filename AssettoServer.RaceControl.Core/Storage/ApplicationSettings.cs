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
    public string AssettoCorsaRoot { get; set; } = string.Empty;
    public string ServerPayloadPath { get; set; } = string.Empty;
    public string LastFpsTrackId { get; set; } = string.Empty;
    public string LastFpsTrackLayoutId { get; set; } = string.Empty;
    public bool WebUiEnabled { get; set; } = true;
    public string WebUiBindAddress { get; set; } = "127.0.0.1";
    public int WebUiPort { get; set; } = 8772;

    public ApplicationSettings Copy() => new()
    {
        SchemaVersion = SchemaVersion,
        Theme = Theme,
        LoadMostRecentPresetOnStartup = LoadMostRecentPresetOnStartup,
        RememberLastPage = RememberLastPage,
        LastPageIndex = LastPageIndex,
        ConfirmBeforeStoppingServerOnExit = ConfirmBeforeStoppingServerOnExit,
        CompactGridRows = CompactGridRows,
        AssettoCorsaRoot = AssettoCorsaRoot,
        ServerPayloadPath = ServerPayloadPath,
        LastFpsTrackId = LastFpsTrackId,
        LastFpsTrackLayoutId = LastFpsTrackLayoutId,
        WebUiEnabled = WebUiEnabled,
        WebUiBindAddress = WebUiBindAddress,
        WebUiPort = WebUiPort,
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
            return new ReleaseDefaults(_paths).LoadSettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<ApplicationSettings>(File.ReadAllText(SettingsPath), JsonOptions)
                ?? new ApplicationSettings();
            settings.ServerPayloadPath = _paths.ResolveComponentPath(settings.ServerPayloadPath);
            return settings;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return new ReleaseDefaults(_paths).LoadSettings();
        }
    }

    public void Save(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _paths.EnsureCreated();
        var temporary = SettingsPath + ".tmp";
        var stored = settings.Copy();
        stored.ServerPayloadPath = _paths.StoreComponentPath(stored.ServerPayloadPath);
        File.WriteAllText(temporary, JsonSerializer.Serialize(stored, JsonOptions));
        File.Move(temporary, SettingsPath, true);
    }
}
