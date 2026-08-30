using System.Numerics;
using System.Text;
using ACEditor.Core.Models;

namespace ACEditor.Core.Formats;

internal static class Kn5Reader
{
    private const int MaxCollectionCount = 50_000_000;
    private enum NodeClass { Base = 1, Mesh = 2, SkinnedMesh = 3 }

    public static TrackNode Read(string path, TrackScene scene, string sourceRoot)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        if (Encoding.ASCII.GetString(reader.ReadBytes(6)) != "sc6969")
            throw new InvalidDataException($"Not a KN5 file: {path}");
        int version = reader.ReadInt32();
        if (version > 5) reader.ReadInt32();

        int textureCount = ReadCount(reader, "texture");
        for (int i = 0; i < textureCount; i++)
        {
            reader.ReadInt32();
            string name = ReadString(reader);
            int length = checked((int)reader.ReadUInt32());
            ValidateCount(length, "texture byte");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            (int width, int height, int mips, string format) = ReadDdsHeader(bytes);
            scene.Textures.Add(new TrackTexture
            {
                Name = name, SourcePath = Path.GetRelativePath(sourceRoot, path) + "#" + name,
                Width = width, Height = height, MipCount = mips, Format = format, EmbeddedData = bytes
            });
        }

        int materialOffset = scene.Materials.Count;
        int materialCount = ReadCount(reader, "material");
        for (int i = 0; i < materialCount; i++)
            scene.Materials.Add(ReadMaterial(reader));

