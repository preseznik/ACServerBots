using System.Text.Json;
using AssettoServer.RaceControl.Core.Runtime;
using NUnit.Framework;

namespace AssettoServer.RaceControl.Tests;

[TestFixture]
public sealed class LiveRaceControlClientTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"race-control-live-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    [Test]
    public async Task ReadsSnapshotsAndWritesAtomicCommands()
    {
        var client = new LiveRaceControlClient(_root);
        Directory.CreateDirectory(client.ControlDirectory);
        await File.WriteAllTextAsync(client.SnapshotPath, """
        {
          "schemaVersion": 1,
          "sequence": 7,
          "serverRunning": true,
          "isSimulation": false,
          "session": { "name": "Race", "type": "Race", "phase": "racing" },
          "cars": [{ "sessionId": 2, "name": "Bot 3", "model": "bmw_m3_e30", "isBot": true, "isActive": true }]
        }
        """);

        var snapshot = client.TryReadSnapshot();
        var commandId = await client.SendCommandAsync(LiveRaceCommand.Restart);
        string commandPath = Directory.GetFiles(client.CommandsDirectory, "*.json").Single();
        using var command = JsonDocument.Parse(await File.ReadAllTextAsync(commandPath));

        Assert.Multiple(() =>
        {
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot!.Sequence, Is.EqualTo(7));
            Assert.That(snapshot.Session.Phase, Is.EqualTo("racing"));
            Assert.That(snapshot.Cars.Single().DisplayName, Does.Contain("Bot 3"));
            Assert.That(command.RootElement.GetProperty("id").GetGuid(), Is.EqualTo(commandId));
            Assert.That(command.RootElement.GetProperty("command").GetString(), Is.EqualTo("restart"));
            Assert.That(Directory.GetFiles(client.CommandsDirectory, "*.tmp"), Is.Empty);
        });
    }

    [Test]
    public void PartialSnapshotIsIgnored()
    {
        var client = new LiveRaceControlClient(_root);
        Directory.CreateDirectory(client.ControlDirectory);
        File.WriteAllText(client.SnapshotPath, "{ not complete");

        Assert.That(client.TryReadSnapshot(), Is.Null);
    }
}
