using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AssettoServer.Server.Ai;
using AssettoServer.Server.Ai.Physics;
using AssettoServer.Server.Ai.Splines;
using AssettoServer.Server.RaceSimulation;
using AssettoServer.Server.Configuration;
using AssettoServer.Server.Fps;
using AssettoServer.Server.Weather;
using AssettoServer.Shared.Weather;
using AssettoServer.Shared.Model;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace AssettoServer.Server.Runtime;

public sealed class RaceControlBridge : IHostedService
{
    private const int SnapshotIntervalMilliseconds = 50;
    private const int TakeoverSnapshotIntervalMilliseconds = 16;
    private const int MaximumTrackPoints = 1500;

    private readonly ACServerConfiguration _configuration;
    private readonly ServerRuntimeOptions _runtimeOptions;
    private readonly ACServer _server;
    private readonly SessionManager _sessionManager;
    private readonly EntryCarManager _entryCarManager;
    private readonly AiSpline? _spline;
    private readonly RaceSimulationTelemetry? _simulationTelemetry;
    private readonly RaceBotPhysicsWorld? _physicsWorld;
    private readonly FpsWorld? _fpsWorld;
    private readonly WeatherManager _weatherManager;
    private readonly Stopwatch _wallClock = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private string _controlDirectory = null!;
    private string _commandsDirectory = null!;
    private string _snapshotPath = null!;
    private string _manualInputPath = null!;
    private long _initialServerTime;
    private long _sequence;
    private long _nextSnapshotAt;
    private long _nextManualInputAt;
    private long _lastManualInputSequence;
    private Guid? _lastCommandId;
    private string? _lastCommand;
    private string? _lastCommandStatus;
    private string? _lastCommandMessage;

    private sealed record CommandEnvelope(Guid Id, string Command, DateTimeOffset RequestedAt,
        double? TimeScale = null, int? SessionId = null, int? WeatherType = null,
        int? TimeOfDaySeconds = null);
    private sealed record ManualInputEnvelope(long Sequence, int SessionId, float Steering,
        float Throttle, float Brake, DateTimeOffset RequestedAt);

    public RaceControlBridge(ACServerConfiguration configuration,
        ServerRuntimeOptions runtimeOptions,
        ACServer server,
        SessionManager sessionManager,
        EntryCarManager entryCarManager,
        WeatherManager weatherManager,
        AiSpline? spline = null,
        RaceSimulationTelemetry? simulationTelemetry = null,
        RaceBotPhysicsWorld? physicsWorld = null,
        FpsWorld? fpsWorld = null)
    {
        _configuration = configuration;
        _runtimeOptions = runtimeOptions;
        _server = server;
        _sessionManager = sessionManager;
        _entryCarManager = entryCarManager;
        _spline = spline;
        _simulationTelemetry = simulationTelemetry;
        _physicsWorld = physicsWorld;
        _fpsWorld = fpsWorld;
        _weatherManager = weatherManager;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _controlDirectory = _runtimeOptions.RaceControlDirectory
                            ?? throw new InvalidOperationException("Race Control directory is not configured");
        _commandsDirectory = Path.Combine(_controlDirectory, "commands");
        _snapshotPath = Path.Combine(_controlDirectory, "state.json");
        _manualInputPath = Path.Combine(_controlDirectory, "manual-input.json");
        Directory.CreateDirectory(_commandsDirectory);

        _initialServerTime = _sessionManager.ServerTimeMilliseconds;
        _wallClock.Start();
        WriteTrack();
        WriteSnapshot(serverRunning: true);
        _server.Update += OnServerUpdate;
        Log.Information("Race Control live bridge ready at {Directory}", _controlDirectory);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _server.Update -= OnServerUpdate;
        WriteSnapshot(serverRunning: false);
        return Task.CompletedTask;
    }

