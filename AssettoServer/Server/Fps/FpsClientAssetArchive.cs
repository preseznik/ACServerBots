using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;

namespace AssettoServer.Server.Fps;

internal static class FpsClientAssetArchive
{
    public const string Route = "/fps/assets/asrc-fps-assets-v7.zip";
    public const string FileName = "asrc-fps-assets-v7.zip";
    public const string ViewmodelFileName = "asrc_assault_rifle_viewmodel.kn5";
    public const string WorldModelFileName = "asrc_assault_rifle_world.kn5";
    public const string RifleDiffuseFileName = "asrc_rifle_diffuse.png";
    public const string OperatorSkinFileName = "asrc_operator_skin.png";
    public const string HudWeaponImageFileName = "asrc_carbine_hud.png";

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
            AddPng(archive, RifleDiffuseFileName,
                "AssettoServer.Server.Fps.Assets.asrc_rifle_diffuse.png");
            AddPng(archive, OperatorSkinFileName,
                "AssettoServer.Server.Fps.Assets.asrc_operator_skin.png");
            AddPng(archive, HudWeaponImageFileName,
                "AssettoServer.Server.Fps.Assets.asrc_carbine_hud.png");
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

    private static void AddPng(ZipArchive archive, string fileName, string resourceName)
    {
        using Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
                                ?? throw new InvalidOperationException(
                                    $"Embedded FPS client asset was not found: {resourceName}");
        Span<byte> magic = stackalloc byte[8];
        resource.ReadExactly(magic);
        ReadOnlySpan<byte> pngMagic = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
        if (resource.Length < 1024 || !magic.SequenceEqual(pngMagic))
            throw new InvalidDataException($"Embedded FPS client asset is not a valid PNG: {resourceName}");
        resource.Position = 0;

        ZipArchiveEntry entry = archive.CreateEntry(fileName, CompressionLevel.Optimal);
        using Stream destination = entry.Open();
        resource.CopyTo(destination);
    }
}
