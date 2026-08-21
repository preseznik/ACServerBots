using AssettoServer.Server.Runtime;

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

    private static ServerRuntimeOptions CreateOptions(int seed) =>
        ServerRuntimeOptions.CreateSimulation(Path.GetTempPath(), seed, 30, 300, 500);
}
