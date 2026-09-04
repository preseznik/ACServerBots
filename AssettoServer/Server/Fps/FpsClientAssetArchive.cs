using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;

namespace AssettoServer.Server.Fps;

internal static class FpsClientAssetArchive
{
    public const string Route = "/fps/assets/asrc-fps-assets-v21.zip";
    public const string FileName = "asrc-fps-assets-v21.zip";
    public const string ViewmodelFileName = "asrc_assault_rifle_viewmodel.kn5";
    public const string WorldModelFileName = "asrc_assault_rifle_world.kn5";
    public const string RifleDiffuseFileName = "asrc_rifle_diffuse.png";
    public const string OperatorSkinFileName = "asrc_operator_skin.png";
    public const string HudWeaponImageFileName = "asrc_carbine_hud.png";
    public const string CompactSmgViewmodelFileName = "asrc_compact_smg_viewmodel.kn5";
    public const string CompactSmgWorldModelFileName = "asrc_compact_smg_world.kn5";
    public const string CompactSmgAttributionFileName = "compact-smg-attribution.txt";
    public static readonly string[] CompactSmgAnimationFileNames =
    [
        "asrc_compact_smg_idle.ksanim",
        "asrc_compact_smg_fire.ksanim",
        "asrc_compact_smg_reload.ksanim",
        "asrc_compact_smg_reload_empty.ksanim",
        "asrc_compact_smg_equip.ksanim",
        "asrc_compact_smg_sprint.ksanim",
    ];
    public const string DesertEagleViewmodelFileName = "asrc_desert_eagle_viewmodel.kn5";
    public const string DesertEagleWorldModelFileName = "asrc_desert_eagle_world.kn5";
    public const string DesertEagleAttributionFileName = "desert-eagle-attribution.txt";
    public static readonly string[] DesertEagleAnimationFileNames =
    [
        "asrc_desert_eagle_idle.ksanim",
        "asrc_desert_eagle_fire.ksanim",
        "asrc_desert_eagle_equip.ksanim",
        "asrc_desert_eagle_sprint.ksanim",
        "asrc_desert_eagle_reload.ksanim",
    ];
    public const string Colt1911ViewmodelFileName = "asrc_colt_1911_viewmodel.kn5";
    public const string Colt1911WorldModelFileName = "asrc_colt_1911_world.kn5";
    public const string Colt1911AttributionFileName = "colt-1911-attribution.txt";
    public static readonly string[] Colt1911AnimationFileNames =
    [
        "asrc_colt_1911_idle.ksanim",
        "asrc_colt_1911_fire.ksanim",
        "asrc_colt_1911_equip.ksanim",
        "asrc_colt_1911_sprint.ksanim",
        "asrc_colt_1911_reload.ksanim",
    ];
    public const string FragGrenadeViewmodelFileName = "asrc_frag_grenade_viewmodel.kn5";
    public const string FragGrenadeWorldModelFileName = "asrc_frag_grenade_world.kn5";
    public const string FragGrenadeThrowFileName = "asrc_frag_grenade_throw.ksanim";
    public const string FragGrenadeAttributionFileName = "frag-grenade-attribution.txt";
    public const string StickyGrenadeViewmodelFileName = "asrc_sticky_grenade_viewmodel.kn5";
    public const string StickyGrenadeWorldModelFileName = "asrc_sticky_grenade_world.kn5";
    public const string StickyGrenadeThrowFileName = "asrc_sticky_grenade_throw.ksanim";
    public const string StickyGrenadeAttributionFileName = "sticky-grenade-attribution.txt";
    public static readonly string[] GrenadeModelFileNames =
    [
        FragGrenadeViewmodelFileName,
        FragGrenadeWorldModelFileName,
        StickyGrenadeViewmodelFileName,
        StickyGrenadeWorldModelFileName,
    ];

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
            AddKn5(archive, CompactSmgViewmodelFileName,
                "AssettoServer.Server.Fps.Assets.asrc_compact_smg_viewmodel.kn5");
            AddKn5(archive, CompactSmgWorldModelFileName,
                "AssettoServer.Server.Fps.Assets.asrc_compact_smg_world.kn5");
            foreach (string fileName in CompactSmgAnimationFileNames)
                AddKsanim(archive, fileName,
                    $"AssettoServer.Server.Fps.Assets.{fileName}");
            AddKn5(archive, DesertEagleViewmodelFileName,
                "AssettoServer.Server.Fps.Assets.asrc_desert_eagle_viewmodel.kn5");
            AddKn5(archive, DesertEagleWorldModelFileName,
                "AssettoServer.Server.Fps.Assets.asrc_desert_eagle_world.kn5");
            foreach (string fileName in DesertEagleAnimationFileNames)
                AddKsanim(archive, fileName,
                    $"AssettoServer.Server.Fps.Assets.{fileName}");
            AddKn5(archive, Colt1911ViewmodelFileName,
                "AssettoServer.Server.Fps.Assets.asrc_colt_1911_viewmodel.kn5");
            AddKn5(archive, Colt1911WorldModelFileName,
                "AssettoServer.Server.Fps.Assets.asrc_colt_1911_world.kn5");
            foreach (string fileName in Colt1911AnimationFileNames)
                AddKsanim(archive, fileName,
                    $"AssettoServer.Server.Fps.Assets.{fileName}");
            foreach (string fileName in GrenadeModelFileNames)
                AddKn5(archive, fileName, $"AssettoServer.Server.Fps.Assets.{fileName}");
            AddKsanim(archive, FragGrenadeThrowFileName,
                $"AssettoServer.Server.Fps.Assets.{FragGrenadeThrowFileName}");
            AddKsanim(archive, StickyGrenadeThrowFileName,
                $"AssettoServer.Server.Fps.Assets.{StickyGrenadeThrowFileName}");
            AddPng(archive, RifleDiffuseFileName,
                "AssettoServer.Server.Fps.Assets.asrc_rifle_diffuse.png");
            AddPng(archive, OperatorSkinFileName,
                "AssettoServer.Server.Fps.Assets.asrc_operator_skin.png");
            AddPng(archive, HudWeaponImageFileName,
                "AssettoServer.Server.Fps.Assets.asrc_carbine_hud.png");
            AddText(archive, DesertEagleAttributionFileName,
                "AssettoServer.Server.Fps.Assets.asrc_desert_eagle_attribution.txt", "ELIZION");
            AddText(archive, Colt1911AttributionFileName,
                "AssettoServer.Server.Fps.Assets.asrc_colt_1911_attribution.txt", "DanaeH");
            AddText(archive, CompactSmgAttributionFileName,
                "AssettoServer.Server.Fps.Assets.asrc_compact_smg_attribution.txt", "Rotuma");
            AddText(archive, FragGrenadeAttributionFileName,
                "AssettoServer.Server.Fps.Assets.asrc_frag_grenade_attribution.txt", "Tiago Lopes");
            AddText(archive, StickyGrenadeAttributionFileName,
                "AssettoServer.Server.Fps.Assets.asrc_sticky_grenade_attribution.txt", "Simplix");
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

