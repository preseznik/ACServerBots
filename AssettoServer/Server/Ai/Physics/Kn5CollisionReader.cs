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

internal sealed class Kn5CollisionData
{
    public List<Kn5Triangle> Triangles { get; } = [];
    public List<Vector3> Vertices { get; } = [];
    public List<Kn5NamedTransform> NamedTransforms { get; } = [];
    public HashSet<string> MeshNames { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Minimal reader for the public KN5 container format. It deliberately ignores textures,
/// materials and rendering data and retains only node transforms and collision geometry.
/// </summary>
internal static class Kn5CollisionReader
{
    private const int MaxCollectionCount = 50_000_000;

    public static Kn5CollisionData Read(string path, Func<string, bool> includeMesh)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        if (Encoding.ASCII.GetString(reader.ReadBytes(6)) != "sc6969")
            throw new InvalidDataException($"Not a KN5 file: {path}");

        int version = reader.ReadInt32();
        if (version > 5)
            reader.ReadInt32();

        int textureCount = ReadCount(reader, "texture");
        for (int i = 0; i < textureCount; i++)
        {
            reader.ReadInt32();
            ReadString(reader);
            int length = checked((int)reader.ReadUInt32());
            Skip(reader, length);
        }

        int materialCount = ReadCount(reader, "material");
        for (int i = 0; i < materialCount; i++)
            SkipMaterial(reader);

        var result = new Kn5CollisionData();
        ReadNode(reader, Matrix4x4.Identity, includeMesh, result);
        return result;
    }

    private static void ReadNode(BinaryReader reader, Matrix4x4 parentTransform,
        Func<string, bool> includeMesh, Kn5CollisionData result)
    {
        var nodeClass = (Kn5NodeClass)reader.ReadInt32();
        if (nodeClass is < Kn5NodeClass.Base or > Kn5NodeClass.SkinnedMesh)
            throw new InvalidDataException($"Unsupported KN5 node class: {(int)nodeClass}");

        string name = ReadString(reader);
        int childCount = ReadCount(reader, "node child");
        bool active = reader.ReadBoolean();
        Matrix4x4 worldTransform = parentTransform;

        switch (nodeClass)
        {
            case Kn5NodeClass.Base:
            {
                var localTransform = ReadMatrix(reader);
                worldTransform = localTransform * parentTransform;
                if (active)
                    result.NamedTransforms.Add(new Kn5NamedTransform(name, worldTransform));
                break;
            }
            case Kn5NodeClass.Mesh:
                ReadMesh(reader, name, active, worldTransform, includeMesh, result, skinned: false);
                break;
            case Kn5NodeClass.SkinnedMesh:
                ReadMesh(reader, name, active, worldTransform, includeMesh, result, skinned: true);
                break;
        }

        for (int i = 0; i < childCount; i++)
            ReadNode(reader, worldTransform, includeMesh, result);
    }

    private static void ReadMesh(BinaryReader reader, string name, bool active, Matrix4x4 worldTransform,
        Func<string, bool> includeMesh, Kn5CollisionData result, bool skinned)
    {
        reader.ReadBoolean(); // Cast shadows
        reader.ReadBoolean(); // Visible
        reader.ReadBoolean(); // Transparent

        if (skinned)
        {
            int boneCount = checked((int)reader.ReadUInt32());
            ValidateCount(boneCount, "bone");
            for (int i = 0; i < boneCount; i++)
            {
                ReadString(reader);
                Skip(reader, 16 * sizeof(float));
            }
        }

        int vertexCount = checked((int)reader.ReadUInt32());
        ValidateCount(vertexCount, "vertex");
        var vertices = new Vector3[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            vertices[i] = Vector3.Transform(ReadVector3(reader), worldTransform);
            Skip(reader, skinned ? 64 : 32); // normal, UV, tangent, optional weights and bone indices
        }

        int indexCount = checked((int)reader.ReadUInt32());
        ValidateCount(indexCount, "index");
        var indices = new ushort[indexCount];
        for (int i = 0; i < indexCount; i++)
            indices[i] = reader.ReadUInt16();

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
    }

    private static void SkipMaterial(BinaryReader reader)
    {
        ReadString(reader);
        ReadString(reader);
        Skip(reader, 6); // blend, alpha tested, depth mode
        int propertyCount = ReadCount(reader, "material property");
        for (int i = 0; i < propertyCount; i++)
        {
            ReadString(reader);
            Skip(reader, 10 * sizeof(float));
        }
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
