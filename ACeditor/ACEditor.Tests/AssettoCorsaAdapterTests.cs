using ACEditor.Core.Formats;
using ACEditor.Core.Models;

namespace ACEditor.Tests;

[TestFixture]
public sealed class AssettoCorsaAdapterTests
{
    [Test]
    public async Task ImportsKn5MaterialsMeshAndAiSpline()
    {
        using var temporary = new TemporaryDirectory();
        string root = SyntheticFixtures.CreateAssettoCorsaTrack(temporary.Create());
        var adapter = new AssettoCorsaTrackAdapter();

        TrackProbeResult probe = await adapter.ProbeAsync(root);
        TrackProject project = await adapter.ImportAsync(root);

        Assert.Multiple(() =>
        {
            Assert.That(probe.Confidence, Is.GreaterThanOrEqualTo(50));
            Assert.That(project.SourceFormat, Is.EqualTo(TrackFormat.AssettoCorsa));
            Assert.That(project.Scene.Roots, Has.Count.EqualTo(1));
            Assert.That(project.Scene.Materials.Single().Name, Is.EqualTo("road"));
            Assert.That(project.Scene.Materials.Single().TextureSlots["txDiffuse"], Is.EqualTo("road.dds"));
            Assert.That(project.Scene.Materials.Single().BlendMode, Is.EqualTo(MaterialBlendMode.AlphaToCoverage));
            Assert.That(project.Scene.Materials.Single().AlphaTested, Is.True);
            Assert.That(project.Scene.Materials.Single().DepthMode, Is.EqualTo(MaterialDepthMode.NoWrite));
            Assert.That(project.Scene.Textures.Single().Format, Is.EqualTo("DXT1"));
            Assert.That(project.Scene.Textures.Single().EmbeddedData, Has.Length.EqualTo(136));
            Assert.That(project.Scene.Roots[0].Children[0].Mesh!.Indices, Has.Count.EqualTo(3));
            Assert.That(project.Routes.Single().Points, Has.Count.EqualTo(3));
            Assert.That(project.Routes.Single().Points[0].LeftWidth, Is.EqualTo(4));
            Assert.That(project.SourceArtifacts, Has.Count.EqualTo(4));
        });
    }

    [Test]
    public void RejectsTruncatedKn5()
    {
        using var temporary = new TemporaryDirectory();
        string root = temporary.Create();
        SyntheticFixtures.WriteMinimalKn5(Path.Combine(root, "bad.kn5"), truncate: true);
        File.WriteAllText(Path.Combine(root, "models.ini"), "[MODEL_0]\nFILE=bad.kn5\n");
        Assert.That(async () => await new AssettoCorsaTrackAdapter().ImportAsync(root),
            Throws.TypeOf<EndOfStreamException>());
    }

    [Test]
    public async Task PreservesNativeKn5MeshVisibility()
    {
        using var temporary = new TemporaryDirectory();
        string root = temporary.Create();
        SyntheticFixtures.WriteMinimalKn5(Path.Combine(root, "hidden.kn5"), meshVisible: false);
        File.WriteAllText(Path.Combine(root, "models.ini"), "[MODEL_0]\nFILE=hidden.kn5\n");

        TrackProject project = await new AssettoCorsaTrackAdapter().ImportAsync(root);

        Assert.That(project.Scene.Roots[0].Children[0].Mesh!.SourceVisible, Is.False);
    }

    [Test]
    public async Task PreservesNativeKn5RenderabilitySeparatelyFromVisibility()
    {
        using var temporary = new TemporaryDirectory();
        string root = temporary.Create();
        SyntheticFixtures.WriteMinimalKn5(Path.Combine(root, "physics.kn5"), meshRenderable: false);
        File.WriteAllText(Path.Combine(root, "models.ini"), "[MODEL_0]\nFILE=physics.kn5\n");

        TrackMesh mesh = (await new AssettoCorsaTrackAdapter().ImportAsync(root))
            .Scene.Roots[0].Children[0].Mesh!;

        Assert.Multiple(() =>
        {
            Assert.That(mesh.SourceVisible, Is.True);
            Assert.That(mesh.SourceRenderable, Is.False);
        });
    }
}
