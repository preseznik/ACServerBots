using System.IO.Compression;
using AssettoServer.Server.Fps;

namespace AssettoServer.Tests;

public sealed class FpsClientAssetArchiveTests
{
    [Test]
    public void Archive_ContainsRealSmgPistolsPlaceholderGrenadesTexturesAndAttribution()
    {
        byte[] bytes = FpsClientAssetArchive.GetArchive();
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        Assert.That(bytes, Has.Length.GreaterThan(10_000));
        Assert.That(bytes.AsSpan(0, 2).SequenceEqual("PK"u8), Is.True);
        Assert.That(FpsClientAssetArchive.Route,
            Is.EqualTo("/fps/assets/asrc-fps-assets-v20.zip"));
        Assert.That(archive.Entries.Select(entry => entry.FullName), Is.EquivalentTo(new[]
        {
            FpsClientAssetArchive.ViewmodelFileName,
            FpsClientAssetArchive.WorldModelFileName,
            FpsClientAssetArchive.CompactSmgViewmodelFileName,
            FpsClientAssetArchive.CompactSmgWorldModelFileName,
            FpsClientAssetArchive.CompactSmgAttributionFileName,
            FpsClientAssetArchive.DesertEagleViewmodelFileName,
            FpsClientAssetArchive.DesertEagleWorldModelFileName,
            FpsClientAssetArchive.DesertEagleAttributionFileName,
            FpsClientAssetArchive.Colt1911ViewmodelFileName,
            FpsClientAssetArchive.Colt1911WorldModelFileName,
            FpsClientAssetArchive.Colt1911AttributionFileName,
            FpsClientAssetArchive.RifleDiffuseFileName,
            FpsClientAssetArchive.OperatorSkinFileName,
            FpsClientAssetArchive.HudWeaponImageFileName,
        }.Concat(FpsClientAssetArchive.CompactSmgAnimationFileNames)
            .Concat(FpsClientAssetArchive.DesertEagleAnimationFileNames)
            .Concat(FpsClientAssetArchive.Colt1911AnimationFileNames)
            .Concat(FpsClientAssetArchive.PlaceholderItemFileNames)));

        Assert.Multiple(() =>
        {
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                using Stream asset = entry.Open();
                if (entry.FullName.EndsWith(".txt", StringComparison.Ordinal))
                {
                    using var reader = new StreamReader(asset);
                    string attribution = reader.ReadToEnd();
                    string expectedAuthor = entry.FullName switch
                    {
                        var name when name == FpsClientAssetArchive.CompactSmgAttributionFileName =>
                            "Rotuma",
                        var name when name == FpsClientAssetArchive.Colt1911AttributionFileName =>
                            "DanaeH",
                        _ => "ELIZION",
                    };
                    Assert.That(attribution, Does.Contain(expectedAuthor));
                    Assert.That(attribution, Does.Contain("CC BY 4.0"));
                    continue;
                }
                var magic = new byte[8];
                asset.ReadExactly(magic);
                bool valid;
                if (entry.FullName.EndsWith(".kn5", StringComparison.Ordinal))
                    valid = magic.AsSpan(0, 6).SequenceEqual("sc6969"u8);
                else if (entry.FullName.EndsWith(".ksanim", StringComparison.Ordinal))
                    valid = BitConverter.ToUInt32(magic, 0) == 2;
                else
                    valid = magic.AsSpan().SequenceEqual(
                        new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a });
                Assert.That(valid, Is.True, entry.FullName);
                Assert.That(entry.Length, Is.GreaterThan(1_024), entry.FullName);
            }

            byte[] rifle = ReadEntry(archive, FpsClientAssetArchive.WorldModelFileName);
            byte[] compactSmg = ReadEntry(archive,
                FpsClientAssetArchive.CompactSmgWorldModelFileName);
            byte[] compactSmgViewmodel = ReadEntry(archive,
                FpsClientAssetArchive.CompactSmgViewmodelFileName);
            Assert.That(compactSmgViewmodel, Has.Length.GreaterThan(30_000_000));
            Assert.That(compactSmg, Has.Length.GreaterThan(3_000_000));
            Assert.That(compactSmg.SequenceEqual(rifle), Is.False,
                "The Compact SMG must not regress to the rifle placeholder payload");
            foreach (string animation in FpsClientAssetArchive.CompactSmgAnimationFileNames)
                Assert.That(ReadEntry(archive, animation), Has.Length.GreaterThan(10_000), animation);
            byte[] desertEagle = ReadEntry(archive,
                FpsClientAssetArchive.DesertEagleWorldModelFileName);
            byte[] desertEagleViewmodel = ReadEntry(archive,
                FpsClientAssetArchive.DesertEagleViewmodelFileName);
            Assert.That(desertEagleViewmodel, Has.Length.GreaterThan(24_000_000));
            Assert.That(desertEagle, Has.Length.GreaterThan(6_000_000));
            Assert.That(desertEagle.SequenceEqual(rifle), Is.False,
                "The Desert Eagle must not regress to the rifle placeholder payload");
            foreach (string animation in FpsClientAssetArchive.DesertEagleAnimationFileNames)
                Assert.That(ReadEntry(archive, animation), Has.Length.GreaterThan(10_000), animation);
            byte[] colt1911 = ReadEntry(archive,
                FpsClientAssetArchive.Colt1911WorldModelFileName);
            byte[] colt1911Viewmodel = ReadEntry(archive,
                FpsClientAssetArchive.Colt1911ViewmodelFileName);
            Assert.That(colt1911Viewmodel, Has.Length.GreaterThan(10_000_000));
            Assert.That(colt1911, Has.Length.GreaterThan(1_000_000));
            Assert.That(colt1911.SequenceEqual(rifle), Is.False,
                "The Colt 1911 must not regress to the rifle placeholder payload");
            foreach (string animation in FpsClientAssetArchive.Colt1911AnimationFileNames)
                Assert.That(ReadEntry(archive, animation), Has.Length.GreaterThan(10_000), animation);
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
