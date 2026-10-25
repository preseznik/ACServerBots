using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

namespace AssettoServer.Server.Ai.Physics;

internal enum Kn5NodeClass
{
    Base = 1,
    Mesh = 2,
    SkinnedMesh = 3
}

internal readonly record struct Kn5Triangle(Vector3 A, Vector3 B, Vector3 C);
internal readonly record struct Kn5NamedTransform(string Name, Matrix4x4 Transform);
internal readonly record struct Kn5CollisionMeshRange(string Name, int TriangleStart, int TriangleCount,
    Vector3 BoundsMin, Vector3 BoundsMax);

internal sealed class Kn5CollisionData
{
    public List<Kn5Triangle> Triangles { get; } = [];
    public List<Vector3> Vertices { get; } = [];
    public List<Kn5NamedTransform> NamedTransforms { get; } = [];
    public List<Kn5CollisionMeshRange> MeshRanges { get; } = [];
    public HashSet<string> MeshNames { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Minimal reader for the public KN5 container format. It deliberately ignores textures,
/// materials and rendering data and retains only node transforms and collision geometry.
/// </summary>
internal static class Kn5CollisionReader
{
    private const int MaxCollectionCount = 50_000_000;

    public static Kn5CollisionData Read(string path, Func<string, bool> includeMesh, bool includeTriangles = true)
    {
        bool hasLeadingEmptyTexture = false;
        try
        {
            return ReadFile(path, includeMesh, includeTriangles, false, out hasLeadingEmptyTexture);
        }
        catch (InvalidDataException) when (hasLeadingEmptyTexture)
        {
            // Some KN5 exporters count a leading four-byte zero placeholder as a texture.
            // Try the full record first so ordinary inactive textures retain their layout.
            try
            {
                return ReadFile(path, includeMesh, includeTriangles, true, out _);
            }
            catch (InvalidDataException)
            {
                // Preserve the original diagnostic if the placeholder layout is invalid too.
            }
            throw;
        }
    }

    private static Kn5CollisionData ReadFile(string path, Func<string, bool> includeMesh,
        bool includeTriangles, bool skipLeadingEmptyTexture, out bool hasLeadingEmptyTexture)
    {
        hasLeadingEmptyTexture = false;
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        string stage = "header";
        try
        {
            if (Encoding.ASCII.GetString(reader.ReadBytes(6)) != "sc6969")
                throw new InvalidDataException($"Not a KN5 file: {path}");

            int version = reader.ReadInt32();
            if (version > 5)
                reader.ReadInt32();

            stage = "texture count";
            int textureCount = ReadCount(reader, "texture");
            for (int i = 0; i < textureCount; i++)
            {
                stage = $"texture {i} header";
                int state = reader.ReadInt32();
                if (i == 0 && state == 0)
                {
                    hasLeadingEmptyTexture = true;
                    if (skipLeadingEmptyTexture)
                        continue;
                }
                string name = ReadString(reader);
                stage = $"texture {i} '{name}' data";
                int length = checked((int)reader.ReadUInt32());
                Skip(reader, length);
            }

            stage = "material count";
            int materialCount = ReadCount(reader, "material");
            for (int i = 0; i < materialCount; i++)
            {
                stage = $"material {i} header";
                SkipMaterial(reader, ref stage);
            }

            var result = new Kn5CollisionData();
            stage = "root node header";
            ReadNode(reader, Matrix4x4.Identity, includeMesh, includeTriangles, result, ref stage);
            return result;
        }
        catch (Exception exception)
        {
            throw new InvalidDataException($"Failed to parse KN5 '{Path.GetFullPath(path)}' "
                                           + $"during {stage} at byte {stream.Position} of {stream.Length}: "
                                           + exception.Message, exception);
        }
    }

    private static void ReadNode(BinaryReader reader, Matrix4x4 parentTransform,
        Func<string, bool> includeMesh, bool includeTriangles, Kn5CollisionData result, ref string stage)
    {
        var nodeClass = (Kn5NodeClass)reader.ReadInt32();
        if (nodeClass is < Kn5NodeClass.Base or > Kn5NodeClass.SkinnedMesh)
            throw new InvalidDataException($"Unsupported KN5 node class: {(int)nodeClass}");

        string name = ReadString(reader);
        stage = $"node '{name}' header";
        int childCount = ReadCount(reader, "node child");
        bool active = reader.ReadBoolean();
        Matrix4x4 worldTransform = parentTransform;

        switch (nodeClass)
        {
            case Kn5NodeClass.Base:
            {
                stage = $"node '{name}' transform";
                var localTransform = ReadMatrix(reader);
                worldTransform = localTransform * parentTransform;
                if (active)
                    result.NamedTransforms.Add(new Kn5NamedTransform(name, worldTransform));
                break;
            }
            case Kn5NodeClass.Mesh:
                ReadMesh(reader, name, active, worldTransform, includeMesh, includeTriangles, result, false, ref stage);
                break;
            case Kn5NodeClass.SkinnedMesh:
                ReadMesh(reader, name, active, worldTransform, includeMesh, includeTriangles, result, true, ref stage);
                break;
        }

        for (int i = 0; i < childCount; i++)
        {
            stage = $"child {i} header of node '{name}'";
            ReadNode(reader, worldTransform, includeMesh, includeTriangles, result, ref stage);
        }
    }

    private static void ReadMesh(BinaryReader reader, string name, bool active, Matrix4x4 worldTransform,
        Func<string, bool> includeMesh, bool includeTriangles, Kn5CollisionData result, bool skinned,
        ref string stage)
    {
        stage = $"mesh '{name}' flags";
        reader.ReadBoolean(); // Cast shadows
        reader.ReadBoolean(); // Visible
        reader.ReadBoolean(); // Transparent

        if (skinned)
        {
            stage = $"mesh '{name}' bones";
            int boneCount = checked((int)reader.ReadUInt32());
            ValidateCount(boneCount, "bone");
            for (int i = 0; i < boneCount; i++)
            {
                ReadString(reader);
                Skip(reader, 16 * sizeof(float));
            }
        }

        stage = $"mesh '{name}' vertices";
        int vertexCount = checked((int)reader.ReadUInt32());
        ValidateCount(vertexCount, "vertex");
        var vertices = new Vector3[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            vertices[i] = Vector3.Transform(ReadVector3(reader), worldTransform);
            Skip(reader, skinned ? 64 : 32); // normal, UV, tangent, optional weights and bone indices
        }

        stage = $"mesh '{name}' indices";
        int indexCount = checked((int)reader.ReadUInt32());
        ValidateCount(indexCount, "index");
        var indices = new ushort[indexCount];
        for (int i = 0; i < indexCount; i++)
            indices[i] = reader.ReadUInt16();

        stage = $"mesh '{name}' properties";
        reader.ReadUInt32(); // Material id
        reader.ReadUInt32(); // Layer
        reader.ReadSingle(); // LOD in
        reader.ReadSingle(); // LOD out
        if (!skinned)
        {
            Skip(reader, 4 * sizeof(float)); // bounding sphere
            reader.ReadBoolean(); // renderable
        }

        if (!active || !includeMesh(name))
            return;

        result.MeshNames.Add(name);
        result.Vertices.AddRange(vertices);
        if (!includeTriangles)
            return;
        stage = $"mesh '{name}' triangles";
        int triangleStart = result.Triangles.Count;
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            int a = indices[i];
            int b = indices[i + 1];
            int c = indices[i + 2];
            if (a >= vertices.Length || b >= vertices.Length || c >= vertices.Length)
                throw new InvalidDataException($"KN5 mesh {name} contains an invalid vertex index");
            var triangle = new Kn5Triangle(vertices[a], vertices[b], vertices[c]);
            if (Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A).LengthSquared() > 1e-10f)
                result.Triangles.Add(triangle);
        }
        if (result.Triangles.Count > triangleStart)
        {
            var boundsMin = new Vector3(float.PositiveInfinity);
            var boundsMax = new Vector3(float.NegativeInfinity);
            foreach (var vertex in vertices)
            {
                boundsMin = Vector3.Min(boundsMin, vertex);
                boundsMax = Vector3.Max(boundsMax, vertex);
            }
            result.MeshRanges.Add(new Kn5CollisionMeshRange(name, triangleStart,
                result.Triangles.Count - triangleStart, boundsMin, boundsMax));
        }
    }

