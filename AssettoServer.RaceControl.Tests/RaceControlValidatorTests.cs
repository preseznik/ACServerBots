using AssettoServer.RaceControl.Core.Models;
using AssettoServer.RaceControl.Core.Validation;
using NUnit.Framework;

namespace AssettoServer.RaceControl.Tests;

public sealed class RaceControlValidatorTests
{
    [Test]
    public void Validate_GridAbovePitCapacityIsWarningNotBlocker()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation(2, true, "car_one");
        var preset = factory.CreatePreset(4);

        var result = new RaceControlValidator().Validate(preset, factory.Scan());

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Messages, Has.Some.Matches<ValidationMessage>(message =>
                message.Severity == ValidationSeverity.Warning && message.Message.Contains("first 2", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void Validate_BotsDisabledDoNotRequireColliderOrFastLane()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation(4, false, "car_one");
        File.Delete(Path.Combine(factory.AcRoot, "content", "cars", "car_one", "collider.kn5"));
        var preset = factory.CreatePreset(2, false);

        var result = new RaceControlValidator().Validate(preset, factory.Scan());

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void Validate_RejectsPublicListener()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation();
        var preset = factory.CreatePreset();
        preset.Network.BindAddress = "8.8.8.8";

        var result = new RaceControlValidator().Validate(preset, factory.Scan());

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Messages, Has.Some.Property("Field").EqualTo("Network"));
    }

    [Test]
    public void Validate_RejectsTimeOfDayOutsideClockRange()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation();
        var preset = factory.CreatePreset();
        preset.Conditions.TimeOfDayHour = 24;

