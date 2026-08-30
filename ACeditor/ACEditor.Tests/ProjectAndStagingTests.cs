using ACEditor.Core.Editing;
using ACEditor.Core.Formats;
using ACEditor.Core.Infrastructure;
using ACEditor.Core.Models;
using ACEditor.Core.Projects;
using ACEditor.Core.Staging;
using ACEditor.Core.Tools;

namespace ACEditor.Tests;

[TestFixture]
public sealed class ProjectAndStagingTests
{
    [Test]
    public async Task ProjectRoundTripRetainsHashesAndDeltasWithoutEmbeddingSceneCache()
    {
        using var temporary = new TemporaryDirectory();
        string root = SyntheticFixtures.CreateAssettoCorsaTrack(Path.Combine(temporary.Create(), "source"));
        TrackProject source = await new AssettoCorsaTrackAdapter().ImportAsync(root);
        string projectPath = Path.Combine(temporary.Path, "track.acedit");
        var store = new TrackProjectStore();
        await store.SaveAsync(source, projectPath);
        TrackProject reopened = await store.LoadAsync(projectPath);

        Assert.Multiple(() =>
        {
            Assert.That(reopened.ProjectId, Is.EqualTo(source.ProjectId));
            Assert.That(reopened.SourceArtifacts.Select(item => item.Sha256),
                Is.EqualTo(source.SourceArtifacts.Select(item => item.Sha256)));
            Assert.That(reopened.Scene.Roots, Is.Empty);
            Assert.That(new FileInfo(projectPath).Length, Is.LessThan(20_000));
        });
    }

    [Test]
    public async Task StagePreservesOpaqueBytesAndNeverChangesSource()
    {
        using var temporary = new TemporaryDirectory();
        string root = SyntheticFixtures.CreateAssettoCorsaTrack(Path.Combine(temporary.Create(), "source"));
        var adapter = new AssettoCorsaTrackAdapter();
        TrackProject project = await adapter.ImportAsync(root);
        byte[] before = File.ReadAllBytes(Path.Combine(root, "opaque.dat"));
        string stage = Path.Combine(temporary.Path, "stage");

        StageResult result = await adapter.StageAsync(project, new StageOptions(stage));

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, string.Join(Environment.NewLine, result.Issues));
            Assert.That(File.ReadAllBytes(Path.Combine(stage, "opaque.dat")), Is.EqualTo(before));
            Assert.That(File.ReadAllBytes(Path.Combine(root, "opaque.dat")), Is.EqualTo(before));
            Assert.That(File.Exists(Path.Combine(stage, ".aceditor-stage.json")), Is.True);
        });
    }

    [Test]
    public async Task StageMutationChangesOnlyTemporaryCopyAndReportsStagedHash()
    {
        using var temporary = new TemporaryDirectory();
        string root = SyntheticFixtures.CreateAssettoCorsaTrack(Path.Combine(temporary.Create(), "source"));
        TrackProject project = await new AssettoCorsaTrackAdapter().ImportAsync(root);
        string sourceFile = Path.Combine(root, "models.ini");
        string sourceBefore = ContentHash.Sha256(sourceFile);
        string stage = Path.Combine(temporary.Path, "stage");

        StageResult result = await new SafeStagingService().StageAsync(project, new StageOptions(stage),
            prepareStagedCopy: (stagedRoot, _) =>
                File.AppendAllText(Path.Combine(stagedRoot, "models.ini"), "\n; staged edit\n"));
        string stagedFile = Path.Combine(stage, "models.ini");
        SourceArtifact manifestEntry = result.Manifest.Single(item => item.RelativePath == "models.ini");

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, string.Join(Environment.NewLine, result.Issues));
            Assert.That(ContentHash.Sha256(sourceFile), Is.EqualTo(sourceBefore));
            Assert.That(ContentHash.Sha256(stagedFile), Is.Not.EqualTo(sourceBefore));
            Assert.That(manifestEntry.Sha256, Is.EqualTo(ContentHash.Sha256(stagedFile)));
            Assert.That(manifestEntry.Length, Is.EqualTo(new FileInfo(stagedFile).Length));
        });
    }

    [Test]
    public async Task StageBlocksAnEditThatRequiresKn5Rewrite()
    {
        using var temporary = new TemporaryDirectory();
        string root = SyntheticFixtures.CreateAssettoCorsaTrack(Path.Combine(temporary.Create(), "source"));
        var adapter = new AssettoCorsaTrackAdapter();
        TrackProject project = await adapter.ImportAsync(root);
        project.EditDeltas.Add(new TrackEditDelta
        {
            Kind = "mesh.topology", TargetId = "1ROAD", RequiredArtifact = "track.kn5"
        });

        StageResult result = await adapter.StageAsync(project, new StageOptions(Path.Combine(temporary.Path, "stage")));

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Issues.Any(issue => issue.Code == "OPAQUE_WRITE_BLOCKED"), Is.True);
    }

    [Test]
    public void UndoRedoRestoresProperty()
    {
        string value = "before";
        var history = new UndoRedoStack();
        history.Execute(new PropertyEditCommand<string>("rename", next => value = next, value, "after"));
        Assert.That(value, Is.EqualTo("after"));
        history.Undo();
        Assert.That(value, Is.EqualTo("before"));
        history.Redo();
        Assert.That(value, Is.EqualTo("after"));
    }

    [Test]
    public async Task BlenderRoundTripExportsValidGlbWithStableNodes()
    {
        using var temporary = new TemporaryDirectory();
        string root = SyntheticFixtures.CreateAssettoCorsaTrack(Path.Combine(temporary.Create(), "source"));
        TrackProject project = await new AssettoCorsaTrackAdapter().ImportAsync(root);
        var service = new BlenderRoundTripService();
        BlenderWorkspace workspace = service.Export(project, project.Scene.Roots,
            Path.Combine(temporary.Path, "blender"));
        BlenderRoundTripDiff diff = service.Inspect(workspace.GlbPath, workspace.GlbPath);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(workspace.GlbPath), Is.True);
            Assert.That(File.Exists(workspace.ManifestPath), Is.True);
            Assert.That(File.Exists(workspace.BootstrapScript), Is.True);
            Assert.That(diff.CanReimport, Is.True);
            Assert.That(diff.BeforeSha256, Is.EqualTo(diff.AfterSha256));
        });
    }
}
