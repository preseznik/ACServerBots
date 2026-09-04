using AssettoServer.RaceControl.Core.Infrastructure;
using AssettoServer.RaceControl.Core.Models;
using AssettoServer.RaceControl.Core.Storage;
using NUnit.Framework;

namespace AssettoServer.RaceControl.Tests;

public sealed class FpsStorageTests
{
    [Test]
    public void PresetStore_SeparatesRacingAndFpsCollections()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation();
        var store = new PresetStore(new RaceControlPaths(factory.DataRoot));
        var racing = factory.CreatePreset();
        racing.Name = "Race";
        var fps = factory.CreatePreset();
        fps.Id = Guid.NewGuid();
        fps.Name = "Deathmatch";
        fps.Mode = EventMode.Fps;
        store.Save(racing);
        store.Save(fps);

        Assert.Multiple(() =>
        {
            Assert.That(store.List(EventMode.Racing).Select(item => item.Name), Is.EqualTo(new[] { "Race" }));
            Assert.That(store.List(EventMode.Fps).Select(item => item.Name), Is.EqualTo(new[] { "Deathmatch" }));
            Assert.That(store.List(), Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void PresetStore_OlderFpsPresetDefaultsToBlocksTheme()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation();
        var store = new PresetStore(new RaceControlPaths(factory.DataRoot));
        var preset = factory.CreatePreset();
        preset.Mode = EventMode.Fps;
        preset.Name = "Legacy FPS";
        string path = store.Save(preset);
        string json = File.ReadAllText(path).Replace(
            "    \"Theme\": \"Blocks\",\r\n", string.Empty, StringComparison.Ordinal)
            .Replace("    \"Theme\": \"Blocks\",\n", string.Empty,
                StringComparison.Ordinal);
        File.WriteAllText(path, json);

        RaceControlPreset loaded = store.Load(path);

        Assert.That(loaded.Fps.Theme, Is.EqualTo(FpsVisualTheme.Blocks));
    }

    [Test]
    public void ArenaStore_RoundTripsAppDataSidecar()
    {
        using var factory = new TestContentFactory();
        var paths = new RaceControlPaths(factory.DataRoot);
        var store = new FpsArenaStore(paths);
        var expected = new FpsArenaDefinition
        {
            TrackId = "magione",
            LayoutId = string.Empty,
            BoundsMin = new() { X = -20, Y = -2, Z = -30 },
            BoundsMax = new() { X = 20, Y = 8, Z = 30 },
            PlayableBoundary =
            [
                new() { X = -10, Z = -10 }, new() { X = 10, Z = -10 },
                new() { X = 10, Z = 10 }, new() { X = -10, Z = 10 },
            ],
            SpawnPoints =
            [
                new() { Position = new() { X = -5, Y = 0, Z = 0 }, YawRadians = 1.2 },
                new() { Position = new() { X = 5, Y = 0, Z = 0 }, YawRadians = -1.2 },
            ],
            Navigation = new()
            {
                NodeCount = 128,
                ComponentCount = 1,
                ConnectedSpawnCount = 2,
                WalkLinkCount = 512,
                TraversalLinkCount = 4,
            },
            CollisionIncludeMeshes = ["FPS_SOLID_*"],
            CollisionExcludeMeshes = ["*_FOLIAGE"],
        };

        string path = store.Save(expected);
        var actual = store.Load("magione", string.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(path, Does.StartWith(paths.FpsArenasDirectory));
            Assert.That(store.IsPrepared("magione", string.Empty), Is.True);
            Assert.That(actual?.SpawnPoints, Has.Count.EqualTo(2));
            Assert.That(actual?.BoundsMax.Z, Is.EqualTo(30));
            Assert.That(actual?.PlayableBoundary, Has.Count.EqualTo(4));
            Assert.That(actual?.OutOfBoundsSeconds, Is.EqualTo(3));
            Assert.That(actual?.PreparationVersion,
                Is.EqualTo(FpsArenaDefinition.CurrentPreparationVersion));
            Assert.That(actual?.CollisionIncludeMeshes, Is.EqualTo(new[] { "FPS_SOLID_*" }));
            Assert.That(actual?.CollisionExcludeMeshes, Is.EqualTo(new[] { "*_FOLIAGE" }));
            Assert.That(actual?.Navigation.NodeCount, Is.EqualTo(128));
        });
    }

    [Test]
    public void ArenaStore_RejectsSidecarsFromOlderCollisionPreparation()
    {
        using var factory = new TestContentFactory();
        var store = new FpsArenaStore(new RaceControlPaths(factory.DataRoot));
        store.Save(new FpsArenaDefinition
        {
            PreparationVersion = FpsArenaDefinition.CurrentPreparationVersion - 1,
            TrackId = "legacy",
            SpawnPoints = [new(), new()],
        });

        Assert.That(store.IsPrepared("legacy", string.Empty), Is.False);
    }
}
