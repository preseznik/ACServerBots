using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AssettoServer.Server.Ai.Splines;
using AssettoServer.Server.Configuration;
using AssettoServer.Shared.Model;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace AssettoServer.Server.Runtime;

public sealed class RaceControlBridge : IHostedService
{
    private const int SnapshotIntervalMilliseconds = 50;
    private const int MaximumTrackPoints = 1500;

    private readonly ACServerConfiguration _configuration;
    private readonly ServerRuntimeOptions _runtimeOptions;
    private readonly ACServer _server;
    private readonly SessionManager _sessionManager;
    private readonly EntryCarManager _entryCarManager;
    private readonly AiSpline? _spline;
    private readonly Stopwatch _wallClock = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private string _controlDirectory = null!;
    private string _commandsDirectory = null!;
    private string _snapshotPath = null!;
    private long _initialServerTime;
    private long _sequence;
    private long _nextSnapshotAt;
    private Guid? _lastCommandId;
    private string? _lastCommand;
    private string? _lastCommandStatus;
    private string? _lastCommandMessage;

    private sealed record CommandEnvelope(Guid Id, string Command, DateTimeOffset RequestedAt,
        double? TimeScale = null);

    public RaceControlBridge(ACServerConfiguration configuration,
        ServerRuntimeOptions runtimeOptions,
        ACServer server,
        SessionManager sessionManager,
        EntryCarManager entryCarManager,
        AiSpline? spline = null)
    {
        _configuration = configuration;
        _runtimeOptions = runtimeOptions;
        _server = server;
        _sessionManager = sessionManager;
        _entryCarManager = entryCarManager;
        _spline = spline;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _controlDirectory = _runtimeOptions.RaceControlDirectory
                            ?? throw new InvalidOperationException("Race Control directory is not configured");
        _commandsDirectory = Path.Combine(_controlDirectory, "commands");
        _snapshotPath = Path.Combine(_controlDirectory, "state.json");
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
            bool commandProcessed = ProcessCommands();
            if (commandProcessed || _wallClock.ElapsedMilliseconds >= _nextSnapshotAt)
            {
                WriteSnapshot(serverRunning: true);
                _nextSnapshotAt = _wallClock.ElapsedMilliseconds + SnapshotIntervalMilliseconds;
            }
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Race Control live bridge update failed");
        }
    }

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
                    _ => throw new InvalidDataException($"Unknown race command '{command.Command}'"),
                };
                _lastCommandStatus = accepted ? "accepted" : "rejected";
                _lastCommandMessage = accepted && command.Command.Equals("simulation_time_scale",
                        StringComparison.OrdinalIgnoreCase)
                    ? $"Simulation time acceleration set to {_runtimeOptions.TargetRealTimeFactor:F0}x."
                    : accepted
                        ? $"Race {command.Command} command accepted."
                    : command.Command.Equals("stop", StringComparison.OrdinalIgnoreCase)
                        ? "There is no active race to stop."
                        : command.Command.Equals("simulation_time_scale", StringComparison.OrdinalIgnoreCase)
                            ? "Time acceleration can only be changed during a simulation and must be 1x to 100x."
                            : "No race session is configured.";
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

    private void WriteTrack()
    {
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
        long now = _sessionManager.ServerTimeMilliseconds;
        var session = _sessionManager.CurrentSession;
        string phase = session.IsStoppedByRaceControl ? "stopped"
            : session.Configuration.Type != SessionType.Race ? "waiting"
            : now < session.StartTimeMilliseconds ? "countdown"
            : session.HasSentRaceOverPacket ? "finished"
            : "racing";
        var results = session.Results;
        var cars = _entryCarManager.EntryCars.Select(car =>
        {
            EntryCarResult? result = null;
            results?.TryGetValue(car.SessionId, out result);
            var ai = car.AiControlled ? car.GetRaceAiStateSnapshot() : null;
            Vector3 position = ai?.Position ?? car.Status.Position;
            Vector3 velocity = ai?.Velocity ?? car.Status.Velocity;
            double normalizedPosition = ai != null && _spline is { Points.Length: > 1 }
                ? ai.Value.SplinePointId / (double)(_spline.Points.Length - 1)
                : car.Status.NormalizedPosition;
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
                z = position.Z,
                velocityX = velocity.X,
                velocityZ = velocity.Z,
                headingRadians = car.Status.Rotation.X,
                speedKmh = Math.Sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z) * 3.6,
                normalizedPosition,
                lap = result?.NumLaps ?? 0,
                racePosition = result == null ? null : (int?)result.RacePos + 1,
                isDnf = result?.IsDnf ?? false,
                hasFinished = result?.HasCompletedLastLap ?? false,
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
