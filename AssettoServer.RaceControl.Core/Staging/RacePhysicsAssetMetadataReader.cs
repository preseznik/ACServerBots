using System.IO.Compression;
using System.Text;

namespace AssettoServer.RaceControl.Core.Staging;

internal static class RacePhysicsAssetMetadataReader
{
    private const string Magic = "ASRPHY01";

    public static int ReadGridSlotCount(string path)
    {
        using var file = File.OpenRead(path);
        using var compressed = new BrotliStream(file, CompressionMode.Decompress);
        using var reader = new BinaryReader(compressed, Encoding.UTF8);
        if (Encoding.ASCII.GetString(reader.ReadBytes(Magic.Length)) != Magic)
            throw new InvalidDataException($"Invalid race physics asset: {path}");
        int version = reader.ReadInt32();
        if (version is < 7 or > 8)
            throw new InvalidDataException($"Unsupported race physics asset version {version}: {path}");
        int count = reader.ReadInt32();
        if (count is < 0 or > 254)
            throw new InvalidDataException($"Invalid race physics grid count: {count}");
        return count;
    }
}
