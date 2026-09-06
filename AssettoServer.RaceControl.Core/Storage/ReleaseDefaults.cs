using System.Text.Json;
using System.Text.Json.Serialization;
using AssettoServer.RaceControl.Core.Infrastructure;
using AssettoServer.RaceControl.Core.Models;
using AssettoServer.Release;

namespace AssettoServer.RaceControl.Core.Storage;

public sealed class ReleaseDefaults(RaceControlPaths paths)
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public ApplicationSettings LoadSettings()
    {
        string file = Path.Combine(paths.PackageRoot, "Defaults", "settings.json");
        return File.Exists(file)
            ? SanitizeSettings(JsonSerializer.Deserialize<ApplicationSettings>(File.ReadAllText(file), JsonOptions)
                               ?? new ApplicationSettings())
            : new ApplicationSettings();
    }

    public RaceControlPreset? CreatePreset(EventMode mode, string acRoot, string serverPayload)
    {
        string file = Path.Combine(paths.PackageRoot, "Defaults", mode == EventMode.Fps ? "fps.json" : "racing.json");
        if (!File.Exists(file)) return null;
        var preset = JsonSerializer.Deserialize<RaceControlPreset>(File.ReadAllText(file), JsonOptions)
                     ?? throw new InvalidDataException("Empty release defaults.");
        preset.Id = Guid.NewGuid();
        preset.Mode = mode;
        preset.AssettoCorsaRoot = acRoot;
        preset.ServerPayloadPath = serverPayload;
        if (mode == EventMode.Fps)
            preset.Fps.Arena = new FpsArenaStore(paths).Load(preset.TrackId, preset.TrackLayoutId);
        return preset;
    }

    public RaceControlPreset? CreateStartupPreset(string acRoot, string serverPayload)
    {
        string file = Path.Combine(paths.PackageRoot, "Defaults", "startup.json");
        bool fps = File.Exists(file) && JsonSerializer.Deserialize<StartupDefault>(File.ReadAllText(file), JsonOptions)?.Mode == EventMode.Fps;
        if (fps && FpsAssetResources.IsAvailable(typeof(ReleaseDefaults).Assembly))
        {
            var preset = CreatePreset(EventMode.Fps, acRoot, serverPayload);
            if (preset is not null && (FpsMapPack.FindRoot(paths.PackageRoot) is not null
                || Directory.Exists(Path.Combine(acRoot, "content", "tracks", preset.TrackId))))
                return preset;
        }
        return CreatePreset(EventMode.Racing, acRoot, serverPayload);
    }

    // Build-time allowlists: only typed preferences survive; local state is reset.
    public static ApplicationSettings SanitizeSettings(ApplicationSettings source) => new()
    {
        Theme = source.Theme,
        CompactGridRows = source.CompactGridRows,
        LoadMostRecentPresetOnStartup = source.LoadMostRecentPresetOnStartup,
        RememberLastPage = source.RememberLastPage,
        ConfirmBeforeStoppingServerOnExit = source.ConfirmBeforeStoppingServerOnExit,
        WebUiEnabled = source.WebUiEnabled,
        WebUiPort = 8772,
    };

    public static RaceControlPreset SanitizePreset(RaceControlPreset source)
    {
        var result = JsonSerializer.Deserialize<RaceControlPreset>(
            JsonSerializer.Serialize(source, JsonOptions), JsonOptions)!;
        result.Id = Guid.Empty;
        result.AssettoCorsaRoot = string.Empty;
        result.ServerPayloadPath = string.Empty;
        result.Name = source.Mode == EventMode.Fps ? "FPS defaults" : "Racing defaults";
        result.ServerName = source.Mode == EventMode.Fps ? "AssettoServer LAN FPS Match" : "AssettoServer LAN Race";
        result.Network = new NetworkOptions();
        result.Bots.NamePrefix = "Bot";
        result.Fps.Arena = null;
        for (int index = 0; index < result.Grid.Count; index++)
        {
            var slot = result.Grid[index];
            slot.DriverName = $"{(source.Mode == EventMode.Fps ? "Operative" : "Bot")} {index + 1:00}";
            slot.TeamName = "Race Control";
            slot.NationCode = string.Empty;
        }
        return result;
    }

    private sealed record StartupDefault(EventMode Mode);
}
