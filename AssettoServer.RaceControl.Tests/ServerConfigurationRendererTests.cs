using AssettoServer.RaceControl.Core.Configuration;
using AssettoServer.RaceControl.Core.Models;
using NUnit.Framework;

namespace AssettoServer.RaceControl.Tests;

public sealed class ServerConfigurationRendererTests
{
    [Test]
    public void Render_PhysicalGridLimitTruncatesOverstatedUiCapacity()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation(20, true, "car_one");
        var preset = factory.CreatePreset(12);

        var rendered = new ServerConfigurationRenderer().Render(preset, factory.Scan(), 7);

        Assert.Multiple(() =>
        {
            Assert.That(rendered.EffectiveGrid, Has.Count.EqualTo(7));
            Assert.That(rendered.ServerConfiguration.Get("SERVER", "MAX_CLIENTS"), Is.EqualTo("7"));
            Assert.That(rendered.EntryList.Sections.Count(section => section.Name.StartsWith("CAR_", StringComparison.Ordinal)), Is.EqualTo(7));
        });
    }

    [Test]
    public void Render_TrimsLastEntriesToPitCapacityAndKeepsMixedModels()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation(2, true, "car_one", "car_two");
        var preset = factory.CreatePreset(3);

        var rendered = new ServerConfigurationRenderer().Render(preset, factory.Scan());

        Assert.Multiple(() =>
        {
            Assert.That(rendered.EffectiveGrid, Has.Count.EqualTo(2));
            Assert.That(rendered.Cars.Select(car => car.Id), Is.EquivalentTo(new[] { "car_one", "car_two" }));
            Assert.That(rendered.ServerConfiguration.Get("SERVER", "MAX_CLIENTS"), Is.EqualTo("2"));
            Assert.That(rendered.EntryList.Get("CAR_0", "MODEL"), Is.EqualTo("car_one"));
            Assert.That(rendered.EntryList.Get("CAR_1", "MODEL"), Is.EqualTo("car_two"));
            Assert.That(rendered.EntryList.Get("CAR_0", "AI"), Is.EqualTo("auto"));
        });
    }

    [Test]
    public void Render_DisablingBotsMakesEveryEntryHumanOnlyWithoutChangingPresetModes()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation();
        var preset = factory.CreatePreset(2, false);
        preset.Grid[0].Mode = SlotMode.Fixed;

        var rendered = new ServerConfigurationRenderer().Render(preset, factory.Scan());

        Assert.Multiple(() =>
        {
            Assert.That(rendered.EntryList.Get("CAR_0", "AI"), Is.EqualTo("none"));
            Assert.That(rendered.EntryList.Get("CAR_1", "AI"), Is.EqualTo("none"));
            Assert.That(preset.Grid[0].Mode, Is.EqualTo(SlotMode.Fixed));
            Assert.That(rendered.ExtraConfiguration, Does.Contain("EnableAi: false"));
        });
    }

    [Test]
    public void Render_MissingWeatherSelectionFallsBackToInstalledClearWeather()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation();
        var preset = factory.CreatePreset();
        preset.Conditions.WeatherId = null!;

        var rendered = new ServerConfigurationRenderer().Render(preset, factory.Scan());

        Assert.That(rendered.ServerConfiguration.Get("WEATHER_0", "GRAPHICS"), Is.EqualTo("3_clear"));
    }

    [Test]
    public void Render_TimeOfDayHourWritesEquivalentAssettoCorsaSunAngle()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation();
        var preset = factory.CreatePreset();
        preset.Conditions.TimeOfDayHour = 18;

        var rendered = new ServerConfigurationRenderer().Render(preset, factory.Scan());

        Assert.Multiple(() =>
        {
            Assert.That(preset.Conditions.SunAngleDegrees, Is.EqualTo(80));
            Assert.That(preset.Conditions.TimeOfDayHour, Is.EqualTo(18));
            Assert.That(rendered.ServerConfiguration.Get("SERVER", "SUN_ANGLE"), Is.EqualTo("80"));
        });
    }

    [Test]
    public void Render_PlayerJoinSlotSelectionIsWrittenToRaceConfiguration()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation();
        var preset = factory.CreatePreset();
        preset.Bots.JoinSlotSelection = PlayerJoinSlotSelection.Last;

        var rendered = new ServerConfigurationRenderer().Render(preset, factory.Scan());

        Assert.That(rendered.ExtraConfiguration,
            Does.Contain("    JoinSlotSelection: Last"));
    }

    [Test]
    public void Render_ParodyDriverNameToggleIsWrittenToAiConfiguration()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation();
        var preset = factory.CreatePreset();
        preset.Bots.UseParodyNames = true;
        preset.Bots.NamePrefix = "Custom";

        var rendered = new ServerConfigurationRenderer().Render(preset, factory.Scan());

        Assert.Multiple(() =>
        {
            Assert.That(rendered.ExtraConfiguration,
                Does.Contain("  UseParodyNames: true"));
            Assert.That(rendered.ExtraConfiguration,
                Does.Contain("  NamePrefix: \"Custom\""));
        });
    }

    [Test]
    public void Render_RacecraftVarianceAndPerSlotOverridesAreWritten()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation();
        var preset = factory.CreatePreset();
        preset.Bots.DifficultyVariancePercent = 22;
        preset.Bots.AggressionVariancePercent = 35;
        preset.Grid[0].Difficulty = 0.91;
        preset.Grid[0].Aggression = 0.73;

        var rendered = new ServerConfigurationRenderer().Render(preset, factory.Scan());

        Assert.Multiple(() =>
        {
            Assert.That(rendered.ExtraConfiguration,
                Does.Contain("    DifficultyVariancePercent: 22"));
            Assert.That(rendered.ExtraConfiguration,
                Does.Contain("    AggressionVariancePercent: 35"));
            Assert.That(rendered.EntryList.Get("CAR_0", "AI_DIFFICULTY"), Is.EqualTo("0.91"));
            Assert.That(rendered.EntryList.Get("CAR_0", "AI_AGGRESSION"), Is.EqualTo("0.73"));
            Assert.That(rendered.EntryList.Get("CAR_1", "AI_DIFFICULTY"), Is.EqualTo("-1"));
            Assert.That(rendered.EntryList.Get("CAR_1", "AI_AGGRESSION"), Is.EqualTo("-1"));
        });
    }

    [Test]
    public void Render_SpectatorsAreAppendedWithoutConsumingPhysicalGridSlots()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation(2, true, "race_car", "camera_car");
        var preset = factory.CreatePreset(4);
        preset.Sessions.ReverseGrid = true;
        preset.Grid[0].CarId = "camera_car";
        preset.Grid[0].Mode = SlotMode.Spectator;
        preset.Grid[1].CarId = "race_car";
        preset.Grid[1].Mode = SlotMode.Auto;
        preset.Grid[2].CarId = "race_car";
        preset.Grid[2].Mode = SlotMode.None;
        preset.Grid[3].CarId = "camera_car";
        preset.Grid[3].Mode = SlotMode.Spectator;

        var rendered = new ServerConfigurationRenderer().Render(preset, factory.Scan());

        Assert.Multiple(() =>
        {
            Assert.That(rendered.EffectiveGrid.Select(slot => slot.Mode),
                Is.EqualTo(new[] { SlotMode.Auto, SlotMode.None, SlotMode.Spectator, SlotMode.Spectator }));
            Assert.That(rendered.ServerConfiguration.Get("SERVER", "MAX_CLIENTS"), Is.EqualTo("4"));
            Assert.That(rendered.ServerConfiguration.Get("SERVER", "REVERSED_GRID_RACE_POSITIONS"), Is.EqualTo("2"));
            Assert.That(rendered.EntryList.Get("CAR_0", "SPECTATOR_MODE"), Is.EqualTo("0"));
            Assert.That(rendered.EntryList.Get("CAR_2", "SPECTATOR_MODE"), Is.EqualTo("1"));
            Assert.That(rendered.EntryList.Get("CAR_2", "AI"), Is.EqualTo("none"));
            Assert.That(rendered.Cars.Select(car => car.Id), Is.EquivalentTo(new[] { "race_car", "camera_car" }));
            Assert.That(rendered.RacingCars.Select(car => car.Id), Is.EqualTo(new[] { "race_car" }));
        });
    }

    [Test]
    public void Render_FpsUsesCarrierRolesLongPracticeAndAuthoritativeConfiguration()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation(8, true, "carrier", "unused_car");
        var preset = factory.CreatePreset(4);
        preset.Mode = EventMode.Fps;
        preset.Fps.CarrierCarId = "carrier";
        preset.Fps.TimeLimitMinutes = 10;
        preset.Fps.KillLimit = 20;
        preset.Fps.Arena = Arena();
        preset.Grid[0].Mode = SlotMode.Auto;
        preset.Grid[1].Mode = SlotMode.Fixed;
        preset.Grid[2].Mode = SlotMode.None;
        preset.Grid[3].Mode = SlotMode.Spectator;
        foreach (var slot in preset.Grid) slot.CarId = "unused_car";

        var rendered = new ServerConfigurationRenderer().Render(preset, factory.Scan());

        Assert.Multiple(() =>
        {
            Assert.That(rendered.ServerConfiguration.Get("SERVER", "CLIENT_SEND_INTERVAL_HZ"), Is.EqualTo("60"));
            Assert.That(rendered.ServerConfiguration.Get("PRACTICE", "INFINITE"), Is.EqualTo("1"));
            Assert.That(rendered.ServerConfiguration.Sections.Any(section => section.Name == "RACE"), Is.False);
            Assert.That(rendered.EffectiveGrid.Select(slot => slot.CarId), Is.All.EqualTo("carrier"));
            Assert.That(rendered.EntryList.Get("CAR_0", "FPS_ROLE"), Is.EqualTo("Auto"));
            Assert.That(rendered.EntryList.Get("CAR_1", "FPS_ROLE"), Is.EqualTo("Bot"));
            Assert.That(rendered.EntryList.Get("CAR_2", "FPS_ROLE"), Is.EqualTo("Human"));
            Assert.That(rendered.EntryList.Get("CAR_3", "FPS_ROLE"), Is.EqualTo("Spectator"));
            Assert.That(rendered.EntryList.Get("CAR_0", "AI"), Is.EqualTo("none"));
            Assert.That(rendered.ExtraConfiguration, Does.Contain("MinimumCSPVersion: 4053"));
            Assert.That(rendered.ExtraConfiguration, Does.Contain("Fps:"));
            Assert.That(rendered.ExtraConfiguration, Does.Contain("  KillLimit: 20"));
            Assert.That(rendered.ExtraConfiguration, Does.Contain("    GeometryPath: fps-arena-geometry.bin"));
            Assert.That(rendered.ExtraConfiguration, Does.Contain("    NavigationPath: fps-arena-navigation.bin"));
        });

        static FpsArenaDefinition Arena() => new()
        {
            TrackId = "test_track",
            BoundsMin = new() { X = -10, Y = -2, Z = -10 },
            BoundsMax = new() { X = 10, Y = 5, Z = 10 },
            SpawnPoints =
            [
                new() { Position = new() { X = -5, Y = 0, Z = 0 } },
                new() { Position = new() { X = 5, Y = 0, Z = 0 } },
            ],
            Navigation = new()
            {
                NodeCount = 64,
                ComponentCount = 1,
                ConnectedSpawnCount = 2,
            },
        };
    }
}
