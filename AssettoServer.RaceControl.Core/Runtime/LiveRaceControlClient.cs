using System.Text;
using System.Text.Json;

namespace AssettoServer.RaceControl.Core.Runtime;

public enum LiveRaceCommand
{
    Start,
    Stop,
    Restart,
}

public sealed class LiveRaceControlClient
{
    public const string DirectoryName = "race-control-live";
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public string ControlDirectory { get; }
    public string CommandsDirectory => Path.Combine(ControlDirectory, "commands");
    public string SnapshotPath => Path.Combine(ControlDirectory, "state.json");
    public string TrackPath => Path.Combine(ControlDirectory, "track.json");
    public string ManualInputPath => Path.Combine(ControlDirectory, "manual-input.json");
    public string SimulationSummaryPath => Path.Combine(GetSimulationOutputDirectory(
        Path.GetDirectoryName(ControlDirectory)!), "summary.json");

    public LiveRaceControlClient(string instanceRoot)
    {
        ControlDirectory = GetControlDirectory(instanceRoot);
    }

    public static string GetControlDirectory(string instanceRoot) =>
        Path.Combine(Path.GetFullPath(instanceRoot), DirectoryName);

    public static string GetSimulationOutputDirectory(string instanceRoot) =>
        Path.Combine(Path.GetFullPath(instanceRoot), "simulation");

    public LiveRaceSnapshot? TryReadSnapshot() => TryRead<LiveRaceSnapshot>(SnapshotPath);
    public LiveTrackMap? TryReadTrack() => TryRead<LiveTrackMap>(TrackPath);
    public SimulationRaceSummary? TryReadSimulationSummary() =>
        TryRead<SimulationRaceSummary>(SimulationSummaryPath);

    public async Task<Guid> SendCommandAsync(LiveRaceCommand command,
        CancellationToken cancellationToken = default)
        => await SendCommandAsync(command.ToString().ToLowerInvariant(), null, null, cancellationToken);

    public async Task<Guid> SendSimulationTimeScaleAsync(double timeScale,
        CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(timeScale) || timeScale is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(timeScale));
        return await SendCommandAsync("simulation_time_scale", timeScale, null, cancellationToken);
    }

    public Task<Guid> SendBotStopAsync(int sessionId, bool stop,
        CancellationToken cancellationToken = default) =>
        SendBotCommandAsync(stop ? "bot_stop" : "bot_go", sessionId, cancellationToken);

    public Task<Guid> SendBotTeleportToP1Async(int sessionId,
        CancellationToken cancellationToken = default) =>
        SendBotCommandAsync("bot_teleport_p1", sessionId, cancellationToken);

    public Task<Guid> SendBotTakeoverAsync(int sessionId, bool takeOver,
        CancellationToken cancellationToken = default) =>
        SendBotCommandAsync(takeOver ? "bot_takeover" : "bot_release", sessionId, cancellationToken);

    public async Task WriteManualInputAsync(int sessionId, float steering, float throttle, float brake,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(ControlDirectory);
        string temporary = ManualInputPath + $".{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        var now = DateTimeOffset.UtcNow;
        string json = JsonSerializer.Serialize(new
        {
            sequence = now.UtcTicks,
            sessionId,
            steering = Math.Clamp(steering, -1, 1),
            throttle = Math.Clamp(throttle, 0, 1),
            brake = Math.Clamp(brake, 0, 1),
            requestedAt = now,
        }, _jsonOptions);
        await File.WriteAllTextAsync(temporary, json, new UTF8Encoding(false), cancellationToken);
        File.Move(temporary, ManualInputPath, true);
    }

    private Task<Guid> SendBotCommandAsync(string command, int sessionId,
        CancellationToken cancellationToken)
    {
        if (sessionId is < 0 or > 253)
            throw new ArgumentOutOfRangeException(nameof(sessionId));
        return SendCommandAsync(command, null, sessionId, cancellationToken);
    }

    private async Task<Guid> SendCommandAsync(string command, double? timeScale, int? sessionId,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(CommandsDirectory);
        var id = Guid.NewGuid();
        string filename = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfffffff}-{id:N}.json";
        string destination = Path.Combine(CommandsDirectory, filename);
        string temporary = destination + ".tmp";
        string json = JsonSerializer.Serialize(new
        {
            id,
            command,
            requestedAt = DateTimeOffset.UtcNow,
            timeScale,
            sessionId,
        }, _jsonOptions);
        await File.WriteAllTextAsync(temporary, json, new UTF8Encoding(false), cancellationToken);
        File.Move(temporary, destination);
        return id;
    }

    private T? TryRead<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path))
                return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<T>(stream, _jsonOptions);
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or JsonException)
        {
            return null;
        }
    }
}

