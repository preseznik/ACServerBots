using System.IO.Compression;
using AssettoServer.Server.Fps;

namespace AssettoServer.Tests;

public sealed class FpsClientAssetArchiveTests
{
    [Test]
    public void Archive_ContainsValidFlatKn5Models()
    {
        byte[] bytes = FpsClientAssetArchive.GetArchive();
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        Assert.That(bytes, Has.Length.GreaterThan(10_000));
        Assert.That(bytes.AsSpan(0, 2).SequenceEqual("PK"u8), Is.True);
        Assert.That(archive.Entries.Select(entry => entry.FullName), Is.EquivalentTo(new[]
        {
            FpsClientAssetArchive.ViewmodelFileName,
            FpsClientAssetArchive.WorldModelFileName,
        }));

        Assert.Multiple(() =>
        {
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                using Stream model = entry.Open();
                var magic = new byte[6];
                model.ReadExactly(magic);
                Assert.That(magic.AsSpan().SequenceEqual("sc6969"u8), Is.True, entry.FullName);
                Assert.That(entry.Length, Is.GreaterThan(1_024), entry.FullName);
            }
        });
    }
}
