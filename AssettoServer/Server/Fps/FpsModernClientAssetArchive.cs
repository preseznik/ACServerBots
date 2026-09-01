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
    public const string Route = "/fps/assets/asrc-fps-modern-v7.zip";
    public const string FileName = "asrc-fps-modern-v7.zip";
    public const string OperatorFileName = "asrc_modern_operator_carbine.kn5";
    public const string ViewmodelFileName = "asrc_modern_carbine_viewmodel.kn5";
    public const string PickupFileName = "asrc_modern_carbine_pickup.kn5";
    public const string ManifestFileName = "asrc-modern-assets.json";
    private const string ResourcePrefix = "AssettoServer.Server.Fps.ModernAssets.";

    private static readonly Lazy<byte[]> Archive = new(CreateArchive);

    public static byte[] GetArchive() => Archive.Value;

    private static byte[] CreateArchive()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string[] resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (resources.Length < 30)
            throw new InvalidDataException("Embedded Modern FPS asset set is incomplete");

        var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string resourceName in resources)
        {
            string fileName = resourceName[ResourcePrefix.Length..];
            using Stream resource = assembly.GetManifestResourceStream(resourceName)
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
