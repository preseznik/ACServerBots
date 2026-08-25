using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using AssettoServer.Server.Ai.Physics;

namespace AssettoServer.Server.Fps;

internal sealed class FpsArenaAsset
{
    public int PreparationVersion { get; init; } = 1;
    public required string TrackId { get; init; }
    public required string LayoutId { get; init; }
    public required FpsArenaPoint BoundsMin { get; init; }
    public required FpsArenaPoint BoundsMax { get; init; }
    public required IReadOnlyList<FpsArenaSpawn> SpawnPoints { get; init; }
}

internal sealed record FpsArenaPoint(float X, float Y, float Z)
{
    public static FpsArenaPoint From(Vector3 value) => new(value.X, value.Y, value.Z);
}

internal sealed record FpsArenaSpawn(FpsArenaPoint Position, float YawRadians);
internal sealed record FpsArenaBuildResult(int SpawnPoints, int TrackTriangles);

internal static class FpsArenaAssetBuilder
{
    private static readonly Regex GridNodeRegex = new("^AC_START_(\\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static FpsArenaBuildResult Build(string assettoCorsaRoot, string track,
        string? layout, string outputPath)
    {
        string trackRoot = Path.Combine(Path.GetFullPath(assettoCorsaRoot), "content", "tracks", track);
        string modelsIni = string.IsNullOrWhiteSpace(layout)
            ? Path.Combine(trackRoot, "models.ini")
            : Path.Combine(trackRoot, $"models_{layout}.ini");
        var triangles = new List<Kn5Triangle>();
        var grid = new SortedDictionary<int, RaceGridPose>();

        foreach (string modelFile in RacePhysicsAssetBuilder.ReadModelFiles(modelsIni, trackRoot, track))
        {
            var model = Kn5CollisionReader.Read(modelFile, RacePhysicsAssetBuilder.IsPhysicalTrackMesh);
            triangles.AddRange(model.Triangles);
            foreach (var node in model.NamedTransforms)
            {
                var match = GridNodeRegex.Match(node.Name);
                if (match.Success)
                    grid.TryAdd(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                        RaceGridPose.FromMatrix(node.Transform));
            }
        }

        if (triangles.Count == 0)
            throw new InvalidDataException($"No physical track meshes were found in {modelsIni}");
        if (grid.Count < 2)
            throw new InvalidDataException("An FPS arena needs at least two AC_START transforms for safe prototype spawns");

        var grounded = grid.Values.Take(32)
            .Select(pose => RacePhysicsAssetBuilder.GroundGridPose(pose, triangles))
            .ToArray();
        var spawns = grounded.Select(pose =>
        {
            var forward = Vector3.Transform(Vector3.UnitZ, pose.Orientation) with { Y = 0 };
            float yaw = forward.LengthSquared() > 0.0001f
                ? MathF.Atan2(forward.X, forward.Z)
                : 0;
            return new FpsArenaSpawn(FpsArenaPoint.From(pose.Position + Vector3.UnitY * 0.05f), yaw);
        }).ToArray();

        float minX = grounded.Min(pose => pose.Position.X) - 45;
        float maxX = grounded.Max(pose => pose.Position.X) + 45;
        float minZ = grounded.Min(pose => pose.Position.Z) - 45;
        float maxZ = grounded.Max(pose => pose.Position.Z) + 45;
        float minY = grounded.Min(pose => pose.Position.Y) - 2;
        float maxY = grounded.Max(pose => pose.Position.Y) + 20;
        var asset = new FpsArenaAsset
        {
            TrackId = track,
            LayoutId = layout ?? string.Empty,
            BoundsMin = new FpsArenaPoint(minX, minY, minZ),
            BoundsMax = new FpsArenaPoint(maxX, maxY, maxZ),
            SpawnPoints = spawns,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(asset, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
        return new FpsArenaBuildResult(spawns.Length, triangles.Count);
    }
}
