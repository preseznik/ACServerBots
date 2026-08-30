using ACEditor.App.Controls;
using ACEditor.Core.Formats;
using ACEditor.Core.Infrastructure;
using ACEditor.Core.Models;
using ACEditor.Core.Staging;
using ACEditor.Core.Tools;
using EgoEngineLibrary.Formats.Pssg;
using EgoEngineLibrary.Graphics;
using System.Text.Json;
using Vortice.Direct3D;
using Vortice.Direct3D11;

namespace ACEditor.Tests;

[TestFixture, Category("LocalIntegration")]
public sealed class LocalIntegrationTests
{
    private static void RequireGate()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("ACEDITOR_LOCAL_INTEGRATION"), "1",
                StringComparison.Ordinal))
            Assert.Ignore("Set ACEDITOR_LOCAL_INTEGRATION=1 to read installed game content.");
    }

    [Test]
    public async Task ImportsAllKsNurburgringLayoutsWithoutChangingSource()
    {
        RequireGate();
        const string root = @"C:\Program Files (x86)\Steam\steamapps\common\assettocorsa\content\tracks\ks_nurburgring";
        if (!Directory.Exists(root)) Assert.Ignore("Local ks_nurburgring install is unavailable.");
        string models = Path.Combine(root, "models_layout_gp_a.ini");
        string before = ContentHash.Sha256(models);

        TrackProject project = await new AssettoCorsaTrackAdapter().ImportAsync(root);
        TrackNode[] nodes = project.Scene.Roots.SelectMany(Flatten).ToArray();
        TrackMesh[] meshes = nodes.Where(node => node.Mesh is not null).Select(node => node.Mesh!).ToArray();
        TrackNode[] sourceHiddenNodes = nodes.Where(node => node.Mesh is { SourceVisible: false }).ToArray();
        TrackNode[] sourceNonRenderableNodes = nodes.Where(node => node.Mesh is { SourceRenderable: false }).ToArray();
        var textureNames = project.Scene.Textures.Select(texture => texture.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        int mappedMeshes = meshes.Count(mesh => (uint)mesh.MaterialIndex < project.Scene.Materials.Count &&
            project.Scene.Materials[mesh.MaterialIndex].TextureSlots.Values.Any(textureNames.Contains));
        int alphaTested = project.Scene.Materials.Count(material => material.AlphaTested);
        int alphaBlended = project.Scene.Materials.Count(material => material.BlendMode == MaterialBlendMode.AlphaBlend);
        int alphaToCoverage = project.Scene.Materials.Count(material => material.BlendMode == MaterialBlendMode.AlphaToCoverage);
        int depthNoWrite = project.Scene.Materials.Count(material => material.DepthMode == MaterialDepthMode.NoWrite);
        int depthOff = project.Scene.Materials.Count(material => material.DepthMode == MaterialDepthMode.Off);
        TestContext.Progress.WriteLine($"Roots={project.Scene.Roots.Count}; nodes={nodes.Length}; meshes={meshes.Length}; " +
            $"materials={project.Scene.Materials.Count}; textures={project.Scene.Textures.Count}; mappedMeshes={mappedMeshes}; " +
            $"alphaTested={alphaTested}; alphaBlended={alphaBlended}; alphaToCoverage={alphaToCoverage}; " +
            $"depthNoWrite={depthNoWrite}; depthOff={depthOff}; sourceHiddenMeshes={sourceHiddenNodes.Length}; " +
            $"sourceNonRenderableMeshes={sourceNonRenderableNodes.Length}");
        foreach (IGrouping<string, TrackNode> group in sourceHiddenNodes.GroupBy(node => node.SourceFile))
            TestContext.Progress.WriteLine($"Hidden by KN5: {group.Key} = {group.Count()} meshes");
        foreach (IGrouping<string, TrackNode> group in sourceNonRenderableNodes.GroupBy(node => node.SourceFile))
            TestContext.Progress.WriteLine($"Non-renderable by KN5: {group.Key} = {group.Count()} meshes");
        foreach (TrackNode sceneRoot in project.Scene.Roots)
        {
            TestContext.Progress.WriteLine($"{sceneRoot.Ownership}: {sceneRoot.Name}");
            TrackMesh[] rootMeshes = Flatten(sceneRoot).Where(node => node.Mesh is not null)
                .Select(node => node.Mesh!).ToArray();
            string[] rootMaterials = rootMeshes.Where(mesh => (uint)mesh.MaterialIndex < project.Scene.Materials.Count)
                .Select(mesh => project.Scene.Materials[mesh.MaterialIndex].Name)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name).ToArray();
            TestContext.Progress.WriteLine($"  meshes={rootMeshes.Length}; triangles={rootMeshes.Sum(mesh => mesh.Indices.Count / 3)}; " +
                $"materials={string.Join(", ", rootMaterials)}");
        }
        var textureErrors = new List<string>();
        FeatureLevel[] levels = [FeatureLevel.Level_11_0];
        using ID3D11Device previewDevice = D3D11.D3D11CreateDevice(
            DriverType.Warp, DeviceCreationFlags.None, levels);
        foreach (TrackTexture texture in project.Scene.Textures)
        {
            if (texture.EmbeddedData is not { Length: > 0 } bytes) continue;
            try
            {
                using ID3D11ShaderResourceView view =
                    DdsTextureLoader.CreateShaderResourceView(previewDevice, bytes);
            }
            catch (Exception exception) { textureErrors.Add($"{texture.Name}: {exception.Message}"); }
        }

        Assert.Multiple(() =>
        {
            Assert.That(project.LayoutIds, Is.EquivalentTo(new[]
                { "layout_gp_a", "layout_gp_b", "layout_sprint_a", "layout_sprint_b" }));
            Assert.That(project.Scene.Roots, Is.Not.Empty);
            Assert.That(project.Scene.Materials, Is.Not.Empty);
            Assert.That(project.Scene.Textures, Is.Not.Empty);
            Assert.That(mappedMeshes, Is.GreaterThan(0));
            Assert.That(alphaTested + alphaBlended + alphaToCoverage, Is.GreaterThan(0));
            Assert.That(textureErrors, Is.Empty, string.Join(Environment.NewLine, textureErrors));
            Assert.That(project.Routes, Is.Not.Empty);
            Assert.That(ContentHash.Sha256(models), Is.EqualTo(before));
        });
    }

    private static IEnumerable<TrackNode> Flatten(TrackNode node)
    {
        yield return node;
        foreach (TrackNode child in node.Children)
        foreach (TrackNode descendant in Flatten(child))
            yield return descendant;
    }

    [Test]
    public async Task ImportsBajaIronRoutesAndPinsEgoToolchainWithoutChangingSource()
    {
        RequireGate();
        const string root = @"C:\Program Files (x86)\Steam\steamapps\common\Dirt 2\tracks\baja\baja_iron";
        if (!Directory.Exists(root)) Assert.Ignore("Local baja_iron install is unavailable.");
        string trackSplit = Path.Combine(root, "tracksplit.pssg");
        string before = ContentHash.Sha256(trackSplit);
        ToolchainPaths tools = new ToolchainDiscovery().Discover();

        var adapter = new Dirt2TrackAdapter(tools);
        TrackProject project = await adapter.ImportAsync(root);
        TrackNode[] nodes = project.Scene.Roots.SelectMany(Flatten).ToArray();
        TrackMesh[] meshes = nodes.Where(node => node.Mesh is not null).Select(node => node.Mesh!).ToArray();
        var textureErrors = new List<string>();
        FeatureLevel[] levels = [FeatureLevel.Level_11_0];
        using ID3D11Device previewDevice = D3D11.D3D11CreateDevice(
            DriverType.Warp, DeviceCreationFlags.None, levels);
        foreach (TrackTexture texture in project.Scene.Textures)
        {
            if (texture.EmbeddedData is not { Length: > 0 } bytes) continue;
            try
            {
                using ID3D11ShaderResourceView view =
                    DdsTextureLoader.CreateShaderResourceView(previewDevice, bytes);
            }
            catch (Exception exception) { textureErrors.Add($"{texture.Name}: {exception.Message}"); }
        }
        int sourceNodeCount;
        int reopenedNodeCount;
        using (FileStream input = File.OpenRead(trackSplit))
        using (var saved = new MemoryStream())
        {
            PssgFile pssg = PssgFile.Open(input);
            sourceNodeCount = pssg.GetNodes().Count();
            pssg.Save(saved);
            saved.Position = 0;
            reopenedNodeCount = PssgFile.Open(saved).GetNodes().Count();
        }

        TrackTexture replacementTarget = project.Scene.Textures.First(texture =>
            texture.SourcePath.StartsWith("tracksplit.pssg#", StringComparison.OrdinalIgnoreCase) &&
            texture.EmbeddedData is { Length: > 0 });
        using var temporary = new TemporaryDirectory();
        string temporaryRoot = temporary.Create();
        string replacementPath = Path.Combine(temporaryRoot, "replacement.dds");
        await File.WriteAllBytesAsync(replacementPath, SyntheticFixtures.CreateDxt1Dds());
        project.EditDeltas.Add(new TrackEditDelta
        {
            Kind = PssgTextureEditService.EditKind,
            TargetId = replacementTarget.SourcePath,
            RequiredArtifact = "tracksplit.pssg",
            AfterJson = JsonSerializer.Serialize(new PssgTextureReplacement(
                replacementPath, ContentHash.Sha256(replacementPath)))
        });
        string stagePath = Path.Combine(temporaryRoot, "baja-stage");
        StageResult staged = await adapter.StageAsync(project, new StageOptions(stagePath));
        DdsImage stagedTexture;
        string textureId = replacementTarget.SourcePath["tracksplit.pssg#".Length..];
        using (FileStream stageStream = File.OpenRead(Path.Combine(stagePath, "tracksplit.pssg")))
        {
            PssgFile stagedPssg = PssgFile.Open(stageStream);
            PssgNode stagedTextureNode = stagedPssg.FindNodes("TEXTURE", "id", textureId).Single();
            using var ddsOutput = new MemoryStream();
            stagedTextureNode.ToDdsFile(cubePreview: false).Write(ddsOutput, -1);
            stagedTexture = DdsTextureLoader.Parse(ddsOutput.ToArray());
        }
        TestContext.Progress.WriteLine($"PSSG roots={project.Scene.Roots.Count}; nodes={nodes.Length}; " +
            $"meshes={meshes.Length}; triangles={meshes.Sum(mesh => mesh.Indices.Count / 3)}; " +
            $"materials={project.Scene.Materials.Count}; textures={project.Scene.Textures.Count}; " +
            $"unlockedRoots={project.Scene.Roots.Count(node => !node.IsLocked)}");

        Assert.Multiple(() =>
        {
            Assert.That(project.ToolchainVersions["EgoEngineLibrary"], Does.StartWith("15.0.0"));
            Assert.That(project.LayoutIds, Has.Count.GreaterThanOrEqualTo(2));
            Assert.That(project.Routes, Has.Count.GreaterThanOrEqualTo(2));
            Assert.That(meshes, Is.Not.Empty);
            Assert.That(project.Scene.Materials, Is.Not.Empty);
            Assert.That(project.Scene.Textures.Any(texture => texture.EmbeddedData is { Length: > 0 }), Is.True);
            Assert.That(project.Scene.Roots.Any(node => !node.IsLocked), Is.True);
            Assert.That(textureErrors, Is.Empty, string.Join(Environment.NewLine, textureErrors));
            Assert.That(reopenedNodeCount, Is.EqualTo(sourceNodeCount));
            Assert.That(staged.Succeeded, Is.True, string.Join(Environment.NewLine, staged.Issues));
            Assert.That(stagedTexture.Width, Is.EqualTo(4));
            Assert.That(stagedTexture.Height, Is.EqualTo(4));
            Assert.That(project.SourceArtifacts.Any(item => item.WriteDisposition == WriteDisposition.Blocked), Is.True);
            Assert.That(ContentHash.Sha256(trackSplit), Is.EqualTo(before));
        });
    }
}
