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
    private const int Version = 5;

    public required IReadOnlyList<RaceGridPose> Grid { get; init; }
    public required IReadOnlyList<Kn5Triangle> TrackTriangles { get; init; }
    public required IReadOnlyList<Kn5Triangle> TrackBarrierTriangles { get; init; }
    public required IReadOnlyDictionary<string, Vector3[]> CarColliderVertices { get; init; }
    public required IReadOnlyDictionary<string, RaceWheelCollider[]> CarWheelColliders { get; init; }

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

        var triangles = new Kn5Triangle[ReadCount(reader, 20_000_000, "track triangle")];
        for (int i = 0; i < triangles.Length; i++)
            triangles[i] = new Kn5Triangle(ReadVector3(reader), ReadVector3(reader), ReadVector3(reader));
        var barrierTriangles = new Kn5Triangle[ReadCount(reader, 20_000_000, "track barrier triangle")];
        for (int i = 0; i < barrierTriangles.Length; i++)
            barrierTriangles[i] = new Kn5Triangle(ReadVector3(reader), ReadVector3(reader), ReadVector3(reader));

        var cars = new Dictionary<string, Vector3[]>(StringComparer.OrdinalIgnoreCase);
        var wheelColliders = new Dictionary<string, RaceWheelCollider[]>(StringComparer.OrdinalIgnoreCase);
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
        }

        return new RacePhysicsAsset
        {
            Grid = grid,
            TrackTriangles = triangles,
            TrackBarrierTriangles = barrierTriangles,
            CarColliderVertices = cars,
            CarWheelColliders = wheelColliders
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
        if (gridTransforms.Count == 0 || gridTransforms.Keys.First() != 0 ||
            !gridTransforms.Keys.SequenceEqual(Enumerable.Range(0, gridTransforms.Count)))
            throw new InvalidDataException($"Track must expose a contiguous AC_START_0..n race grid: {track}");

        var groundedGrid = gridTransforms.Values
            .Select(pose => GroundGridPose(pose, trackTriangles))
            .ToArray();

        var colliders = new Dictionary<string, Vector3[]>(StringComparer.OrdinalIgnoreCase);
        var wheelColliders = new Dictionary<string, RaceWheelCollider[]>(StringComparer.OrdinalIgnoreCase);
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
            wheelColliders.Add(model, ReadWheelColliders(visualModel.NamedTransforms, model));
        }

        var asset = new RacePhysicsAsset
        {
            Grid = groundedGrid,
            TrackTriangles = trackTriangles,
            TrackBarrierTriangles = trackBarrierTriangles,
            CarColliderVertices = colliders,
            CarWheelColliders = wheelColliders
        };
        asset.Save(outputPath);
        return new RacePhysicsBuildResult(asset.Grid.Count,
            asset.TrackTriangles.Count + asset.TrackBarrierTriangles.Count, colliders.Count,
            includedMeshes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static IReadOnlyList<string> ReadModelFiles(string modelsIni, string trackRoot, string track)
    {
        if (!File.Exists(modelsIni))
        {
            string fallback = Path.Combine(trackRoot, $"{track}.kn5");
            if (!File.Exists(fallback))
                throw new FileNotFoundException($"Track models file was not found: {modelsIni}");
            return [fallback];
        }

        var files = new List<string>();
        foreach (string rawLine in File.ReadLines(modelsIni))
        {
            string line = rawLine.Trim();
            if (!line.StartsWith("FILE=", StringComparison.OrdinalIgnoreCase))
                continue;
            string relative = line[5..].Trim();
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
        const float maximumDropMeters = 5;
        float surfaceY = float.NegativeInfinity;
        foreach (var triangle in triangles)
        {
            if (!TryGetSurfaceHeight(triangle, pose.Position.X, pose.Position.Z, out float candidateY))
                continue;
            if (candidateY <= pose.Position.Y + 0.1f && candidateY >= pose.Position.Y - maximumDropMeters)
                surfaceY = Math.Max(surfaceY, candidateY);
        }

        if (!float.IsFinite(surfaceY))
            throw new InvalidDataException($"AC grid position {pose.Position} has no physical track surface below it");
        return pose with { Position = pose.Position with { Y = surfaceY } };
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
        IReadOnlyList<Kn5NamedTransform> transforms, string model)
    {
        string[] wheelNames = ["WHEEL_LF", "WHEEL_RF", "WHEEL_LR", "WHEEL_RR"];
        var wheels = new RaceWheelCollider[wheelNames.Length];
        for (int i = 0; i < wheelNames.Length; i++)
        {
            string name = wheelNames[i];
            var matches = transforms.Where(node => node.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException($"Car {model} must expose exactly one {name} transform; found {matches.Length}");

            var center = matches[0].Transform.Translation;
            float radius = center.Y;
            if (!float.IsFinite(radius) || radius is < 0.1f or > 1.5f)
                throw new InvalidDataException($"Car {model} has an invalid {name} wheel radius/height: {radius}");
            wheels[i] = new RaceWheelCollider(center, radius);
        }
        return wheels;
    }
}

internal readonly record struct RacePhysicsBuildResult(int GridSlots, int TrackTriangles, int CarColliders,
    IReadOnlyList<string> IncludedTrackMeshes);
