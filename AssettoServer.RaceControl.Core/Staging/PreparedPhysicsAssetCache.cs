using System.Security.Cryptography;
using System.Text;
using AssettoServer.RaceControl.Core.Configuration;
using AssettoServer.RaceControl.Core.Infrastructure;
using AssettoServer.RaceControl.Core.Models;

namespace AssettoServer.RaceControl.Core.Staging;

internal sealed record FpsAssetCachePaths(string GeometryPath, string NavigationPath)
{
    public bool IsComplete => File.Exists(GeometryPath) && File.Exists(NavigationPath);

    public void CopyTo(string geometryOutput, string navigationOutput)
    {
        File.Copy(GeometryPath, geometryOutput, true);
        File.Copy(NavigationPath, navigationOutput, true);
    }

    public void StoreFrom(string geometryInput, string navigationInput)
    {
        StoreAtomically(geometryInput, GeometryPath);
        StoreAtomically(navigationInput, NavigationPath);
    }

    private static void StoreAtomically(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = $"{destination}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.Copy(source, temporary, true);
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

internal static class PreparedPhysicsAssetCache
{
    public static string GetRacePath(RaceControlPaths paths, RaceControlPreset preset,
        RenderedServerConfiguration rendered)
    {
        var inputs = GetTrackModelInputs(rendered.Track);
        inputs.Add(Path.Combine(preset.ServerPayloadPath, "AssettoServer.exe"));
        inputs.Add(rendered.Track.FastLanePath);
        foreach (var car in rendered.RacingCars)
        {
            inputs.Add(car.ColliderPath ?? string.Empty);
            inputs.Add(car.DataAcdPath ?? string.Empty);
            inputs.Add(Path.Combine(car.RootPath, "lods.ini"));
            inputs.Add(Path.Combine(car.RootPath, "data", "tyres.ini"));
            inputs.Add(Path.Combine(car.RootPath, "data", "car.ini"));
            inputs.AddRange(Directory.EnumerateFiles(car.RootPath, "*.kn5",
                SearchOption.TopDirectoryOnly));
        }
        return CachePath(paths, "race-physics", inputs);
    }

    public static FpsAssetCachePaths GetFpsPaths(RaceControlPaths paths,
        RaceControlPreset preset, AcTrackLayout track)
    {
        var inputs = GetTrackModelInputs(track);
        string[] values =
        [
            $"preparation={FpsArenaDefinition.CurrentPreparationVersion}",
            FormattableString.Invariant($"padding={preset.Fps.ArenaBoundsPaddingMeters:R}"),
            $"include={string.Join(';', preset.Fps.Arena?.CollisionIncludeMeshes ?? [])}",
            $"exclude={string.Join(';', preset.Fps.Arena?.CollisionExcludeMeshes ?? [])}",
        ];
        return new FpsAssetCachePaths(
            CachePath(paths, "fps-arena-geometry", inputs, values),
            CachePath(paths, "fps-arena-navigation", inputs, values));
    }

    private static List<string> GetTrackModelInputs(AcTrackLayout track)
    {
        var inputs = new List<string>
        {
            track.ModelsIniPath,
        };
        using var models = File.Exists(track.ModelsIniPath)
            ? File.OpenText(track.ModelsIniPath)
            : null;
        if (models is not null)
        {
            string? line;
            while ((line = models.ReadLine()) is not null)
            {
                var separator = line.IndexOf('=');
                if (separator > 0 && line[..separator].Trim().Equals("FILE",
                        StringComparison.OrdinalIgnoreCase))
                {
                    string relative = line[(separator + 1)..].Split(';', 2)[0].Trim();
                    if (!string.IsNullOrWhiteSpace(relative))
                        inputs.Add(Path.Combine(track.RootPath, relative));
                }
            }
        }

        return inputs;
    }

    private static string CachePath(RaceControlPaths paths, string prefix,
        IEnumerable<string> inputs, IEnumerable<string>? values = null)
    {
        var keyBuilder = new StringBuilder();
        foreach (var path in inputs.Where(File.Exists)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            var info = new FileInfo(path);
            keyBuilder.Append(Path.GetFullPath(path)).Append('|').Append(info.Length).Append('|')
                .Append(info.LastWriteTimeUtc.Ticks).AppendLine();
        }
        if (values is not null)
        {
            foreach (string value in values.Order(StringComparer.Ordinal))
                keyBuilder.Append("value|").Append(value).AppendLine();
        }

        var hash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(keyBuilder.ToString()))).ToLowerInvariant();
        return Path.Combine(paths.CacheDirectory, "Physics", $"{prefix}-{hash}.bin");
    }
}
