using AssettoServer.RaceControl.Core.Configuration;
using AssettoServer.RaceControl.Core.Infrastructure;
using AssettoServer.RaceControl.Core.Staging;
using AssettoServer.RaceControl.Core.Validation;
using NUnit.Framework;

namespace AssettoServer.RaceControl.Tests;

public sealed class ServerInstanceStagerTests
{
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
}
