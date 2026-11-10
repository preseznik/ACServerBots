using AssettoServer.Server.Runtime;

namespace AssettoServer.Tests;

[TestFixture]
public class RaceResultsRecorderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 20, 0, 0, TimeSpan.Zero);
    private static readonly RaceRunKey Race = new(2, 60_000);

    [Test]
    public void FinishedRace_IsSavedAndUpdatedAsMoreCarsFinish()
    {
        var recorder = new RaceResultsRecorder();
        RaceResultLine[] lines = [Line(0, 1, laps: 9), Line(1, 2, laps: 9)];

        var whileRacing = recorder.Observe(Race, "Race", 10, "racing", Now, () => lines);
        lines = [Line(0, 1, laps: 10, finished: true), Line(1, 2, laps: 9)];
        var leaderFinished = recorder.Observe(Race, "Race", 10, "finished", Now, () => lines);
        var unchanged = recorder.Observe(Race, "Race", 10, "finished", Now, () => lines);
        lines = [Line(0, 1, laps: 10, finished: true), Line(1, 2, laps: 10, finished: true)];
        var secondFinished = recorder.Observe(Race, "Race", 10, "finished", Now, () => lines);
        var leftSession = recorder.Observe(null, "Practice", 0, "waiting", Now, () => []);

        Assert.Multiple(() =>
        {
            Assert.That(whileRacing, Is.Empty);
            Assert.That(leaderFinished.Single().Status, Is.EqualTo("finished"));
            Assert.That(unchanged, Is.Empty);
            Assert.That(secondFinished.Single().Results.Count(line => line.Finished), Is.EqualTo(2));
            Assert.That(leftSession, Is.Empty, "a race already saved isn't saved again when the session moves on");
        });
    }

    [Test]
    public void RaceLeftBeforeFinishing_IsSavedAsEnded()
    {
        var recorder = new RaceResultsRecorder();
        recorder.Observe(Race, "Race", 30, "racing", Now, () => [Line(0, 1, laps: 12), Line(1, 2, laps: 11)]);

        var captures = recorder.Observe(null, "Next", 0, "waiting", Now.AddMinutes(1), () => []);

        Assert.Multiple(() =>
        {
            Assert.That(captures.Single().Status, Is.EqualTo("ended"));
            Assert.That(captures.Single().Results[0].Laps, Is.EqualTo(12u));
            Assert.That(captures.Single().StartedAt, Is.EqualTo(Now));
            Assert.That(captures.Single().TargetLaps, Is.EqualTo(30));
        });
    }

    [Test]
    public void RestartBeforeAnyLap_SavesNothing()
    {
        var recorder = new RaceResultsRecorder();
        recorder.Observe(Race, "Race", 10, "racing", Now, () => [Line(0, 1, laps: 0)]);

        var captures = recorder.Observe(Race with { StartTimeMilliseconds = 120_000 }, "Race", 10, "racing", Now,
            () => [Line(0, 1, laps: 0)]);

        Assert.That(captures, Is.Empty);
    }

    [Test]
    public void StoppedRace_IsSavedAsStopped()
    {
        var recorder = new RaceResultsRecorder();
        recorder.Observe(Race, "Race", 10, "racing", Now, () => [Line(0, 1, laps: 4)]);

        var captures = recorder.Observe(Race, "Race", 10, "stopped", Now, () => [Line(0, 1, laps: 4)]);

        Assert.That(captures.Single().Status, Is.EqualTo("stopped"));
    }

    [Test]
    public void ServerStopMidRace_FlushesOnce()
    {
        var recorder = new RaceResultsRecorder();
        recorder.Observe(Race, "Race", 10, "racing", Now, () => [Line(0, 1, laps: 4, drivers: ["Alice", "Bob"])]);

        var first = recorder.Flush(Now);
        var second = recorder.Flush(Now);

        Assert.Multiple(() =>
        {
            Assert.That(first?.Status, Is.EqualTo("server_stopped"));
            Assert.That(first?.Results[0].Drivers, Is.EqualTo(new[] { "Alice", "Bob" }));
            Assert.That(second, Is.Null);
        });
    }

    [Test]
    public void NoRace_FlushesNothing()
        => Assert.That(new RaceResultsRecorder().Flush(Now), Is.Null);

    private static RaceResultLine Line(int sessionId, int position, uint laps, bool finished = false,
        bool dnf = false, string[]? drivers = null)
        => new(sessionId, position, $"Driver {sessionId}", drivers ?? [], "", "car", false, laps,
            laps * 90_000, 90_000, finished, dnf);
}
