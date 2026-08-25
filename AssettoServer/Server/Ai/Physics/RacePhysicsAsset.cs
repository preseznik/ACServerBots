using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace AssettoServer.Server.Ai.Physics;

public readonly record struct RaceGridPose(Vector3 Position, Quaternion Orientation)
{
    internal static RaceGridPose FromMatrix(Matrix4x4 matrix)
    {
        if (!Matrix4x4.Decompose(matrix, out _, out var orientation, out var position))
            throw new InvalidDataException("AC grid transform could not be decomposed");
        return new RaceGridPose(position, Quaternion.Normalize(orientation));
    }
}

internal readonly record struct RaceWheelCollider(Vector3 Center, float Radius);

internal sealed class RacePhysicsAsset
{
    private const string Magic = "ASRPHY01";
    private const int Version = 8;

    public required IReadOnlyList<RaceGridPose> Grid { get; init; }
    public required IReadOnlyList<RaceRoutePoint> RouteSurface { get; init; }
    public required IReadOnlyList<Kn5Triangle> TrackTriangles { get; init; }
    public required IReadOnlyList<Kn5Triangle> TrackBarrierTriangles { get; init; }
    public required IReadOnlyDictionary<string, Vector3[]> CarColliderVertices { get; init; }
    public required IReadOnlyDictionary<string, RaceWheelCollider[]> CarWheelColliders { get; init; }
    public required IReadOnlyDictionary<string, float> CarProtocolReferenceHeights { get; init; }

    public static RacePhysicsAsset Load(string path)
    {
        using var file = File.OpenRead(path);
        using var compressed = new BrotliStream(file, CompressionMode.Decompress);
        using var reader = new BinaryReader(compressed, Encoding.UTF8);
        if (Encoding.ASCII.GetString(reader.ReadBytes(Magic.Length)) != Magic)
            throw new InvalidDataException($"Invalid race physics asset: {path}");
        if (reader.ReadInt32() != Version)
            throw new InvalidDataException($"Unsupported race physics asset version: {path}");

        var grid = new RaceGridPose[ReadCount(reader, 254, "grid")];
        for (int i = 0; i < grid.Length; i++)
            grid[i] = new RaceGridPose(ReadVector3(reader), ReadQuaternion(reader));

        var routeSurface = new RaceRoutePoint[ReadCount(reader, 2_000_000, "route surface point")];
        for (int i = 0; i < routeSurface.Length; i++)
            routeSurface[i] = new RaceRoutePoint(ReadVector3(reader), reader.ReadSingle(),
                reader.ReadSingle(), ReadVector3(reader));

        var triangles = new Kn5Triangle[ReadCount(reader, 20_000_000, "track triangle")];
        for (int i = 0; i < triangles.Length; i++)
            triangles[i] = new Kn5Triangle(ReadVector3(reader), ReadVector3(reader), ReadVector3(reader));
        var barrierTriangles = new Kn5Triangle[ReadCount(reader, 20_000_000, "track barrier triangle")];
        for (int i = 0; i < barrierTriangles.Length; i++)
            barrierTriangles[i] = new Kn5Triangle(ReadVector3(reader), ReadVector3(reader), ReadVector3(reader));

        var cars = new Dictionary<string, Vector3[]>(StringComparer.OrdinalIgnoreCase);
        var wheelColliders = new Dictionary<string, RaceWheelCollider[]>(StringComparer.OrdinalIgnoreCase);
        var protocolReferenceHeights = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        int carCount = ReadCount(reader, 254, "car collider");
        for (int i = 0; i < carCount; i++)
        {
            string model = reader.ReadString();
            var vertices = new Vector3[ReadCount(reader, 1_000_000, "car collider vertex")];
            for (int j = 0; j < vertices.Length; j++)
                vertices[j] = ReadVector3(reader);
            cars.Add(model, vertices);
            var wheels = new RaceWheelCollider[ReadCount(reader, 16, "car wheel collider")];
            for (int j = 0; j < wheels.Length; j++)
                wheels[j] = new RaceWheelCollider(ReadVector3(reader), reader.ReadSingle());
            wheelColliders.Add(model, wheels);
            float protocolReferenceHeight = reader.ReadSingle();
            if (!float.IsFinite(protocolReferenceHeight) || protocolReferenceHeight is < 0.05f or > 2f)
                throw new InvalidDataException($"Invalid protocol reference height for {model}: {protocolReferenceHeight}");
            protocolReferenceHeights.Add(model, protocolReferenceHeight);
        }

        return new RacePhysicsAsset
        {
            Grid = grid,
            RouteSurface = routeSurface,
            TrackTriangles = triangles,
            TrackBarrierTriangles = barrierTriangles,
            CarColliderVertices = cars,
            CarWheelColliders = wheelColliders,
            CarProtocolReferenceHeights = protocolReferenceHeights
        };
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path);
        using var compressed = new BrotliStream(file, CompressionLevel.Optimal);
        using var writer = new BinaryWriter(compressed, Encoding.UTF8);
        writer.Write(Encoding.ASCII.GetBytes(Magic));
        writer.Write(Version);
        writer.Write(Grid.Count);
        foreach (var pose in Grid)
        {
            Write(writer, pose.Position);
            Write(writer, pose.Orientation);
        }
        writer.Write(RouteSurface.Count);
        foreach (var point in RouteSurface)
        {
            Write(writer, point.Position);
            writer.Write(point.SideLeft);
            writer.Write(point.SideRight);
            Write(writer, point.SurfaceNormal);
        }
        writer.Write(TrackTriangles.Count);
        foreach (var triangle in TrackTriangles)
        {
            Write(writer, triangle.A);
            Write(writer, triangle.B);
            Write(writer, triangle.C);
        }
        writer.Write(TrackBarrierTriangles.Count);
        foreach (var triangle in TrackBarrierTriangles)
        {
            Write(writer, triangle.A);
            Write(writer, triangle.B);
            Write(writer, triangle.C);
        }
        writer.Write(CarColliderVertices.Count);
        foreach (var (model, vertices) in CarColliderVertices.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            writer.Write(model);
            writer.Write(vertices.Length);
            foreach (var vertex in vertices)
                Write(writer, vertex);
            var wheels = CarWheelColliders[model];
            writer.Write(wheels.Length);
            foreach (var wheel in wheels)
            {
                Write(writer, wheel.Center);
                writer.Write(wheel.Radius);
            }
            writer.Write(CarProtocolReferenceHeights[model]);
        }
    }

    private static int ReadCount(BinaryReader reader, int maximum, string label)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > maximum)
            throw new InvalidDataException($"Invalid {label} count: {count}");
        return count;
    }

    private static Vector3 ReadVector3(BinaryReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    private static Quaternion ReadQuaternion(BinaryReader reader) => Quaternion.Normalize(
        new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));

    private static void Write(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }

    private static void Write(BinaryWriter writer, Quaternion value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
        writer.Write(value.W);
    }
}

