using AssettoServer.Release;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace AssettoServer.Server.Fps;

internal static class FpsModernClientAssetArchive
{
    // CSP caches web.loadRemoteAssets() payloads by URL. Advance this revision whenever
    // any embedded KN5 or KSANIM changes, otherwise clients keep the previous poses.
    public const int AssetRevision = 11;
    public const string Route = "/fps/assets/asrc-fps-modern-v11.zip";
    public const string FileName = "asrc-fps-modern-v11.zip";
    public const string GhostFileName = "asrc_modern_ghost_carbine.kn5";
    public const string OperatorFileName = "asrc_modern_operator_carbine.kn5";
    public const string ViewmodelFileName = "asrc_modern_carbine_viewmodel.kn5";
    public const string PickupFileName = "asrc_modern_carbine_pickup.kn5";
    public const string Team2UniformFileName = "asrc_modern_team2_uniform.png";
    public const string Team2GearFileName = "asrc_modern_team2_gear.png";
    public const string ManifestFileName = "asrc-modern-assets.json";
    private const string ResourcePrefix = "AssettoServer.Server.Fps.ModernAssets.";

    private static readonly Lazy<byte[]> Archive = new(CreateArchive);

    public static byte[] GetArchive() => Archive.Value;

    private static byte[] CreateArchive()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string[] resources = FpsAssetResources.Names(assembly)
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (resources.Length < 32)
            throw new InvalidDataException("Embedded Modern FPS asset set is incomplete");

