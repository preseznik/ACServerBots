using ACEditor.App.Controls;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace ACEditor.Tests;

[TestFixture]
public sealed class DdsTextureLoaderTests
{
    [Test]
    public void ParsesDxt1TextureForGpuUpload()
    {
        DdsImage image = DdsTextureLoader.Parse(SyntheticFixtures.CreateDxt1Dds());

        Assert.Multiple(() =>
        {
            Assert.That(image.Format, Is.EqualTo(Format.BC1_UNorm));
            Assert.That(image.Width, Is.EqualTo(4));
            Assert.That(image.Height, Is.EqualTo(4));
            Assert.That(image.Mips, Has.Count.EqualTo(1));
            Assert.That(image.Mips[0].RowPitch, Is.EqualTo(8));
            Assert.That(image.Mips[0].SlicePitch, Is.EqualTo(8));
        });
    }

    [Test]
    public void RejectsTruncatedTextureData()
    {
        byte[] bytes = SyntheticFixtures.CreateDxt1Dds()[..132];

        Assert.That(() => DdsTextureLoader.Parse(bytes), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void UploadsParsedTextureToDirect3D11()
    {
        FeatureLevel[] levels = [FeatureLevel.Level_11_0];
        using (ID3D11Device device = D3D11.D3D11CreateDevice(
                   DriverType.Warp, DeviceCreationFlags.None, levels))
        using (ID3D11ShaderResourceView view =
               DdsTextureLoader.CreateShaderResourceView(device, SyntheticFixtures.CreateDxt1Dds()))
        {
            Assert.That(view, Is.Not.Null);
        }
    }
}
