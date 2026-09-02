using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using AssettoServer.Server.Ai.Physics;

namespace AssettoServer.Server.Fps;

internal sealed class FpsArenaAsset
{
    public const int CurrentPreparationVersion = 4;
    public int PreparationVersion { get; init; } = CurrentPreparationVersion;
    public required string TrackId { get; init; }
    public required string LayoutId { get; init; }
    public required FpsArenaPoint BoundsMin { get; init; }
    public required FpsArenaPoint BoundsMax { get; init; }
    public required IReadOnlyList<FpsArenaSpawn> SpawnPoints { get; init; }
    public required FpsArenaNavigationSummary Navigation { get; init; }
    public FpsArenaCollisionSummary? Collision { get; init; }
    public float BoundsPaddingMeters { get; init; } = 45;
    public IReadOnlyList<string> CollisionIncludeMeshes { get; init; } = [];
    public IReadOnlyList<string> CollisionExcludeMeshes { get; init; } = [];
}

internal sealed record FpsArenaPoint(float X, float Y, float Z)
{
    public static FpsArenaPoint From(Vector3 value) => new(value.X, value.Y, value.Z);
}

internal sealed record FpsArenaSpawn(FpsArenaPoint Position, float YawRadians);
internal sealed record FpsArenaNavigationSummary(int Version, float CellSize, int NodeCount,
    int ComponentCount, int ConnectedSpawnCount, int WalkLinkCount, int TraversalLinkCount);
internal sealed record FpsArenaCollisionSummary(int Version, int TriangleCount,
    int BvhNodeCount, int BvhLeafCount, int MaximumLeafTriangles);
internal sealed record FpsArenaBuildResult(int SpawnPoints, int TrackTriangles,
    int PhysicalTriangles, int SupplementalTriangles, int CollisionMeshes,
    int NavigationNodes, int NavigationComponents, int ConnectedNavigationSpawns,
    int NavigationWalkLinks, int NavigationTraversalLinks, int BvhNodes, int BvhLeaves,
    int BvhMaximumLeafTriangles);