        var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string resourceName in resources)
        {
            string fileName = resourceName[ResourcePrefix.Length..];
            using Stream resource = FpsAssetResources.Open(assembly, resourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded Modern FPS asset was not found: {resourceName}");
            using var copy = new MemoryStream();
            resource.CopyTo(copy);
            copy.Position = 0;
            Validate(copy, fileName);
            assets.Add(fileName, copy.ToArray());
        }
        ValidateManifest(assets);

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string fileName, byte[] data) in assets)
            {
                ZipArchiveEntry entry = archive.CreateEntry(fileName, CompressionLevel.Optimal);
                using Stream destination = entry.Open();
                destination.Write(data);
            }
        }

        return output.ToArray();
    }

    internal static void Validate(Stream stream, string fileName)
    {
        if (stream.Length < 32)
            throw new InvalidDataException($"Modern FPS asset is too small: {fileName}");
        if (fileName.EndsWith(".kn5", StringComparison.OrdinalIgnoreCase))
        {
            Span<byte> magic = stackalloc byte[6];
            stream.ReadExactly(magic);
            if (!magic.SequenceEqual("sc6969"u8))
                throw new InvalidDataException($"Modern FPS asset is not a KN5: {fileName}");
        }
        else if (fileName.EndsWith(".ksanim", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            if (reader.ReadUInt32() != 2)
                throw new InvalidDataException($"Modern FPS animation is not KSANIM v2: {fileName}");
        }
        else if (fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            Span<byte> magic = stackalloc byte[8];
            stream.ReadExactly(magic);
            ReadOnlySpan<byte> pngMagic = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
            if (!magic.SequenceEqual(pngMagic))
                throw new InvalidDataException($"Modern FPS team texture is not PNG: {fileName}");
        }
        else if (fileName.Equals(ManifestFileName, StringComparison.Ordinal))
        {
            using JsonDocument manifest = JsonDocument.Parse(stream);
            if (manifest.RootElement.GetProperty("schemaVersion").GetInt32() != 1
                || manifest.RootElement.GetProperty("theme").GetString() != "Modern"
                || manifest.RootElement.GetProperty("validation").GetProperty("status")
                    .GetString() != "passed")
                throw new InvalidDataException("Modern FPS asset manifest has the wrong theme");
        }
        else
        {
            throw new InvalidDataException($"Unexpected Modern FPS asset type: {fileName}");
        }
    }

    private static void ValidateManifest(IReadOnlyDictionary<string, byte[]> assets)
    {
        if (!assets.TryGetValue(ManifestFileName, out byte[]? manifestData))
            throw new InvalidDataException("Modern FPS asset manifest is missing");
        using JsonDocument manifest = JsonDocument.Parse(manifestData);
        JsonElement root = manifest.RootElement;
        if (!root.GetProperty("redistributionRightsConfirmedByUser").GetBoolean()
            || root.GetProperty("sources").GetProperty("m4a1Used").GetBoolean())
            throw new InvalidDataException("Modern FPS asset provenance is invalid");
        JsonElement files = root.GetProperty("files");
        if (files.EnumerateObject().Count() != assets.Count - 1)
            throw new InvalidDataException("Modern FPS asset manifest file count is invalid");
        foreach (JsonProperty file in files.EnumerateObject())
        {
            if (!assets.TryGetValue(file.Name, out byte[]? data)
                || !Convert.ToHexString(SHA256.HashData(data)).Equals(
                    file.Value.GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Modern FPS asset hash mismatch: {file.Name}");
        }
        JsonElement teamSkins = root.GetProperty("operator").GetProperty("teamSkins");
        JsonElement ghost = root.GetProperty("operators").GetProperty("ghost");
        if (ghost.GetProperty("file").GetString() != GhostFileName
            || !assets.TryGetValue(GhostFileName, out var ghostBytes)
            || ghostBytes.Length > 60_000_000
            || ghost.GetProperty("triangles").GetInt32() > 40_000
            || ghost.GetProperty("materials").GetInt32() != 4
            || ghost.GetProperty("bones").GetInt32() != 68
            || ghost.GetProperty("sharedAnimations").GetString() != "officer"
            || !root.GetProperty("validation").GetProperty("ghostSharedSkeletonValidated").GetBoolean())
            throw new InvalidDataException("Modern Ghost asset integrity is invalid");
        if (teamSkins.GetProperty("team2Uniform").GetString() != Team2UniformFileName
            || teamSkins.GetProperty("team2Gear").GetString() != Team2GearFileName)
            throw new InvalidDataException("Modern FPS Team 2 skin metadata is invalid");
        foreach (string model in new[] { "officer", "ghost" })
        {
            var skins = root.GetProperty("operators").GetProperty(model).GetProperty("skins");
            if (skins.GetArrayLength() != (model == "ghost" ? 3 : 2))
                throw new InvalidDataException($"Modern FPS {model} skin catalog is incomplete");
            int id = 0;
            foreach (var skin in skins.EnumerateArray())
            {
                if (skin.GetProperty("id").GetInt32() != id++)
                    throw new InvalidDataException($"Modern FPS {model} skin ID is invalid");
                foreach (string field in id == 1 ? new[] { "portrait" } : new[] { "portrait", "uniform", "gear" })
                {
                    string fileName = skin.GetProperty(field).GetString() ?? string.Empty;
                    if (!fileName.EndsWith(".png", StringComparison.Ordinal) || !assets.ContainsKey(fileName))
                        throw new InvalidDataException($"Modern FPS {model} skin asset is missing: {fileName}");
                }
            }
        }
        if (root.GetProperty("operator").GetProperty("triangles").GetInt32() > 40_000
            || root.GetProperty("operator").GetProperty("materials").GetInt32() > 4
            || root.GetProperty("viewmodel").GetProperty("triangles").GetInt32() > 30_000
            || root.GetProperty("viewmodel").GetProperty("materials").GetInt32() > 3
            || root.GetProperty("pickup").GetProperty("triangles").GetInt32() > 6_000
            || root.GetProperty("pickup").GetProperty("materials").GetInt32() != 1
            || root.GetProperty("validation").GetProperty("viewmodelSkinnedMeshes").GetInt32() < 2
            || root.GetProperty("validation").GetProperty("viewmodelWeaponSkinnedMeshes").GetInt32() != 1
            || root.GetProperty("validation").GetProperty("pickupRigidMeshes").GetInt32() != 1
            || !root.GetProperty("validation").GetProperty("stancePosesValidated").GetBoolean()
            || !root.GetProperty("validation").GetProperty("deathCollapseValidated").GetBoolean()
            || !root.GetProperty("validation").GetProperty("uniqueNodeNames").GetBoolean())
            throw new InvalidDataException("Modern FPS model integrity is invalid");
    }
}
