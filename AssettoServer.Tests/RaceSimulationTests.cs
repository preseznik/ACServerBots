using AssettoServer.Server;
using AssettoServer.Server.Runtime;
using AssettoServer.Server.RaceSimulation;

namespace AssettoServer.Tests;

[TestFixture]
public class RaceSimulationTests
{
    [Test]
    public void ManualClockAdvancesWithoutWallTime()
    {
        var clock = new ManualServerClock();
        clock.Start();

        for (int i = 0; i < 60; i++)
            clock.AdvanceFixedStep(60);

        Assert.That(clock.ElapsedMilliseconds, Is.EqualTo(1000));
    }

    [Test]
    public void SeededRaceRandomIsReproducible()
    {
        var first = new RaceRandomSource(CreateOptions(seed: 42));
        var second = new RaceRandomSource(CreateOptions(seed: 42));

        var firstValues = Enumerable.Range(0, 20).Select(_ => first.Next(10_000)).ToArray();
        var secondValues = Enumerable.Range(0, 20).Select(_ => second.Next(10_000)).ToArray();

        Assert.That(secondValues, Is.EqualTo(firstValues));
    }

    [Test]
    public void DifferentSeedsProduceDifferentRaceRandomSequences()
    {
        var first = new RaceRandomSource(CreateOptions(seed: 1));
        var second = new RaceRandomSource(CreateOptions(seed: 2));

        var firstValues = Enumerable.Range(0, 10).Select(_ => first.Next(10_000)).ToArray();
        var secondValues = Enumerable.Range(0, 10).Select(_ => second.Next(10_000)).ToArray();

        Assert.That(secondValues, Is.Not.EqualTo(firstValues));
    }

    [Test]
    public void SimulationStopRequestPreservesReason()
    {
        var options = CreateOptions(seed: 1);

        options.RequestSimulationStop("completed");

        Assert.That(options.SimulationStopRequested, Is.True);
        Assert.That(options.SimulationCompletionReason, Is.EqualTo("completed"));
    }

    [TestCase(0, 300, 500)]
    [TestCase(30, 0, 500)]
    [TestCase(30, 300, 10)]
    public void SimulationOptionsRejectUnsafeBounds(int minutes, int wallSeconds, int sampleMilliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ServerRuntimeOptions.CreateSimulation(Path.GetTempPath(), 1, minutes,
                wallSeconds, sampleMilliseconds));
    }

    [Test]
    public void SimulationCanUseLapLimitInsteadOfTimeLimit()
    {
        var options = ServerRuntimeOptions.CreateSimulation(Path.GetTempPath(), 1, 0,
            300, 500, maximumSimulatedLaps: 3);

        Assert.Multiple(() =>
        {
            Assert.That(options.MaximumSimulatedMilliseconds, Is.Zero);
            Assert.That(options.MaximumSimulatedLaps, Is.EqualTo(3));
        });
    }

    [Test]
    public void SimulationRejectsSimultaneousTimeAndLapLimits()
    {
        Assert.Throws<ArgumentException>(() =>
            ServerRuntimeOptions.CreateSimulation(Path.GetTempPath(), 1, 30,
                300, 500, maximumSimulatedLaps: 3));
    }

    [TestCase(0, 0)]
    [TestCase(1000, 150)]
    public void SimulationPacingDoesNotDelayUnlimitedOrBehindSchedule(long simulatedMilliseconds,
        double wallMilliseconds)
    {
        double targetFactor = simulatedMilliseconds == 0 ? 0 : 10;

        Assert.That(ACServer.CalculateSimulationPacingDelayMilliseconds(
            simulatedMilliseconds, wallMilliseconds, targetFactor), Is.EqualTo(0));
    }

    [Test]
    public void SimulationPacingBoundsSleepWhileAheadOfTarget()
    {
        Assert.That(ACServer.CalculateSimulationPacingDelayMilliseconds(1000, 50, 10), Is.EqualTo(20));
        Assert.That(ACServer.CalculateSimulationPacingDelayMilliseconds(1000, 99.5, 10), Is.EqualTo(1));
    }

    [TestCase(0.5)]
    [TestCase(101)]
    public void SimulationOptionsRejectUnsafeTimeScale(double factor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ServerRuntimeOptions.CreateSimulation(Path.GetTempPath(), 1, 30, 300, 500,
                targetRealTimeFactor: factor));
    }

    [Test]
    public void RunningSimulationAcceptsSafeTimeScaleChangesOnly()
    {
        var options = ServerRuntimeOptions.CreateSimulation(Path.GetTempPath(), 1, 30, 300, 500,
            targetRealTimeFactor: 10);

        Assert.Multiple(() =>
        {
            Assert.That(options.TrySetTargetRealTimeFactor(25), Is.True);
            Assert.That(options.TargetRealTimeFactor, Is.EqualTo(25));
            Assert.That(options.TrySetTargetRealTimeFactor(0), Is.False);
            Assert.That(options.TargetRealTimeFactor, Is.EqualTo(25));
            Assert.That(ServerRuntimeOptions.CreateLiveServer(null).TrySetTargetRealTimeFactor(10), Is.False);
        });
    }

    [Test]
    public void BotStatisticsTrackSpeedStopsAndRecoveriesAfterMovement()
    {
        var statistics = new RaceSimulationBotStatistics();

        statistics.Observe(0, 10, 0);
        statistics.Observe(500, 10, 0, 3);
        statistics.Observe(1000, 0, 0, 8);
        statistics.Observe(1500, 0, 2, 8);
        statistics.Observe(2000, 5, 2, 12);

        Assert.Multiple(() =>
        {
            Assert.That(statistics.AverageSpeedKilometersPerHour, Is.EqualTo(18).Within(0.01));
            Assert.That(statistics.TopSpeedKilometersPerHour, Is.EqualTo(36).Within(0.01));
            Assert.That(statistics.DistanceKilometers, Is.EqualTo(0.01).Within(0.0001));
            Assert.That(statistics.FullStopCount, Is.EqualTo(1));
            Assert.That(statistics.FullyStoppedMilliseconds, Is.EqualTo(1000));
            Assert.That(statistics.RecoveryCount, Is.EqualTo(2));
            Assert.That(statistics.ContactEpisodeCount, Is.EqualTo(2));
            Assert.That(statistics.ContactManifolds, Is.EqualTo(12));
            Assert.That(statistics.RecoveriesPer100Kilometers, Is.EqualTo(20_000).Within(1));
            Assert.That(statistics.ContactEpisodesPer100Kilometers, Is.EqualTo(20_000).Within(1));
            Assert.That(statistics.FullStopsPer100Kilometers, Is.EqualTo(10_000).Within(1));
        });
    }

    private static ServerRuntimeOptions CreateOptions(int seed) =>
        ServerRuntimeOptions.CreateSimulation(Path.GetTempPath(), seed, 30, 300, 500);
}
