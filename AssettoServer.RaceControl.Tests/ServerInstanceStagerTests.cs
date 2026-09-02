using System.IO.Compression;
using AssettoServer.RaceControl.Core.Configuration;
using AssettoServer.RaceControl.Core.Infrastructure;
using AssettoServer.RaceControl.Core.Models;
using AssettoServer.RaceControl.Core.Staging;
using AssettoServer.RaceControl.Core.Storage;
using AssettoServer.RaceControl.Core.Validation;
using NUnit.Framework;

namespace AssettoServer.RaceControl.Tests;

public sealed class ServerInstanceStagerTests
{
    [Test]
    public void RacePhysicsMetadataReader_ReadsPhysicalGridCapacity()
    {
        string path = Path.Combine(Path.GetTempPath(), $"race-physics-{Guid.NewGuid():N}.bin");
        try
        {
            using (var file = File.Create(path))
            using (var compressed = new BrotliStream(file, CompressionLevel.Fastest))
            using (var writer = new BinaryWriter(compressed))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("ASRPHY01"));
                writer.Write(7);
                writer.Write(44);
            }

            Assert.That(RacePhysicsAssetMetadataReader.ReadGridSlotCount(path), Is.EqualTo(44));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Test]
    public async Task StageAsync_CreatesIsolatedHumanOnlyInstanceAndDoesNotExposePasswordsInManifest()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation(2, false, "car_one");
        var preset = factory.CreatePreset(3, false);
        preset.Network.JoinPassword = "join-secret";
        preset.Network.AdminPassword = "admin-secret";
        var stager = new ServerInstanceStager(
            new RaceControlPaths(factory.DataRoot),
            new RaceControlValidator(),
            new ServerConfigurationRenderer());

        var instance = await stager.StageAsync(preset, factory.Scan());
        var recent = new InstanceCatalog(new RaceControlPaths(factory.DataRoot)).List();

        var presetRoot = Path.Combine(instance.RootPath, "presets", ServerInstanceStager.PresetName);
        var manifest = await File.ReadAllTextAsync(Path.Combine(instance.RootPath, "race-control-instance.json"));
        Assert.Multiple(() =>
        {
            Assert.That(instance.SlotCount, Is.EqualTo(2));
            Assert.That(instance.BotSlotCount, Is.Zero);
            Assert.That(File.Exists(Path.Combine(presetRoot, "server_cfg.ini")), Is.True);
            Assert.That(File.Exists(Path.Combine(instance.RootPath, "support.dll")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(instance.RootPath, "plugins")), Is.True);
            Assert.That(IniDocument.Load(Path.Combine(presetRoot, "entry_list.ini")).Get("CAR_0", "AI"), Is.EqualTo("none"));
            Assert.That(manifest, Does.Not.Contain("join-secret"));
            Assert.That(manifest, Does.Not.Contain("admin-secret"));
            Assert.That(Directory.Exists(Path.Combine(factory.PayloadRoot, "presets")), Is.False);
            Assert.That(recent.Single().RootPath, Is.EqualTo(instance.RootPath));
        });
    }

    [Test]
    public async Task StageAsync_ReusesWorkingInstanceAndArchivesOnlyCompactRunArtifacts()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation(2, false, "car_one");
        var paths = new RaceControlPaths(factory.DataRoot);
        var stager = new ServerInstanceStager(paths, new RaceControlValidator(),
            new ServerConfigurationRenderer());
        var preset = factory.CreatePreset(2, false);

        var first = await stager.StageAsync(preset, factory.Scan());
        Directory.CreateDirectory(Path.Combine(first.RootPath, "simulation"));
        Directory.CreateDirectory(Path.Combine(first.RootPath, "logs"));
        await File.WriteAllTextAsync(Path.Combine(first.RootPath, "simulation", "summary.json"),
            "{\"status\":\"completed\"}");
        await File.WriteAllTextAsync(Path.Combine(first.RootPath, "simulation", "events.jsonl"),
            "{\"event\":\"finish\"}\n");
        await File.WriteAllTextAsync(Path.Combine(first.RootPath, "simulation", "samples.jsonl"),
            "sample-one\nsample-two\n");
        await File.WriteAllTextAsync(Path.Combine(first.RootPath, "logs", "server.txt"),
            "important log");

        var second = await stager.StageAsync(preset, factory.Scan());
        string archive = Directory.GetDirectories(paths.HistoryDirectory).Single();
        string compressedSamples = Path.Combine(archive, "simulation", "samples.jsonl.gz");
        await using var compressed = File.OpenRead(compressedSamples);
        await using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        string restoredSamples = await reader.ReadToEndAsync();
        var catalog = new InstanceCatalog(paths).List();

        Assert.Multiple(() =>
        {
            Assert.That(first.RootPath, Is.EqualTo(paths.WorkingInstanceDirectory));
            Assert.That(second.RootPath, Is.EqualTo(first.RootPath));
            Assert.That(File.Exists(Path.Combine(archive, "race-control-instance.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(archive, "archive-info.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(archive, "presets", ServerInstanceStager.PresetName,
                "server_cfg.ini")), Is.True);
            Assert.That(File.Exists(Path.Combine(archive, "simulation", "summary.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(archive, "simulation", "events.jsonl")), Is.True);
            Assert.That(File.Exists(Path.Combine(archive, "logs", "server.txt")), Is.True);
            Assert.That(File.Exists(Path.Combine(archive, "AssettoServer.exe")), Is.False,
                "compact history must not duplicate the standalone server payload");
            Assert.That(File.Exists(Path.Combine(archive, "support.dll")), Is.False);
            Assert.That(restoredSamples, Is.EqualTo("sample-one\nsample-two\n"));
            Assert.That(catalog.Count, Is.EqualTo(2));
            Assert.That(catalog.Count(item => item.IsCompactHistory), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task StageAsync_ReusesFpsAssetsCachedDuringArenaPreparation()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation(8, false, "car_one");
        var paths = new RaceControlPaths(factory.DataRoot);
        var catalog = factory.Scan();
        var preset = factory.CreatePreset(4);
        preset.Mode = EventMode.Fps;
        preset.Fps.CarrierCarId = "car_one";
        var arena = new FpsArenaDefinition
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
        preset.Fps.Arena = arena;
        string sourceGeometry = Path.Combine(factory.Root, "prepared-geometry.bin");
        string sourceNavigation = Path.Combine(factory.Root, "prepared-navigation.bin");
        byte[] geometry = [1, 2, 3, 4];
        byte[] navigation = [5, 6, 7, 8];
        await File.WriteAllBytesAsync(sourceGeometry, geometry);
        await File.WriteAllBytesAsync(sourceNavigation, navigation);
        var arenaStore = new FpsArenaStore(paths);
        new FpsArenaPreparationService(arenaStore, paths).PersistPreparedArena(preset,
            catalog.Tracks.Single(), arena, sourceGeometry, sourceNavigation);

        var stager = new ServerInstanceStager(paths, new RaceControlValidator(),
            new ServerConfigurationRenderer());
        var instance = await stager.StageAsync(preset, catalog);
        string presetRoot = Path.Combine(instance.RootPath, "presets",
            ServerInstanceStager.PresetName);

        Assert.Multiple(() =>
        {
            Assert.That(instance.PhysicsCacheHit, Is.True);
            Assert.That(arenaStore.IsPrepared("test_track", string.Empty), Is.True);
            Assert.That(File.ReadAllBytes(Path.Combine(presetRoot, "fps-arena-geometry.bin")),
                Is.EqualTo(geometry));
            Assert.That(File.ReadAllBytes(Path.Combine(presetRoot, "fps-arena-navigation.bin")),
                Is.EqualTo(navigation));
        });
    }

    [Test]
    public void FpsAssetCache_IgnoresUnrelatedServerRebuildButTracksModelChanges()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation(8, false, "car_one");
        var paths = new RaceControlPaths(factory.DataRoot);
        var catalog = factory.Scan();
        var preset = factory.CreatePreset(4);
        var track = catalog.Tracks.Single();

        var initial = PreparedPhysicsAssetCache.GetFpsPaths(paths, preset, track);
        File.AppendAllText(Path.Combine(factory.PayloadRoot, "AssettoServer.exe"), " rebuilt");
        var afterServerRebuild = PreparedPhysicsAssetCache.GetFpsPaths(paths, preset, track);
        preset.Fps.ArenaBoundsPaddingMeters = 20;
        var afterPaddingChange = PreparedPhysicsAssetCache.GetFpsPaths(paths, preset, track);
        File.AppendAllText(Path.Combine(track.RootPath, "track.kn5"), " changed");
        var afterTrackChange = PreparedPhysicsAssetCache.GetFpsPaths(paths, preset, track);

        Assert.Multiple(() =>
        {
            Assert.That(afterServerRebuild, Is.EqualTo(initial));
            Assert.That(afterPaddingChange, Is.Not.EqualTo(initial));
            Assert.That(afterTrackChange, Is.Not.EqualTo(initial));
        });
    }

    [Test]
    public async Task InstancePackageExporter_CreatesCompletePortableZipOnDemand()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation(2, false, "car_one");
        var paths = new RaceControlPaths(factory.DataRoot);
        var stager = new ServerInstanceStager(paths, new RaceControlValidator(),
            new ServerConfigurationRenderer());
        var instance = await stager.StageAsync(factory.CreatePreset(2, false), factory.Scan());
        string destination = Path.Combine(factory.Root, "exported-server.zip");

        await new InstancePackageExporter().ExportAsync(instance.RootPath, destination);

        using var archive = ZipFile.OpenRead(destination);
        var entries = archive.Entries.Select(entry => entry.FullName).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(entries, Does.Contain("AssettoServer.exe"));
            Assert.That(entries, Does.Contain("support.dll"));
            Assert.That(entries, Does.Contain("race-control-instance.json"));
            Assert.That(entries, Does.Contain("presets/race-control/server_cfg.ini"));
            Assert.That(entries, Does.Contain("presets/race-control/entry_list.ini"));
            Assert.That(entries, Does.Contain("presets/race-control/extra_cfg.yml"));
        });
    }
}
