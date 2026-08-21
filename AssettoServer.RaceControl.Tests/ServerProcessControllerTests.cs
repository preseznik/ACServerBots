using AssettoServer.RaceControl.Core.Runtime;
using NUnit.Framework;

namespace AssettoServer.RaceControl.Tests;

public sealed class ServerProcessControllerTests
{
    [Test]
    public void OwnedServerPathMustBeAssettoServerInsideRaceControlInstances()
    {
        string instances = Path.Combine(Path.GetTempPath(), "Race Control", "Instances");

        Assert.Multiple(() =>
        {
            Assert.That(ServerProcessController.IsOwnedServerExecutable(
                Path.Combine(instances, "race-123", "AssettoServer.exe"), instances), Is.True);
            Assert.That(ServerProcessController.IsOwnedServerExecutable(
                Path.Combine(instances, "race-123", "unrelated.exe"), instances), Is.False);
            Assert.That(ServerProcessController.IsOwnedServerExecutable(
                Path.Combine(Path.GetDirectoryName(instances)!, "Instances-old", "race-123", "AssettoServer.exe"),
                instances), Is.False, "a path-prefix match must not claim a server outside the owned directory");
            Assert.That(ServerProcessController.IsOwnedServerExecutable(
                Path.Combine(Path.GetTempPath(), "Content Manager", "AssettoServer.exe"), instances), Is.False,
                "Content Manager or manually launched servers must not be terminated");
        });
    }
}
