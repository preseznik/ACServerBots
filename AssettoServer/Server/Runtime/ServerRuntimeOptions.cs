using System;
using System.Diagnostics;
using System.Threading;

namespace AssettoServer.Server.Runtime;

public interface IServerClock
{
    long ElapsedMilliseconds { get; }
    void Start();
}

public sealed class RealTimeServerClock : IServerClock
{
    private readonly Stopwatch _stopwatch = new();

    public long ElapsedMilliseconds => _stopwatch.ElapsedMilliseconds;
    public void Start() => _stopwatch.Start();
}

public sealed class ManualServerClock : IServerClock
{
    private long _elapsedTicks;
    private long _fixedStepRemainder;

    public long ElapsedMilliseconds => _elapsedTicks / TimeSpan.TicksPerMillisecond;

    public void Start()
    {
    }

    public void Advance(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));
        _elapsedTicks += duration.Ticks;
    }

    public void AdvanceFixedStep(int updatesPerSecond)
    {
        if (updatesPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(updatesPerSecond));
        _elapsedTicks += TimeSpan.TicksPerSecond / updatesPerSecond;
        _fixedStepRemainder += TimeSpan.TicksPerSecond % updatesPerSecond;
        if (_fixedStepRemainder < updatesPerSecond)
            return;
        _elapsedTicks += _fixedStepRemainder / updatesPerSecond;
        _fixedStepRemainder %= updatesPerSecond;
    }
}

public sealed class ServerRuntimeOptions
{
    private int _simulationStopRequested;
    public bool IsRaceSimulation { get; init; }
    public int SimulationSeed { get; init; } = 1;
    public string SimulationOutputDirectory { get; init; } = "simulation";
    public long MaximumSimulatedMilliseconds { get; init; } = 30 * 60_000;
    public int MaximumWallTimeSeconds { get; init; } = 300;
    public int SampleIntervalMilliseconds { get; init; } = 500;
    public double TargetRealTimeFactor { get; init; }
    public IServerClock Clock { get; init; } = new RealTimeServerClock();
    public string? RaceControlDirectory { get; init; }
    public string? SimulationCompletionReason { get; set; }
    public ManualResetEventSlim SimulationReady { get; } = new(false);
    public bool SimulationStopRequested => Volatile.Read(ref _simulationStopRequested) != 0;

    public void RequestSimulationStop(string reason)
    {
        SimulationCompletionReason = reason;
        Interlocked.Exchange(ref _simulationStopRequested, 1);
    }

    public static ServerRuntimeOptions CreateSimulation(string outputDirectory, int seed,
        int maximumSimulatedMinutes, int maximumWallTimeSeconds, int sampleIntervalMilliseconds,
        string? raceControlDirectory = null, double targetRealTimeFactor = 0)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("Simulation output directory is required", nameof(outputDirectory));
        if (maximumSimulatedMinutes is < 1 or > 1440)
            throw new ArgumentOutOfRangeException(nameof(maximumSimulatedMinutes));
        if (maximumWallTimeSeconds is < 1 or > 86_400)
            throw new ArgumentOutOfRangeException(nameof(maximumWallTimeSeconds));
        if (sampleIntervalMilliseconds is < 50 or > 60_000)
            throw new ArgumentOutOfRangeException(nameof(sampleIntervalMilliseconds));
        if (targetRealTimeFactor != 0 && targetRealTimeFactor is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(targetRealTimeFactor));

        return new ServerRuntimeOptions
        {
            IsRaceSimulation = true,
            SimulationSeed = seed,
            SimulationOutputDirectory = System.IO.Path.GetFullPath(outputDirectory),
            MaximumSimulatedMilliseconds = maximumSimulatedMinutes * 60_000L,
            MaximumWallTimeSeconds = maximumWallTimeSeconds,
            SampleIntervalMilliseconds = sampleIntervalMilliseconds,
            TargetRealTimeFactor = targetRealTimeFactor,
            Clock = new ManualServerClock(),
            RaceControlDirectory = NormalizeOptionalDirectory(raceControlDirectory),
        };
    }

    public static ServerRuntimeOptions CreateLiveServer(string? raceControlDirectory) => new()
    {
        RaceControlDirectory = NormalizeOptionalDirectory(raceControlDirectory),
    };

    private static string? NormalizeOptionalDirectory(string? directory) =>
        string.IsNullOrWhiteSpace(directory) ? null : System.IO.Path.GetFullPath(directory);
}

public interface IRaceRandomSource
{
    int Next(int maxValue);
    int Next(int minValue, int maxValue);
    double NextDouble();
    float NextSingle(float minValue, float maxValue);
}

public sealed class RaceRandomSource : IRaceRandomSource
{
    private readonly object _sync = new();
    private readonly Random? _seededRandom;

    public RaceRandomSource(ServerRuntimeOptions runtimeOptions)
    {
        if (runtimeOptions.IsRaceSimulation)
            _seededRandom = new Random(runtimeOptions.SimulationSeed);
    }

    public int Next(int maxValue) => Use(random => random.Next(maxValue));
    public int Next(int minValue, int maxValue) => Use(random => random.Next(minValue, maxValue));
    public double NextDouble() => Use(random => random.NextDouble());
    public float NextSingle(float minValue, float maxValue) =>
        minValue + (float)NextDouble() * (maxValue - minValue);

    private T Use<T>(Func<Random, T> operation)
    {
        if (_seededRandom == null)
            return operation(Random.Shared);
        lock (_sync)
            return operation(_seededRandom);
    }
}
