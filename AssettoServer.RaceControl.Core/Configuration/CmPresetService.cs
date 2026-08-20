using System.Globalization;
using AssettoServer.RaceControl.Core.Models;

namespace AssettoServer.RaceControl.Core.Configuration;

public sealed class CmPresetService
{
    public IReadOnlyList<CmPresetSummary> List(string assettoCorsaRoot)
    {
        var root = Path.Combine(assettoCorsaRoot, "server", "presets");
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateDirectories(root)
            .Where(directory => File.Exists(Path.Combine(directory, "server_cfg.ini"))
                && File.Exists(Path.Combine(directory, "entry_list.ini")))
            .Select(directory => new CmPresetSummary(
                Path.GetFileName(directory),
                directory,
                new[]
                {
                    File.GetLastWriteTime(Path.Combine(directory, "server_cfg.ini")),
                    File.GetLastWriteTime(Path.Combine(directory, "entry_list.ini")),
                }.Max()))
            .OrderByDescending(summary => summary.ModifiedAt)
            .ToArray();
    }

    public RaceControlPreset Import(CmPresetSummary summary, string assettoCorsaRoot, string serverPayloadPath)
    {
        var server = IniDocument.Load(Path.Combine(summary.Path, "server_cfg.ini"));
        var entries = IniDocument.Load(Path.Combine(summary.Path, "entry_list.ini"));
        var grid = entries.Sections.Where(section => section.Name.StartsWith("CAR_", StringComparison.OrdinalIgnoreCase))
            .OrderBy(section => ParseInt(section.Name[4..], int.MaxValue))
            .Select(section => new GridSlotPreset
            {
                CarId = section.Get("MODEL") ?? string.Empty,
                SkinId = section.Get("SKIN") ?? string.Empty,
                DriverName = section.Get("DRIVERNAME") ?? string.Empty,
                TeamName = section.Get("TEAM") ?? string.Empty,
                NationCode = section.Get("NATION") ?? string.Empty,
                BallastKg = ParseInt(section.Get("BALLAST"), 0),
                RestrictorPercent = ParseInt(section.Get("RESTRICTOR"), 0),
                Mode = ParseMode(section.Get("AI")),
            })
            .ToList();

        if (grid.Count == 1)
        {
            grid.Add(CloneSlot(grid[0], "Bot 02"));
        }

        var preset = RaceControlPreset.CreateDefault(assettoCorsaRoot, serverPayloadPath);
        preset.Name = summary.Name;
        preset.ServerName = server.Get("SERVER", "NAME") ?? summary.Name;
        preset.TrackId = server.Get("SERVER", "TRACK") ?? string.Empty;
        preset.TrackLayoutId = server.Get("SERVER", "CONFIG_TRACK") ?? string.Empty;
        preset.Grid = grid.Count > 0 ? grid : preset.Grid;
        preset.Network.JoinPassword = server.Get("SERVER", "PASSWORD") ?? string.Empty;
        preset.Network.AdminPassword = server.Get("SERVER", "ADMIN_PASSWORD") ?? string.Empty;
        preset.Network.TcpPort = ParsePort(server.Get("SERVER", "TCP_PORT"), 9600);
        preset.Network.UdpPort = ParsePort(server.Get("SERVER", "UDP_PORT"), 9600);
        preset.Network.HttpPort = ParsePort(server.Get("SERVER", "HTTP_PORT"), 8081);
        preset.Sessions.PracticeEnabled = server.FindSection("PRACTICE") is not null;
        preset.Sessions.PracticeMinutes = ParseInt(server.Get("PRACTICE", "TIME"), 10);
        preset.Sessions.QualifyingEnabled = server.FindSection("QUALIFY") is not null;
        preset.Sessions.QualifyingMinutes = ParseInt(server.Get("QUALIFY", "TIME"), 10);
        preset.Sessions.RaceLaps = ParseInt(server.Get("RACE", "LAPS"), 3);
        preset.Sessions.RaceOverTimeSeconds = ParseInt(server.Get("SERVER", "RACE_OVER_TIME"), 60);
        preset.Sessions.ResultScreenSeconds = ParseInt(server.Get("SERVER", "RESULT_SCREEN_TIME"), 30);
        preset.Rules.FuelRatePercent = ParseInt(server.Get("SERVER", "FUEL_RATE"), 0);
        preset.Rules.DamageRatePercent = ParseInt(server.Get("SERVER", "DAMAGE_MULTIPLIER"), 0);
        preset.Rules.TyreWearRatePercent = ParseInt(server.Get("SERVER", "TYRE_WEAR_RATE"), 0);
        preset.Conditions.WeatherId = server.Get("WEATHER_0", "GRAPHICS") ?? "3_clear";
        preset.Conditions.SunAngleDegrees = ParseInt(server.Get("SERVER", "SUN_ANGLE"), 16);
        preset.Conditions.AmbientTemperatureCelsius = ParseInt(server.Get("WEATHER_0", "BASE_TEMPERATURE_AMBIENT"), 22);
        preset.Conditions.RoadTemperatureCelsius = preset.Conditions.AmbientTemperatureCelsius
            + ParseInt(server.Get("WEATHER_0", "BASE_TEMPERATURE_ROAD"), 6);
        preset.Bots.Enabled = preset.Grid.Any(slot => slot.Mode != SlotMode.None);
        return preset;
    }

    public string ExportNew(RaceControlPreset preset, AcContentCatalog catalog, ServerConfigurationRenderer renderer)
    {
        var root = Path.Combine(preset.AssettoCorsaRoot, "server", "presets");
        Directory.CreateDirectory(root);
        var baseName = "RACE_CONTROL_" + FileNameSanitizer(preset.Name).ToUpperInvariant();
        var destination = Path.Combine(root, baseName);
        for (var suffix = 2; Directory.Exists(destination); suffix++)
        {
            destination = Path.Combine(root, $"{baseName}_{suffix}");
        }

        Directory.CreateDirectory(destination);
        var rendered = renderer.Render(preset, catalog);
        rendered.ServerConfiguration.Save(Path.Combine(destination, "server_cfg.ini"));
        rendered.EntryList.Save(Path.Combine(destination, "entry_list.ini"));
        return destination;
    }

    private static GridSlotPreset CloneSlot(GridSlotPreset source, string name) => new()
    {
        CarId = source.CarId,
        SkinId = source.SkinId,
        DriverName = name,
        TeamName = source.TeamName,
        NationCode = source.NationCode,
        Mode = SlotMode.Auto,
    };

    private static SlotMode ParseMode(string? value) => value?.ToLowerInvariant() switch
    {
        "fixed" => SlotMode.Fixed,
        "none" => SlotMode.None,
        _ => SlotMode.Auto,
    };

    private static ushort ParsePort(string? value, ushort fallback) => ushort.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    private static int ParseInt(string? value, int fallback) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static string FileNameSanitizer(string value) => new(value.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').ToArray());
}

public sealed record CmPresetSummary(string Name, string Path, DateTime ModifiedAt);
