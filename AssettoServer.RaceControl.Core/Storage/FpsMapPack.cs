using System.Security.Cryptography;
using System.Text.Json;
using AssettoServer.RaceControl.Core.Infrastructure;
using AssettoServer.RaceControl.Core.Models;
using AssettoServer.RaceControl.Core.Staging;

namespace AssettoServer.RaceControl.Core.Storage;

public static class FpsMapPack
{
    public static string? FindRoot(string? packageRoot = null)
    {
        string root = packageRoot ?? AppContext.BaseDirectory;
        string? configured = AppContext.GetData("ASRC.FpsMapsRoot") as string;
        string[] candidates = configured is null
            ? [Path.Combine(root, "Data", "Packs", "FpsMaps"), Path.Combine(root, "Packs", "FpsMaps")]
            : [configured];
        return candidates.FirstOrDefault(path => File.Exists(Path.Combine(path, "asrc-fps-maps.json")));
    }

    public static void ValidateRoot(string root)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "asrc-fps-maps.json")));
        if (document.RootElement.GetProperty("schemaVersion").GetInt32() != 1
            || document.RootElement.GetProperty("preparationVersion").GetInt32() != FpsArenaDefinition.CurrentPreparationVersion)
            throw new InvalidDataException("The FPS map pack uses an unsupported preparation format.");
        foreach (var map in document.RootElement.GetProperty("maps").EnumerateArray())
        {
            string id = map.GetProperty("trackId").GetString()!;
            RequireIdentifier(id);
            if (!File.Exists(Path.Combine(root, "content", "tracks", id, "race-control", "arena.json")))
                throw new InvalidDataException("The map pack is missing an arena definition: " + id);
        }
    }

    public static string? ArenaPath(RaceControlPaths paths, string trackId, string layoutId)
    {
        if (!string.IsNullOrEmpty(layoutId) || !IsIdentifier(trackId)) return null;
        string? root = FindRoot(paths.PackageRoot);
        if (root is null) return null;
        string file = Path.Combine(root, "content", "tracks", trackId, "race-control", "arena.json");
        return File.Exists(file) ? file : null;
    }

    internal static bool TryPopulateCache(RaceControlPaths paths, RaceControlPreset preset,
        AcTrackLayout track, FpsAssetCachePaths cache)
    {
        string? root = FindRoot(paths.PackageRoot);
        if (root is null || !string.IsNullOrEmpty(track.LayoutId) || !IsIdentifier(track.TrackId)) return false;
        string prepared = Path.Combine(root, "content", "tracks", track.TrackId, "race-control");
        string manifest = Path.Combine(prepared, "prepared.json");
        if (!File.Exists(manifest)) return false;
        var seed = JsonSerializer.Deserialize<PreparedMap>(File.ReadAllText(manifest), ReleaseDefaults.JsonOptions)!;
        if (seed.PreparationVersion != FpsArenaDefinition.CurrentPreparationVersion
            || seed.BoundsPaddingMeters != preset.Fps.ArenaBoundsPaddingMeters
            || !seed.CollisionIncludeMeshes.SequenceEqual(preset.Fps.Arena?.CollisionIncludeMeshes ?? [])
            || !seed.CollisionExcludeMeshes.SequenceEqual(preset.Fps.Arena?.CollisionExcludeMeshes ?? []))
            return false;
        // Compare the actual map inputs, never the source PC's cache key or timestamps.
        if (seed.Inputs.Count == 0) return false;
        foreach (var input in seed.Inputs)
        {
            string file = SafeChild(track.RootPath, input.Key);
            if (!File.Exists(file) || !MatchesHash(file, input.Value)) return false;
        }
        string geometry = Path.Combine(prepared, "geometry.bin");
        string navigation = Path.Combine(prepared, "navigation.bin");
        if (!MatchesHash(geometry, seed.GeometrySha256) || !MatchesHash(navigation, seed.NavigationSha256))
            throw new InvalidDataException("The bundled arena preparation files failed their checksum check.");
        paths.RequireInsidePackage(cache.GeometryPath);
        paths.RequireInsidePackage(cache.NavigationPath);
        cache.StoreFrom(geometry, navigation);
        return true;
    }

    private static bool MatchesHash(string file, string expected)
    {
        using var input = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(input)).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeChild(string root, string relative)
    {
        string path = Path.GetFullPath(Path.Combine(root, relative));
        if (relative.Contains(':') || !path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid map input path.");
        return path;
    }

    private static void RequireIdentifier(string id)
    {
        if (!IsIdentifier(id))
            throw new InvalidDataException("Invalid bundled map identifier.");
    }

    private static bool IsIdentifier(string id) => !string.IsNullOrEmpty(id)
        && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private sealed record PreparedMap(int PreparationVersion, double BoundsPaddingMeters,
        string[] CollisionIncludeMeshes, string[] CollisionExcludeMeshes, Dictionary<string, string> Inputs,
        string GeometrySha256, string NavigationSha256);
}