    private static void SkipMaterial(BinaryReader reader, ref string stage)
    {
        string name = ReadString(reader);
        stage = $"material '{name}' header";
        ReadString(reader);
        Skip(reader, 6); // blend, alpha tested, depth mode
        stage = $"material '{name}' properties";
        int propertyCount = ReadCount(reader, "material property");
        for (int i = 0; i < propertyCount; i++)
        {
            ReadString(reader);
            Skip(reader, 10 * sizeof(float));
        }
        stage = $"material '{name}' texture mappings";
        int mappingCount = ReadCount(reader, "texture mapping");
        for (int i = 0; i < mappingCount; i++)
        {
            ReadString(reader);
            reader.ReadInt32();
            ReadString(reader);
        }
    }

    private static Matrix4x4 ReadMatrix(BinaryReader reader) => new(
        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    private static Vector3 ReadVector3(BinaryReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    private static string ReadString(BinaryReader reader)
    {
        int length = ReadCount(reader, "string");
        return Encoding.UTF8.GetString(reader.ReadBytes(length));
    }

    private static int ReadCount(BinaryReader reader, string label)
    {
        int count = reader.ReadInt32();
        ValidateCount(count, label);
        return count;
    }

    private static void ValidateCount(int count, string label)
    {
        if (count < 0 || count > MaxCollectionCount)
            throw new InvalidDataException($"Invalid KN5 {label} count: {count}");
    }

    private static void Skip(BinaryReader reader, int length)
    {
        if (length < 0 || reader.BaseStream.Seek(length, SeekOrigin.Current) > reader.BaseStream.Length)
            throw new EndOfStreamException();
    }
}
