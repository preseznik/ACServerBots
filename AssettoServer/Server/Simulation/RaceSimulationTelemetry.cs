using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AssettoServer.Server.Ai.Physics;
using AssettoServer.Server.Configuration;
using AssettoServer.Server.Runtime;
using AssettoServer.Shared.Model;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace AssettoServer.Server.RaceSimulation;

public sealed class RaceSimulationTelemetry : IHostedService
{
    private readonly object _sync = new();
    private readonly ACServerConfiguration _configuration;
    private readonly ServerRuntimeOptions _runtimeOptions;
    private readonly EntryCarManager _entryCarManager;
    private readonly SessionManager _sessionManager;
    private readonly RaceBotPhysicsWorld _physicsWorld;
    private readonly ACServer _server;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly Stopwatch _wallClock = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };
    private readonly Dictionary<byte, BotCounters> _previous = [];
    private readonly Dictionary<byte, BotCounters> _totals = [];
    private readonly Dictionary<byte, long> _lastMovingAt = [];
    private readonly Dictionary<byte, RaceSimulationBotStatistics> _botStatistics = [];
    private readonly List<StoppedObstacleEpisodeResult> _stoppedObstacleEpisodes = [];
    private readonly HashSet<string> _reportedAnomalies = [];
    private readonly Dictionary<string, int> _anomalyCounts = new(StringComparer.Ordinal);
    private StreamWriter? _events;
    private StreamWriter? _samples;
    private long _nextSampleAt;
    private int _sampleCount;
    private bool _started;
    private bool _stopping;
    private string _completionReason = "cancelled";
    private int _sessionGeneration;
    private object[]? _lastCompletedRaceResults;
    private string? _lastCompletedRaceName;
    private RacePhysicsDiagnostics? _runDiagnostics;
    private StoppedObstacleEpisode? _activeStoppedObstacleEpisode;

    private readonly record struct BotCounters(uint Laps, int Recoveries, int PassCommits,
        int SeparatedPasses, int CompletedPasses, int StoppedObstaclePassCommits,
        int StoppedObstaclePassesCompleted);

    private sealed record StoppedObstacleEpisode(int SessionId, long StartedAt,
        int SessionGeneration, int BaselineCommits, int BaselineCompleted,
        long BaselineContacts);

    private sealed record StoppedObstacleEpisodeResult(int SessionId, long StartedAt,
        long EndedAt, long DurationMilliseconds, int SessionGeneration, string EndReason,
        int PassCommits, int PassesCompleted, long ContactManifolds);

    public RaceSimulationTelemetry(ACServerConfiguration configuration,
        ServerRuntimeOptions runtimeOptions,
        EntryCarManager entryCarManager,
        SessionManager sessionManager,
        RaceBotPhysicsWorld physicsWorld,
        ACServer server,
        IHostApplicationLifetime applicationLifetime)
    {
        _configuration = configuration;
        _runtimeOptions = runtimeOptions;
        _entryCarManager = entryCarManager;
        _sessionManager = sessionManager;
        _physicsWorld = physicsWorld;
        _server = server;
        _applicationLifetime = applicationLifetime;
        _server.Update += OnServerUpdate;
        _sessionManager.SessionChanged += OnSessionChanged;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_runtimeOptions.SimulationOutputDirectory);
        _events = CreateWriter("events.jsonl");
        _samples = CreateWriter("samples.jsonl");
        _wallClock.Start();
        _started = true;
        WriteEvent("run_started", new
        {
            version = ThisAssembly.AssemblyInformationalVersion,
            buildId = typeof(RaceSimulationTelemetry).Assembly.ManifestModule.ModuleVersionId,
            track = _configuration.CSPTrackOptions.Track,
            layout = _configuration.Server.TrackConfig,
            seed = _runtimeOptions.SimulationSeed,
            bots = _entryCarManager.EntryCars.Count(car => car.AiControlled),
            updateHz = _configuration.Extra.AiParams.Race.UpdateHz,
            fidelity = _configuration.Extra.AiParams.Race.Physics.Fidelity.ToString(),
            maximumSimulatedMilliseconds = _runtimeOptions.MaximumSimulatedMilliseconds,
            maximumSimulatedLaps = _runtimeOptions.MaximumSimulatedLaps,
            maximumWallTimeSeconds = _runtimeOptions.MaximumWallTimeSeconds,
            targetRealTimeFactor = _runtimeOptions.TargetRealTimeFactor,
        });
        _runtimeOptions.SimulationReady.Set();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (!_started)
                return Task.CompletedTask;
            _stopping = true;
            _server.Update -= OnServerUpdate;
            _sessionManager.SessionChanged -= OnSessionChanged;
            CompleteStoppedObstacleEpisode("simulation_ended");
            CaptureSample(force: true);
            WriteEvent("run_stopped", new { reason = _completionReason });
            WriteSummary();
            _events?.Dispose();
            _samples?.Dispose();
            _events = null;
            _samples = null;
            _started = false;
        }
        return Task.CompletedTask;
    }

    private void OnSessionChanged(SessionManager sender, SessionChangedEventArgs args)
    {
        lock (_sync)
        {
            if (args.PreviousSession?.Configuration.Type == SessionType.Race)
            {
                _lastCompletedRaceResults = BuildResults(args.PreviousSession, _botStatistics);
                _lastCompletedRaceName = args.PreviousSession.Configuration.Name;
            }
            CompleteStoppedObstacleEpisode("session_changed");
            _sessionGeneration++;
            _previous.Clear();
            _lastMovingAt.Clear();
            _botStatistics.Clear();
            if (_started)
            {
                WriteEvent("session_changed", new
                {
                    generation = _sessionGeneration,
                    previous = args.PreviousSession?.Configuration.Name,
                    next = args.NextSession.Configuration.Name,
                    type = args.NextSession.Configuration.Type.ToString(),
                });
            }
        }
    }

    public void RecordControlCommand(Guid id, string command, string status,
        int? sessionId, double? timeScale, DateTimeOffset requestedAt, string? message)
    {
        lock (_sync)
        {
            if (!_started || _stopping)
                return;
            WriteEvent("control_command", new
            {
                id,
                command,
                status,
                sessionId,
                timeScale,
                requestedAt,
                message,
                sessionGeneration = _sessionGeneration,
                session = _sessionManager.CurrentSession.Configuration.Name,
            });
            if (!string.Equals(status, "accepted", StringComparison.OrdinalIgnoreCase)
                || !sessionId.HasValue)
                return;
            if (string.Equals(command, "bot_stop", StringComparison.OrdinalIgnoreCase))
                StartStoppedObstacleEpisode(sessionId.Value);
            else if (string.Equals(command, "bot_go", StringComparison.OrdinalIgnoreCase))
                CompleteStoppedObstacleEpisode("bot_go");
        }
    }

    private void StartStoppedObstacleEpisode(int sessionId)
    {
        CompleteStoppedObstacleEpisode("replaced_by_new_stop");
        int commits = _totals.Values.Sum(value => value.StoppedObstaclePassCommits);
        int completed = _totals.Values.Sum(value => value.StoppedObstaclePassesCompleted);
        long contacts = GetContactManifoldsFor(sessionId);
        _activeStoppedObstacleEpisode = new StoppedObstacleEpisode(sessionId,
            _sessionManager.ServerTimeMilliseconds, _sessionGeneration,
            commits, completed, contacts);
    }

    private void CompleteStoppedObstacleEpisode(string reason)
    {
        if (_activeStoppedObstacleEpisode == null)
            return;
        var episode = _activeStoppedObstacleEpisode;
        _activeStoppedObstacleEpisode = null;
        long endedAt = _sessionManager.ServerTimeMilliseconds;
        var result = new StoppedObstacleEpisodeResult(episode.SessionId,
            episode.StartedAt, endedAt, Math.Max(0, endedAt - episode.StartedAt),
            episode.SessionGeneration, reason,
            Math.Max(0, _totals.Values.Sum(value => value.StoppedObstaclePassCommits)
                        - episode.BaselineCommits),
            Math.Max(0, _totals.Values.Sum(value => value.StoppedObstaclePassesCompleted)
                        - episode.BaselineCompleted),
            Math.Max(0, GetContactManifoldsFor(episode.SessionId) - episode.BaselineContacts));
        _stoppedObstacleEpisodes.Add(result);
        if (_started)
            WriteEvent("stopped_obstacle_episode_completed", result);
    }

    private long GetContactManifoldsFor(int sessionId)
    {
        if (sessionId is < 0 or > byte.MaxValue)
            return 0;
        long total = 0;
        foreach (var car in _entryCarManager.EntryCars)
        {
            if (car.SessionId == sessionId)
                continue;
            total += _physicsWorld.GetVehicleContactManifoldCount((byte)sessionId,
                car.SessionId);
        }
        return total;
    }

    private StreamWriter CreateWriter(string filename) => new(
        Path.Combine(_runtimeOptions.SimulationOutputDirectory, filename), false,
        new UTF8Encoding(false)) { AutoFlush = true };

    private void OnServerUpdate(ACServer sender, EventArgs args)
    {
        lock (_sync)
        {
            if (!_started || _stopping)
                return;

            CaptureEvents();
            CaptureSample(force: false);

            if (HasCompletedRace())
            {
                RequestStop("completed");
                return;
            }
            if (_runtimeOptions.MaximumSimulatedMilliseconds > 0
                && _sessionManager.ServerTimeMilliseconds >= _runtimeOptions.MaximumSimulatedMilliseconds)
            {
                RequestStop("maximum_simulated_time");
                return;
            }
            if (HasReachedLapLimit())
            {
                RequestStop("maximum_simulated_laps");
                return;
            }
            if (_wallClock.Elapsed.TotalSeconds >= _runtimeOptions.MaximumWallTimeSeconds)
            {
                RequestStop("maximum_wall_time");
                return;
            }
        }
    }

    private bool HasReachedLapLimit()
    {
        if (_runtimeOptions.MaximumSimulatedLaps <= 0)
            return false;
        var session = _sessionManager.CurrentSession;
        if (session.Configuration.Type != SessionType.Race
            || _sessionManager.ServerTimeMilliseconds <= session.StartTimeMilliseconds
            || session.Results == null)
            return false;
        return _entryCarManager.EntryCars.Where(car => car.AiControlled)
            .Any(car => session.Results.TryGetValue(car.SessionId, out var result)
                        && result.NumLaps >= (uint)_runtimeOptions.MaximumSimulatedLaps);
    }

    private bool HasCompletedRace()
    {
        var session = _sessionManager.CurrentSession;
        if (session.Configuration.Type != SessionType.Race
            || _sessionManager.ServerTimeMilliseconds <= session.StartTimeMilliseconds
            || session.Results == null)
            return false;
        var bots = _entryCarManager.EntryCars.Where(car => car.AiControlled).ToArray();
        return bots.Length > 0 && bots.All(car => session.Results.TryGetValue(car.SessionId, out var result)
                                                   && (result.HasCompletedLastLap || result.IsDnf));
    }

    private void RequestStop(string reason)
    {
        if (_stopping)
            return;
        _completionReason = reason;
        _runtimeOptions.RequestSimulationStop(reason);
        _stopping = true;
        WriteEvent("stop_requested", new { reason });
        _applicationLifetime.StopApplication();
    }

    private void CaptureEvents()
    {
        var results = _sessionManager.CurrentSession.Results;
        foreach (var car in _entryCarManager.EntryCars.Where(car => car.AiControlled))
        {
            var ai = car.GetRaceAiStateSnapshot();
            if (ai == null)
                continue;
            _physicsWorld.TryGetBotState(car.SessionId, out var physics);
            uint laps = results != null && results.TryGetValue(car.SessionId, out var result)
                ? result.NumLaps
                : 0;
            var current = new BotCounters(laps, physics.RecoveryCount, ai.Value.PassCommitCount,
                ai.Value.SeparatedPassCount, ai.Value.CompletedPassCount,
                ai.Value.StoppedObstaclePassCommitCount,
                ai.Value.StoppedObstaclePassCompletedCount);
            _previous.TryGetValue(car.SessionId, out var previous);

            _totals.TryGetValue(car.SessionId, out var total);
            _totals[car.SessionId] = Add(total, PositiveDelta(previous, current));

            WriteCounterEvents(car, "lap_completed", previous.Laps, current.Laps);
            WriteCounterEvents(car, "recovery", previous.Recoveries, current.Recoveries);
            if (current.Recoveries > previous.Recoveries)
                ReportAnomaly(car, "recovery", new { count = current.Recoveries });
            WriteCounterEvents(car, "pass_committed", previous.PassCommits, current.PassCommits);
            WriteCounterEvents(car, "pass_separated", previous.SeparatedPasses, current.SeparatedPasses);
            WriteCounterEvents(car, "pass_completed", previous.CompletedPasses, current.CompletedPasses);
            WriteCounterEvents(car, "stopped_obstacle_pass_committed",
                previous.StoppedObstaclePassCommits, current.StoppedObstaclePassCommits);
            WriteCounterEvents(car, "stopped_obstacle_pass_completed",
                previous.StoppedObstaclePassesCompleted, current.StoppedObstaclePassesCompleted);
            _previous[car.SessionId] = current;
        }
    }

    private static BotCounters PositiveDelta(BotCounters previous, BotCounters current) => new(
        current.Laps >= previous.Laps ? current.Laps - previous.Laps : current.Laps,
        current.Recoveries >= previous.Recoveries
            ? current.Recoveries - previous.Recoveries : current.Recoveries,
        current.PassCommits >= previous.PassCommits
            ? current.PassCommits - previous.PassCommits : current.PassCommits,
        current.SeparatedPasses >= previous.SeparatedPasses
            ? current.SeparatedPasses - previous.SeparatedPasses : current.SeparatedPasses,
        current.CompletedPasses >= previous.CompletedPasses
            ? current.CompletedPasses - previous.CompletedPasses : current.CompletedPasses,
        current.StoppedObstaclePassCommits >= previous.StoppedObstaclePassCommits
            ? current.StoppedObstaclePassCommits - previous.StoppedObstaclePassCommits
            : current.StoppedObstaclePassCommits,
        current.StoppedObstaclePassesCompleted >= previous.StoppedObstaclePassesCompleted
            ? current.StoppedObstaclePassesCompleted - previous.StoppedObstaclePassesCompleted
            : current.StoppedObstaclePassesCompleted);

    private static BotCounters Add(BotCounters first, BotCounters second) => new(
        first.Laps + second.Laps,
        first.Recoveries + second.Recoveries,
        first.PassCommits + second.PassCommits,
        first.SeparatedPasses + second.SeparatedPasses,
        first.CompletedPasses + second.CompletedPasses,
        first.StoppedObstaclePassCommits + second.StoppedObstaclePassCommits,
        first.StoppedObstaclePassesCompleted + second.StoppedObstaclePassesCompleted);

    private void WriteCounterEvents(EntryCar car, string type, long previous, long current)
    {
        for (long value = previous + 1; value <= current; value++)
            WriteEvent(type, new { sessionId = car.SessionId, car.Model, count = value });
    }

    private void CaptureSample(bool force)
    {
        long now = _sessionManager.ServerTimeMilliseconds;
        if (!force && now < _nextSampleAt)
            return;
        _nextSampleAt = now + _runtimeOptions.SampleIntervalMilliseconds;
        _sampleCount++;

        var results = _sessionManager.CurrentSession.Results;
        var bots = new List<object>();
        foreach (var car in _entryCarManager.EntryCars.Where(car => car.AiControlled))
        {
            var ai = car.GetRaceAiStateSnapshot();
            if (ai == null)
                continue;
            _physicsWorld.TryGetBotState(car.SessionId, out var physics);
            _physicsWorld.TryGetBotTelemetry(car.SessionId, out var physicsTelemetry);
            EntryCarResult? result = null;
            results?.TryGetValue(car.SessionId, out result);

            TrackAnomalies(car, ai.Value, physics, physicsTelemetry, now);
            if (_sessionManager.CurrentSession.Configuration.Type == SessionType.Race
                && now >= _sessionManager.CurrentSession.StartTimeMilliseconds)
            {
                if (!_botStatistics.TryGetValue(car.SessionId, out var statistics))
                    _botStatistics[car.SessionId] = statistics = new RaceSimulationBotStatistics();
                statistics.Observe(now, ai.Value.CurrentSpeed, physics.RecoveryCount,
                    GetContactManifoldsFor(car.SessionId));
            }
            bots.Add(new
            {
                sessionId = car.SessionId,
                name = car.AiName,
                model = car.Model,
                lap = result?.NumLaps ?? 0,
                racePosition = result?.RacePos ?? 0,
                splinePoint = ai.Value.SplinePointId,
                position = Vector(ai.Value.Position),
                velocity = Vector(ai.Value.Velocity),
                speedMetersPerSecond = ai.Value.CurrentSpeed,
                targetSpeedMetersPerSecond = ai.Value.TargetSpeed,
                lateralOffsetMeters = ai.Value.LateralOffsetMeters,
                maximumLateralOffsetMeters = ai.Value.MaximumLateralOffsetMeters,
                closestObstacleMeters = ai.Value.ClosestObstacleMeters,
                steeringAngleRadians = ai.Value.SteeringAngleRadians,
                slipAngleDegrees = physics.SlipAngleDegrees,
                heightErrorMeters = physicsTelemetry.HeightErrorMeters,
                suspensionCompressionMeters = physicsTelemetry.SuspensionCompressionMeters,
                uprightDot = physicsTelemetry.UprightDot,
                upwardSpeedMetersPerSecond = physicsTelemetry.UpwardSpeedMetersPerSecond,
                excessUpwardSpeedMetersPerSecond = physicsTelemetry.ExcessUpwardSpeedMetersPerSecond,
                groundedWheels = physicsTelemetry.GroundedWheelCount,
                surfaceDiscontinuities = physicsTelemetry.SurfaceDiscontinuityCount,
                recoveries = physics.RecoveryCount,
                trackCorrections = physicsTelemetry.TrackCorrectionCount,
                stoppedForObstacle = ai.Value.IsStoppedForObstacle,
                overtaking = ai.Value.IsOvertaking,
                overtakeTargetSessionId = ai.Value.OvertakeTargetSessionId,
                passingSide = ai.Value.IsOvertaking ? ai.Value.PassingLeft ? "left" : "right" : null,
                passCommits = ai.Value.PassCommitCount,
                separatedPasses = ai.Value.SeparatedPassCount,
                completedPasses = ai.Value.CompletedPassCount,
                stoppedObstaclePassCommits = ai.Value.StoppedObstaclePassCommitCount,
                stoppedObstaclePassesCompleted = ai.Value.StoppedObstaclePassCompletedCount,
            });
        }

        var diagnostics = _physicsWorld.GetDiagnostics();
        AccumulateDiagnostics(diagnostics);
        TrackAggregateAnomalies(diagnostics);
        WriteJsonLine(_samples, new
        {
            simulatedMilliseconds = now,
            sessionGeneration = _sessionGeneration,
            session = _sessionManager.CurrentSession.Configuration.Name,
            sessionType = _sessionManager.CurrentSession.Configuration.Type.ToString(),
            raceStarted = now >= _sessionManager.CurrentSession.StartTimeMilliseconds,
            bots,
            physics = diagnostics,
        });
    }

    private void AccumulateDiagnostics(RacePhysicsDiagnostics current)
    {
        if (_runDiagnostics == null)
        {
            _runDiagnostics = current;
            return;
        }
        var previous = _runDiagnostics.Value;
        _runDiagnostics = new RacePhysicsDiagnostics(
            Math.Max(previous.BotCount, current.BotCount),
            Math.Min(previous.MinimumY, current.MinimumY),
            Math.Max(previous.MaximumY, current.MaximumY),
            Math.Max(previous.MaximumSpeed, current.MaximumSpeed),
            Math.Max(previous.MaximumSlipAngleDegrees, current.MaximumSlipAngleDegrees),
            Math.Max(previous.MaximumSteeringAngleDegrees, current.MaximumSteeringAngleDegrees),
            Math.Max(previous.MaximumUpwardSpeed, current.MaximumUpwardSpeed),
            Math.Max(previous.MaximumExcessUpwardSpeed, current.MaximumExcessUpwardSpeed),
            Math.Max(previous.MaximumSplineHeightError, current.MaximumSplineHeightError),
            Math.Max(previous.MaximumSuspensionCompression, current.MaximumSuspensionCompression),
            Math.Min(previous.MinimumUprightDot, current.MinimumUprightDot),
            Math.Max(previous.OverturnedBots, current.OverturnedBots),
            Math.Max(previous.TotalRecoveries, current.TotalRecoveries),
            Math.Max(previous.TotalTrackCorrections, current.TotalTrackCorrections),
            Math.Min(previous.MinimumGroundedWheelCount, current.MinimumGroundedWheelCount),
            Math.Max(previous.TotalSurfaceDiscontinuities, current.TotalSurfaceDiscontinuities),
            Math.Max(previous.LaunchedBots, current.LaunchedBots),
            Math.Max(previous.LaunchStepSpread, current.LaunchStepSpread),
            Math.Max(previous.StaticPairTests, current.StaticPairTests),
            Math.Max(previous.StaticManifolds, current.StaticManifolds),
            Math.Max(previous.VehicleManifolds, current.VehicleManifolds));
    }

    private void TrackAggregateAnomalies(RacePhysicsDiagnostics diagnostics)
    {
        if (diagnostics.MaximumSplineHeightError > 1.25f)
            ReportGlobalAnomaly("surface_height", new { diagnostics.MaximumSplineHeightError });
        if (diagnostics.MaximumSuspensionCompression > 0.12f)
            ReportGlobalAnomaly("suspension_compression", new { diagnostics.MaximumSuspensionCompression });
        if (diagnostics.MinimumUprightDot < 0.25f || diagnostics.OverturnedBots > 0)
            ReportGlobalAnomaly("overturned", new { diagnostics.MinimumUprightDot, diagnostics.OverturnedBots });
        if (diagnostics.MaximumUpwardSpeed > 12f)
            ReportGlobalAnomaly("vertical_launch", new { diagnostics.MaximumUpwardSpeed });
        if (diagnostics.MaximumExcessUpwardSpeed > 4f)
            ReportGlobalAnomaly("unexpected_vertical_launch", new { diagnostics.MaximumExcessUpwardSpeed });
        if (diagnostics.MaximumSlipAngleDegrees > 45f)
            ReportGlobalAnomaly("excessive_slip", new { diagnostics.MaximumSlipAngleDegrees });
    }

    private void TrackAnomalies(EntryCar car, RaceAiStateSnapshot ai, RaceBotPhysicsState physics,
        RaceBotPhysicsTelemetry telemetry, long now)
    {
        if (ai.CurrentSpeed >= 0.5f)
            _lastMovingAt[car.SessionId] = now;
        else if (!_lastMovingAt.ContainsKey(car.SessionId))
            _lastMovingAt[car.SessionId] = _sessionManager.CurrentSession.StartTimeMilliseconds;

        bool raceActive = now >= _sessionManager.CurrentSession.StartTimeMilliseconds;
        if (raceActive && now - _lastMovingAt[car.SessionId] > 10_000)
            ReportAnomaly(car, "stuck", new { stoppedMilliseconds = now - _lastMovingAt[car.SessionId] });
        if (telemetry.HeightErrorMeters > 1.25f)
            ReportAnomaly(car, "surface_height", new { telemetry.HeightErrorMeters });
        if (telemetry.SuspensionCompressionMeters > 0.12f)
            ReportAnomaly(car, "suspension_compression", new { telemetry.SuspensionCompressionMeters });
        if (telemetry.UprightDot < 0.25f)
            ReportAnomaly(car, "overturned", new { telemetry.UprightDot });
        if (telemetry.UpwardSpeedMetersPerSecond > 12f)
            ReportAnomaly(car, "vertical_launch", new { telemetry.UpwardSpeedMetersPerSecond });
        if (telemetry.ExcessUpwardSpeedMetersPerSecond > 4f)
            ReportAnomaly(car, "unexpected_vertical_launch",
                new { telemetry.ExcessUpwardSpeedMetersPerSecond, telemetry.GroundedWheelCount });
        if (physics.SlipAngleDegrees > 45f && ai.CurrentSpeed > 5f)
            ReportAnomaly(car, "excessive_slip", new { physics.SlipAngleDegrees });
    }

    private void ReportAnomaly(EntryCar car, string code, object details)
    {
        string key = $"{_sessionGeneration}:{car.SessionId}:{code}";
        if (!_reportedAnomalies.Add(key))
            return;
        _anomalyCounts[code] = _anomalyCounts.GetValueOrDefault(code) + 1;
        WriteEvent("anomaly", new { code, sessionId = car.SessionId, car.Model, details });
    }

    private void ReportGlobalAnomaly(string code, object details)
    {
        if (!_reportedAnomalies.Add($"{_sessionGeneration}:global:{code}"))
            return;
        _anomalyCounts[code] = _anomalyCounts.GetValueOrDefault(code) + 1;
        WriteEvent("anomaly", new { code, details });
    }

    private void WriteSummary()
    {
        var diagnostics = _runDiagnostics ?? _physicsWorld.GetDiagnostics();
        diagnostics = diagnostics with
        {
            TotalRecoveries = _totals.Values.Sum(value => value.Recoveries),
        };
        var contactPair = _physicsWorld.GetMostFrequentVehicleContactPair();
        bool currentRace = _sessionManager.CurrentSession.Configuration.Type == SessionType.Race;
        object[] results = currentRace
            ? BuildResults(_sessionManager.CurrentSession, _botStatistics)
            : _lastCompletedRaceResults ?? [];
        string? resultsSession = currentRace
            ? _sessionManager.CurrentSession.Configuration.Name
            : _lastCompletedRaceName;
        double wallMilliseconds = Math.Max(1, _wallClock.Elapsed.TotalMilliseconds);
        var summary = new
        {
            schemaVersion = 3,
            completedAt = DateTimeOffset.UtcNow,
            status = _completionReason,
            version = ThisAssembly.AssemblyInformationalVersion,
            buildId = typeof(RaceSimulationTelemetry).Assembly.ManifestModule.ModuleVersionId,
            track = _configuration.CSPTrackOptions.Track,
            layout = _configuration.Server.TrackConfig,
            seed = _runtimeOptions.SimulationSeed,
            updateHz = _configuration.Extra.AiParams.Race.UpdateHz,
            fidelity = _configuration.Extra.AiParams.Race.Physics.Fidelity.ToString(),
            botCount = _entryCarManager.EntryCars.Count(car => car.AiControlled),
            simulatedMilliseconds = _sessionManager.ServerTimeMilliseconds,
            wallMilliseconds,
            realTimeFactor = _sessionManager.ServerTimeMilliseconds / wallMilliseconds,
            targetRealTimeFactor = _runtimeOptions.TargetRealTimeFactor,
            sampleCount = _sampleCount,
            anomalyCount = _anomalyCounts.Values.Sum(),
            anomalies = _anomalyCounts,
            passCommits = _totals.Values.Sum(value => value.PassCommits),
            separatedPasses = _totals.Values.Sum(value => value.SeparatedPasses),
            completedPasses = _totals.Values.Sum(value => value.CompletedPasses),
            stoppedObstaclePassCommits = _totals.Values.Sum(value =>
                value.StoppedObstaclePassCommits),
            stoppedObstaclePassesCompleted = _totals.Values.Sum(value =>
                value.StoppedObstaclePassesCompleted),
            stoppedObstacleEpisodes = _stoppedObstacleEpisodes,
            physics = diagnostics,
            mostFrequentContactPair = new { contactPair.A, contactPair.B, contactPair.Count },
            resultsSession,
            results,
        };
        File.WriteAllText(Path.Combine(_runtimeOptions.SimulationOutputDirectory, "summary.json"),
            JsonSerializer.Serialize(summary, new JsonSerializerOptions(_jsonOptions) { WriteIndented = true }),
            new UTF8Encoding(false));
        Log.Information("Race simulation {Status}: {SimulatedSeconds:F1} simulated seconds in {WallSeconds:F1} wall seconds ({Factor:F1}x), {Anomalies} anomalies",
            _completionReason, _sessionManager.ServerTimeMilliseconds / 1000d, _wallClock.Elapsed.TotalSeconds,
            _sessionManager.ServerTimeMilliseconds / wallMilliseconds, _anomalyCounts.Values.Sum());
    }

    private object[] BuildResults(SessionState session,
        IReadOnlyDictionary<byte, RaceSimulationBotStatistics> statistics)
    {
        var cars = _entryCarManager.EntryCars.ToDictionary(car => car.SessionId);
        return session.Results?
            .OrderBy(pair => pair.Value.RacePos)
            .Select(pair =>
            {
                var botStatistics = statistics.GetValueOrDefault(pair.Key);
                return (object)new
                {
                    sessionId = pair.Key,
                    pair.Value.Name,
                    model = cars.GetValueOrDefault(pair.Key)?.Model ?? string.Empty,
                    pair.Value.RacePos,
                    pair.Value.NumLaps,
                    pair.Value.LastLap,
                    pair.Value.BestLap,
                    pair.Value.TotalTime,
                    pair.Value.HasCompletedLastLap,
                    pair.Value.IsDnf,
                    elapsedMilliseconds = botStatistics?.ObservedMilliseconds ?? 0,
                    averageSpeedKmh = botStatistics?.AverageSpeedKilometersPerHour ?? 0,
                    topSpeedKmh = botStatistics?.TopSpeedKilometersPerHour ?? 0,
                    crashCount = botStatistics?.ContactEpisodeCount ?? 0,
                    contactManifolds = botStatistics?.ContactManifolds ?? 0,
                    recoveryCount = botStatistics?.RecoveryCount ?? 0,
                    fullStopCount = botStatistics?.FullStopCount ?? 0,
                    fullyStoppedMilliseconds = botStatistics?.FullyStoppedMilliseconds ?? 0,
                };
            }).ToArray() ?? [];
    }

    private void WriteEvent(string type, object data) => WriteJsonLine(_events, new
    {
        simulatedMilliseconds = _sessionManager.CurrentSession == null ? 0 : _sessionManager.ServerTimeMilliseconds,
        type,
        data,
    });

    private void WriteJsonLine(StreamWriter? writer, object value)
    {
        if (writer == null)
            return;
        writer.WriteLine(JsonSerializer.Serialize(value, _jsonOptions));
    }

    private static float[] Vector(System.Numerics.Vector3 value) => [value.X, value.Y, value.Z];
}
