using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using BepuPhysics.Collidables;
using BepuUtilities.Memory;

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

internal sealed class RacePhysicsAsset
{
    private const string Magic = "ASRPHY01";
    private const int Version = 2;

    public required IReadOnlyList<RaceGridPose> Grid { get; init; }
    public required IReadOnlyList<Kn5Triangle> TrackTriangles { get; init; }
    public required IReadOnlyDictionary<string, Vector3[]> CarColliderVertices { get; init; }
    public required IReadOnlyDictionary<string, Vector3[]> CarVisualSupportVertices { get; init; }

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

        var cars = new Dictionary<string, Vector3[]>(StringComparer.OrdinalIgnoreCase);
        var visualSupports = new Dictionary<string, Vector3[]>(StringComparer.OrdinalIgnoreCase);
        int carCount = ReadCount(reader, 254, "car collider");
        for (int i = 0; i < carCount; i++)
        {
            string model = reader.ReadString();
            var vertices = new Vector3[ReadCount(reader, 1_000_000, "car collider vertex")];
            for (int j = 0; j < vertices.Length; j++)
                vertices[j] = ReadVector3(reader);
            cars.Add(model, vertices);
            var visualVertices = new Vector3[ReadCount(reader, 1_000_000, "car visual support vertex")];
            for (int j = 0; j < visualVertices.Length; j++)
                visualVertices[j] = ReadVector3(reader);
            visualSupports.Add(model, visualVertices);
        }

        return new RacePhysicsAsset
        {
            Grid = grid,
            TrackTriangles = triangles,
            CarColliderVertices = cars,
            CarVisualSupportVertices = visualSupports
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
        writer.Write(CarColliderVertices.Count);
        foreach (var (model, vertices) in CarColliderVertices.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            writer.Write(model);
            writer.Write(vertices.Length);
            foreach (var vertex in vertices)
                Write(writer, vertex);
            var visualVertices = CarVisualSupportVertices[model];
            writer.Write(visualVertices.Length);
            foreach (var vertex in visualVertices)
                Write(writer, vertex);
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
        var surfaceKeys = ReadSurfaceKeys(trackRoot, trackConfig);
        var trackTriangles = new List<Kn5Triangle>();
        var gridTransforms = new SortedDictionary<int, RaceGridPose>();
        var includedMeshes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string modelFile in modelFiles)
        {
            bool wallFile = Path.GetFileNameWithoutExtension(modelFile)
                .Contains("wall", StringComparison.OrdinalIgnoreCase);
            bool IncludeTrackMesh(string name) => wallFile || IsPhysicalTrackMesh(name, surfaceKeys);
            var data = Kn5CollisionReader.Read(modelFile, IncludeTrackMesh);
            trackTriangles.AddRange(data.Triangles);
            includedMeshes.UnionWith(data.MeshNames);
            foreach (var node in data.NamedTransforms)
            {
                var match = GridNodeRegex().Match(node.Name);
                if (match.Success)
                    gridTransforms.TryAdd(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                        RaceGridPose.FromMatrix(node.Transform));
            }
        }

        if (trackTriangles.Count == 0)
            throw new InvalidDataException($"No physical track meshes were found in {modelsIni}");
        if (gridTransforms.Count == 0 || gridTransforms.Keys.First() != 0 ||
            !gridTransforms.Keys.SequenceEqual(Enumerable.Range(0, gridTransforms.Count)))
            throw new InvalidDataException($"Track must expose a contiguous AC_START_0..n race grid: {track}");

        var colliders = new Dictionary<string, Vector3[]>(StringComparer.OrdinalIgnoreCase);
        var visualSupports = new Dictionary<string, Vector3[]>(StringComparer.OrdinalIgnoreCase);
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
            var visualModel = Kn5CollisionReader.Read(visualModelPath, _ => true, includeTriangles: false);
            var visualSupport = CreateSupportHull(Deduplicate(visualModel.Vertices));
            if (visualSupport.Length < 4)
                throw new InvalidDataException($"Car visual model has insufficient geometry: {model}");
            visualSupports.Add(model, visualSupport);
        }

        var asset = new RacePhysicsAsset
        {
            Grid = gridTransforms.Values.ToArray(),
            TrackTriangles = trackTriangles,
            CarColliderVertices = colliders,
            CarVisualSupportVertices = visualSupports
        };
        asset.Save(outputPath);
        return new RacePhysicsBuildResult(asset.Grid.Count, asset.TrackTriangles.Count, colliders.Count,
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

    private static HashSet<string> ReadSurfaceKeys(string trackRoot, string? trackConfig)
    {
        string layoutRoot = string.IsNullOrWhiteSpace(trackConfig) ? trackRoot : Path.Combine(trackRoot, trackConfig);
        string surfacesPath = Path.Combine(layoutRoot, "data", "surfaces.ini");
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(surfacesPath))
            return keys;
        foreach (string rawLine in File.ReadLines(surfacesPath))
        {
            string line = rawLine.Trim();
            if (line.StartsWith("KEY=", StringComparison.OrdinalIgnoreCase) && line.Length > 4)
                keys.Add(line[4..].Trim());
        }
        return keys;
    }

    internal static bool IsPhysicalTrackMesh(string meshName, IReadOnlySet<string> surfaceKeys)
    {
        string normalized = meshName;
        int digitCount = 0;
        while (digitCount < normalized.Length && char.IsDigit(normalized[digitCount]))
            digitCount++;
        if (digitCount > 0)
            return true; // AC's numbered mesh convention marks physical surfaces.
        normalized = normalized[digitCount..];
        int subIndex = normalized.IndexOf("_SUB", StringComparison.OrdinalIgnoreCase);
        if (subIndex >= 0)
            normalized = normalized[..subIndex];
        if (surfaceKeys.Any(key => normalized.StartsWith(key, StringComparison.OrdinalIgnoreCase)))
            return true;
        return normalized.StartsWith("WALL", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("COLLIDER", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("COLLISION", StringComparison.OrdinalIgnoreCase);
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

    private static Vector3[] CreateSupportHull(Vector3[] vertices)
    {
        var pool = new BufferPool();
        var hull = new ConvexHull(vertices.AsSpan(), pool, out var center);
        try
        {
            var result = new List<Vector3>();
            var seen = new HashSet<(ushort Bundle, ushort Inner)>();
            for (int i = 0; i < hull.FaceVertexIndices.Length; i++)
            {
                var index = hull.FaceVertexIndices[i];
                if (!seen.Add((index.BundleIndex, index.InnerIndex)))
                    continue;
                hull.GetPoint(index, out var point);
                result.Add(point + center);
            }
            return Deduplicate(result);
        }
        finally
        {
            hull.Dispose(pool);
            pool.Clear();
        }
    }
}

internal readonly record struct RacePhysicsBuildResult(int GridSlots, int TrackTriangles, int CarColliders,
    IReadOnlyList<string> IncludedTrackMeshes);
