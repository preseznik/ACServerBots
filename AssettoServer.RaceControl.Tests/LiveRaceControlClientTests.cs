using System.Text.Json;
using AssettoServer.RaceControl.Core.Runtime;
using NUnit.Framework;

namespace AssettoServer.RaceControl.Tests;

[TestFixture]
public sealed class LiveRaceControlClientTests
{
    [TestCase((byte)0, "R")]
    [TestCase((byte)1, "N")]
    [TestCase((byte)2, "1")]
    [TestCase((byte)7, "6")]
    public void LiveCarConvertsProtocolGearForHud(byte protocolGear, string expected)
    {
        Assert.That(new LiveRaceCar { ProtocolGear = protocolGear }.GearDisplay,
            Is.EqualTo(expected));
    }

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
          "cars": [{ "sessionId": 2, "name": "Bot 3", "model": "bmw_m3_e30", "isBot": true, "isActive": true,
            "x": 10, "y": 3.5, "z": 20, "orientationY": 0.707, "orientationW": 0.707,
            "forwardX": 1, "forwardY": 0, "forwardZ": 0,
            "stoppedObstaclePassCommits": 2, "stoppedObstaclePassesCompleted": 1 }]
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
            Assert.That(snapshot.Cars.Single().StoppedObstaclePassCommits, Is.EqualTo(2));
            Assert.That(snapshot.Cars.Single().StoppedObstaclePassesCompleted, Is.EqualTo(1));
            Assert.That(snapshot.Cars.Single().Y, Is.EqualTo(3.5f));
            Assert.That(snapshot.Cars.Single().OrientationW, Is.EqualTo(0.707f));
            Assert.That(snapshot.Cars.Single().ForwardX, Is.EqualTo(1));
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