    private void OnServerUpdate(object sender, EventArgs args)
    {
        try
        {
            if (_wallClock.ElapsedMilliseconds >= _nextManualInputAt)
            {
                ProcessManualInput();
                _nextManualInputAt = _wallClock.ElapsedMilliseconds + 16;
            }
            bool commandProcessed = ProcessCommands();
            if (commandProcessed || _wallClock.ElapsedMilliseconds >= _nextSnapshotAt)
            {
                WriteSnapshot(serverRunning: true);
                bool manualControlActive = _entryCarManager.EntryCars.Any(car =>
                    car.GetRaceControlMode() == RaceControlBotControlMode.Manual);
                _nextSnapshotAt = _wallClock.ElapsedMilliseconds
                                  + GetSnapshotIntervalMilliseconds(manualControlActive);
            }
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Race Control live bridge update failed");
        }
    }

    internal static int GetSnapshotIntervalMilliseconds(bool manualControlActive) =>
        manualControlActive ? TakeoverSnapshotIntervalMilliseconds : SnapshotIntervalMilliseconds;

    private bool ProcessCommands()
    {
        bool processed = false;
        foreach (string path in Directory.EnumerateFiles(_commandsDirectory, "*.json")
                     .OrderBy(Path.GetFileName, StringComparer.Ordinal)
                     .Take(20))
        {
            processed = true;
            bool deleteCommand = true;
            try
            {
                var command = JsonSerializer.Deserialize<CommandEnvelope>(File.ReadAllText(path), _jsonOptions)
                              ?? throw new InvalidDataException("Command document is empty");
                _lastCommandId = command.Id;
                _lastCommand = command.Command;
                bool accepted = command.Command.ToLowerInvariant() switch
                {
                    "start" => _sessionManager.StartRaceFromControl(),
                    "stop" => _sessionManager.StopRaceFromControl(),
                    "restart" => _sessionManager.RestartRaceFromControl(),
                    "simulation_time_scale" => command.TimeScale.HasValue
                                               && _runtimeOptions.TrySetTargetRealTimeFactor(command.TimeScale.Value),
                    "bot_stop" => TrySetBotMode(command.SessionId, RaceControlBotControlMode.Stopped),
                    "bot_go" => TrySetBotMode(command.SessionId, RaceControlBotControlMode.Automatic),
                    "bot_takeover" => TrySetBotMode(command.SessionId, RaceControlBotControlMode.Manual),
                    "bot_release" => TrySetBotMode(command.SessionId, RaceControlBotControlMode.Automatic),
                    "bot_teleport_p1" => TryTeleportBotToP1(command.SessionId),
                    "fps_environment" => TrySetFpsEnvironment(command.WeatherType,
                        command.TimeOfDaySeconds),
                    _ => throw new InvalidDataException($"Unknown race command '{command.Command}'"),
                };
                _lastCommandStatus = accepted ? "accepted" : "rejected";
                _lastCommandMessage = GetCommandMessage(command, accepted);
                _simulationTelemetry?.RecordControlCommand(command.Id, command.Command,
                    _lastCommandStatus, command.SessionId, command.TimeScale,
                    command.RequestedAt, _lastCommandMessage);
                Log.Information("Race Control command {Command} ({CommandId}) was {Status}",
                    command.Command, command.Id, _lastCommandStatus);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The launcher publishes commands with a rename. A very fast simulation tick can
                // observe the directory entry before Windows releases the writer's final handle.
                deleteCommand = false;
            }
            catch (Exception exception)
            {
                _lastCommandStatus = "error";
                _lastCommandMessage = exception.Message;
                Log.Warning(exception, "Could not process Race Control command {Path}", path);
            }
            finally
            {
                if (deleteCommand)
                    File.Delete(path);
            }
        }

        return processed;
    }

