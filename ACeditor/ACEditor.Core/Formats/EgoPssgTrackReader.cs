using System.Buffers.Binary;
using System.Numerics;
using ACEditor.Core.Models;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Graphics;

namespace ACEditor.Core.Formats;

internal static class EgoPssgTrackReader
{
    public static TrackNode Read(string path, TrackScene scene, string sourceRoot, string ownership)
    {
        string relative = Path.GetRelativePath(sourceRoot, path);
        using FileStream stream = File.OpenRead(path);
        PssgFile file = PssgFile.Open(stream);
        int textureOffset = scene.Textures.Count;
        Dictionary<string, string> textureNames = ReadTextures(file, scene, relative);
        var materialIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var root = new TrackNode
        {
            Name = Path.GetFileName(path),
            StableSourceId = relative.Replace('\\', '/'),
            SourceFile = relative,
            Ownership = ownership,
            IsVisible = true,
            IsLocked = false
        };
        var parents = new Dictionary<PssgNode, TrackNode>();

        foreach (PssgNode instance in file.GetNodes().Where(IsRenderInstance))
        {
            string dataSourceId = TrimLink(GetString(instance, "indices"));
            if (string.IsNullOrWhiteSpace(dataSourceId))
                dataSourceId = TrimLink(GetString(instance.ChildNodes.FirstOrDefault(child =>
                    child.Name.Equals("RENDERINSTANCESOURCE", StringComparison.OrdinalIgnoreCase)), "source"));
            PssgNode? dataSource = file.FindNodes("RENDERDATASOURCE", "id", dataSourceId).FirstOrDefault();
            if (dataSource is null) continue;

            var reader = new RenderDataSourceReader(dataSource);
            if (!reader.Primitive.Equals("triangles", StringComparison.OrdinalIgnoreCase)) continue;
            PssgNode sourceParent = instance.ParentNode ?? file.RootNode;
            if (!parents.TryGetValue(sourceParent, out TrackNode? parent))
            {
                string parentId = GetString(sourceParent, "id");
                if (string.IsNullOrWhiteSpace(parentId)) parentId = sourceParent.Name;
                Matrix4x4 world = ReadWorldTransform(sourceParent);
                parent = new TrackNode
                {
                    Name = parentId,
                    StableSourceId = $"{relative.Replace('\\', '/')}#{parentId}",
                    SourceFile = relative,
                    Ownership = ownership,
                    IsVisible = true,
                    IsLocked = false,
                    Transform = Transform44.FromMatrix(world)
                };
                parents.Add(sourceParent, parent);
                root.Children.Add(parent);
            }

            string shaderId = TrimLink(GetString(instance, "shader"));
            int materialIndex = GetMaterialIndex(file, scene, relative, shaderId, textureNames, materialIndices);
            Matrix4x4 meshWorld = ReadWorldTransform(sourceParent);
            TrackMesh mesh = ReadMesh(reader, instance, meshWorld, materialIndex, parent.Name);
            string instanceId = GetString(instance, "id");
            if (string.IsNullOrWhiteSpace(instanceId)) instanceId = $"stream-{parent.Children.Count}";
            parent.Children.Add(new TrackNode
            {
                Name = instanceId,
                StableSourceId = $"{parent.StableSourceId}/{instanceId}",
                SourceFile = relative,
                Ownership = ownership,
                IsVisible = true,
                IsLocked = false,
                Mesh = mesh
            });
        }

        root.IsLocked = root.Children.Count == 0 && scene.Textures.Count == textureOffset;
        return root;
    }