    private static void AddKsanim(ZipArchive archive, string fileName, string resourceName)
    {
        using Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
                                ?? throw new InvalidOperationException(
                                    $"Embedded FPS client animation was not found: {resourceName}");
        Span<byte> version = stackalloc byte[4];
        resource.ReadExactly(version);
        if (resource.Length < 32 || BitConverter.ToUInt32(version) != 2)
            throw new InvalidDataException(
                $"Embedded FPS client animation is not a valid KSANIM: {resourceName}");
        resource.Position = 0;

        ZipArchiveEntry entry = archive.CreateEntry(fileName, CompressionLevel.Optimal);
        using Stream destination = entry.Open();
        resource.CopyTo(destination);
    }

    private static void AddText(ZipArchive archive, string fileName, string resourceName,
        string expectedAuthor)
    {
        using Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
                                ?? throw new InvalidOperationException(
                                    $"Embedded FPS client asset was not found: {resourceName}");
        using var reader = new StreamReader(resource, leaveOpen: true);
        string text = reader.ReadToEnd();
        if (!text.Contains("CC BY 4.0", StringComparison.Ordinal)
            || !text.Contains(expectedAuthor, StringComparison.Ordinal))
            throw new InvalidDataException($"Embedded FPS attribution is invalid: {resourceName}");
        ZipArchiveEntry entry = archive.CreateEntry(fileName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(text);
    }
}