internal static partial class RacePhysicsAssetBuilder
{
    private const float GridLaunchSupportBehindMeters = 3f;
    private const float GridLaunchSupportAheadMeters = 80f;
    private const float MinimumGridLaunchSupportHalfWidthMeters = 4.5f;
    private const float MaximumGridLaunchSupportHalfWidthMeters = 12f;
    private const float GridLaunchSupportLateralMarginMeters = 3f;
    private const float GridLaunchSupportVerticalMarginMeters = 1.5f;
    private const float MaximumRouteGridSnapMeters = 6f;
    private const float MaximumLayoutGridSnapMeters = 3f;

    [GeneratedRegex(@"^AC_START_(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GridNodeRegex();

    public static RacePhysicsBuildResult Build(string assettoCorsaRoot, string track, string? trackConfig,
        IEnumerable<string> carModels, string outputPath)
    {
        string gameRoot = Path.GetFullPath(assettoCorsaRoot);
        string trackRoot = Path.Combine(gameRoot, "content", "tracks", track);
        if (!Directory.Exists(trackRoot))
            throw new DirectoryNotFoundException($"Track is not installed: {track}");

        string modelsIni = string.IsNullOrWhiteSpace(trackConfig)
            ? Path.Combine(trackRoot, "models.ini")
            : Path.Combine(trackRoot, $"models_{trackConfig}.ini");
        string fastLanePath = RaceRouteSurfaceBuilder.FindFastLane(trackRoot, trackConfig);
        var route = RaceRouteSurfaceBuilder.ReadFastLane(fastLanePath);
        var modelFiles = ReadModelFiles(modelsIni, trackRoot, track);
        var trackTriangles = new List<Kn5Triangle>();
        var trackBarrierTriangles = new List<Kn5Triangle>();
        var gridTransforms = new SortedDictionary<int, RaceGridPose>();
        var includedMeshes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string modelFile in modelFiles)
        {
            var data = Kn5CollisionReader.Read(modelFile, IsPhysicalTrackMesh);
            foreach (var mesh in data.MeshRanges)
            {
                var target = IsBarrierTrackMesh(mesh.Name) ? trackBarrierTriangles : trackTriangles;
                for (int i = mesh.TriangleStart; i < mesh.TriangleStart + mesh.TriangleCount; i++)
                    target.Add(data.Triangles[i]);
            }
            includedMeshes.UnionWith(data.MeshNames);
            foreach (var node in data.NamedTransforms)
            {
                var match = GridNodeRegex().Match(node.Name);
                if (match.Success)
                    gridTransforms.TryAdd(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                        RaceGridPose.FromMatrix(node.Transform));
            }
        }

        trackTriangles = DeduplicateTriangles(trackTriangles);
        trackBarrierTriangles = DeduplicateTriangles(trackBarrierTriangles);
        if (trackTriangles.Count == 0)
            throw new InvalidDataException($"No physical track meshes were found in {modelsIni}");
        // Keep the selected layout's raw physical road for grid grounding. A legal staggered
        // grid can extend beyond the triangle samples retained by the centreline route filter
        // (Vallelunga Classic is one stock example), even though AC_START_n is directly above
        // a valid numbered ROAD mesh.
        var layoutGridGroundingTriangles = trackTriangles;
        var routeSurface = RaceRouteSurfaceBuilder.Filter(trackTriangles, route);
        trackTriangles = DeduplicateTriangles(routeSurface.Triangles);
        var routeBarriers = routeSurface.UsesSplineRibbon
            ? new RaceRouteBarrierResult([], trackBarrierTriangles.Count)
            : RaceRouteSurfaceBuilder.FilterBarriers(trackBarrierTriangles, route);
        trackBarrierTriangles = DeduplicateTriangles(routeBarriers.Triangles);
        if (trackTriangles.Count == 0)
            throw new InvalidDataException($"No physical road surface matched {fastLanePath}");
        if (gridTransforms.Count == 0 || gridTransforms.Keys.First() != 0 ||
            !gridTransforms.Keys.SequenceEqual(Enumerable.Range(0, gridTransforms.Count)))
            throw new InvalidDataException($"Track must expose a contiguous AC_START_0..n race grid: {track}");

        var groundedGrid = new RaceGridPose[gridTransforms.Count];
        int layoutGridGroundingFallbacks = 0;
        int snappedGridPositions = 0;
        float maximumGridSnapDistance = 0;
        int gridIndex = 0;
        foreach (var pose in gridTransforms.Values)
        {
            var grounded = GroundGridPose(pose,
                routeSurface.GridGroundingTriangles, layoutGridGroundingTriangles,
                out bool usedLayoutFallback, out float snapDistance);
            groundedGrid[gridIndex++] = grounded;
            if (usedLayoutFallback)
            {
                layoutGridGroundingFallbacks++;
            }
            if (snapDistance > 0.001f)
            {
                snappedGridPositions++;
                maximumGridSnapDistance = Math.Max(maximumGridSnapDistance, snapDistance);
            }
        }

        var unsupportedGrid = groundedGrid
            .Where(pose => !HasGridLaunchSupport(pose, trackTriangles))
            .ToArray();
        int gridLaunchSupportTriangles = 0;
        if (unsupportedGrid.Length > 0)
        {
            int routeTriangleCount = trackTriangles.Count;
            trackTriangles = DeduplicateTriangles(trackTriangles.Concat(
                unsupportedGrid.SelectMany(pose => BuildGridLaunchSupportTriangles(
                    pose, route, layoutGridGroundingTriangles))));
            gridLaunchSupportTriangles = trackTriangles.Count - routeTriangleCount;
        }
        foreach (var pose in groundedGrid)
        {
            if (!HasGridLaunchSupport(pose, trackTriangles))
            {
                throw new InvalidDataException($"AC grid position {pose.Position} has no continuous "
                                               + "launch footprint in the final race physics collider");
            }
        }

        var colliders = new Dictionary<string, Vector3[]>(StringComparer.OrdinalIgnoreCase);
        var wheelColliders = new Dictionary<string, RaceWheelCollider[]>(StringComparer.OrdinalIgnoreCase);
        var protocolReferenceHeights = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        var carCalibrations = new List<RaceCarCalibrationBuildResult>();
        foreach (string model in carModels.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string carRoot = Path.Combine(gameRoot, "content", "cars", model);
            string colliderPath = Path.Combine(carRoot, "collider.kn5");
            if (!File.Exists(colliderPath))
                throw new FileNotFoundException($"Car has no collider.kn5: {model}", colliderPath);
            var collider = Kn5CollisionReader.Read(colliderPath, _ => true);
            var vertices = Deduplicate(collider.Vertices);
            if (vertices.Length < 4)
                throw new InvalidDataException($"Car collider has insufficient geometry: {model}");
            colliders.Add(model, vertices);

            string visualModelPath = FindVisualModelFile(carRoot, model);
            var visualModel = Kn5CollisionReader.Read(visualModelPath, _ => false, includeTriangles: false);
            var calibration = AcCarPhysicsReader.Read(carRoot, model);
            var wheels = ReadWheelColliders(visualModel.NamedTransforms, model, calibration);
            wheelColliders.Add(model, wheels);
            float protocolReferenceHeight = GetProtocolReferenceHeight(visualModel.NamedTransforms,
                model, calibration);
            protocolReferenceHeights.Add(model, protocolReferenceHeight);
            carCalibrations.Add(new RaceCarCalibrationBuildResult(model, wheels[0].Radius,
                wheels[2].Radius, protocolReferenceHeight, calibration.GraphicsOffset,
                calibration.Source));
        }

        var asset = new RacePhysicsAsset
        {
            Grid = groundedGrid,
            RouteSurface = routeSurface.ProjectedRoute,
            TrackTriangles = trackTriangles,
            TrackBarrierTriangles = trackBarrierTriangles,
            CarColliderVertices = colliders,
            CarWheelColliders = wheelColliders,
            CarProtocolReferenceHeights = protocolReferenceHeights
        };
        asset.Save(outputPath);
        return new RacePhysicsBuildResult(asset.Grid.Count,
            asset.TrackTriangles.Count + asset.TrackBarrierTriangles.Count, colliders.Count,
            includedMeshes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            routeSurface.SourceTriangles, asset.TrackTriangles.Count, routeSurface.CenterlineCoverage,
            routeSurface.UsesSplineRibbon, routeBarriers.SourceTriangles,
            asset.TrackBarrierTriangles.Count, layoutGridGroundingFallbacks,
            snappedGridPositions, maximumGridSnapDistance,
            gridLaunchSupportTriangles, carCalibrations);
    }

    internal static IReadOnlyList<string> ReadModelFiles(string modelsIni, string trackRoot, string track)
    {
        if (!File.Exists(modelsIni))
        {
            string fallback = Path.Combine(trackRoot, $"{track}.kn5");
            if (!File.Exists(fallback))
                throw new FileNotFoundException($"Track models file was not found: {modelsIni}");
            return [fallback];
        }

        var files = new List<string>();
        // FILE entries in DYNAMIC_OBJECT sections are optional scenery spawned by the
        // client, not part of the physical circuit. Stock Spa references two such KN5s
        // that are intentionally absent from the dedicated-server installation.
        // Keep accepting sectionless model lists for older mod tracks.
        bool isTrackModelSection = true;
        foreach (string rawLine in File.ReadLines(modelsIni))
        {
            string line = rawLine.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                string section = line[1..^1].Trim();
                isTrackModelSection = section.Equals("MODEL", StringComparison.OrdinalIgnoreCase)
                                      || section.StartsWith("MODEL_", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!isTrackModelSection)
                continue;
            if (!line.StartsWith("FILE=", StringComparison.OrdinalIgnoreCase))
                continue;
            string relative = line[5..].Split(';', 2)[0].Trim();
            if (string.IsNullOrWhiteSpace(relative))
                continue;
            string fullPath = Path.GetFullPath(Path.Combine(trackRoot, relative));
            if (!fullPath.StartsWith(Path.GetFullPath(trackRoot) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Track models file escapes its track directory: {relative}");
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"Track KN5 referenced by models file was not found: {relative}", fullPath);
            files.Add(fullPath);
        }
        if (files.Count == 0)
            throw new InvalidDataException($"Track models file contains no FILE entries: {modelsIni}");
        return files;
    }

    internal static bool IsPhysicalTrackMesh(string meshName)
    {
        if (meshName.Length > 0 && char.IsDigit(meshName[0]))
            return true; // AC's numbered mesh convention marks physical surfaces.

        // Some mod tracks use explicit, unnumbered collision names. Do not infer collision from a
        // surfaces.ini key: visual meshes such as curb_graph and crb-grph share those prefixes and
        // can overlap the real road badly enough to launch rigid bodies.
        return meshName.StartsWith("WALL", StringComparison.OrdinalIgnoreCase)
               || meshName.Contains("COLLIDER", StringComparison.OrdinalIgnoreCase)
               || meshName.Contains("COLLISION", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsBarrierTrackMesh(string meshName)
    {
        int prefixLength = 0;
        while (prefixLength < meshName.Length && char.IsDigit(meshName[prefixLength]))
            prefixLength++;
        string normalized = meshName[prefixLength..];
        return normalized.StartsWith("WALL", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("COLLIDER", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("COLLISION", StringComparison.OrdinalIgnoreCase);
    }

    internal static List<Kn5Triangle> DeduplicateTriangles(IEnumerable<Kn5Triangle> triangles)
    {
        var seen = new HashSet<TriangleKey>();
        var result = new List<Kn5Triangle>();
        foreach (var triangle in triangles)
        {
            if (seen.Add(TriangleKey.FromTriangle(triangle)))
                result.Add(triangle);
        }
        return result;
    }

    private readonly record struct QuantizedVertex(long X, long Y, long Z) : IComparable<QuantizedVertex>
    {
        public int CompareTo(QuantizedVertex other)
        {
            int x = X.CompareTo(other.X);
            if (x != 0) return x;
            int y = Y.CompareTo(other.Y);
            return y != 0 ? y : Z.CompareTo(other.Z);
        }

        public static QuantizedVertex FromVector(Vector3 value) => new(
            (long)MathF.Round(value.X * 10_000),
            (long)MathF.Round(value.Y * 10_000),
            (long)MathF.Round(value.Z * 10_000));
    }

    private readonly record struct TriangleKey(QuantizedVertex A, QuantizedVertex B, QuantizedVertex C)
    {
        public static TriangleKey FromTriangle(Kn5Triangle triangle)
        {
            var vertices = new[]
            {
                QuantizedVertex.FromVector(triangle.A),
                QuantizedVertex.FromVector(triangle.B),
                QuantizedVertex.FromVector(triangle.C)
            };
            Array.Sort(vertices);
            return new TriangleKey(vertices[0], vertices[1], vertices[2]);
        }
    }

    private static Vector3[] Deduplicate(IEnumerable<Vector3> vertices)
    {
        var seen = new HashSet<(int X, int Y, int Z)>();
        var result = new List<Vector3>();
        foreach (var vertex in vertices)
        {
            var key = ((int)MathF.Round(vertex.X * 100_000), (int)MathF.Round(vertex.Y * 100_000),
                (int)MathF.Round(vertex.Z * 100_000));
            if (seen.Add(key))
                result.Add(vertex);
        }
        return result.ToArray();
    }

    private static string FindVisualModelFile(string carRoot, string model)
    {
        string lodsPath = Path.Combine(carRoot, "lods.ini");
        if (File.Exists(lodsPath))
        {
            foreach (string rawLine in File.ReadLines(lodsPath))
            {
                string line = rawLine.Trim();
                if (!line.StartsWith("FILE=", StringComparison.OrdinalIgnoreCase))
                    continue;
                string candidate = Path.GetFullPath(Path.Combine(carRoot, line[5..].Trim()));
                if (candidate.StartsWith(Path.GetFullPath(carRoot) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase) && File.Exists(candidate))
                    return candidate;
            }
        }

        string conventional = Path.Combine(carRoot, $"{model}.kn5");
        if (File.Exists(conventional))
            return conventional;
        return Directory.EnumerateFiles(carRoot, "*.kn5", SearchOption.TopDirectoryOnly)
                   .Where(path => !Path.GetFileName(path).Equals("collider.kn5", StringComparison.OrdinalIgnoreCase))
                   .OrderByDescending(path => new FileInfo(path).Length)
                   .FirstOrDefault()
               ?? throw new FileNotFoundException($"Car has no visual KN5 model: {model}", conventional);
    }

    internal static RaceGridPose GroundGridPose(RaceGridPose pose, IReadOnlyList<Kn5Triangle> triangles)
    {
        if (TryGroundGridPose(pose, triangles, 5, out var grounded))
            return grounded;
        throw new InvalidDataException($"AC grid position {pose.Position} has no physical track surface below it");
    }

    internal static RaceGridPose GroundGridPose(RaceGridPose pose,
        IReadOnlyList<Kn5Triangle> routeTriangles,
        IReadOnlyList<Kn5Triangle> layoutTriangles, out bool usedLayoutFallback)
        => GroundGridPose(pose, routeTriangles, layoutTriangles, out usedLayoutFallback, out _);

    internal static RaceGridPose GroundGridPose(RaceGridPose pose,
        IReadOnlyList<Kn5Triangle> routeTriangles,
        IReadOnlyList<Kn5Triangle> layoutTriangles, out bool usedLayoutFallback,
        out float snapDistance)
    {
        if (TryGroundGridPose(pose, routeTriangles, 5, out var grounded))
        {
            usedLayoutFallback = false;
            snapDistance = 0;
            return grounded;
        }

        if (TrySnapAndGroundGridPose(pose, routeTriangles, maximumDropMeters: 5,
                MaximumRouteGridSnapMeters, out grounded, out snapDistance))
        {
            usedLayoutFallback = false;
            return grounded;
        }

        // AC_START transforms normally sit roughly one metre above the road. Keep this fallback
        // deliberately tighter than route grounding so a missing upper deck cannot silently snap
        // a multilevel grid to unrelated geometry below it.
        if (TryGroundGridPose(pose, layoutTriangles, 1.5f, out grounded))
        {
            usedLayoutFallback = true;
            snapDistance = 0;
            return grounded;
        }


        if (TrySnapAndGroundGridPose(pose, layoutTriangles, maximumDropMeters: 1.5f,
                MaximumLayoutGridSnapMeters, out grounded, out snapDistance))
        {
            usedLayoutFallback = true;
            return grounded;
        }

        snapDistance = 0;
        throw new InvalidDataException($"AC grid position {pose.Position} has no physical track "
                                       + "surface below it in the selected route or layout");
    }

    private static bool TryGroundGridPose(RaceGridPose pose, IReadOnlyList<Kn5Triangle> triangles,
        float maximumDropMeters, out RaceGridPose grounded)
    {
        float surfaceY = float.NegativeInfinity;
        foreach (var triangle in triangles)
        {
            if (!TryGetSurfaceHeight(triangle, pose.Position.X, pose.Position.Z, out float candidateY))
                continue;
            if (candidateY <= pose.Position.Y + 0.1f && candidateY >= pose.Position.Y - maximumDropMeters)
                surfaceY = Math.Max(surfaceY, candidateY);
        }

        if (!float.IsFinite(surfaceY))
        {
            grounded = default;
            return false;
        }
        grounded = pose with { Position = pose.Position with { Y = surfaceY } };
        return true;
    }

    private static bool TrySnapAndGroundGridPose(RaceGridPose pose,
        IReadOnlyList<Kn5Triangle> triangles, float maximumDropMeters,
        float maximumSnapMeters, out RaceGridPose grounded, out float snapDistance)
    {
        Vector2 point = new(pose.Position.X, pose.Position.Z);
        float maximumDistanceSquared = maximumSnapMeters * maximumSnapMeters;
        float bestDistanceSquared = float.PositiveInfinity;
        float bestVerticalDifference = float.PositiveInfinity;
        Vector3 best = default;
        foreach (var triangle in triangles)
        {
            if (!RaceRouteSurfaceBuilder.IsUsableSurface(triangle))
                continue;
            Vector2 candidate = ClosestPointOnTriangleBoundary(point,
                new Vector2(triangle.A.X, triangle.A.Z),
                new Vector2(triangle.B.X, triangle.B.Z),
                new Vector2(triangle.C.X, triangle.C.Z));
            float distanceSquared = Vector2.DistanceSquared(point, candidate);
            if (distanceSquared > maximumDistanceSquared
                || !TryGetSurfaceHeight(triangle, candidate.X, candidate.Y, out float candidateY)
                || candidateY > pose.Position.Y + 0.1f
                || candidateY < pose.Position.Y - maximumDropMeters)
                continue;

            float verticalDifference = Math.Abs(candidateY - pose.Position.Y);
            if (distanceSquared > bestDistanceSquared + 1e-4f
                || Math.Abs(distanceSquared - bestDistanceSquared) <= 1e-4f
                && verticalDifference >= bestVerticalDifference)
                continue;
            bestDistanceSquared = distanceSquared;
            bestVerticalDifference = verticalDifference;
            best = new Vector3(candidate.X, candidateY, candidate.Y);
        }

        if (!float.IsFinite(bestDistanceSquared))
        {
            grounded = default;
            snapDistance = 0;
            return false;
        }

        grounded = pose with { Position = best };
        snapDistance = MathF.Sqrt(bestDistanceSquared);
        return true;
    }

    private static Vector2 ClosestPointOnTriangleBoundary(Vector2 point,
        Vector2 a, Vector2 b, Vector2 c)
    {
        Vector2 ab = ClosestPointOnSegment(point, a, b);
        Vector2 bc = ClosestPointOnSegment(point, b, c);
        Vector2 ca = ClosestPointOnSegment(point, c, a);
        float abDistance = Vector2.DistanceSquared(point, ab);
        float bcDistance = Vector2.DistanceSquared(point, bc);
        float caDistance = Vector2.DistanceSquared(point, ca);
        return abDistance <= bcDistance && abDistance <= caDistance
            ? ab
            : bcDistance <= caDistance ? bc : ca;
    }

    private static Vector2 ClosestPointOnSegment(Vector2 point, Vector2 from, Vector2 to)
    {
        Vector2 segment = to - from;
        float lengthSquared = segment.LengthSquared();
        float progress = lengthSquared <= 1e-8f
            ? 0
            : Math.Clamp(Vector2.Dot(point - from, segment) / lengthSquared, 0, 1);
        return from + segment * progress;
    }

    internal static IReadOnlyList<Kn5Triangle> BuildGridLaunchSupportTriangles(
        RaceGridPose pose, IReadOnlyList<RaceRoutePoint> route,
        IReadOnlyList<Kn5Triangle> layoutTriangles)
    {
        var forward = Vector3.Transform(Vector3.UnitZ, pose.Orientation) with { Y = 0 };
        if (forward.LengthSquared() < 1e-6f)
            forward = Vector3.UnitZ;
        else
            forward = Vector3.Normalize(forward);
        var from = pose.Position - forward * GridLaunchSupportBehindMeters;
        var to = pose.Position + forward * GridLaunchSupportAheadMeters;

        float nearestRouteDistance = route.Count == 0
            ? MinimumGridLaunchSupportHalfWidthMeters
            : MathF.Sqrt(route.Min(point => HorizontalDistanceSquared(point.Position, pose.Position)));
        float halfWidth = Math.Clamp(nearestRouteDistance + GridLaunchSupportLateralMarginMeters,
            MinimumGridLaunchSupportHalfWidthMeters, MaximumGridLaunchSupportHalfWidthMeters);

        float minimumY = pose.Position.Y;
        float maximumY = pose.Position.Y;
        float routeBandHalfWidth = halfWidth + GridLaunchSupportLateralMarginMeters;
        foreach (var point in route)
        {
            if (HorizontalDistanceSquaredToSegment(point.Position, from, to)
                > routeBandHalfWidth * routeBandHalfWidth)
                continue;
            minimumY = Math.Min(minimumY, point.Position.Y);
            maximumY = Math.Max(maximumY, point.Position.Y);
        }
        minimumY -= GridLaunchSupportVerticalMarginMeters;
        maximumY += GridLaunchSupportVerticalMarginMeters;

        var result = new List<Kn5Triangle>();
        foreach (var triangle in layoutTriangles)
        {
            float triangleMinimumY = Math.Min(triangle.A.Y, Math.Min(triangle.B.Y, triangle.C.Y));
            float triangleMaximumY = Math.Max(triangle.A.Y, Math.Max(triangle.B.Y, triangle.C.Y));
            if (triangleMaximumY < minimumY || triangleMinimumY > maximumY
                || !RaceRouteSurfaceBuilder.IsUsableSurface(triangle)
                || !TriangleTouchesHorizontalCorridor(triangle, from, to, halfWidth))
                continue;
            result.Add(triangle);
        }
        return result;
    }

    internal static bool HasGridLaunchSupport(RaceGridPose pose,
        IReadOnlyList<Kn5Triangle> finalTriangles)
    {
        var forward = Vector3.Transform(Vector3.UnitZ, pose.Orientation) with { Y = 0 };
        if (forward.LengthSquared() < 1e-6f)
            forward = Vector3.UnitZ;
        else
            forward = Vector3.Normalize(forward);
        var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        float[] longitudinalSamples = [-1.5f, 0, 1.5f, 5, 10, 15, 20, 25];
        float[] lateralSamples = [-1.15f, 0, 1.15f];
        foreach (float lateral in lateralSamples)
        {
            float referenceY = pose.Position.Y;
            foreach (float longitudinal in longitudinalSamples)
            {
                var sample = pose.Position + forward * longitudinal + right * lateral;
                if (!TryFindSurfaceHeight(sample, referenceY, finalTriangles,
                        maximumHeightDifferenceMeters: 1.5f, out referenceY))
                    return false;
            }
        }
        return true;
    }

    private static bool TryFindSurfaceHeight(Vector3 point, float referenceY,
        IReadOnlyList<Kn5Triangle> triangles, float maximumHeightDifferenceMeters,
        out float surfaceY)
    {
        surfaceY = 0;
        float bestDifference = float.PositiveInfinity;
        foreach (var triangle in triangles)
        {
            if (!TryGetSurfaceHeight(triangle, point.X, point.Z, out float candidateY))
                continue;
            float difference = Math.Abs(candidateY - referenceY);
            if (difference > maximumHeightDifferenceMeters || difference >= bestDifference)
                continue;
            bestDifference = difference;
            surfaceY = candidateY;
        }
        return float.IsFinite(bestDifference);
    }

    private static bool TriangleTouchesHorizontalCorridor(Kn5Triangle triangle,
        Vector3 from, Vector3 to, float halfWidth)
    {
        Span<Vector3> samples = stackalloc Vector3[7]
        {
            triangle.A,
            triangle.B,
            triangle.C,
            (triangle.A + triangle.B) * 0.5f,
            (triangle.B + triangle.C) * 0.5f,
            (triangle.C + triangle.A) * 0.5f,
            (triangle.A + triangle.B + triangle.C) / 3f
        };
        float maximumDistanceSquared = halfWidth * halfWidth;
        foreach (var sample in samples)
        {
            if (HorizontalDistanceSquaredToSegment(sample, from, to) <= maximumDistanceSquared)
                return true;
        }
        return false;
    }

    private static float HorizontalDistanceSquared(Vector3 first, Vector3 second)
    {
        float x = first.X - second.X;
        float z = first.Z - second.Z;
        return x * x + z * z;
    }

    private static float HorizontalDistanceSquaredToSegment(Vector3 point,
        Vector3 from, Vector3 to)
    {
        var segment = (to - from) with { Y = 0 };
        var relative = (point - from) with { Y = 0 };
        float lengthSquared = segment.LengthSquared();
        float progress = lengthSquared <= 1e-6f
            ? 0
            : Math.Clamp(Vector3.Dot(relative, segment) / lengthSquared, 0, 1);
        var closest = from + segment * progress;
        return HorizontalDistanceSquared(point, closest);
    }

    private static bool TryGetSurfaceHeight(Kn5Triangle triangle, float x, float z, out float height)
    {
        float normalY = Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A).Y;
        if (Math.Abs(normalY) <= 1e-6f)
        {
            height = 0;
            return false;
        }

        float denominator = (triangle.B.Z - triangle.C.Z) * (triangle.A.X - triangle.C.X)
                            + (triangle.C.X - triangle.B.X) * (triangle.A.Z - triangle.C.Z);
        if (Math.Abs(denominator) < 1e-8f)
        {
            height = 0;
            return false;
        }

        float a = ((triangle.B.Z - triangle.C.Z) * (x - triangle.C.X)
                   + (triangle.C.X - triangle.B.X) * (z - triangle.C.Z)) / denominator;
        float b = ((triangle.C.Z - triangle.A.Z) * (x - triangle.C.X)
                   + (triangle.A.X - triangle.C.X) * (z - triangle.C.Z)) / denominator;
        float c = 1 - a - b;
        const float edgeTolerance = -1e-4f;
        if (a < edgeTolerance || b < edgeTolerance || c < edgeTolerance)
        {
            height = 0;
            return false;
        }

        height = a * triangle.A.Y + b * triangle.B.Y + c * triangle.C.Y;
        return true;
    }

    internal static RaceWheelCollider[] ReadWheelColliders(
        IReadOnlyList<Kn5NamedTransform> transforms, string model,
        RaceCarPhysicsCalibration calibration = default)
    {
        string[] wheelNames = ["WHEEL_LF", "WHEEL_RF", "WHEEL_LR", "WHEEL_RR"];
        var wheels = new RaceWheelCollider[wheelNames.Length];
        for (int i = 0; i < wheelNames.Length; i++)
        {
            string name = wheelNames[i];
            var matches = transforms.Where(node => node.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException($"Car {model} must expose exactly one {name} transform; found {matches.Length}");

            var visualCenter = matches[0].Transform.Translation;
            float radius = calibration.IsAuthoritative
                ? i < 2 ? calibration.FrontTyreRadius : calibration.RearTyreRadius
                : visualCenter.Y;
            if (!float.IsFinite(radius) || radius is < 0.1f or > 1.5f)
                throw new InvalidDataException($"Car {model} has an invalid {name} wheel radius/height: {radius}");
            // The KN5 wheel node is a visual transform and is not a reliable tyre radius. Keep its
            // authored axle position, but ground the physical wheel center using the actual tyre.
            var physicalCenter = visualCenter with { Y = radius };
            wheels[i] = new RaceWheelCollider(physicalCenter, radius);
        }
        return wheels;
    }

    internal static float GetProtocolReferenceHeight(IReadOnlyList<Kn5NamedTransform> transforms,
        string model, RaceCarPhysicsCalibration calibration)
    {
        string[] wheelNames = ["WHEEL_LF", "WHEEL_RF", "WHEEL_LR", "WHEEL_RR"];
        float visualWheelHeight = wheelNames.Select(name => transforms.Single(node =>
                node.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Transform.Translation.Y)
            .Average();
        // AC applies GRAPHICS_OFFSET to the rendered KN5 client-side. The network reference must
        // compensate for that offset and for visual wheel nodes that are not authored at the real
        // tyre radius, otherwise some cars render deeply buried while their physical wheels are
        // correctly grounded.
        float referenceHeight = calibration.IsAuthoritative
            ? GetProtocolReferenceHeight(
                (calibration.FrontTyreRadius + calibration.RearTyreRadius) * 0.5f,
                visualWheelHeight, calibration.GraphicsOffset.Y)
            : visualWheelHeight;
        if (!float.IsFinite(referenceHeight) || referenceHeight is < 0.05f or > 2f)
            throw new InvalidDataException($"Car {model} has an invalid visual/protocol reference height: {referenceHeight}");
        return referenceHeight;
    }

    internal static float GetProtocolReferenceHeight(float tyreRadius, float visualWheelHeight,
        float graphicsOffsetY)
    {
        const float visualTyreClearanceMeters = 0.02f;
        return tyreRadius - visualWheelHeight - graphicsOffsetY + visualTyreClearanceMeters;
    }
}

internal readonly record struct RacePhysicsBuildResult(int GridSlots, int TrackTriangles, int CarColliders,
    IReadOnlyList<string> IncludedTrackMeshes, int SourceDriveTriangles, int RouteDriveTriangles,
    double RouteCoverage, bool UsesSplineRibbon, int SourceBarrierTriangles, int RouteBarrierTriangles,
    int LayoutGridGroundingFallbacks, int SnappedGridPositions, float MaximumGridSnapDistance,
    int GridLaunchSupportTriangles,
    IReadOnlyList<RaceCarCalibrationBuildResult> CarCalibrations);

internal readonly record struct RaceCarCalibrationBuildResult(string Model, float FrontTyreRadius,
    float RearTyreRadius, float ProtocolReferenceHeight, Vector3 GraphicsOffset, string Source);