    private static Dictionary<string, string> ReadTextures(PssgFile file, TrackScene scene, string relative)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (PssgNode node in file.FindNodes("TEXTURE"))
        {
            string id = GetString(node, "id");
            if (string.IsNullOrWhiteSpace(id)) continue;
            string uniqueName = $"{relative.Replace('\\', '/')}#{id}";
            names[id] = uniqueName;
            byte[]? ddsBytes = null;
            try
            {
                using var output = new MemoryStream();
                node.ToDdsFile(cubePreview: false).Write(output, -1);
                ddsBytes = output.ToArray();
            }
            catch (NotSupportedException)
            {
                // Keep the texture inventory even when the installed library cannot convert its texel format.
            }
            scene.Textures.Add(new TrackTexture
            {
                Name = uniqueName,
                SourcePath = uniqueName,
                Width = checked((int)GetUInt(node, "width")),
                Height = checked((int)GetUInt(node, "height")),
                MipCount = checked((int)GetUInt(node, "numberMipMapLevels")) + 1,
                Format = GetString(node, "texelFormat"),
                EmbeddedData = ddsBytes
            });
        }
        return names;
    }

    private static int GetMaterialIndex(PssgFile file, TrackScene scene, string relative, string shaderId,
        IReadOnlyDictionary<string, string> textureNames, IDictionary<string, int> materialIndices)
    {
        string key = relative + "#" + shaderId;
        if (materialIndices.TryGetValue(key, out int existing)) return existing;
        PssgNode? shader = file.FindNodes("SHADERINSTANCE", "id", shaderId).FirstOrDefault();
        string sourceShader = shader is null ? "unknown" : TrimLink(GetString(shader, "shaderGroup"));
        var material = new TrackMaterial
        {
            Name = string.IsNullOrWhiteSpace(shaderId) ? "Unassigned PSSG material" : shaderId,
            SourceShader = sourceShader,
            IsApproximation = true
        };
        if (shader is not null)
        {
            foreach (PssgNode input in shader.ChildNodes.Where(child =>
                         child.Name.Equals("SHADERINPUT", StringComparison.OrdinalIgnoreCase)))
            {
                string parameter = "parameter_" + GetUInt(input, "parameterID");
                string type = GetString(input, "type");
                if (type.Equals("texture", StringComparison.OrdinalIgnoreCase))
                {
                    string sourceTexture = TrimLink(GetString(input, "texture"));
                    if (textureNames.TryGetValue(sourceTexture, out string? uniqueTexture))
                        material.TextureSlots[parameter] = uniqueTexture;
                }
                else if (input.Value is { Length: >= 4 })
                {
                    material.Properties[parameter] = ReadBigEndianFloats(input.Value);
                }
            }
        }
        string shaderIdentity = material.Name + " " + material.SourceShader;
        if (shaderIdentity.Contains("blend", StringComparison.OrdinalIgnoreCase))
            material.BlendMode = MaterialBlendMode.AlphaBlend;
        else if (shaderIdentity.Contains("alpha", StringComparison.OrdinalIgnoreCase) ||
                 shaderIdentity.Contains("foliage", StringComparison.OrdinalIgnoreCase) ||
                 shaderIdentity.Contains("fence", StringComparison.OrdinalIgnoreCase))
            material.AlphaTested = true;
        int index = scene.Materials.Count;
        scene.Materials.Add(material);
        materialIndices[key] = index;
        return index;
    }

    private static TrackMesh ReadMesh(RenderDataSourceReader reader, PssgNode instance, Matrix4x4 world,
        int materialIndex, string name)
    {
        int vertexCount = checked((int)reader.VertexCount);
        var mesh = new TrackMesh { Name = name, MaterialIndex = materialIndex };
        mesh.Positions.Capacity = vertexCount;
        mesh.Normals.Capacity = vertexCount;
        mesh.TextureCoordinates.Capacity = vertexCount;
        for (uint index = 0; index < reader.VertexCount; index++)
        {
            mesh.Positions.Add(Position3.FromVector(Vector3.Transform(reader.GetPosition(index), world)));
            Vector3 normal = reader.HasNormals ? Vector3.TransformNormal(reader.GetNormal(index), world) : Vector3.Zero;
            if (normal.LengthSquared() > 0.000001f) normal = Vector3.Normalize(normal);
            mesh.Normals.Add(Position3.FromVector(normal));
            Vector2 uv = reader.TexCoordSetCount > 0 ? reader.GetTexCoord(index, 0) : Vector2.Zero;
            mesh.TextureCoordinates.Add(new Position3(uv.X, uv.Y, 0));
        }

        int indexOffset = checked((int)GetUInt(instance, "indexOffset"));
        int indexCount = checked((int)GetUInt(instance, "indicesCountFromOffset"));
        IEnumerable<(uint A, uint B, uint C)> triangles = indexCount > 0
            ? reader.GetTriangles(indexOffset, indexCount)
            : reader.GetTriangles();
        foreach ((uint a, uint b, uint c) in triangles)
        {
            mesh.Indices.Add(checked((int)a));
            mesh.Indices.Add(checked((int)b));
            mesh.Indices.Add(checked((int)c));
        }
        return mesh;
    }

    private static bool IsRenderInstance(PssgNode node) =>
        node.HasAttribute("shader") &&
        (node.HasAttribute("indices") || node.ChildNodes.Any(child =>
            child.Name.Equals("RENDERINSTANCESOURCE", StringComparison.OrdinalIgnoreCase)));

    private static Matrix4x4 ReadWorldTransform(PssgNode node)
    {
        var ancestry = new Stack<PssgNode>();
        for (PssgNode? current = node; current is not null; current = current.ParentNode)
            ancestry.Push(current);
        Matrix4x4 world = Matrix4x4.Identity;
        while (ancestry.TryPop(out PssgNode? current))
        {
            PssgNode? transform = current.ChildNodes.FirstOrDefault(child =>
                child.Name.Equals("TRANSFORM", StringComparison.OrdinalIgnoreCase));
            if (transform?.Value is { Length: >= 64 } bytes)
                world = ReadMatrix(bytes) * world;
        }
        return world;
    }

    private static Matrix4x4 ReadMatrix(ReadOnlySpan<byte> bytes)
    {
        float[] values = ReadBigEndianFloats(bytes[..64]);
        return new Matrix4x4(
            values[0], values[1], values[2], values[3],
            values[4], values[5], values[6], values[7],
            values[8], values[9], values[10], values[11],
            values[12], values[13], values[14], values[15]);
    }

    private static float[] ReadBigEndianFloats(ReadOnlySpan<byte> bytes)
    {
        var values = new float[bytes.Length / sizeof(float)];
        for (int index = 0; index < values.Length; index++)
            values[index] = BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(index * sizeof(float), sizeof(float))));
        return values;
    }

    private static string GetString(PssgNode? node, string name) => node is not null && node.HasAttribute(name)
        ? node.Attributes[name].Value?.ToString() ?? string.Empty
        : string.Empty;

    private static uint GetUInt(PssgNode node, string name)
    {
        if (!node.HasAttribute(name)) return 0;
        object value = node.Attributes[name].Value;
        return value switch
        {
            uint result => result,
            int result when result >= 0 => (uint)result,
            _ when uint.TryParse(value.ToString(), out uint result) => result,
            _ => 0
        };
    }

    private static string TrimLink(string value) => value.TrimStart('#');
}