        string relative = Path.GetRelativePath(sourceRoot, path);
        TrackNode root = ReadNode(reader, Matrix4x4.Identity, relative, materialOffset);
        root.Name = $"{Path.GetFileName(path)} · {root.Name}";
        root.StableSourceId = relative.Replace('\\', '/') + ":root";
        return root;
    }

    private static TrackMaterial ReadMaterial(BinaryReader reader)
    {
        var material = new TrackMaterial { Name = ReadString(reader), SourceShader = ReadString(reader) };
        material.BlendMode = (MaterialBlendMode)reader.ReadByte();
        material.AlphaTested = reader.ReadBoolean();
        material.DepthMode = (MaterialDepthMode)reader.ReadInt32();
        int propertyCount = ReadCount(reader, "material property");
        for (int i = 0; i < propertyCount; i++)
        {
            string name = ReadString(reader);
            var values = new float[10];
            for (int value = 0; value < values.Length; value++) values[value] = reader.ReadSingle();
            material.Properties[name] = values;
        }
        int mappingCount = ReadCount(reader, "texture mapping");
        for (int i = 0; i < mappingCount; i++)
        {
            string slot = ReadString(reader);
            reader.ReadInt32();
            material.TextureSlots[slot] = ReadString(reader);
        }
        return material;
    }

    private static TrackNode ReadNode(BinaryReader reader, Matrix4x4 parentWorld,
        string sourceFile, int materialOffset, string stableParent = "", int siblingIndex = 0)
    {
        var nodeClass = (NodeClass)reader.ReadInt32();
        if (nodeClass is < NodeClass.Base or > NodeClass.SkinnedMesh)
            throw new InvalidDataException($"Unsupported KN5 node class: {(int)nodeClass}");
        string name = ReadString(reader);
        int childCount = ReadCount(reader, "node child");
        bool active = reader.ReadBoolean();
        string stableId = string.IsNullOrEmpty(stableParent)
            ? $"{siblingIndex}:{name}" : $"{stableParent}/{siblingIndex}:{name}";
        var node = new TrackNode
        {
            Name = name, SourceFile = sourceFile, StableSourceId = sourceFile.Replace('\\', '/') + ":" + stableId,
            IsVisible = active, IsLocked = false
        };
        Matrix4x4 world = parentWorld;
        switch (nodeClass)
        {
            case NodeClass.Base:
                Matrix4x4 local = ReadMatrix(reader);
                node.Transform = Transform44.FromMatrix(local);
                world = local * parentWorld;
                break;
            case NodeClass.Mesh:
                node.Mesh = ReadMesh(reader, name, world, materialOffset, skinned: false);
                break;
            case NodeClass.SkinnedMesh:
                node.Mesh = ReadMesh(reader, name, world, materialOffset, skinned: true);
                node.IsLocked = true;
                break;
        }
        for (int i = 0; i < childCount; i++)
            node.Children.Add(ReadNode(reader, world, sourceFile, materialOffset, stableId, i));
        return node;
    }

    private static TrackMesh ReadMesh(BinaryReader reader, string name, Matrix4x4 world,
        int materialOffset, bool skinned)
    {
        bool castsShadows = reader.ReadBoolean();
        bool visible = reader.ReadBoolean();
        bool transparent = reader.ReadBoolean();
        if (skinned)
        {
            int boneCount = checked((int)reader.ReadUInt32());
            ValidateCount(boneCount, "bone");
            for (int i = 0; i < boneCount; i++) { ReadString(reader); Skip(reader, 64); }
        }

        int vertexCount = checked((int)reader.ReadUInt32());
        ValidateCount(vertexCount, "vertex");
        var mesh = new TrackMesh
        {
            Name = name,
            SourceCastsShadows = castsShadows,
            SourceVisible = visible,
            SourceTransparent = transparent
        };
        mesh.Positions.Capacity = vertexCount;
        mesh.Normals.Capacity = vertexCount;
        mesh.TextureCoordinates.Capacity = vertexCount;
        for (int i = 0; i < vertexCount; i++)
        {
            Vector3 position = Vector3.Transform(ReadVector3(reader), world);
            Vector3 normal = Vector3.TransformNormal(ReadVector3(reader), world);
            if (normal.LengthSquared() > 0.000001f) normal = Vector3.Normalize(normal);
            var uv = new Position3(reader.ReadSingle(), reader.ReadSingle(), 0);
            Skip(reader, 3 * sizeof(float));
            if (skinned) Skip(reader, 8 * sizeof(float));
            mesh.Positions.Add(Position3.FromVector(position));
            mesh.Normals.Add(Position3.FromVector(normal));
            mesh.TextureCoordinates.Add(uv);
        }
        int indexCount = checked((int)reader.ReadUInt32());
        ValidateCount(indexCount, "index");
        mesh.Indices.Capacity = indexCount;
        for (int i = 0; i < indexCount; i++)
        {
            int index = reader.ReadUInt16();
            if (index >= vertexCount) throw new InvalidDataException($"KN5 mesh {name} has an invalid index.");
            mesh.Indices.Add(index);
        }
        mesh.MaterialIndex = checked((int)reader.ReadUInt32()) + materialOffset;
        reader.ReadUInt32();
        reader.ReadSingle();
        reader.ReadSingle();
        if (!skinned)
        {
            Skip(reader, 4 * sizeof(float));
            mesh.SourceRenderable = reader.ReadBoolean();
        }
        if (!visible) mesh.CollisionRole = CollisionRole.VisualOnly;
        return mesh;
    }

    private static (int Width, int Height, int Mips, string Format) ReadDdsHeader(byte[] bytes)
    {
        if (bytes.Length < 32 || Encoding.ASCII.GetString(bytes, 0, 4) != "DDS ") return (0, 0, 0, "embedded");
        int height = BitConverter.ToInt32(bytes, 12);
        int width = BitConverter.ToInt32(bytes, 16);
        int mips = BitConverter.ToInt32(bytes, 28);
        string format = bytes.Length >= 88 ? Encoding.ASCII.GetString(bytes, 84, 4).TrimEnd('\0') : "DDS";
        return (width, height, Math.Max(1, mips), string.IsNullOrWhiteSpace(format) ? "DDS" : format);
    }

    private static Matrix4x4 ReadMatrix(BinaryReader reader) => new(
        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    private static Vector3 ReadVector3(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    private static string ReadString(BinaryReader reader)
    {
        int length = ReadCount(reader, "string");
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        return Encoding.UTF8.GetString(bytes);
    }
    private static int ReadCount(BinaryReader reader, string label)
    {
        int count = reader.ReadInt32(); ValidateCount(count, label); return count;
    }
    private static void ValidateCount(int count, string label)
    {
        if (count < 0 || count > MaxCollectionCount) throw new InvalidDataException($"Invalid KN5 {label} count: {count}");
    }
    private static void Skip(BinaryReader reader, int length)
    {
        if (length < 0 || reader.BaseStream.Seek(length, SeekOrigin.Current) > reader.BaseStream.Length)
            throw new EndOfStreamException();
    }
}
