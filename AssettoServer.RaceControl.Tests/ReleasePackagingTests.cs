using System.IO.Compression;
using AssettoServer.RaceControl.Core.Infrastructure;
using AssettoServer.RaceControl.Core.Models;
using AssettoServer.RaceControl.Core.Staging;
using AssettoServer.RaceControl.Core.Storage;
using AssettoServer.Release;
using NUnit.Framework;

namespace AssettoServer.RaceControl.Tests;

public sealed class ReleasePackagingTests
{
    [Test]
    public void PortableSettingsAndPresetsSurviveDirectoryMove()
    {
        using var factory = new TestContentFactory();
        string original = Path.Combine(factory.Root, "original");
        Directory.CreateDirectory(original);
        File.WriteAllText(Path.Combine(original, "portable.json"), "{}");
        var paths = new RaceControlPaths(packageRoot: original);
        string payload = Path.Combine(original, "lib", "Server");
        var settings = new ApplicationSettings { ServerPayloadPath = payload };
        var settingsStore = new ApplicationSettingsStore(paths);
        settingsStore.Save(settings);
        var preset = RaceControlPreset.CreateDefault(factory.AcRoot, payload);
        string saved = new PresetStore(paths).Save(preset);
        Assert.That(File.ReadAllText(settingsStore.SettingsPath), Does.Not.Contain(original.Replace("\\", "\\\\")));
        Assert.That(settings.ServerPayloadPath, Is.EqualTo(payload), "Saving must not mutate the live path.");
        string moved = Path.Combine(factory.Root, "moved package");
        Directory.Move(original, moved);
        var relocated = new RaceControlPaths(packageRoot: moved);
        Assert.That(relocated.DataRoot, Is.EqualTo(Path.Combine(moved, "Data")));
        Assert.That(new ApplicationSettingsStore(relocated).Load().ServerPayloadPath,
            Is.EqualTo(Path.Combine(moved, "lib", "Server")));
        Assert.That(new PresetStore(relocated).Load(Path.Combine(relocated.PresetsDirectory,
            Path.GetFileName(saved))).ServerPayloadPath, Is.EqualTo(Path.Combine(moved, "lib", "Server")));
    }

    [Test]
    public void PortableStorageRejectsOutsideRoot()
    {
        using var factory = new TestContentFactory();
        File.WriteAllText(Path.Combine(factory.Root, "portable.json"), "{}");
        var paths = new RaceControlPaths(packageRoot: factory.Root);
        Assert.Throws<IOException>(() => paths.RequireInsidePackage(factory.Root + "-elsewhere/file.zip"));
        Assert.Throws<IOException>(() => paths.RequireInsidePackage(Path.Combine(factory.Root, "..", "outside")));
        Assert.Throws<IOException>(() => new RaceControlPaths(Path.GetTempPath(), factory.Root));
    }

    [TestCase("../escape.txt")]
    [TestCase("folder/../../escape.txt")]
    [TestCase("/absolute.txt")]
    [TestCase("C:/absolute.txt")]
    [TestCase("file.txt:stream")]
    [TestCase("folder\\..\\escape.txt")]
    public void ComponentExtractionRejectsUnsafeNames(string entryName)
    {
        using var factory = new TestContentFactory();
        string zip = Path.Combine(factory.Root, "bad.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            archive.CreateEntry(entryName);
        string destination = Path.Combine(factory.Root, "output");
        Directory.CreateDirectory(destination);
        Assert.ThrowsAsync<InvalidDataException>(() => ComponentInstaller.ExtractAsync(zip, destination));
    }

    [Test]
    public void FailedImportPreservesExistingComponentAndCleansTemporaryFiles()
    {
        using var factory = new TestContentFactory();
        var paths = new RaceControlPaths(factory.DataRoot);
        Directory.CreateDirectory(paths.FpsAssetsDirectory);
        File.WriteAllText(Path.Combine(paths.FpsAssetsDirectory, "keep.txt"), "existing pack");
        string zip = Path.Combine(factory.Root, "bad.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("asrc-fps-client.json").Open()))
                writer.Write($$"""{"clientPackVersion":{{ReleaseIdentity.FpsPackVersion}}}""");
            using (var writer = new StreamWriter(archive.CreateEntry("payload-sha256.json").Open()))
                writer.Write("""{"asrc-fps-client.json":"incorrect"}""");
        }
        Assert.ThrowsAsync<InvalidDataException>(() => new ComponentInstaller(paths).ImportAsync(zip, "fps"));
        Assert.That(File.ReadAllText(Path.Combine(paths.FpsAssetsDirectory, "keep.txt")), Is.EqualTo("existing pack"));
        Assert.That(Directory.GetDirectories(Path.GetDirectoryName(paths.FpsAssetsDirectory)!, "*.incoming-*"), Is.Empty);
    }

    [Test]
    public void ForkSelectionRejectsUpstreamAndMismatchedRelease()
    {
        using var factory = new TestContentFactory();
        Assert.That(ReleaseIdentity.ValidateServer(factory.Root), Is.Not.Null);
        string manifest = Path.Combine(factory.Root, "race-control-server.json");
        File.WriteAllText(manifest, ReleaseIdentity.CapabilitiesJson.Replace(ReleaseIdentity.Version, "999.0"));
        Assert.That(ReleaseIdentity.ValidateServer(factory.Root), Is.Not.Null);
        File.WriteAllText(manifest, ReleaseIdentity.CapabilitiesJson);
        Assert.That(ReleaseIdentity.ValidateServer(factory.Root), Is.Null);
    }
}