    private void ProcessManualInput()
    {
        if (!File.Exists(_manualInputPath))
            return;
        try
        {
            using var stream = new FileStream(_manualInputPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var input = JsonSerializer.Deserialize<ManualInputEnvelope>(stream, _jsonOptions);
            if (input == null || input.Sequence <= _lastManualInputSequence)
                return;
            _lastManualInputSequence = input.Sequence;
            if ((uint)input.SessionId >= _entryCarManager.EntryCars.Length)
                return;
            _entryCarManager.EntryCars[input.SessionId].TrySetRaceControlInput(
                input.Steering, input.Throttle, input.Brake, input.RequestedAt);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
        }
    }

    private bool TrySetBotMode(int? sessionId, RaceControlBotControlMode mode) =>
        sessionId is >= 0 && sessionId < _entryCarManager.EntryCars.Length
        && _entryCarManager.EntryCars[sessionId.Value].TrySetRaceControlMode(mode);

    private bool TryTeleportBotToP1(int? sessionId)
    {
        if (sessionId is not >= 0 || sessionId >= _entryCarManager.EntryCars.Length
                                   || _spline is not { Points.Length: > 1 }
                                   || _sessionManager.CurrentSession.Configuration.Type != SessionType.Race
                                   || _sessionManager.CurrentSession.Results == null)
            return false;
        var leader = _entryCarManager.EntryCars
            .Where(car => _sessionManager.CurrentSession.Results.ContainsKey(car.SessionId))
            .OrderBy(car => _sessionManager.CurrentSession.Results[car.SessionId].RacePos)
            .FirstOrDefault();
        if (leader == null)
            return false;
        int leaderPoint = leader.GetRaceAiStateSnapshot()?.SplinePointId
                          ?? _spline.WorldToSpline(leader.Status.Position).PointId;
        if (leaderPoint < 0)
            return false;
        return _entryCarManager.EntryCars[sessionId.Value]
            .TryTeleportRaceControlBot(AdvanceSplinePoint(leaderPoint, 12));
    }

    private bool TrySetFpsEnvironment(int? weatherType, int? timeOfDaySeconds) =>
        _fpsWorld != null
        && weatherType is >= 0 and <= byte.MaxValue
        && timeOfDaySeconds is >= 0 and < 24 * 60 * 60
        && _weatherManager.SetRaceControlEnvironment((WeatherFxType)weatherType.Value,
            timeOfDaySeconds.Value);

    private int AdvanceSplinePoint(int startPoint, float distanceMeters)
    {
        int point = Math.Clamp(startPoint, 0, _spline!.Points.Length - 1);
        float distance = 0;
        int steps = 0;
        while (distance < distanceMeters && steps++ < _spline.Points.Length)
        {
            int next = (point + 1) % _spline.Points.Length;
            distance += Vector3.Distance(_spline.Points[point].Position, _spline.Points[next].Position);
            point = next;
        }
        return point;
    }

    private string GetCommandMessage(CommandEnvelope command, bool accepted)
    {
        if (accepted)
        {
            return command.Command.ToLowerInvariant() switch
            {
                "simulation_time_scale" => $"Simulation time acceleration set to {_runtimeOptions.TargetRealTimeFactor:F0}x.",
                "bot_stop" => $"Bot {command.SessionId} stopped.",
                "bot_go" => $"Bot {command.SessionId} returned to AI control.",
                "bot_takeover" => $"Manual control enabled for bot {command.SessionId}.",
                "bot_release" => $"Manual control released for bot {command.SessionId}.",
                "bot_teleport_p1" => $"Bot {command.SessionId} teleported ahead of the current leader.",
                "fps_environment" => $"Environment set to {(WeatherFxType)command.WeatherType!.Value} at "
                                     + $"{TimeSpan.FromSeconds(command.TimeOfDaySeconds!.Value):hh\\:mm}.",
                _ => $"Race {command.Command} command accepted.",
            };
        }
        return command.Command.ToLowerInvariant() switch
        {
            "stop" => "There is no active race to stop.",
            "simulation_time_scale" => "Time acceleration can only be changed during a simulation and must be 1x to 100x.",
            "bot_stop" or "bot_go" or "bot_takeover" or "bot_release" or "bot_teleport_p1" =>
                "The selected slot is not an active server-controlled race bot.",
            "fps_environment" => "Environment controls require a running FPS match and valid weather/time values.",
            _ => "No race session is configured.",
        };
    }

    private void WriteTrack()
    {
        FpsLiveArenaSnapshot? fpsArena = _fpsWorld?.GetLiveArenaSnapshot();
        if (fpsArena != null)
        {
            AtomicWrite(Path.Combine(_controlDirectory, "track.json"), new
            {
                schemaVersion = 2,
                track = _configuration.Server.Track,
                layout = _configuration.Server.TrackConfig,
                isFpsArena = true,
                minimumX = fpsArena.BoundsMin.X,
                maximumX = fpsArena.BoundsMax.X,
                minimumZ = fpsArena.BoundsMin.Z,
                maximumZ = fpsArena.BoundsMax.Z,
                arenaCellSize = fpsArena.CellSize,
                arenaCells = fpsArena.Cells.Select(cell => new { x = cell.X, z = cell.Y }),
                points = Array.Empty<object>(),
            });
            return;
        }

        if (_spline == null)
        {
            AtomicWrite(Path.Combine(_controlDirectory, "track.json"), new
            {
                schemaVersion = 1,
                track = _configuration.Server.Track,
                layout = _configuration.Server.TrackConfig,
                points = Array.Empty<object>(),
            });
            return;
        }

        var points = _spline.Points;
        int step = Math.Max(1, (int)Math.Ceiling(points.Length / (double)MaximumTrackPoints));
        var sampled = new object[(points.Length + step - 1) / step];
        int target = 0;
        for (int index = 0; index < points.Length; index += step)
        {
            ref readonly var point = ref points[index];
            sampled[target++] = new
            {
                x = point.Position.X,
                y = point.Position.Y,
                z = point.Position.Z,
                leftWidth = Math.Max(0, point.SideLeft),
                rightWidth = Math.Max(0, point.SideRight),
            };
        }

        AtomicWrite(Path.Combine(_controlDirectory, "track.json"), new
        {
            schemaVersion = 1,
            track = _configuration.Server.Track,
            layout = _configuration.Server.TrackConfig,
            points = sampled,
        });
    }

    private void WriteSnapshot(bool serverRunning)
    {
        FpsLiveMatchSnapshot? fpsMatch = _fpsWorld?.GetLiveMatchSnapshot();
        if (fpsMatch != null)
        {
            WriteFpsSnapshot(fpsMatch, serverRunning);
            return;
        }

        long now = _sessionManager.ServerTimeMilliseconds;
        var session = _sessionManager.CurrentSession;
        string phase = session.IsStoppedByRaceControl ? "stopped"
            : session.Configuration.Type != SessionType.Race ? "waiting"
            : now < session.StartTimeMilliseconds ? "countdown"
            : session.HasSentRaceOverPacket ? "finished"
            : "racing";
        var results = session.Results;
        var cars = _entryCarManager.EntryCars.Where(car => !car.IsSpectator).Select(car =>
        {
            EntryCarResult? result = null;
            results?.TryGetValue(car.SessionId, out result);
            var ai = car.AiControlled ? car.GetRaceAiStateSnapshot() : null;
            RaceBotPhysicsTelemetry? physicsTelemetry = null;
            if (car.AiControlled && _physicsWorld?.TryGetBotTelemetry(
                    car.SessionId, out var telemetry) == true)
                physicsTelemetry = telemetry;
            Vector3 position = ai?.Position ?? car.Status.Position;
            Vector3 velocity = ai?.Velocity ?? car.Status.Velocity;
            // Bot position, velocity and orientation must come from the same authoritative AI
            // state. EntryCar.Status belongs to a connected client and remains identity for an
            // unclaimed bot slot, which previously left every chase-view car facing world +Z.
            Quaternion orientation = ResolveTelemetryOrientation(ai, car.Status.Rotation);
            RaceBotVehicleTelemetry drivetrain = ResolveTelemetryDrivetrain(ai,
                car.Status.Gear, car.Status.EngineRpm);
            Vector3 forward = Vector3.Transform(Vector3.UnitZ, orientation);
            double normalizedPosition = ai != null && _spline is { Points.Length: > 1 }
                ? ai.Value.SplinePointId / (double)(_spline.Points.Length - 1)
                : car.Status.NormalizedPosition;
            var manualInput = car.GetRaceControlInput();
            return new
            {
                sessionId = (int)car.SessionId,
                name = car.Client?.Name ?? car.AiName ?? $"Slot {car.SessionId + 1}",
                model = car.Model,
                skin = car.Skin,
                isBot = car.AiControlled,
                isConnected = car.Client?.HasSentFirstUpdate == true,
                isActive = car.AiControlled || car.Client?.HasSentFirstUpdate == true,
                x = position.X,
                y = position.Y,
                z = position.Z,
                velocityX = velocity.X,
                velocityY = velocity.Y,
                velocityZ = velocity.Z,
                headingRadians = MathF.Atan2(forward.Z, -forward.X),
                orientationX = orientation.X,
                orientationY = orientation.Y,
                orientationZ = orientation.Z,
                orientationW = orientation.W,
                forwardX = forward.X,
                forwardY = forward.Y,
                forwardZ = forward.Z,
                speedKmh = Math.Sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z) * 3.6,
                protocolGear = drivetrain.ProtocolGear,
                engineRpm = drivetrain.EngineRpm,
                targetSpeedKmh = (ai?.TargetSpeed ?? 0) * 3.6,
                effectiveTargetSpeedKmh = (physicsTelemetry?.EffectiveTargetSpeedMetersPerSecond ?? 0) * 3.6,
                courseBoundaryErrorMeters = physicsTelemetry?.CourseBoundaryErrorMeters ?? 0,
                courseDriveScale = physicsTelemetry?.CourseDriveScale ?? 1,
                normalizedPosition,
                lap = result?.NumLaps ?? 0,
                stoppedObstaclePassCommits = ai?.StoppedObstaclePassCommitCount ?? 0,
                stoppedObstaclePassesCompleted = ai?.StoppedObstaclePassCompletedCount ?? 0,
                racePosition = result == null ? null : (int?)result.RacePos + 1,
                isDnf = result?.IsDnf ?? false,
                hasFinished = result?.HasCompletedLastLap ?? false,
                controlMode = car.GetRaceControlMode().ToString().ToLowerInvariant(),
                manualSteering = manualInput.Steering,
                manualThrottle = manualInput.Throttle,
                manualBrake = manualInput.Brake,
            };
        }).ToArray();
        double wallMilliseconds = Math.Max(1, _wallClock.Elapsed.TotalMilliseconds);
        double realTimeFactor = Math.Max(0, now - _initialServerTime) / wallMilliseconds;

        AtomicWrite(_snapshotPath, new
        {
            schemaVersion = 1,
            sequence = ++_sequence,
            capturedAt = DateTimeOffset.UtcNow,
            serverRunning,
            isSimulation = _runtimeOptions.IsRaceSimulation,
            simulatedMilliseconds = now,
            realTimeFactor,
            maximumSimulatedMilliseconds = _runtimeOptions.MaximumSimulatedMilliseconds,
            maximumSimulatedLaps = _runtimeOptions.MaximumSimulatedLaps,
            targetRealTimeFactor = _runtimeOptions.TargetRealTimeFactor,
            session = new
            {
                index = _sessionManager.CurrentSessionIndex,
                name = session.Configuration.Name,
                type = session.Configuration.Type.ToString(),
                phase,
                startTimeMilliseconds = session.StartTimeMilliseconds,
                countdownMilliseconds = Math.Max(0, session.StartTimeMilliseconds - now),
                timeLeftMilliseconds = session.TimeLeftMilliseconds,
                laps = session.Configuration.Laps,
            },
            lastCommand = _lastCommandId == null ? null : new
            {
                id = _lastCommandId,
                command = _lastCommand,
                status = _lastCommandStatus,
                message = _lastCommandMessage,
            },
            cars,
        });
    }

