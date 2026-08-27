using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;

namespace AssettoServer.Server.Fps;

internal static class FpsClientAssetArchive
{
    public const string Route = "/fps/assets/asrc-fps-assets-v4.zip";
    public const string FileName = "asrc-fps-assets-v4.zip";
    public const string ViewmodelFileName = "asrc_assault_rifle_viewmodel.kn5";
    public const string WorldModelFileName = "asrc_assault_rifle_world.kn5";

    private static readonly Lazy<byte[]> Archive = new(CreateArchive);

    public static byte[] GetArchive() => Archive.Value;

    private static byte[] CreateArchive()
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddKn5(archive, ViewmodelFileName,
                "AssettoServer.Server.Fps.Assets.asrc_assault_rifle_viewmodel.kn5");
            AddKn5(archive, WorldModelFileName,
                "AssettoServer.Server.Fps.Assets.asrc_assault_rifle_world.kn5");
        }

        return output.ToArray();
    }

    private static void AddKn5(ZipArchive archive, string fileName, string resourceName)
    {
        using Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
                                ?? throw new InvalidOperationException(
                                    $"Embedded FPS client asset was not found: {resourceName}");
        if (resource.Length < 1024)
            throw new InvalidDataException($"Embedded FPS client asset is too small: {resourceName}");

        Span<byte> magic = stackalloc byte[6];
        resource.ReadExactly(magic);
        if (!magic.SequenceEqual("sc6969"u8))
            throw new InvalidDataException($"Embedded FPS client asset is not a valid KN5: {resourceName}");
        resource.Position = 0;

        ZipArchiveEntry entry = archive.CreateEntry(fileName, CompressionLevel.Optimal);
        using Stream destination = entry.Open();
        resource.CopyTo(destination);
    }
}