public sealed class LiveRaceSnapshot
{
    public int SchemaVersion { get; set; }
    public long Sequence { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public bool ServerRunning { get; set; }
    public bool IsSimulation { get; set; }
    public long SimulatedMilliseconds { get; set; }
    public double RealTimeFactor { get; set; }
    public long MaximumSimulatedMilliseconds { get; set; }
    public int MaximumSimulatedLaps { get; set; }
    public double TargetRealTimeFactor { get; set; }
    public LiveRaceSession Session { get; set; } = new();
    public LiveRaceCommandResult? LastCommand { get; set; }
    public List<LiveRaceCar> Cars { get; set; } = [];

    public double SimulationProgressPercent
    {
        get
        {
            if (!IsSimulation)
                return 0;
            double timeLimitProgress = MaximumSimulatedMilliseconds <= 0
                ? 0
                : SimulatedMilliseconds / (double)MaximumSimulatedMilliseconds;
            double lapLimitProgress = MaximumSimulatedLaps <= 0
                ? 0
                : LeadingLapProgress / MaximumSimulatedLaps;
            return Math.Clamp(Math.Max(Math.Max(timeLimitProgress, lapLimitProgress),
                GetRaceCompletionRatio()) * 100, 0, 100);
        }
    }

    public long EstimatedRemainingSimulatedMilliseconds
    {
        get
        {
            if (!IsSimulation)
                return 0;
            double remaining = MaximumSimulatedMilliseconds <= 0
                ? double.PositiveInfinity
                : Math.Max(0, MaximumSimulatedMilliseconds - SimulatedMilliseconds);
            double lapLimitProgress = MaximumSimulatedLaps <= 0
                ? 0
                : Math.Clamp(LeadingLapProgress / MaximumSimulatedLaps, 0, 1);
            double raceProgress = GetRaceCompletionRatio();
            long raceElapsed = Math.Max(0, SimulatedMilliseconds - Session.StartTimeMilliseconds);
            if (lapLimitProgress > 0 && raceElapsed > 0)
                remaining = Math.Min(remaining,
                    raceElapsed * (1 - lapLimitProgress) / lapLimitProgress);
            if (raceProgress > 0 && raceElapsed > 0)
                remaining = Math.Min(remaining, raceElapsed * (1 - raceProgress) / raceProgress);
            return double.IsFinite(remaining) ? (long)Math.Max(0, remaining) : 0;
        }
    }

    public double LeadingLapProgress => Cars
        .Where(car => car.IsActive && !car.IsDnf)
        .Select(car => car.Lap + Math.Clamp(car.NormalizedPosition, 0, 1))
        .DefaultIfEmpty(0)
        .Max();

    private double GetRaceCompletionRatio()
    {
        if (!Session.Type.Equals("Race", StringComparison.OrdinalIgnoreCase) || Session.Laps <= 0)
            return 0;
        var competitors = Cars.Where(car => car.IsActive && !car.IsDnf).ToArray();
        if (competitors.Length == 0)
            return Cars.Any(car => car.IsActive) ? 1 : 0;
        return competitors.Min(car => car.HasFinished
            ? 1
            : Math.Clamp(car.Lap / (double)Session.Laps, 0, 1));
    }
}

public sealed class LiveRaceSession
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public long StartTimeMilliseconds { get; set; }
    public long CountdownMilliseconds { get; set; }
    public int TimeLeftMilliseconds { get; set; }
    public int Laps { get; set; }
}