    private void WriteFpsSnapshot(FpsLiveMatchSnapshot match, bool serverRunning)
    {
        var ranking = match.Actors
            .Where(actor => actor.Active)
            .OrderByDescending(actor => actor.Kills)
            .ThenBy(actor => actor.Deaths)
            .ThenBy(actor => actor.Id)
            .Select((actor, index) => (actor.Id, Position: index + 1))
            .ToDictionary(item => item.Id, item => item.Position);
        var cars = match.Actors.Select(actor =>
        {
            Vector3 forward = new(MathF.Sin(actor.Yaw), 0, MathF.Cos(actor.Yaw));
            float halfYaw = actor.Yaw * 0.5f;
            int? position = ranking.TryGetValue(actor.Id, out int rankedPosition)
                ? rankedPosition
                : null;
            return new
            {
                sessionId = (int)actor.Id,
                name = actor.Name,
                model = "FPS Operator",
                skin = string.Empty,
                isBot = actor.IsBot,
                isConnected = actor.Active && !actor.IsBot,
                isActive = actor.Active && !actor.Dead,
                x = actor.Position.X,
                y = actor.Position.Y,
                z = actor.Position.Z,
                velocityX = actor.Velocity.X,
                velocityY = actor.Velocity.Y,
                velocityZ = actor.Velocity.Z,
                headingRadians = actor.Yaw + MathF.PI * 0.5f,
                orientationX = 0,
                orientationY = MathF.Sin(halfYaw),
                orientationZ = 0,
                orientationW = MathF.Cos(halfYaw),
                forwardX = forward.X,
                forwardY = forward.Y,
                forwardZ = forward.Z,
                speedKmh = Math.Sqrt(actor.Velocity.X * actor.Velocity.X
                                     + actor.Velocity.Z * actor.Velocity.Z) * 3.6,
                protocolGear = 1,
                engineRpm = 0,
                normalizedPosition = 0,
                lap = 0,
                racePosition = position,
                isDnf = actor.Dead,
                hasFinished = match.State == FpsMatchState.Finished,
                controlMode = actor.IsBot ? "automatic" : "human",
                health = actor.Health,
                kills = actor.Kills,
                deaths = actor.Deaths,
            };
        }).ToArray();
        string phase = match.State switch
        {
            FpsMatchState.Waiting => "waiting",
            FpsMatchState.Finished => "finished",
            _ => "running",
        };

        AtomicWrite(_snapshotPath, new
        {
            schemaVersion = 2,
            sequence = ++_sequence,
            capturedAt = DateTimeOffset.UtcNow,
            serverRunning,
            isSimulation = false,
            isFps = true,
            environment = new
            {
                weatherType = (int)_weatherManager.CurrentWeather.UpcomingType.WeatherFxType,
                weatherName = _weatherManager.CurrentWeather.UpcomingType.WeatherFxType.ToString(),
                timeOfDaySeconds = _weatherManager.CurrentDateTime.Hour * 3600
                                   + _weatherManager.CurrentDateTime.Minute * 60
                                   + _weatherManager.CurrentDateTime.Second,
            },
            simulatedMilliseconds = (long)Math.Round(match.ElapsedSeconds * 1000),
            session = new
            {
                index = 0,
                name = "Current match",
                type = "Deathmatch",
                phase,
                startTimeMilliseconds = 0,
                countdownMilliseconds = 0,
                timeLeftMilliseconds = (int)Math.Max(0, Math.Round(match.RemainingSeconds * 1000)),
                laps = 0,
                killLimit = match.KillLimit,
                winnerId = match.WinnerId,
            },
            lastCommand = _lastCommandId == null ? null : new
            {
                id = _lastCommandId,
                command = _lastCommand,
                status = _lastCommandStatus,
                message = _lastCommandMessage,
            },
            cars,
        });
    }

    internal static Quaternion ResolveTelemetryOrientation(RaceAiStateSnapshot? ai,
        Vector3 clientRotation) => ai?.Orientation
                                   ?? RacePhysicsMath.FromProtocolRotation(clientRotation);

    internal static RaceBotVehicleTelemetry ResolveTelemetryDrivetrain(RaceAiStateSnapshot? ai,
        byte clientGear, ushort clientEngineRpm) => ai.HasValue
        ? new RaceBotVehicleTelemetry(ai.Value.ProtocolGear, ai.Value.EngineRpm)
        : new RaceBotVehicleTelemetry(clientGear, clientEngineRpm);

    private void AtomicWrite(string path, object value)
    {
        string temporaryPath = path + $".{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, _jsonOptions), new UTF8Encoding(false));
            File.Move(temporaryPath, path, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A reader that does not share delete access must not stall the authoritative tick.
            // The next wall-clock snapshot will retry with a complete new file.
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
