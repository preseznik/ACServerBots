using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace ACEditor.App.Controls;

internal static class DdsTextureLoader
{
    private const uint DdsMagic = 0x20534444;

    public static ID3D11ShaderResourceView CreateShaderResourceView(ID3D11Device device, byte[] ddsBytes)
    {
        DdsImage image = Parse(ddsBytes);
        GCHandle pin = GCHandle.Alloc(image.PixelData, GCHandleType.Pinned);
        try
        {
            IntPtr baseAddress = pin.AddrOfPinnedObject();
            SubresourceData[] subresources = image.Mips.Select(mip => new SubresourceData(
                IntPtr.Add(baseAddress, mip.Offset), mip.RowPitch, mip.SlicePitch)).ToArray();
            var description = new Texture2DDescription(image.Format, (uint)image.Width, (uint)image.Height,
                1, (uint)image.Mips.Count, BindFlags.ShaderResource, ResourceUsage.Default,
                CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None);
            using ID3D11Texture2D texture = device.CreateTexture2D(description, subresources);
            return device.CreateShaderResourceView(texture);
        }
        finally
        {
            pin.Free();
        }
    }

    internal static DdsImage Parse(byte[] bytes)
    {
        if (bytes.Length < 128 || BitConverter.ToUInt32(bytes, 0) != DdsMagic ||
            BitConverter.ToUInt32(bytes, 4) != 124 || BitConverter.ToUInt32(bytes, 76) != 32)
            throw new InvalidDataException("Invalid or truncated DDS header.");

        int width = checked((int)BitConverter.ToUInt32(bytes, 16));
        int height = checked((int)BitConverter.ToUInt32(bytes, 12));
        int mipCount = Math.Max(1, checked((int)BitConverter.ToUInt32(bytes, 28)));
        if (width <= 0 || height <= 0 || mipCount > 32)
            throw new InvalidDataException("DDS dimensions or mip count are invalid.");

        string fourCc = Encoding.ASCII.GetString(bytes, 84, 4).TrimEnd('\0');
        int dataOffset = 128;
        Format format;
        int sourceBitsPerPixel = 0;

        if (fourCc == "DX10")
        {
            if (bytes.Length < 148 || BitConverter.ToUInt32(bytes, 132) != 3 ||
                BitConverter.ToUInt32(bytes, 140) != 1)
                throw new InvalidDataException("Only single 2D DDS textures are supported.");
            format = (Format)BitConverter.ToInt32(bytes, 128);
            dataOffset = 148;
        }
        else
        {
            format = fourCc switch
            {
                "DXT1" => Format.BC1_UNorm,
                "DXT3" => Format.BC2_UNorm,
                "DXT5" => Format.BC3_UNorm,
                "ATI1" or "BC4U" => Format.BC4_UNorm,
                "ATI2" or "BC5U" => Format.BC5_UNorm,
                _ => ResolveRgbFormat(bytes, out sourceBitsPerPixel)
            };
        }

        if (!IsSupported(format))
            throw new InvalidDataException($"Unsupported DDS format: {format}.");

        if (sourceBitsPerPixel == 24)
            return ConvertBgr24(bytes, dataOffset, width, height, mipCount);

        var mips = new List<DdsMip>(mipCount);
        int offset = dataOffset;
        int mipWidth = width, mipHeight = height;
        for (int index = 0; index < mipCount; index++)
        {
            GetSurfaceInfo(format, mipWidth, mipHeight, out uint rowPitch, out uint slicePitch);
            if ((long)offset + slicePitch > bytes.Length)
                throw new InvalidDataException("DDS mip data is truncated.");
            mips.Add(new DdsMip(offset, rowPitch, slicePitch));
            offset = checked(offset + (int)slicePitch);
            mipWidth = Math.Max(1, mipWidth / 2);
            mipHeight = Math.Max(1, mipHeight / 2);
        }
        return new DdsImage(format, width, height, bytes, mips);
    }

    private static Format ResolveRgbFormat(byte[] bytes, out int bitsPerPixel)
    {
        bitsPerPixel = checked((int)BitConverter.ToUInt32(bytes, 88));
        uint red = BitConverter.ToUInt32(bytes, 92);
        uint green = BitConverter.ToUInt32(bytes, 96);
        uint blue = BitConverter.ToUInt32(bytes, 100);
        uint alpha = BitConverter.ToUInt32(bytes, 104);
        if (bitsPerPixel is 24 or 32 && red == 0x00ff0000 && green == 0x0000ff00 && blue == 0x000000ff)
            return alpha == 0 ? Format.B8G8R8X8_UNorm : Format.B8G8R8A8_UNorm;
        if (bitsPerPixel == 32 && red == 0x000000ff && green == 0x0000ff00 && blue == 0x00ff0000)
            return Format.R8G8B8A8_UNorm;
        throw new InvalidDataException($"Unsupported DDS RGB layout ({bitsPerPixel} bits).");
    }