    [Test]
    public void TrackCacheReloadsWhenReusableInstanceReplacesEmptyTrack()
    {
        var client = new LiveRaceControlClient(_root);
        var cache = new LiveTrackFileCache();
        Directory.CreateDirectory(client.ControlDirectory);
        File.WriteAllText(client.TrackPath, """
        {
          "schemaVersion": 1,
          "track": "fps_arena",
          "layout": "",
          "points": []
        }
        """);
        File.SetLastWriteTimeUtc(client.TrackPath, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.That(cache.TryReadChanged(client), Is.Null,
            "An empty FPS map must not poison the reusable instance cache.");

        string replacement = client.TrackPath + ".tmp";
        File.WriteAllText(replacement, """
        {
          "schemaVersion": 1,
          "track": "ks_nordschleife",
          "layout": "nordschleife",
          "points": [
            { "x": 1, "y": 2, "z": 3, "leftWidth": 5, "rightWidth": 5 },
            { "x": 4, "y": 2, "z": 6, "leftWidth": 5, "rightWidth": 5 }
          ]
        }
        """);
        File.Move(replacement, client.TrackPath, true);
        File.SetLastWriteTimeUtc(client.TrackPath, new DateTime(2026, 1, 1, 0, 0, 1, DateTimeKind.Utc));

        LiveTrackMap? loaded = cache.TryReadChanged(client);

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.Track, Is.EqualTo("ks_nordschleife"));
            Assert.That(loaded.Points, Has.Count.EqualTo(2));
            Assert.That(cache.TryReadChanged(client), Is.Null,
                "An unchanged track file should not be parsed every monitor tick.");
        });
    }

    [Test]
    public void ReadsAuthoritativeFpsPlayersAndPreparedArenaCells()
    {
        var client = new LiveRaceControlClient(_root);
        var cache = new LiveTrackFileCache();
        Directory.CreateDirectory(client.ControlDirectory);
        File.WriteAllText(client.SnapshotPath, """
        {
          "schemaVersion": 2,
          "sequence": 19,
          "serverRunning": true,
          "isFps": true,
          "session": { "name": "Current match", "type": "Deathmatch", "phase": "running",
            "timeLeftMilliseconds": 119000, "killLimit": 20 },
          "cars": [{ "sessionId": 3, "name": "Operative 04", "model": "FPS Operator",
            "isBot": true, "isActive": true, "x": 12.5, "y": 3, "z": -8,
            "headingRadians": 1.5, "health": 66, "kills": 7, "deaths": 2 }]
        }
        """);
        File.WriteAllText(client.TrackPath, """
        {
          "schemaVersion": 2,
          "track": "fire_pit",
          "layout": "",
          "isFpsArena": true,
          "minimumX": -57.3,
          "maximumX": 57.5,
          "minimumZ": -57.85,
          "maximumZ": 57.55,
          "arenaCellSize": 0.6,
          "arenaCells": [{ "x": 1.2, "z": 2.4 }, { "x": 1.8, "z": 2.4 }],
          "points": []
        }
        """);

        LiveRaceSnapshot? snapshot = client.TryReadSnapshot();
        LiveTrackMap? arena = cache.TryReadChanged(client);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot!.IsFps, Is.True);
            Assert.That(snapshot.Session.KillLimit, Is.EqualTo(20));
            Assert.That(snapshot.Cars.Single().Name, Is.EqualTo("Operative 04"));
            Assert.That(snapshot.Cars.Single().Health, Is.EqualTo(66));
            Assert.That(snapshot.Cars.Single().Kills, Is.EqualTo(7));
            Assert.That(snapshot.Cars.Single().Deaths, Is.EqualTo(2));
            Assert.That(arena, Is.Not.Null);
            Assert.That(arena!.HasFpsArena, Is.True);
            Assert.That(arena.ArenaCells, Has.Count.EqualTo(2));
            Assert.That(arena.MinimumX, Is.EqualTo(-57.3f));
        });
    }

    [Test]
    public async Task WritesValidatedLiveSimulationTimeScaleCommand()
    {
        var client = new LiveRaceControlClient(_root);

        var commandId = await client.SendSimulationTimeScaleAsync(25);
        string commandPath = Directory.GetFiles(client.CommandsDirectory, "*.json").Single();
        using var command = JsonDocument.Parse(await File.ReadAllTextAsync(commandPath));

        Assert.Multiple(() =>
        {
            Assert.That(command.RootElement.GetProperty("id").GetGuid(), Is.EqualTo(commandId));
            Assert.That(command.RootElement.GetProperty("command").GetString(),
                Is.EqualTo("simulation_time_scale"));
            Assert.That(command.RootElement.GetProperty("timeScale").GetDouble(), Is.EqualTo(25));
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await client.SendSimulationTimeScaleAsync(101));
        });
    }

    [Test]
    public async Task WritesBotControlCommandsAndLatestManualInput()
    {
        var client = new LiveRaceControlClient(_root);

        var commandId = await client.SendBotStopAsync(3, stop: true);
        string commandPath = Directory.GetFiles(client.CommandsDirectory, "*.json").Single();
        using var command = JsonDocument.Parse(await File.ReadAllTextAsync(commandPath));
        await client.WriteManualInputAsync(3, -0.5f, 0.75f, 0.25f);
        using var manualInput = JsonDocument.Parse(await File.ReadAllTextAsync(client.ManualInputPath));

        Assert.Multiple(() =>
        {
            Assert.That(command.RootElement.GetProperty("id").GetGuid(), Is.EqualTo(commandId));
            Assert.That(command.RootElement.GetProperty("command").GetString(), Is.EqualTo("bot_stop"));
            Assert.That(command.RootElement.GetProperty("sessionId").GetInt32(), Is.EqualTo(3));
            Assert.That(manualInput.RootElement.GetProperty("sessionId").GetInt32(), Is.EqualTo(3));
            Assert.That(manualInput.RootElement.GetProperty("steering").GetSingle(), Is.EqualTo(-0.5f));
            Assert.That(manualInput.RootElement.GetProperty("throttle").GetSingle(), Is.EqualTo(0.75f));
            Assert.That(manualInput.RootElement.GetProperty("brake").GetSingle(), Is.EqualTo(0.25f));
            Assert.That(Directory.GetFiles(client.ControlDirectory, "*.tmp"), Is.Empty);
        });
    }

    [Test]
    public void SimulationProgressUsesCloserRaceOrTimeLimit()
    {
        var snapshot = new LiveRaceSnapshot
        {
            IsSimulation = true,
            SimulatedMilliseconds = 100_000,
            MaximumSimulatedMilliseconds = 600_000,
            Session = new LiveRaceSession
            {
                Type = "Race",
                Phase = "racing",
                Laps = 3,
                StartTimeMilliseconds = 10_000,
            },
            Cars =
            [
                new LiveRaceCar { IsActive = true, Lap = 1 },
                new LiveRaceCar { IsActive = true, Lap = 2 },
                new LiveRaceCar { IsActive = true, IsDnf = true },
            ],
        };

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.SimulationProgressPercent, Is.EqualTo(100d / 3).Within(0.01));
            Assert.That(snapshot.EstimatedRemainingSimulatedMilliseconds, Is.EqualTo(180_000));
        });
    }

    [Test]
    public void LapLimitedSimulationProgressTracksTheLeader()
    {
        var snapshot = new LiveRaceSnapshot
        {
            IsSimulation = true,
            SimulatedMilliseconds = 120_000,
            MaximumSimulatedLaps = 4,
            Session = new LiveRaceSession
            {
                Type = "Race",
                Phase = "racing",
                Laps = 10,
                StartTimeMilliseconds = 20_000,
            },
            Cars =
            [
                new LiveRaceCar { IsActive = true, Lap = 1, NormalizedPosition = 0.5f },
                new LiveRaceCar { IsActive = true, Lap = 2, NormalizedPosition = 0.25f },
            ],
        };

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.LeadingLapProgress, Is.EqualTo(2.25).Within(0.001));
            Assert.That(snapshot.SimulationProgressPercent, Is.EqualTo(56.25).Within(0.01));
            Assert.That(snapshot.EstimatedRemainingSimulatedMilliseconds, Is.EqualTo(77_777).Within(1));
        });
    }

    [Test]
    public void ReadsStructuredSimulationResults()
    {
        var client = new LiveRaceControlClient(_root);
        Directory.CreateDirectory(Path.GetDirectoryName(client.SimulationSummaryPath)!);
        File.WriteAllText(client.SimulationSummaryPath, """
        {
          "schemaVersion": 3,
          "completedAt": "2026-08-22T12:00:00Z",
          "status": "completed",
          "track": "magione",
          "simulatedMilliseconds": 185000,
          "realTimeFactor": 10.0,
          "anomalyCount": 0,
          "stoppedObstaclePassCommits": 4,
          "stoppedObstaclePassesCompleted": 3,
          "stoppedObstacleEpisodes": [{
            "sessionId": 0, "startedAt": 40000, "endedAt": 50000,
            "durationMilliseconds": 10000, "sessionGeneration": 1,
            "endReason": "bot_go", "passCommits": 4, "passesCompleted": 3,
            "contactManifolds": 2
          }],
          "physics": { "vehicleManifolds": 12 },
          "results": [{
            "sessionId": 0, "name": "Bot 1", "model": "bmw_m3_e30",
            "racePos": 0, "numLaps": 3, "bestLap": 61234, "totalTime": 184500,
            "hasCompletedLastLap": true, "averageSpeedKmh": 93.2, "topSpeedKmh": 171.4,
            "crashCount": 1, "fullStopCount": 2, "fullyStoppedMilliseconds": 1500
          }]
        }
        """);

        var summary = client.TryReadSimulationSummary();

        Assert.Multiple(() =>
        {
            Assert.That(summary, Is.Not.Null);
            Assert.That(summary!.Outcome, Is.EqualTo("RACE COMPLETE"));
            Assert.That(summary.Overview, Does.Contain("12 contact frames"));
            Assert.That(summary.Overview, Does.Contain("3/4 passes completed"));
            Assert.That(summary.StoppedObstacleEpisodes.Single().EndReason, Is.EqualTo("bot_go"));
            Assert.That(summary.Results.Single().Position, Is.EqualTo(1));
            Assert.That(summary.Results.Single().BestLapTime, Is.EqualTo("1:01.234"));
            Assert.That(summary.Results.Single().CrashCount, Is.EqualTo(1));
            Assert.That(summary.Results.Single().FullStopCount, Is.EqualTo(2));
        });
    }
}
