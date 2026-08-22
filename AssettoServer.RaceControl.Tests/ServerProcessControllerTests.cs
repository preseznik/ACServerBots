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

    [Test]
    public void SimulationLaunchIncludesLiveControlAndBoundedSimulationArguments()
    {
        var simulation = new RaceSimulationLaunchOptions(@"C:\instance\simulation", 17, 90, 240, 250);

        var info = ServerProcessController.CreateStartInfo(
            @"C:\instance\AssettoServer.exe", @"C:\instance", "race-control",
            @"C:\instance\shutdown.signal", @"C:\instance\race-control-live", simulation);
        var arguments = info.ArgumentList.ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(arguments, Does.Contain("--race-control-directory"));
            Assert.That(arguments, Does.Contain(@"C:\instance\race-control-live"));
            Assert.That(arguments, Does.Contain("--simulate-race"));
            Assert.That(arguments, Does.Contain("17"));
            Assert.That(arguments, Does.Contain("90"));
            Assert.That(arguments, Does.Contain("240"));
            Assert.That(arguments, Does.Contain("250"));
        });
    }
}
