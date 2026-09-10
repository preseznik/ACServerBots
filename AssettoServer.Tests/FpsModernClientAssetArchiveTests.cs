using System.IO.Compression;
using AssettoServer.Server.Fps;

namespace AssettoServer.Tests;

public sealed class FpsModernClientAssetArchiveTests
{
    [Test]
    public void Archive_ContainsValidatedModernModelsAnimationsAndManifest()
    {
        byte[] bytes = FpsModernClientAssetArchive.GetArchive();
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        string[] names = archive.Entries.Select(entry => entry.FullName).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(FpsModernClientAssetArchive.Route,
                Is.EqualTo("/fps/assets/asrc-fps-modern-v11.zip"));
            Assert.That(FpsModernClientAssetArchive.AssetRevision, Is.EqualTo(11));
            Assert.That(bytes.AsSpan(0, 2).SequenceEqual("PK"u8), Is.True);
            Assert.That(names, Does.Contain(FpsModernClientAssetArchive.OperatorFileName));
            Assert.That(names, Does.Contain(FpsModernClientAssetArchive.GhostFileName));
            Assert.That(names, Does.Contain("asrc_operator_ghost.png"));
            Assert.That(names, Does.Contain("asrc_operator_officer.png"));
            Assert.That(names, Does.Contain("asrc_modern_ghost_team2_uniform.png"));
            Assert.That(names, Does.Contain("asrc_modern_ghost_team2_gear.png"));
            Assert.That(names, Does.Contain("asrc_modern_ghost_desert_uniform.png"));
            Assert.That(names, Does.Contain("asrc_modern_ghost_desert_gear.png"));
            Assert.That(names, Does.Contain("asrc_operator_officer_bluegrey.png"));
            Assert.That(names, Does.Contain("asrc_operator_ghost_bluegrey.png"));
            Assert.That(names, Does.Contain("asrc_operator_ghost_desert.png"));
            Assert.That(names, Does.Contain(FpsModernClientAssetArchive.ViewmodelFileName));
            Assert.That(names, Does.Contain(FpsModernClientAssetArchive.PickupFileName));
            Assert.That(names, Does.Contain(FpsModernClientAssetArchive.Team2UniformFileName));
            Assert.That(names, Does.Contain(FpsModernClientAssetArchive.Team2GearFileName));
            Assert.That(names, Does.Contain(FpsModernClientAssetArchive.ManifestFileName));
            Assert.That(names.Count(name => name.EndsWith(".ksanim",
                StringComparison.OrdinalIgnoreCase)), Is.EqualTo(26));
        });

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            using Stream asset = entry.Open();
            using var copy = new MemoryStream();
            asset.CopyTo(copy);
            copy.Position = 0;
            Assert.DoesNotThrow(() =>
                FpsModernClientAssetArchive.Validate(copy, entry.FullName), entry.FullName);
        }
    }
}
