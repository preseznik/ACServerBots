using System.Text.Json;
using System.Text.Json.Serialization;
using AssettoServer.RaceControl.Core.Infrastructure;
using AssettoServer.RaceControl.Core.Models;

namespace AssettoServer.RaceControl.Core.Storage;

public sealed class PresetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly RaceControlPaths _paths;

    public PresetStore(RaceControlPaths paths) => _paths = paths;

    public IReadOnlyList<PresetSummary> List(EventMode? mode = null)
    {
        _paths.EnsureCreated();
        return Directory.EnumerateFiles(_paths.PresetsDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(path =>
            {
                try
                {
                    var preset = Load(path);
                    return new PresetSummary(preset.Id, preset.Name, path, File.GetLastWriteTime(path))
                    {
                        Mode = preset.Mode,
                    };
                }
                catch (Exception exception) when (exception is IOException or JsonException)
                {
                    return null;
                }
            })
            .OfType<PresetSummary>()
            .Where(summary => mode is null || summary.Mode == mode)
            .OrderByDescending(summary => summary.ModifiedAt)
            .ToArray();
    }

    public RaceControlPreset Load(string path)
    {
        var json = File.ReadAllText(path);
        var preset = JsonSerializer.Deserialize<RaceControlPreset>(json, JsonOptions)
            ?? throw new InvalidDataException($"Preset is empty: {path}");
        if (preset.SchemaVersion > RaceControlPreset.CurrentSchemaVersion)
            throw new InvalidDataException(
                $"Unsupported preset schema {preset.SchemaVersion}: {path}");
        preset.Fps.Loadouts ??= new FpsLoadoutOptions();
        preset.Fps.Loadouts.AllowedMainWeapons ??=
            [FpsMainWeapon.AssaultRifle, FpsMainWeapon.CompactSmg];
        preset.Fps.Loadouts.AllowedLethals ??=
            [FpsLethalEquipment.FragGrenade, FpsLethalEquipment.StickyGrenade];
        preset.Fps.Loadouts.AllowedSecondaryWeapons ??=
            [FpsSecondaryWeapon.DesertEagle, FpsSecondaryWeapon.Colt1911];
        preset.Fps.Loadouts.HumanDefault ??= new FpsLoadoutPreset();
        preset.Fps.Loadouts.BotDefault ??= new FpsLoadoutPreset();
        preset.SchemaVersion = RaceControlPreset.CurrentSchemaVersion;
        return preset;
    }

    public string Save(RaceControlPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        _paths.EnsureCreated();
        var path = Path.Combine(_paths.PresetsDirectory, $"{FileNameSanitizer.Slug(preset.Name)}-{preset.Id:N}.json");
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(preset, JsonOptions));
        File.Move(temporary, path, true);
        return path;
    }
}

public sealed record PresetSummary(Guid Id, string Name, string Path, DateTime ModifiedAt)
{
    public EventMode Mode { get; init; }
}
