using System.Security.Cryptography;
using System.Text.Json;
using AssettoServer.RaceControl.Core.Content;
using AssettoServer.RaceControl.Core.Infrastructure;
using AssettoServer.RaceControl.Core.Models;
using AssettoServer.RaceControl.Core.Staging;
using AssettoServer.RaceControl.Core.Storage;
using NUnit.Framework;

namespace AssettoServer.RaceControl.Tests;

public sealed class ReleaseContentTests
{
    [Test]
    public void ExportResetsIdentityAndMachineStateButPreservesGameplay()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation();
        var source = factory.CreatePreset();
        source.Mode = EventMode.Fps;
        source.Name = source.ServerName = source.Bots.NamePrefix = "private-owner";
        source.Network.JoinPassword = source.Network.AdminPassword = "private-secret";
        source.Grid[0].DriverName = source.Grid[0].TeamName = source.Grid[0].NationCode = "private-owner";
        source.Fps.MatchType = FpsMatchType.TeamDeathmatch;
        source.Fps.Bots.Difficulty = 0.13;
        source.Fps.KillLimit = 100;
        source.Conditions.SunAngleDegrees = 32;
        var safe = ReleaseDefaults.SanitizePreset(source);
        string json = JsonSerializer.Serialize(safe, ReleaseDefaults.JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain("private-"));
            Assert.That(json, Does.Not.Contain(source.Id.ToString()));
            Assert.That(safe.AssettoCorsaRoot, Is.Empty);
            Assert.That(safe.ServerPayloadPath, Is.Empty);
            Assert.That(safe.Network.BindAddress, Is.EqualTo("127.0.0.1"));
            Assert.That(safe.Fps.MatchType, Is.EqualTo(FpsMatchType.TeamDeathmatch));
            Assert.That(safe.Fps.Bots.Difficulty, Is.EqualTo(0.13));
            Assert.That(safe.Fps.KillLimit, Is.EqualTo(100));
            Assert.That(safe.Conditions.SunAngleDegrees, Is.EqualTo(32));
            Assert.That(source.Name, Is.EqualTo("private-owner"), "Export must not modify the source.");
        });
        var settings = ReleaseDefaults.SanitizeSettings(new ApplicationSettings
        {
            Theme = AppThemeMode.Light, CompactGridRows = true, LastPageIndex = 6,
            AssettoCorsaRoot = factory.AcRoot, ServerPayloadPath = factory.PayloadRoot,
            WebUiBindAddress = "192.168.123.45", WebUiPort = 9999,
        });
        Assert.Multiple(() =>
        {
            Assert.That(settings.Theme, Is.EqualTo(AppThemeMode.Light));
            Assert.That(settings.CompactGridRows, Is.True);
            Assert.That(settings.LastPageIndex, Is.Zero);
            Assert.That(settings.AssettoCorsaRoot, Is.Empty);
            Assert.That(settings.ServerPayloadPath, Is.Empty);
            Assert.That(settings.WebUiBindAddress, Is.EqualTo("127.0.0.1"));
            Assert.That(settings.WebUiPort, Is.EqualTo(8772));
        });
    }

    [Test]
    public void FreshInstallUsesTemplatesWhileSavedPreferencesAndArenaWin()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation();
        var paths = CreatePack(factory);
        string defaults = Path.Combine(paths.PackageRoot, "Defaults");
        Directory.CreateDirectory(defaults);
        File.WriteAllText(Path.Combine(defaults, "settings.json"), """{"Theme":"Light","CompactGridRows":true}""");
        var preset = factory.CreatePreset();
        preset.Mode = EventMode.Fps;
        File.WriteAllText(Path.Combine(defaults, "fps.json"),
            JsonSerializer.Serialize(ReleaseDefaults.SanitizePreset(preset), ReleaseDefaults.JsonOptions));
        var template = new ReleaseDefaults(paths);
        var first = template.CreatePreset(EventMode.Fps, "recipient-game", "recipient-server")!;
        var second = template.CreatePreset(EventMode.Fps, "recipient-game", "recipient-server")!;
        Assert.Multiple(() =>
        {
            Assert.That(first.Id, Is.Not.EqualTo(second.Id));
            Assert.That(first.AssettoCorsaRoot, Is.EqualTo("recipient-game"));
            Assert.That(first.ServerPayloadPath, Is.EqualTo("recipient-server"));
            Assert.That(first.Fps.Arena?.PreparationVersion, Is.EqualTo(FpsArenaDefinition.CurrentPreparationVersion));
            Assert.That(new ApplicationSettingsStore(paths).Load().Theme, Is.EqualTo(AppThemeMode.Light));
        });
        new ApplicationSettingsStore(paths).Save(new ApplicationSettings { Theme = AppThemeMode.Dark });
        var savedArena = new FpsArenaDefinition { TrackId = "test_track", BoundsPaddingMeters = 123 };
        new FpsArenaStore(paths).Save(savedArena);
        Assert.That(new ApplicationSettingsStore(paths).Load().Theme, Is.EqualTo(AppThemeMode.Dark));
        Assert.That(template.CreatePreset(EventMode.Fps, "", "")!.Fps.Arena!.BoundsPaddingMeters, Is.EqualTo(123));
        Assert.That(new FpsArenaStore(paths).Load("unrelated.track", ""), Is.Null);
    }

    [Test]
    public void BundledCatalogOverridesInstalledMapAndRebindsAfterMove()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation();
        var paths = CreatePack(factory);
        var catalog = new AcContentScanner(paths.PackageRoot).Scan(factory.AcRoot);
        Assert.That(catalog.Tracks.Single().RootPath, Does.StartWith(paths.PackageRoot));
        Assert.That(FpsArenaPreparationService.GetTrackContentRoot(catalog.Tracks.Single()),
            Is.EqualTo(FpsMapPack.FindRoot(paths.PackageRoot)), "Regeneration must read the selected bundled map.");
        string moved = Path.Combine(factory.Root, "moved package");
        Directory.Move(paths.PackageRoot, moved);
        var rebound = new AcContentScanner(moved).MergeBundledTracks(factory.AcRoot, catalog);
        Assert.That(rebound.Tracks.Single().RootPath, Does.StartWith(moved));
        Assert.That(new AcContentScanner(moved).Scan(factory.AcRoot).Cars.Count, Is.EqualTo(catalog.Cars.Count));
    }

    [Test]
    public void PreparedFilesRebindByContentWhenPathsAndTimestampsChange()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation();
        var paths = CreatePack(factory);
        var preset = factory.CreatePreset();
        var track = factory.Scan().Tracks.Single(); // Installed copy: different path/timestamp from seed.
        preset.Fps.Arena = new FpsArenaStore(paths).Load(track.TrackId, track.LayoutId);
        preset.Fps.ArenaBoundsPaddingMeters = preset.Fps.Arena!.BoundsPaddingMeters;
        File.SetLastWriteTimeUtc(Path.Combine(track.RootPath, "track.kn5"), DateTime.UtcNow.AddDays(-7));
        var cache = PreparedPhysicsAssetCache.GetFpsPaths(paths, preset, track);
        Assert.That(cache.IsComplete, Is.True);
        Assert.That(File.ReadAllText(cache.GeometryPath), Is.EqualTo("prepared geometry"));
        Assert.That(cache.GeometryPath, Does.StartWith(paths.PackageRoot));
        File.AppendAllText(Path.Combine(track.RootPath, "track.kn5"), " changed");
        var changed = PreparedPhysicsAssetCache.GetFpsPaths(paths, preset, track);
        Assert.That(changed.IsComplete, Is.False, "Changed inputs must never reuse old preparation.");
    }

    [TestCase(true)]
    [TestCase(false)]
    public void PreparationOptionsInvalidateBundledSeed(bool changePadding)
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation();
        var paths = CreatePack(factory);
        var preset = factory.CreatePreset();
        var track = factory.Scan().Tracks.Single();
        preset.Fps.Arena = new FpsArenaStore(paths).Load(track.TrackId, track.LayoutId);
        preset.Fps.ArenaBoundsPaddingMeters = preset.Fps.Arena!.BoundsPaddingMeters;
        if (changePadding) preset.Fps.ArenaBoundsPaddingMeters++;
        else preset.Fps.Arena.CollisionExcludeMeshes.Add("custom");
        Assert.That(PreparedPhysicsAssetCache.GetFpsPaths(paths, preset, track).IsComplete, Is.False);
    }

    [Test]
    public void CorruptedPreparedSeedIsRejected()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation();
        var paths = CreatePack(factory);
        string seed = Path.Combine(FpsMapPack.FindRoot(paths.PackageRoot)!, "content", "tracks", "test_track", "race-control");
        File.AppendAllText(Path.Combine(seed, "geometry.bin"), "tampered");
        var preset = factory.CreatePreset();
        var track = factory.Scan().Tracks.Single();
        preset.Fps.Arena = new FpsArenaStore(paths).Load(track.TrackId, track.LayoutId);
        preset.Fps.ArenaBoundsPaddingMeters = preset.Fps.Arena!.BoundsPaddingMeters;
        Assert.Throws<InvalidDataException>(() => PreparedPhysicsAssetCache.GetFpsPaths(paths, preset, track));
    }

    private static RaceControlPaths CreatePack(TestContentFactory factory)
    {
        string root = Path.Combine(factory.Root, "package");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "portable.json"), "{}");
        string pack = Path.Combine(root, "Packs", "FpsMaps");
        string track = Path.Combine(pack, "content", "tracks", "test_track");
        Directory.CreateDirectory(Path.Combine(track, "ui"));
        string source = Path.Combine(factory.AcRoot, "content", "tracks", "test_track");
        foreach (string name in new[] { "models.ini", "track.kn5", "ui/ui_track.json" })
            File.Copy(Path.Combine(source, name), Path.Combine(track, name));
        File.WriteAllText(Path.Combine(pack, "asrc-fps-maps.json"),
            """{"schemaVersion":1,"packVersion":1,"preparationVersion":4,"maps":[{"trackId":"test_track"}]}""");
        string prepared = Path.Combine(track, "race-control");
        Directory.CreateDirectory(prepared);
        var arena = new FpsArenaDefinition
        {
            TrackId = "test_track", PreparationVersion = FpsArenaDefinition.CurrentPreparationVersion,
            BoundsPaddingMeters = 45,
        };
        File.WriteAllText(Path.Combine(prepared, "arena.json"), JsonSerializer.Serialize(arena));
        File.WriteAllText(Path.Combine(prepared, "geometry.bin"), "prepared geometry");
        File.WriteAllText(Path.Combine(prepared, "navigation.bin"), "prepared navigation");
        string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        File.WriteAllText(Path.Combine(prepared, "prepared.json"), JsonSerializer.Serialize(new
        {
            arena.PreparationVersion, arena.BoundsPaddingMeters, arena.CollisionIncludeMeshes, arena.CollisionExcludeMeshes,
            Inputs = new Dictionary<string, string>
            {
                ["models.ini"] = Hash(Path.Combine(track, "models.ini")),
                ["track.kn5"] = Hash(Path.Combine(track, "track.kn5")),
            },
            GeometrySha256 = Hash(Path.Combine(prepared, "geometry.bin")),
            NavigationSha256 = Hash(Path.Combine(prepared, "navigation.bin")),
        }));
        return new RaceControlPaths(packageRoot: root);
    }
}
