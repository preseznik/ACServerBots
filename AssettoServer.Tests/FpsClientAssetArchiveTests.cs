using System.IO.Compression;
using AssettoServer.Server.Fps;

namespace AssettoServer.Tests;

public sealed class FpsClientAssetArchiveTests
{
    [Test]
    public void Archive_ContainsRealDesertEaglePlaceholdersTexturesArtworkAndAttribution()
    {
        byte[] bytes = FpsClientAssetArchive.GetArchive();
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        Assert.That(bytes, Has.Length.GreaterThan(10_000));
        Assert.That(bytes.AsSpan(0, 2).SequenceEqual("PK"u8), Is.True);
        Assert.That(FpsClientAssetArchive.Route,
            Is.EqualTo("/fps/assets/asrc-fps-assets-v9.zip"));
        Assert.That(archive.Entries.Select(entry => entry.FullName), Is.EquivalentTo(new[]
        {
            FpsClientAssetArchive.ViewmodelFileName,
            FpsClientAssetArchive.WorldModelFileName,
            FpsClientAssetArchive.DesertEagleViewmodelFileName,
            FpsClientAssetArchive.DesertEagleWorldModelFileName,
            FpsClientAssetArchive.DesertEagleAttributionFileName,
            FpsClientAssetArchive.RifleDiffuseFileName,
            FpsClientAssetArchive.OperatorSkinFileName,
            FpsClientAssetArchive.HudWeaponImageFileName,
        }.Concat(FpsClientAssetArchive.PlaceholderItemFileNames)));

        Assert.Multiple(() =>
        {
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                using Stream asset = entry.Open();
                if (entry.FullName.EndsWith(".txt", StringComparison.Ordinal))
                {
                    using var reader = new StreamReader(asset);
                    string attribution = reader.ReadToEnd();
                    Assert.That(attribution, Does.Contain("ELIZION"));
                    Assert.That(attribution, Does.Contain("CC BY 4.0"));
                    continue;
                }
                var magic = new byte[8];
                asset.ReadExactly(magic);
                bool valid = entry.FullName.EndsWith(".kn5", StringComparison.Ordinal)
                    ? magic.AsSpan(0, 6).SequenceEqual("sc6969"u8)
                    : magic.AsSpan().SequenceEqual(
                        new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a });
                Assert.That(valid, Is.True, entry.FullName);
                Assert.That(entry.Length, Is.GreaterThan(1_024), entry.FullName);
            }

            byte[] rifle = ReadEntry(archive, FpsClientAssetArchive.WorldModelFileName);
            byte[] desertEagle = ReadEntry(archive,
                FpsClientAssetArchive.DesertEagleWorldModelFileName);
            Assert.That(desertEagle, Has.Length.GreaterThan(6_000_000));
            Assert.That(desertEagle.SequenceEqual(rifle), Is.False,
                "The Desert Eagle must not regress to the rifle placeholder payload");
        });
    }

    private static byte[] ReadEntry(ZipArchive archive, string fileName)
    {
        using Stream input = archive.GetEntry(fileName)!.Open();
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }
}
