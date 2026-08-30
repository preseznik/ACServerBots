using ACEditor.Core.Formats;
using ACEditor.Core.Models;
using ACEditor.Core.Tools;

namespace ACEditor.Tests;

[TestFixture]
public sealed class Dirt2AdapterTests
{
    [Test]
    public async Task ProbesAndInventoriesOpaqueDirt2FilesWithoutTools()
    {
        using var temporary = new TemporaryDirectory();
        string root = temporary.Create();
        File.WriteAllBytes(Path.Combine(root, "tracksplit.pssg"), "PSSG"u8.ToArray());
        Directory.CreateDirectory(Path.Combine(root, "route_0"));
        File.WriteAllBytes(Path.Combine(root, "route_0", "boundarylines.cqtc"), [3, 1, 4, 1, 5]);
        var adapter = new Dirt2TrackAdapter(new ToolchainPaths());

        TrackProbeResult probe = await adapter.ProbeAsync(root);
        TrackProject project = await adapter.ImportAsync(root);

        Assert.Multiple(() =>
        {
            Assert.That(probe.Confidence, Is.GreaterThan(0));
            Assert.That(project.Scene.Roots.Single().IsLocked, Is.True);
            Assert.That(project.SourceArtifacts.Single(item => item.RelativePath.EndsWith(".cqtc")).WriteDisposition,
                Is.EqualTo(WriteDisposition.Blocked));
            Assert.That(File.ReadAllBytes(Path.Combine(root, "route_0", "boundarylines.cqtc")),
                Is.EqualTo(new byte[] { 3, 1, 4, 1, 5 }));
        });
    }
}
