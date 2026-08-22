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
    {
        Directory.CreateDirectory(CommandsDirectory);
        var id = Guid.NewGuid();
        string commandName = command.ToString().ToLowerInvariant();
        string filename = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfffffff}-{id:N}.json";
        string destination = Path.Combine(CommandsDirectory, filename);
        string temporary = destination + ".tmp";
        string json = JsonSerializer.Serialize(new
        {
            id,
            command = commandName,
            requestedAt = DateTimeOffset.UtcNow,
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
    public LiveRaceSession Session { get; set; } = new();
    public LiveRaceCommandResult? LastCommand { get; set; }
    public List<LiveRaceCar> Cars { get; set; } = [];
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
    public float Z { get; set; }
    public float VelocityX { get; set; }
    public float VelocityZ { get; set; }
    public float HeadingRadians { get; set; }
    public double SpeedKmh { get; set; }
    public float NormalizedPosition { get; set; }
    public uint Lap { get; set; }
    public int? RacePosition { get; set; }
    public bool IsDnf { get; set; }
    public bool HasFinished { get; set; }

    public string DisplayName => $"{SessionId + 1}. {Name} — {Model}";
    public string Kind => IsBot ? "BOT" : IsConnected ? "HUMAN" : "EMPTY";
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
    public SimulationPhysicsSummary Physics { get; set; } = new();
    public List<SimulationRaceResult> Results { get; set; } = [];

    public string Outcome => Status switch
    {
        "completed" => "RACE COMPLETE",
        "maximum_simulated_time" => "SIMULATION TIME LIMIT REACHED",
        "maximum_wall_time" => "WALL-CLOCK LIMIT REACHED",
        _ => "SIMULATION STOPPED",
    };

    public string Overview =>
        $"{Results.Count} cars  •  {FormatDuration(SimulatedMilliseconds)} virtual  •  "
        + $"{RealTimeFactor:F1}× achieved  •  {Physics.VehicleManifolds} contact frames  •  "
        + $"{AnomalyCount} anomalies";

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
