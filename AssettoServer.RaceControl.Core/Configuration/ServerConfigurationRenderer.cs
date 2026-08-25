using System.Globalization;
using System.Text;
using AssettoServer.RaceControl.Core.Models;

namespace AssettoServer.RaceControl.Core.Configuration;

public sealed record RenderedServerConfiguration(
    IniDocument ServerConfiguration,
    IniDocument EntryList,
    string ExtraConfiguration,
    IReadOnlyList<GridSlotPreset> EffectiveGrid,
    AcTrackLayout Track,
    IReadOnlyList<AcCar> Cars,
    IReadOnlyList<AcCar> RacingCars);

public sealed class ServerConfigurationRenderer
{
    public RenderedServerConfiguration Render(RaceControlPreset preset, AcContentCatalog catalog,
        int? physicalGridSlotLimit = null)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(catalog);
        var track = catalog.Tracks.Single(candidate =>
            candidate.TrackId.Equals(preset.TrackId, StringComparison.OrdinalIgnoreCase)
            && candidate.LayoutId.Equals(preset.TrackLayoutId, StringComparison.OrdinalIgnoreCase));
        var slotLimit = track.PitBoxes;
        if (physicalGridSlotLimit.HasValue)
            slotLimit = Math.Min(slotLimit, physicalGridSlotLimit.Value);
        var racingGrid = preset.Grid
            .Where(slot => slot.Mode != SlotMode.Spectator)
            .Take(preset.Mode == EventMode.Fps ? 32 : int.MaxValue)
            .Take(slotLimit)
            .Select(Clone)
            .ToArray();
        if (racingGrid.Length < 2)
            throw new InvalidOperationException("The selected layout exposes fewer than two physical race-grid positions.");
        var spectatorGrid = preset.Grid
            .Where(slot => slot.Mode == SlotMode.Spectator)
            .Take(254 - racingGrid.Length)
            .Select(Clone)
            .ToArray();
        var grid = racingGrid.Concat(spectatorGrid).ToArray();
        if (preset.Mode == EventMode.Fps)
        {
            foreach (var slot in grid)
                slot.CarId = preset.Fps.CarrierCarId;
        }
        var cars = grid.Select(slot => catalog.Cars.Single(car => car.Id.Equals(slot.CarId, StringComparison.OrdinalIgnoreCase)))
            .DistinctBy(car => car.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(car => car.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var racingCars = racingGrid
            .Select(slot => catalog.Cars.Single(car => car.Id.Equals(slot.CarId, StringComparison.OrdinalIgnoreCase)))
            .DistinctBy(car => car.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(car => car.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var weather = catalog.Weather.FirstOrDefault(candidate =>
                candidate.Id.Equals(preset.Conditions.WeatherId, StringComparison.OrdinalIgnoreCase))
            ?? catalog.Weather.FirstOrDefault(candidate => candidate.Id.Equals("3_clear", StringComparison.OrdinalIgnoreCase))
            ?? catalog.Weather.FirstOrDefault();
        var weatherGraphics = weather is null
            ? "3_clear"
            : weather.WeatherFxType.HasValue
                ? $"{weather.Id}_type={weather.WeatherFxType.Value}"
                : weather.Id;

        foreach (var slot in grid)
        {
            var car = cars.Single(candidate => candidate.Id.Equals(slot.CarId, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(slot.SkinId))
            {
                slot.SkinId = car.Skins.FirstOrDefault()?.Id ?? string.Empty;
            }
        }

        return new(
            RenderServerConfiguration(preset, track, grid, cars, weatherGraphics),
            RenderEntryList(preset, grid),
            RenderExtraConfiguration(preset, racingCars),
            grid,
            track,
            cars,
            racingCars);
    }

    private static IniDocument RenderServerConfiguration(
        RaceControlPreset preset,
        AcTrackLayout track,
        IReadOnlyList<GridSlotPreset> grid,
        IReadOnlyList<AcCar> cars,
        string weatherGraphics)
    {
        var ini = new IniDocument();
        Set(ini, "SERVER", "NAME", preset.ServerName);
        Set(ini, "SERVER", "CARS", string.Join(';', cars.Select(car => car.Id)));
        Set(ini, "SERVER", "TRACK", track.TrackId);
        Set(ini, "SERVER", "CONFIG_TRACK", track.LayoutId);
        Set(ini, "SERVER", "SUN_ANGLE", preset.Conditions.SunAngleDegrees);
        Set(ini, "SERVER", "PASSWORD", preset.Network.JoinPassword);
        Set(ini, "SERVER", "ADMIN_PASSWORD", preset.Network.AdminPassword);
        Set(ini, "SERVER", "UDP_PORT", preset.Network.UdpPort);
        Set(ini, "SERVER", "TCP_PORT", preset.Network.TcpPort);
        Set(ini, "SERVER", "HTTP_PORT", preset.Network.HttpPort);
        Set(ini, "SERVER", "CLIENT_SEND_INTERVAL_HZ", preset.Mode == EventMode.Fps ? 60 : preset.Bots.UpdateHz);
        Set(ini, "SERVER", "LOOP_MODE", 1);
        Set(ini, "SERVER", "FUEL_RATE", preset.Rules.FuelRatePercent);
        Set(ini, "SERVER", "DAMAGE_MULTIPLIER", preset.Rules.DamageRatePercent);
        Set(ini, "SERVER", "TYRE_WEAR_RATE", preset.Rules.TyreWearRatePercent);
        Set(ini, "SERVER", "ALLOWED_TYRES_OUT", preset.Rules.AllowedTyresOut);
        Set(ini, "SERVER", "ABS_ALLOWED", preset.Rules.AbsAllowed);
        Set(ini, "SERVER", "TC_ALLOWED", preset.Rules.TractionControlAllowed);
        Set(ini, "SERVER", "STABILITY_ALLOWED", Bool(preset.Rules.StabilityControlAllowed));
        Set(ini, "SERVER", "AUTOCLUTCH_ALLOWED", Bool(preset.Rules.AutoClutchAllowed));
        Set(ini, "SERVER", "TYRE_BLANKETS_ALLOWED", Bool(preset.Rules.TyreBlanketsAllowed));
        Set(ini, "SERVER", "FORCE_VIRTUAL_MIRROR", Bool(preset.Rules.VirtualMirrorAllowed));
        var racingSlotCount = grid.Count(slot => slot.Mode != SlotMode.Spectator);
        Set(ini, "SERVER", "REVERSED_GRID_RACE_POSITIONS", preset.Sessions.ReverseGrid ? racingSlotCount : 0);
        Set(ini, "SERVER", "REGISTER_TO_LOBBY", 0);
        Set(ini, "SERVER", "MAX_CLIENTS", grid.Count);
        Set(ini, "SERVER", "RACE_OVER_TIME", preset.Sessions.RaceOverTimeSeconds);
        Set(ini, "SERVER", "RESULT_SCREEN_TIME", preset.Sessions.ResultScreenSeconds);
        Set(ini, "SERVER", "TIME_OF_DAY_MULT", 1);
        Set(ini, "SERVER", "WELCOME_MESSAGE", "welcome.txt");

        if (preset.Mode == EventMode.Fps)
        {
            Set(ini, "PRACTICE", "NAME", "FPS Deathmatch carrier session");
            Set(ini, "PRACTICE", "TIME", 1440);
            Set(ini, "PRACTICE", "IS_OPEN", 1);
            Set(ini, "PRACTICE", "INFINITE", 1);
        }
        else if (preset.Sessions.PracticeEnabled)
        {
            Set(ini, "PRACTICE", "NAME", "Practice");
            Set(ini, "PRACTICE", "TIME", preset.Sessions.PracticeMinutes);
            Set(ini, "PRACTICE", "IS_OPEN", 1);
            Set(ini, "PRACTICE", "INFINITE", 0);
        }

        if (preset.Mode != EventMode.Fps && preset.Sessions.QualifyingEnabled)
        {
            Set(ini, "QUALIFY", "NAME", "Qualifying");
            Set(ini, "QUALIFY", "TIME", preset.Sessions.QualifyingMinutes);
            Set(ini, "QUALIFY", "IS_OPEN", 1);
        }

        if (preset.Mode != EventMode.Fps)
        {
            Set(ini, "RACE", "NAME", $"{preset.Sessions.RaceLaps}-lap race");
            Set(ini, "RACE", "TIME", 0);
            Set(ini, "RACE", "LAPS", preset.Sessions.RaceLaps);
            Set(ini, "RACE", "WAIT_TIME", 20);
            Set(ini, "RACE", "IS_OPEN", preset.Bots.AllowMidRaceTakeover ? 1 : 0);
        }

        Set(ini, "DYNAMIC_TRACK", "SESSION_START", preset.Conditions.StartingGripPercent);
        Set(ini, "DYNAMIC_TRACK", "RANDOMNESS", preset.Conditions.GripRandomnessPercent);
        Set(ini, "DYNAMIC_TRACK", "SESSION_TRANSFER", preset.Conditions.GripTransferPercent);
        Set(ini, "DYNAMIC_TRACK", "LAP_GAIN", preset.Conditions.LapsPerGripIncrease);

        Set(ini, "WEATHER_0", "GRAPHICS", weatherGraphics);
        Set(ini, "WEATHER_0", "BASE_TEMPERATURE_AMBIENT", preset.Conditions.AmbientTemperatureCelsius);
        Set(ini, "WEATHER_0", "BASE_TEMPERATURE_ROAD", preset.Conditions.RoadTemperatureCelsius - preset.Conditions.AmbientTemperatureCelsius);
        Set(ini, "WEATHER_0", "VARIATION_AMBIENT", 0);
        Set(ini, "WEATHER_0", "VARIATION_ROAD", 0);
        Set(ini, "WEATHER_0", "WIND_BASE_SPEED_MIN", preset.Conditions.WindMinKmh);
        Set(ini, "WEATHER_0", "WIND_BASE_SPEED_MAX", preset.Conditions.WindMaxKmh);
        Set(ini, "WEATHER_0", "WIND_BASE_DIRECTION", preset.Conditions.WindDirectionDegrees);
        Set(ini, "WEATHER_0", "WIND_VARIATION_DIRECTION", 0);
        return ini;
    }

    private static IniDocument RenderEntryList(RaceControlPreset preset, IReadOnlyList<GridSlotPreset> grid)
    {
        var ini = new IniDocument();
        for (var index = 0; index < grid.Count; index++)
        {
            var slot = grid[index];
            var section = $"CAR_{index}";
            Set(ini, section, "MODEL", slot.CarId);
            Set(ini, section, "SKIN", slot.SkinId);
            Set(ini, section, "SPECTATOR_MODE", slot.Mode == SlotMode.Spectator ? 1 : 0);
            Set(ini, section, "DRIVERNAME", slot.DriverName);
            Set(ini, section, "TEAM", slot.TeamName);
            Set(ini, section, "GUID", string.Empty);
            Set(ini, section, "BALLAST", slot.BallastKg);
            Set(ini, section, "RESTRICTOR", slot.RestrictorPercent);
            Set(ini, section, "NATION", slot.NationCode);
            Set(ini, section, "AI", preset.Mode == EventMode.Fps ? "none" : preset.Bots.Enabled ? AiValue(slot.Mode) : "none");
            Set(ini, section, "AI_DIFFICULTY", OptionalRacecraftValue(slot.Difficulty));
            Set(ini, section, "AI_AGGRESSION", OptionalRacecraftValue(slot.Aggression));
            if (preset.Mode == EventMode.Fps)
                Set(ini, section, "FPS_ROLE", FpsRole(slot.Mode));
        }

        return ini;
    }

    private static string RenderExtraConfiguration(RaceControlPreset preset, IReadOnlyList<AcCar> cars)
    {
        var invariant = CultureInfo.InvariantCulture;
        var builder = new StringBuilder();
        Line($"NetworkBindAddress: {Yaml(preset.Network.BindAddress)}");
        Line("UseSteamAuth: false");
        Line("EnableUPnP: false");
        Line("IgnoreConfigurationErrors:");
        Line("  MissingTrackParams: true");
        Line($"MinimumCSPVersion: {(preset.Mode == EventMode.Fps ? 4053 : 2651)}");
        Line($"EnableAi: {Lower(preset.Mode != EventMode.Fps && preset.Bots.Enabled)}");
        Line("AiParams:");
        Line($"  Behavior: {(preset.Mode != EventMode.Fps && preset.Bots.Enabled ? "Race" : "Traffic")}");
        Line("  AutoAssignTrafficCars: false");
        Line("  HideAiCars: false");
        Line($"  UseParodyNames: {Lower(preset.Bots.UseParodyNames)}");
        Line($"  NamePrefix: {Yaml(preset.Bots.NamePrefix)}");
        Line($"  MaxSpeedKph: {cars.Max(car => VehicleProfile.From(car).TopSpeedKph).ToString("0.###", invariant)}");
        Line("  Race:");
        Line($"    Difficulty: {preset.Bots.Difficulty.ToString("0.00", invariant)}");
        Line($"    DifficultyVariancePercent: {preset.Bots.DifficultyVariancePercent.ToString("0.##", invariant)}");
        Line($"    Aggression: {preset.Bots.Aggression.ToString("0.00", invariant)}");
        Line($"    AggressionVariancePercent: {preset.Bots.AggressionVariancePercent.ToString("0.##", invariant)}");
        Line($"    StartSplinePointId: {preset.Bots.StartSplinePointId}");
        Line($"    GridSpacingMeters: {preset.Bots.GridSpacingMeters.ToString("0.###", invariant)}");
        Line($"    UpdateHz: {preset.Bots.UpdateHz}");
        Line("    Physics:");
        Line($"      Fidelity: {preset.Bots.PhysicsFidelity}");
        Line("      AssetFile: race-physics.bin");
        Line($"      Friction: {preset.Bots.SurfaceFriction.ToString("0.###", invariant)}");
        Line($"    AllowMidRaceBotTakeover: {Lower(preset.Mode != EventMode.Fps && preset.Bots.Enabled && preset.Bots.AllowMidRaceTakeover)}");
        Line($"    JoinSlotSelection: {preset.Bots.JoinSlotSelection}");
        Line($"    RestartSessionOnFirstHumanConnect: {Lower(preset.Mode != EventMode.Fps && preset.Bots.Enabled && preset.Bots.RestartWhenFirstHumanConnects)}");
        if (preset.Mode != EventMode.Fps && preset.Bots.Enabled)
        {
            Line("    VehicleProfiles:");
            foreach (var car in cars)
            {
                var profile = VehicleProfile.From(car);
                Line($"      - Model: {Yaml(car.Id)}");
                Line("        Source: ui_car.json");
                Line($"        MassKg: {profile.MassKg.ToString("0.###", invariant)}");
                Line($"        PowerKw: {profile.PowerKw.ToString("0.###", invariant)}");
                Line($"        TopSpeedKph: {profile.TopSpeedKph.ToString("0.###", invariant)}");
                Line($"        ZeroToHundredSeconds: {profile.ZeroToHundredSeconds.ToString("0.###", invariant)}");
                Line("        MaxBrakeDeceleration: 8.5");
                Line("        LateralGripG: 1.0");
                Line("        TyreDiameterMeters: 0.65");
                Line("        EngineIdleRpm: 900");
                Line("        EngineMaxRpm: 7000");
                Line("        GearCount: 6");
            }
        }

        if (preset.Mode == EventMode.Fps)
        {
            var fps = preset.Fps;
            var arena = fps.Arena ?? throw new InvalidOperationException("Prepare the selected layout as an FPS arena before staging it.");
            Line("Fps:");
            Line("  Enabled: true");
            Line("  MatchType: Deathmatch");
            Line($"  TimeLimitMinutes: {fps.TimeLimitMinutes}");
            Line($"  KillLimit: {fps.KillLimit}");
            Line($"  RespawnSeconds: {fps.RespawnSeconds.ToString("0.###", invariant)}");
            Line($"  SpawnProtectionSeconds: {fps.SpawnProtectionSeconds.ToString("0.###", invariant)}");
            Line("  Bots:");
            Line($"    Difficulty: {fps.Bots.Difficulty.ToString("0.###", invariant)}");
            Line($"    DifficultyVariancePercent: {fps.Bots.DifficultyVariancePercent.ToString("0.###", invariant)}");
            Line($"    Aggression: {fps.Bots.Aggression.ToString("0.###", invariant)}");
            Line($"    AggressionVariancePercent: {fps.Bots.AggressionVariancePercent.ToString("0.###", invariant)}");
            Line($"    Health: {fps.Bots.Health}");
            Line("  Arena:");
            Point("BoundsMin", arena.BoundsMin, 4);
            Point("BoundsMax", arena.BoundsMax, 4);
            Line("    SpawnPoints:");
            foreach (var spawn in arena.SpawnPoints)
            {
                Line("      - Position:");
                Line($"          X: {spawn.Position.X.ToString("0.######", invariant)}");
                Line($"          Y: {spawn.Position.Y.ToString("0.######", invariant)}");
                Line($"          Z: {spawn.Position.Z.ToString("0.######", invariant)}");
                Line($"        YawRadians: {spawn.YawRadians.ToString("0.######", invariant)}");
            }

            void Point(string name, FpsPoint point, int spaces)
            {
                var prefix = new string(' ', spaces);
                Line($"{prefix}{name}:");
                Line($"{prefix}  X: {point.X.ToString("0.######", invariant)}");
                Line($"{prefix}  Y: {point.Y.ToString("0.######", invariant)}");
                Line($"{prefix}  Z: {point.Z.ToString("0.######", invariant)}");
            }
        }

        return builder.ToString();

        void Line(string line) => builder.AppendLine(line);
    }

    private static GridSlotPreset Clone(GridSlotPreset source) => new()
    {
        CarId = source.CarId,
        SkinId = source.SkinId,
        DriverName = source.DriverName,
        TeamName = source.TeamName,
        NationCode = source.NationCode,
        BallastKg = source.BallastKg,
        RestrictorPercent = source.RestrictorPercent,
        Difficulty = source.Difficulty,
        Aggression = source.Aggression,
        Mode = source.Mode,
    };

    private static string AiValue(SlotMode mode) => mode switch
    {
        SlotMode.Auto => "auto",
        SlotMode.Fixed => "fixed",
        _ => "none",
    };

    private static string FpsRole(SlotMode mode) => mode switch
    {
        SlotMode.Auto => "Auto",
        SlotMode.Fixed => "Bot",
        SlotMode.None => "Human",
        SlotMode.Spectator => "Spectator",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    private static void Set(IniDocument document, string section, string key, object value) => document.Set(section, key, value);
    private static int Bool(bool value) => value ? 1 : 0;
    private static string Lower(bool value) => value ? "true" : "false";
    private static string OptionalRacecraftValue(double? value) => value.HasValue
        ? value.Value.ToString("0.###", CultureInfo.InvariantCulture)
        : "-1";
    private static string Yaml(string value) => $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private sealed record VehicleProfile(double MassKg, double PowerKw, double TopSpeedKph, double ZeroToHundredSeconds)
    {
        public static VehicleProfile From(AcCar car)
        {
            var mass = Math.Clamp(car.MassKg ?? 1200, 300, 5000);
            var powerKw = Math.Clamp((car.PowerHp ?? 147.5) * 0.745699872, 5, 2000);
            var topSpeed = Math.Clamp(car.TopSpeedKmh ?? 200, 40, 600);
            var acceleration = Math.Clamp(8 * (mass / 1200) * Math.Sqrt(110 / powerKw), 1.5, 60);
            return new(mass, powerKw, topSpeed, acceleration);
        }
    }
}
