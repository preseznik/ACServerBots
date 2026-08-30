using System.Numerics;
using System.Text.Json;
using ACEditor.Core.Infrastructure;
using ACEditor.Core.Models;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Schema2;
using SharpGLTF.Scenes;

namespace ACEditor.Core.Tools;

public sealed record BlenderWorkspace(string GlbPath, string ManifestPath, string BootstrapScript);
public sealed record BlenderRoundTripDiff(IReadOnlyList<string> MissingStableNodes,
    IReadOnlyList<string> AddedNodes, string BeforeSha256, string AfterSha256)
{
    public bool CanReimport => MissingStableNodes.Count == 0;
}

public sealed class BlenderRoundTripService
{
    public BlenderWorkspace Export(TrackProject project, IEnumerable<TrackNode> selectedNodes,
        string workspaceDirectory)
    {
        string root = Path.GetFullPath(workspaceDirectory);
        Directory.CreateDirectory(root);
        var nodes = Flatten(selectedNodes).Where(node => node.Mesh is not null).ToArray();
        if (nodes.Length == 0) throw new InvalidOperationException("The selection contains no mesh nodes.");
        var scene = new SceneBuilder();
        var materialCache = new Dictionary<int, MaterialBuilder>();
        foreach (TrackNode node in nodes)
        {
            TrackMesh source = node.Mesh!;
            MaterialBuilder material = GetMaterial(project.Scene, source.MaterialIndex, materialCache);
            var mesh = new MeshBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>(node.StableSourceId);
            var primitive = mesh.UsePrimitive(material);
            for (int index = 0; index + 2 < source.Indices.Count; index += 3)
            {
                int a = source.Indices[index], b = source.Indices[index + 1], c = source.Indices[index + 2];
                if ((uint)a >= source.Positions.Count || (uint)b >= source.Positions.Count || (uint)c >= source.Positions.Count)
                    throw new InvalidDataException($"Mesh '{source.Name}' contains an invalid index.");
                primitive.AddTriangle(Vertex(source, a), Vertex(source, b), Vertex(source, c));
            }
            scene.AddRigidMesh(mesh, new NodeBuilder(node.StableSourceId));
        }

        string glbPath = Path.Combine(root, "selection.glb");
        string temporaryGlb = Path.Combine(root, $".selection-{Guid.NewGuid():N}.tmp.glb");
        scene.ToGltf2().SaveGLB(temporaryGlb);
        File.Move(temporaryGlb, glbPath, overwrite: true);
        string manifestPath = Path.Combine(root, "roundtrip.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            project.ProjectId,
            ExportedAtUtc = DateTimeOffset.UtcNow,
            SourceGlbSha256 = ContentHash.Sha256(glbPath),
            Nodes = nodes.Select(node => new { node.StableSourceId, node.Name, node.SourceFile,
                Material = node.Mesh!.MaterialIndex }).ToArray()
        }, new JsonSerializerOptions { WriteIndented = true }));
        string script = Path.Combine(root, "open_in_blender.py");
        File.WriteAllText(script, """
            import bpy
            import os
            workspace = os.path.dirname(os.path.abspath(__file__))
            bpy.ops.wm.read_factory_settings(use_empty=True)
            bpy.ops.import_scene.gltf(filepath=os.path.join(workspace, "selection.glb"))
            bpy.ops.wm.save_as_mainfile(filepath=os.path.join(workspace, "selection.blend"))
            """);
        return new BlenderWorkspace(glbPath, manifestPath, script);
    }

    public BlenderRoundTripDiff Inspect(string beforeGlb, string afterGlb)
    {
        ModelRoot before = ModelRoot.Load(beforeGlb);
        ModelRoot after = ModelRoot.Load(afterGlb);
        string[] beforeNodes = before.LogicalNodes.Select(node => node.Name ?? string.Empty)
            .Where(name => name.Length > 0).Distinct(StringComparer.Ordinal).Order().ToArray();
        string[] afterNodes = after.LogicalNodes.Select(node => node.Name ?? string.Empty)
            .Where(name => name.Length > 0).Distinct(StringComparer.Ordinal).Order().ToArray();
        return new BlenderRoundTripDiff(
            beforeNodes.Except(afterNodes, StringComparer.Ordinal).ToArray(),
            afterNodes.Except(beforeNodes, StringComparer.Ordinal).ToArray(),
            ContentHash.Sha256(beforeGlb), ContentHash.Sha256(afterGlb));
    }

    private static MaterialBuilder GetMaterial(TrackScene scene, int index,
        IDictionary<int, MaterialBuilder> cache)
    {
        if (cache.TryGetValue(index, out MaterialBuilder? existing)) return existing;
        TrackMaterial? source = index >= 0 && index < scene.Materials.Count ? scene.Materials[index] : null;
        float hue = (Math.Abs(index) % 7) / 7f;
        var material = new MaterialBuilder(source?.Name ?? $"material-{index}")
            .WithMetallicRoughnessShader().WithMetallicRoughness(0.0f, 0.8f)
            .WithBaseColor(new Vector4(0.35f + hue * 0.2f, 0.42f, 0.48f - hue * 0.15f, 1))
            .WithDoubleSide(true);
        cache[index] = material;
        return material;
    }

    private static VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty> Vertex(TrackMesh mesh, int index)
    {
        Vector3 position = mesh.Positions[index].ToVector();
        Vector3 normal = index < mesh.Normals.Count ? mesh.Normals[index].ToVector() : Vector3.UnitY;
        Position3 uv = index < mesh.TextureCoordinates.Count ? mesh.TextureCoordinates[index] : default;
        return new VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>(
            new VertexPositionNormal(position, normal), new VertexTexture1(new Vector2(uv.X, uv.Y)));
    }

    private static IEnumerable<TrackNode> Flatten(IEnumerable<TrackNode> roots)
    {
        foreach (TrackNode node in roots)
        {
            yield return node;
            foreach (TrackNode child in Flatten(node.Children)) yield return child;
        }
    }
}