public sealed class LiveRaceCommandResult
{
    public Guid Id { get; set; }
    public string Command { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class LiveRaceCar
{
    public int SessionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Skin { get; set; } = string.Empty;
    public bool IsBot { get; set; }
    public bool IsConnected { get; set; }
    public bool IsActive { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }
    public float VelocityZ { get; set; }
    public float HeadingRadians { get; set; }
    public float OrientationX { get; set; }
    public float OrientationY { get; set; }
    public float OrientationZ { get; set; }
    public float OrientationW { get; set; } = 1;
    public float ForwardX { get; set; }
    public float ForwardY { get; set; }
    public float ForwardZ { get; set; } = 1;
    public double SpeedKmh { get; set; }
    public byte ProtocolGear { get; set; }
    public int EngineRpm { get; set; }
    public float NormalizedPosition { get; set; }
    public uint Lap { get; set; }
    public int StoppedObstaclePassCommits { get; set; }
    public int StoppedObstaclePassesCompleted { get; set; }
    public int? RacePosition { get; set; }
    public bool IsDnf { get; set; }
    public bool HasFinished { get; set; }
    public string ControlMode { get; set; } = "automatic";
    public float ManualSteering { get; set; }
    public float ManualThrottle { get; set; }
    public float ManualBrake { get; set; }

    public string DisplayName => $"{SessionId + 1}. {Name} — {Model}";
    public string GearDisplay => ProtocolGear switch
    {
        0 => "R",
        1 => "N",
        _ => $"{ProtocolGear - 1}",
    };
    public string Kind => IsBot ? "BOT" : IsConnected ? "HUMAN" : "EMPTY";
    public bool IsStoppedByRaceControl => ControlMode.Equals("stopped", StringComparison.OrdinalIgnoreCase);
    public bool IsManuallyControlled => ControlMode.Equals("manual", StringComparison.OrdinalIgnoreCase);
}

public sealed class LiveTrackMap
{
    public int SchemaVersion { get; set; }
    public string Track { get; set; } = string.Empty;
    public string Layout { get; set; } = string.Empty;
    public List<LiveTrackPoint> Points { get; set; } = [];
}

public sealed class LiveTrackPoint
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float LeftWidth { get; set; }
    public float RightWidth { get; set; }
}

public sealed class SimulationRaceSummary
{
    public int SchemaVersion { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Track { get; set; } = string.Empty;
    public string Layout { get; set; } = string.Empty;
    public int Seed { get; set; }
    public long SimulatedMilliseconds { get; set; }
    public double WallMilliseconds { get; set; }
    public double RealTimeFactor { get; set; }
    public double TargetRealTimeFactor { get; set; }
    public int AnomalyCount { get; set; }
    public int StoppedObstaclePassCommits { get; set; }
    public int StoppedObstaclePassesCompleted { get; set; }
    public SimulationPhysicsSummary Physics { get; set; } = new();
    public List<SimulationStoppedObstacleEpisode> StoppedObstacleEpisodes { get; set; } = [];
    public List<SimulationRaceResult> Results { get; set; } = [];

    public string Outcome => Status switch
    {
        "completed" => "RACE COMPLETE",
        "maximum_simulated_time" => "SIMULATION TIME LIMIT REACHED",
        "maximum_simulated_laps" => "SIMULATION LAP LIMIT REACHED",
        "maximum_wall_time" => "WALL-CLOCK LIMIT REACHED",
        _ => "SIMULATION STOPPED",
    };

    public string Overview =>
        $"{Results.Count} cars  •  {FormatDuration(SimulatedMilliseconds)} virtual  •  "
        + $"{RealTimeFactor:F1}× achieved  •  {Physics.VehicleManifolds} contact frames  •  "
        + $"{AnomalyCount} anomalies"
        + (StoppedObstacleEpisodes.Count == 0 ? string.Empty
            : $"  •  stopped-car tests {StoppedObstacleEpisodes.Count}: "
              + $"{StoppedObstaclePassesCompleted}/{StoppedObstaclePassCommits} passes completed");

    internal static string FormatDuration(long milliseconds)
    {
        if (milliseconds <= 0)
            return "—";
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        return duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss\.fff")
            : duration.ToString(@"m\:ss\.fff");
    }
}

public sealed class SimulationPhysicsSummary
{
    public long VehicleManifolds { get; set; }
}

public sealed class SimulationStoppedObstacleEpisode
{
    public int SessionId { get; set; }
    public long StartedAt { get; set; }
    public long EndedAt { get; set; }
    public long DurationMilliseconds { get; set; }
    public int SessionGeneration { get; set; }
    public string EndReason { get; set; } = string.Empty;
    public int PassCommits { get; set; }
    public int PassesCompleted { get; set; }
    public long ContactManifolds { get; set; }
}

public sealed class SimulationRaceResult
{
    public int SessionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public uint RacePos { get; set; }
    public uint NumLaps { get; set; }
    public uint LastLap { get; set; }
    public uint BestLap { get; set; }
    public uint TotalTime { get; set; }
    public bool HasCompletedLastLap { get; set; }
    public bool IsDnf { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public double AverageSpeedKmh { get; set; }
    public double TopSpeedKmh { get; set; }
    public int CrashCount { get; set; }
    public long ContactManifolds { get; set; }
    public int RecoveryCount { get; set; }
    public int FullStopCount { get; set; }
    public long FullyStoppedMilliseconds { get; set; }

    public int Position => (int)RacePos + 1;
    public string Driver => string.IsNullOrWhiteSpace(Model) ? Name : $"{Name}  •  {Model}";
    public string Time => SimulationRaceSummary.FormatDuration(
        HasCompletedLastLap && TotalTime > 0 ? TotalTime : ElapsedMilliseconds);
    public string BestLapTime => BestLap >= 999_999_999
        ? "—"
        : SimulationRaceSummary.FormatDuration(BestLap);
    public string AverageSpeed => $"{AverageSpeedKmh:F1}";
    public string TopSpeed => $"{TopSpeedKmh:F1}";
    public string FullyStoppedTime => SimulationRaceSummary.FormatDuration(FullyStoppedMilliseconds);
    public string Outcome => IsDnf ? "DNF" : HasCompletedLastLap ? "FINISHED" : "INCOMPLETE";
}