internal static class FpsArenaAssetBuilder
{
    private static readonly Regex GridNodeRegex = new("^AC_START_(\\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly string[] AutomaticSolidTokens =
    [
        "WALL", "BARRIER", "FLOOR", "STAIR", "ROOF", "CEILING", "BUILDING",
        "HOUSE", "COLUMN", "PILLAR", "FENCE", "RAIL", "DOOR", "ROCK",
        "PLATFORM", "RAMP",
    ];
    private static readonly string[] AutomaticNonSolidTokens =
    [
        "GRASS", "TREE", "BUSH", "LEAF", "DECAL", "SHADOW", "SKY", "CLOUD",
        "LIGHT", "GLOW", "SMOKE", "WATER", "GLASS", "WINDOW", "BANNER", "FLAG",
        "CROWD", "SPECTATOR", "BILLBOARD", "FX_",
    ];

    public static FpsArenaBuildResult Build(string assettoCorsaRoot, string track,
        string? layout, string outputPath, string? geometryOutputPath = null,
        string? navigationOutputPath = null,
        IEnumerable<string>? collisionIncludeMeshes = null,
        IEnumerable<string>? collisionExcludeMeshes = null,
        float boundsPaddingMeters = 45)
    {
        if (!float.IsFinite(boundsPaddingMeters) || boundsPaddingMeters is < 5 or > 100)
            throw new ArgumentOutOfRangeException(nameof(boundsPaddingMeters),
                "FPS arena bounds padding must be between 5 and 100 metres");
        string trackRoot = Path.Combine(Path.GetFullPath(assettoCorsaRoot), "content", "tracks", track);
        string modelsIni = string.IsNullOrWhiteSpace(layout)
            ? Path.Combine(trackRoot, "models.ini")
            : Path.Combine(trackRoot, $"models_{layout}.ini");
        var physicalTriangles = new List<Kn5Triangle>();
        var supplementalTriangles = new List<Kn5Triangle>();
        var grid = new SortedDictionary<int, RaceGridPose>();
        string[] includePatterns = NormalizePatterns(collisionIncludeMeshes);
        string[] excludePatterns = NormalizePatterns(collisionExcludeMeshes);
        int collisionMeshes = 0;

        foreach (string modelFile in RacePhysicsAssetBuilder.ReadModelFiles(modelsIni, trackRoot, track))
        {
            bool collisionProxy = IsCollisionProxyFile(modelFile);
            bool IncludeMesh(string name) => ShouldIncludeMesh(name, collisionProxy,
                includePatterns, excludePatterns);
            var model = Kn5CollisionReader.Read(modelFile, IncludeMesh);
            collisionMeshes += model.MeshRanges.Count;
            foreach (var range in model.MeshRanges)
            {
                var destination = RacePhysicsAssetBuilder.IsPhysicalTrackMesh(range.Name)
                    ? physicalTriangles
                    : supplementalTriangles;
                destination.AddRange(model.Triangles.GetRange(range.TriangleStart,
                    range.TriangleCount));
            }
            foreach (var node in model.NamedTransforms)
            {
                var match = GridNodeRegex.Match(node.Name);
                if (match.Success)
                    grid.TryAdd(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                        RaceGridPose.FromMatrix(node.Transform));
            }
        }

        if (physicalTriangles.Count == 0)
            throw new InvalidDataException($"No physical track meshes were found in {modelsIni}");
        if (grid.Count < 2)
            throw new InvalidDataException("An FPS arena needs at least two AC_START transforms for safe prototype spawns");

        var grounded = grid.Values.Take(32)
            .Select(pose => RacePhysicsAssetBuilder.GroundGridPose(pose, physicalTriangles))
            .ToArray();
        var spawns = grounded.Select(pose =>
        {
            var forward = Vector3.Transform(Vector3.UnitZ, pose.Orientation) with { Y = 0 };
            float yaw = forward.LengthSquared() > 0.0001f
                ? MathF.Atan2(forward.X, forward.Z)
                : 0;
            return new FpsArenaSpawn(FpsArenaPoint.From(pose.Position + Vector3.UnitY * 0.05f), yaw);
        }).ToArray();

        float minX = grounded.Min(pose => pose.Position.X) - boundsPaddingMeters;
        float maxX = grounded.Max(pose => pose.Position.X) + boundsPaddingMeters;
        float minZ = grounded.Min(pose => pose.Position.Z) - boundsPaddingMeters;
        float maxZ = grounded.Max(pose => pose.Position.Z) + boundsPaddingMeters;
        float minY = grounded.Min(pose => pose.Position.Y) - 2;
        float maxY = grounded.Max(pose => pose.Position.Y) + 20;
        var boundedPhysical = RacePhysicsAssetBuilder.DeduplicateTriangles(physicalTriangles)
            .Where(triangle => TouchesBounds(triangle, minX, minY, minZ, maxX, maxY, maxZ))
            .ToArray();
        var boundedSupplemental = RacePhysicsAssetBuilder.DeduplicateTriangles(supplementalTriangles)
            .Where(triangle => TouchesBounds(triangle, minX, minY, minZ, maxX, maxY, maxZ))
            .ToArray();
        var arenaTriangles = RacePhysicsAssetBuilder.DeduplicateTriangles(
            boundedPhysical.Concat(boundedSupplemental).ToArray()).ToArray();
        if (arenaTriangles.Length == 0)
            throw new InvalidDataException("No collision geometry intersects the prepared FPS arena bounds");

        var boundsMin = new FpsArenaPoint(minX, minY, minZ);
        var boundsMax = new FpsArenaPoint(maxX, maxY, maxZ);
        var surface = new FpsArenaSurface(arenaTriangles);
        var navigation = FpsArenaNavigationBuilder.Build(surface, boundsMin, boundsMax, spawns);
        var asset = new FpsArenaAsset
        {
            TrackId = track,
            LayoutId = layout ?? string.Empty,
            BoundsMin = boundsMin,
            BoundsMax = boundsMax,
            SpawnPoints = spawns,
            Navigation = new FpsArenaNavigationSummary(FpsArenaNavigationAsset.CurrentVersion,
                navigation.Asset.CellSize, navigation.Asset.Nodes.Count,
                navigation.Asset.ComponentCount, navigation.ConnectedSpawnPoints,
                navigation.WalkLinks, navigation.TraversalLinks),
            Collision = new FpsArenaCollisionSummary(1, surface.TriangleCount,
                surface.BvhNodeCount, surface.BvhLeafCount,
                surface.BvhMaximumLeafTriangles),
            BoundsPaddingMeters = boundsPaddingMeters,
            CollisionIncludeMeshes = includePatterns,
            CollisionExcludeMeshes = excludePatterns,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(asset, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
        if (!string.IsNullOrWhiteSpace(geometryOutputPath))
            new FpsArenaGeometryAsset { Triangles = arenaTriangles }.Save(geometryOutputPath);
        if (!string.IsNullOrWhiteSpace(navigationOutputPath))
            navigation.Asset.Save(navigationOutputPath);
        return new FpsArenaBuildResult(spawns.Length, arenaTriangles.Length,
            boundedPhysical.Length, boundedSupplemental.Length, collisionMeshes,
            navigation.Asset.Nodes.Count, navigation.Asset.ComponentCount,
            navigation.ConnectedSpawnPoints, navigation.WalkLinks, navigation.TraversalLinks,
            surface.BvhNodeCount, surface.BvhLeafCount,
            surface.BvhMaximumLeafTriangles);
    }

    internal static bool ShouldIncludeMesh(string name, bool collisionProxy,
        IReadOnlyList<string>? includePatterns = null,
        IReadOnlyList<string>? excludePatterns = null)
    {
        if (MatchesAny(name, excludePatterns)) return false;
        if (RacePhysicsAssetBuilder.IsPhysicalTrackMesh(name)) return true;
        if (MatchesAny(name, includePatterns)) return true;
        if (collisionProxy) return true;
        if (name.StartsWith("FPV_", StringComparison.OrdinalIgnoreCase)) return true;
        if (AutomaticNonSolidTokens.Any(token => name.Contains(token,
                StringComparison.OrdinalIgnoreCase))) return false;
        return AutomaticSolidTokens.Any(token => name.Contains(token,
            StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCollisionProxyFile(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        return name.Contains("collision", StringComparison.OrdinalIgnoreCase)
            || name.Contains("collider", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] NormalizePatterns(IEnumerable<string>? patterns) => patterns?
        .SelectMany(pattern => pattern.Split([';', ','], StringSplitOptions.RemoveEmptyEntries))
        .Select(pattern => pattern.Trim())
        .Where(pattern => pattern.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray() ?? [];

    private static bool MatchesAny(string name, IReadOnlyList<string>? patterns)
    {
        if (patterns is null) return false;
        foreach (string pattern in patterns)
        {
            if (FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true))
                return true;
        }
        return false;
    }

    private static bool TouchesBounds(Kn5Triangle triangle, float minX, float minY, float minZ,
        float maxX, float maxY, float maxZ)
    {
        float triangleMinX = MathF.Min(triangle.A.X, MathF.Min(triangle.B.X, triangle.C.X));
        float triangleMinY = MathF.Min(triangle.A.Y, MathF.Min(triangle.B.Y, triangle.C.Y));
        float triangleMinZ = MathF.Min(triangle.A.Z, MathF.Min(triangle.B.Z, triangle.C.Z));
        float triangleMaxX = MathF.Max(triangle.A.X, MathF.Max(triangle.B.X, triangle.C.X));
        float triangleMaxY = MathF.Max(triangle.A.Y, MathF.Max(triangle.B.Y, triangle.C.Y));
        float triangleMaxZ = MathF.Max(triangle.A.Z, MathF.Max(triangle.B.Z, triangle.C.Z));
        return triangleMaxX >= minX && triangleMinX <= maxX
            && triangleMaxY >= minY && triangleMinY <= maxY
            && triangleMaxZ >= minZ && triangleMinZ <= maxZ;
    }
}
