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
                Is.EqualTo("/fps/assets/asrc-fps-modern-v8.zip"));
            Assert.That(FpsModernClientAssetArchive.AssetRevision, Is.EqualTo(8));
            Assert.That(bytes.AsSpan(0, 2).SequenceEqual("PK"u8), Is.True);
            Assert.That(names, Does.Contain(FpsModernClientAssetArchive.OperatorFileName));
            Assert.That(names, Does.Contain(FpsModernClientAssetArchive.ViewmodelFileName));
            Assert.That(names, Does.Contain(FpsModernClientAssetArchive.PickupFileName));
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
