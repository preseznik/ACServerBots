using System.Text;
using AssettoServer.RaceControl.Core.Staging;
using NUnit.Framework;

namespace AssettoServer.RaceControl.Tests;

public sealed class FpsClientPackAssetsTests
{
    [Test]
    public void RifleModelsAreEmbeddedValidKn5Assets()
    {
        byte[] viewmodel = FpsClientPackAssets.GetRifleViewmodel();
        byte[] worldModel = FpsClientPackAssets.GetRifleWorldModel();

        Assert.Multiple(() =>
        {
            Assert.That(Encoding.ASCII.GetString(viewmodel, 0, 6), Is.EqualTo("sc6969"));
            Assert.That(viewmodel.Length, Is.GreaterThan(50_000));
            Assert.That(Encoding.ASCII.GetString(worldModel, 0, 6), Is.EqualTo("sc6969"));
            Assert.That(worldModel.Length, Is.GreaterThan(30_000));
            Assert.That(FpsClientPackAssets.Sha256(viewmodel), Has.Length.EqualTo(64));
            Assert.That(FpsClientPackAssets.RifleViewmodelPath, Does.EndWith(".kn5"));
            Assert.That(FpsClientPackAssets.RifleWorldModelPath, Does.EndWith(".kn5"));
        });
    }

    [Test]
    public void GeneratedDiffuseTexturesAreEmbeddedPngAssets()
    {
        byte[] rifle = FpsClientPackAssets.GetRifleDiffuse();
        byte[] operatorTexture = FpsClientPackAssets.GetOperatorSkin();
        byte[] pngMagic = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

        Assert.Multiple(() =>
        {
            Assert.That(rifle.AsSpan(0, 8).SequenceEqual(pngMagic), Is.True);
            Assert.That(operatorTexture.AsSpan(0, 8).SequenceEqual(pngMagic), Is.True);
            Assert.That(rifle, Has.Length.GreaterThan(100_000));
            Assert.That(operatorTexture, Has.Length.GreaterThan(100_000));
            Assert.That(FpsClientPackAssets.RifleDiffusePath, Does.EndWith(".png"));
            Assert.That(FpsClientPackAssets.OperatorSkinPath,
                Does.EndWith("asrc_operator_skin.png"));
        });
    }

    [Test]
    public void RifleWaveIsAPlayableMonoPcmAsset()
    {
        byte[] wave = FpsClientPackAssets.CreateRifleWave();
        using var reader = new BinaryReader(new MemoryStream(wave), Encoding.ASCII);

        Assert.Multiple(() =>
        {
            Assert.That(Encoding.ASCII.GetString(reader.ReadBytes(4)), Is.EqualTo("RIFF"));
            reader.BaseStream.Position = 8;
            Assert.That(Encoding.ASCII.GetString(reader.ReadBytes(4)), Is.EqualTo("WAVE"));
            reader.BaseStream.Position = 20;
            Assert.That(reader.ReadInt16(), Is.EqualTo(1));
            Assert.That(reader.ReadInt16(), Is.EqualTo(1));
            Assert.That(reader.ReadInt32(), Is.EqualTo(44_100));
            Assert.That(wave.Length, Is.GreaterThan(19_000));
        });
    }
}