    private static DdsImage ConvertBgr24(byte[] source, int sourceOffset, int width, int height, int mipCount)
    {
        int sourceCursor = sourceOffset;
        int totalOutputBytes = 0;
        int mipWidth = width, mipHeight = height;
        for (int index = 0; index < mipCount; index++)
        {
            totalOutputBytes = checked(totalOutputBytes + mipWidth * mipHeight * 4);
            mipWidth = Math.Max(1, mipWidth / 2);
            mipHeight = Math.Max(1, mipHeight / 2);
        }

        byte[] converted = new byte[totalOutputBytes];
        var mips = new List<DdsMip>(mipCount);
        int outputCursor = 0;
        mipWidth = width; mipHeight = height;
        for (int index = 0; index < mipCount; index++)
        {
            int sourceLength = checked(mipWidth * mipHeight * 3);
            if ((long)sourceCursor + sourceLength > source.Length)
                throw new InvalidDataException("DDS 24-bit mip data is truncated.");
            int outputStart = outputCursor;
            for (int pixel = 0; pixel < mipWidth * mipHeight; pixel++)
            {
                converted[outputCursor++] = source[sourceCursor++];
                converted[outputCursor++] = source[sourceCursor++];
                converted[outputCursor++] = source[sourceCursor++];
                converted[outputCursor++] = 255;
            }
            uint rowPitch = checked((uint)(mipWidth * 4));
            uint slicePitch = checked((uint)(mipWidth * mipHeight * 4));
            mips.Add(new DdsMip(outputStart, rowPitch, slicePitch));
            mipWidth = Math.Max(1, mipWidth / 2);
            mipHeight = Math.Max(1, mipHeight / 2);
        }
        return new DdsImage(Format.B8G8R8A8_UNorm, width, height, converted, mips);
    }

    private static bool IsSupported(Format format) => format is
        Format.R8G8B8A8_UNorm or Format.R8G8B8A8_UNorm_SRgb or
        Format.B8G8R8A8_UNorm or Format.B8G8R8A8_UNorm_SRgb or Format.B8G8R8X8_UNorm or Format.B8G8R8X8_UNorm_SRgb or
        Format.BC1_UNorm or Format.BC1_UNorm_SRgb or Format.BC2_UNorm or Format.BC2_UNorm_SRgb or
        Format.BC3_UNorm or Format.BC3_UNorm_SRgb or Format.BC4_UNorm or Format.BC4_SNorm or
        Format.BC5_UNorm or Format.BC5_SNorm or Format.BC6H_Uf16 or Format.BC6H_Sf16 or
        Format.BC7_UNorm or Format.BC7_UNorm_SRgb;

    private static void GetSurfaceInfo(Format format, int width, int height, out uint rowPitch, out uint slicePitch)
    {
        int blockBytes = format is Format.BC1_UNorm or Format.BC1_UNorm_SRgb or Format.BC4_UNorm or Format.BC4_SNorm
            ? 8
            : format is Format.BC2_UNorm or Format.BC2_UNorm_SRgb or Format.BC3_UNorm or Format.BC3_UNorm_SRgb or
                Format.BC5_UNorm or Format.BC5_SNorm or Format.BC6H_Uf16 or Format.BC6H_Sf16 or
                Format.BC7_UNorm or Format.BC7_UNorm_SRgb ? 16 : 0;
        if (blockBytes > 0)
        {
            rowPitch = checked((uint)(Math.Max(1, (width + 3) / 4) * blockBytes));
            slicePitch = checked(rowPitch * (uint)Math.Max(1, (height + 3) / 4));
        }
        else
        {
            rowPitch = checked((uint)(width * 4));
            slicePitch = checked(rowPitch * (uint)height);
        }
    }
}

internal sealed record DdsImage(Format Format, int Width, int Height, byte[] PixelData, IReadOnlyList<DdsMip> Mips);
internal readonly record struct DdsMip(int Offset, uint RowPitch, uint SlicePitch);