        var result = new RaceControlValidator().Validate(preset, factory.Scan());

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Messages, Has.Some.Matches<ValidationMessage>(message =>
            message.Field == "Conditions" && message.Message.Contains("23:00", StringComparison.Ordinal)));
    }

    [Test]
    public void Validate_RejectsOpenRaceBotSplineBeforeStaging()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation();
        string fastLane = Path.Combine(factory.AcRoot, "content", "tracks", "test_track", "ai", "fast_lane.ai");
        using (var stream = File.Create(fastLane))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(-1);
            writer.Write(20);
            for (int index = 0; index < 20; index++)
            {
                writer.Write(index * 10f);
                writer.Write(0f);
                writer.Write(0f);
                writer.Write(100f);
                writer.Write(0f);
            }
        }

        var result = new RaceControlValidator().Validate(factory.CreatePreset(), factory.Scan());

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Messages, Has.Some.Matches<ValidationMessage>(message =>
            message.Field == "Track" && message.Message.Contains("closed fast_lane.ai", StringComparison.Ordinal)));
    }

    [Test]
    public void Validate_SpectatorsDoNotConsumePitBoxesOrRequireBotColliders()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation(2, true, "race_car", "camera_car");
        File.Delete(Path.Combine(factory.AcRoot, "content", "cars", "camera_car", "collider.kn5"));
        var preset = factory.CreatePreset(4);
        preset.Grid[0].CarId = "race_car";
        preset.Grid[1].CarId = "race_car";
        preset.Grid[2].CarId = "camera_car";
        preset.Grid[2].Mode = SlotMode.Spectator;
        preset.Grid[3].CarId = "camera_car";
        preset.Grid[3].Mode = SlotMode.Spectator;

        var result = new RaceControlValidator().Validate(preset, factory.Scan());

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Messages, Has.None.Matches<ValidationMessage>(message =>
                message.Severity == ValidationSeverity.Warning
                && message.Message.Contains("racing entries", StringComparison.Ordinal)));
            Assert.That(result.Messages, Has.Some.Matches<ValidationMessage>(message =>
                message.Severity == ValidationSeverity.Information
                && message.Message.Contains("2 spectator", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void Validate_RejectsInvalidRacecraftVarianceAndSlotOverride()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation();
        var preset = factory.CreatePreset();
        preset.Bots.DifficultyVariancePercent = 101;
        preset.Grid[0].Aggression = -0.1;

        var result = new RaceControlValidator().Validate(preset, factory.Scan());

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Messages, Has.Some.Matches<ValidationMessage>(message =>
                message.Field == "Bots" && message.Message.Contains("Skill variance", StringComparison.Ordinal)));
            Assert.That(result.Messages, Has.Some.Matches<ValidationMessage>(message =>
                message.Field == "Grid[0]" && message.Message.Contains("aggression", StringComparison.OrdinalIgnoreCase)));
        });
    }

    [Test]
    public void Validate_FpsRequiresMatchingPreparedArenaAndValidHealth()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation(8, false, "car_one");
        var preset = factory.CreatePreset(4);
        preset.Mode = EventMode.Fps;
        preset.Fps.CarrierCarId = "car_one";
        preset.Fps.Bots.Health = 250;
        preset.Fps.Theme = (FpsVisualTheme)99;
        preset.Fps.Arena = new FpsArenaDefinition
        {
            TrackId = "another_track",
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
            Collision = new()
            {
                TriangleCount = 100,
                BvhNodeCount = 10,
                BvhLeafCount = 5,
                MaximumLeafTriangles = 20,
            },
        };

        var result = new RaceControlValidator().Validate(preset, factory.Scan());

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Messages, Has.Some.Matches<ValidationMessage>(message =>
                message.Field == "Fps" && message.Message.Contains("health", StringComparison.OrdinalIgnoreCase)));
            Assert.That(result.Messages, Has.Some.Matches<ValidationMessage>(message =>
                message.Field == "Fps.Theme" && message.Message.Contains("Blocks", StringComparison.Ordinal)));
            Assert.That(result.Messages, Has.Some.Matches<ValidationMessage>(message =>
                message.Field == "Fps.Arena" && message.Message.Contains("does not match", StringComparison.OrdinalIgnoreCase)));
            Assert.That(result.Messages, Has.Some.Matches<ValidationMessage>(message =>
                message.Field == "Fps.Arena" && message.Message.Contains("collision BVH", StringComparison.OrdinalIgnoreCase)));
            Assert.That(result.Messages, Has.None.Matches<ValidationMessage>(message =>
                message.Message.Contains("fast_lane", StringComparison.OrdinalIgnoreCase)));
        });
    }

    [Test]
    public void Validate_FpsRejectsEmptyAllowListAndDisallowedDefault()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation(8, false, "car_one");
        var preset = factory.CreatePreset(4);
        preset.Mode = EventMode.Fps;
        preset.Fps.CarrierCarId = "car_one";
        preset.Fps.Loadouts.AllowedMainWeapons.Clear();
        preset.Fps.Loadouts.AllowedSecondaryWeapons.Remove(FpsSecondaryWeapon.DesertEagle);
        preset.Fps.Loadouts.HumanDefault.SecondaryWeapon = FpsSecondaryWeapon.DesertEagle;

        var result = new RaceControlValidator().Validate(preset, factory.Scan());

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Messages, Has.Some.Matches<ValidationMessage>(message =>
                message.Field == "Fps.Loadouts"
                && message.Message.Contains("main weapon", StringComparison.OrdinalIgnoreCase)));
            Assert.That(result.Messages, Has.Some.Matches<ValidationMessage>(message =>
                message.Field == "Fps.Loadouts.HumanDefault"
                && message.Message.Contains("secondary", StringComparison.OrdinalIgnoreCase)));
        });
    }

    [Test]
    public void Validate_FpsAcceptsPreparedArenaWithoutRaceAiAssets()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation(8, false, "car_one");
        File.Delete(Path.Combine(factory.AcRoot, "content", "cars", "car_one", "collider.kn5"));
        var preset = factory.CreatePreset(4);
        preset.Mode = EventMode.Fps;
        preset.Fps.CarrierCarId = "car_one";
        preset.Fps.Arena = new FpsArenaDefinition
        {
            TrackId = "test_track",
            BoundsMin = new() { X = -10, Y = -2, Z = -10 },
            BoundsMax = new() { X = 10, Y = 5, Z = 10 },
            SpawnPoints =
            [
                new() { Position = new() { X = -5, Y = 0, Z = 0 } },
                new() { Position = new() { X = 5, Y = 0, Z = 0 } },
                new() { Position = new() { X = 0, Y = 4, Z = 0 } },
            ],
            Navigation = new()
            {
                NodeCount = 64,
                ComponentCount = 1,
                ConnectedSpawnCount = 2,
            },
            Collision = new()
            {
                TriangleCount = 100,
                BvhNodeCount = 31,
                BvhLeafCount = 16,
                MaximumLeafTriangles = 8,
            },
        };

        var result = new RaceControlValidator().Validate(preset, factory.Scan());

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.True,
                string.Join(Environment.NewLine, result.Messages.Select(message => message.Message)));
            Assert.That(result.Messages, Has.Some.Matches<ValidationMessage>(message =>
                message.Severity == ValidationSeverity.Warning
                && message.Message.Contains("isolated", StringComparison.OrdinalIgnoreCase)));
        });
    }
}
