using System.Text.Json;
using AssettoServer.RaceControl.Core.Infrastructure;
using AssettoServer.RaceControl.Core.Storage;
using AssettoServer.Release;
using NUnit.Framework;

namespace AssettoServer.RaceControl.Tests;

public sealed class ServerPayloadLocatorTests
{
    [Test]
    public void Upgrade_SkipsSavedSettingsAndPresetForOlderServers()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation();
        var paths = new RaceControlPaths(factory.DataRoot, factory.Root);
        string previous = CreateServer(Path.Combine(paths.ServersDirectory, "previous"), "0.0.0");
        string bundled = CreateServer(Path.Combine(factory.Root, "lib", "Server"));
        var settings = new ApplicationSettingsStore(paths);
        settings.Save(new ApplicationSettings { ServerPayloadPath = previous });
        var presets = new PresetStore(paths);
        var preset = factory.CreatePreset();
        preset.ServerPayloadPath = previous;
        presets.Save(preset);

        string? selected = ServerPayloadLocator.Find(true, settings.Load().ServerPayloadPath,
            presets.Load(presets.List().Single().Path).ServerPayloadPath, bundled);

        Assert.That(selected, Is.EqualTo(bundled));
        Assert.That(File.Exists(Path.Combine(previous, "AssettoServer.exe")), Is.True);
    }

    [Test]
    public void MatchingConfiguredServer_TakesPriorityOverBundle()
    {
        using var factory = new TestContentFactory();
        string configured = CreateServer(factory.PayloadRoot);
        string bundled = CreateServer(Path.Combine(factory.Root, "lib", "Server"));

        Assert.That(ServerPayloadLocator.Find(true, configured, bundled), Is.EqualTo(configured));
    }

    [Test]
    public void IncompatibleBundle_FallsBackToMatchingDownloadedServer()
    {
        using var factory = new TestContentFactory();
        string bundled = CreateServer(Path.Combine(factory.Root, "lib", "Server"), "0.0.0");
        string downloaded = CreateServer(Path.Combine(factory.DataRoot, "Servers", ReleaseIdentity.Version));

        Assert.That(ServerPayloadLocator.Find(true, null, "", bundled, downloaded), Is.EqualTo(downloaded));
    }

    [TestCase("version")]
    [TestCase("product")]
    [TestCase("protocol")]
    [TestCase("missing-manifest")]
    [TestCase("invalid-manifest")]
    [TestCase("missing-executable")]
    public void Release_RejectsIncompatibleOrIncompletePayload(string problem)
    {
        using var factory = new TestContentFactory();
        string directory = CreateServer(factory.PayloadRoot,
            problem == "version" ? "0.0.0" : null,
            problem == "product" ? "other/server" : ReleaseIdentity.Product,
            problem == "protocol" ? ReleaseIdentity.ControlProtocol + 1 : ReleaseIdentity.ControlProtocol);
        string manifest = Path.Combine(directory, "race-control-server.json");
        if (problem == "missing-manifest") File.Delete(manifest);
        if (problem == "invalid-manifest") File.WriteAllText(manifest, "{invalid");
        if (problem == "missing-executable") File.Delete(Path.Combine(directory, "AssettoServer.exe"));

        Assert.That(ServerPayloadLocator.Find(true, directory), Is.Null);
    }

    [Test]
    public void DevelopmentBuild_AllowsUnpackagedServer()
    {
        using var factory = new TestContentFactory();
        Directory.CreateDirectory(factory.PayloadRoot);
        File.WriteAllText(Path.Combine(factory.PayloadRoot, "AssettoServer.exe"), "development server");

        Assert.That(ServerPayloadLocator.Find(false, factory.PayloadRoot), Is.EqualTo(factory.PayloadRoot));
    }

    private static string CreateServer(string directory, string? version = null,
        string product = ReleaseIdentity.Product, int protocol = ReleaseIdentity.ControlProtocol)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "AssettoServer.exe"), "test server");
        File.WriteAllText(Path.Combine(directory, "race-control-server.json"), JsonSerializer.Serialize(new
        {
            product, version = version ?? ReleaseIdentity.Version, controlProtocol = protocol
        }));
        return directory;
    }
}
