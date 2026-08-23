using System;
using AssettoServer.Server.Configuration;
using AssettoServer.Server.Configuration.Extra;
using AssettoServer.Server.Ai.Physics;
using Serilog;

namespace AssettoServer.Server.Ai;

public class AiUpdater
{
    private readonly EntryCarManager _entryCarManager;
    private readonly ACServerConfiguration _configuration;
    private readonly SessionManager _sessionManager;
    private readonly RaceBotPhysicsWorld? _racePhysicsWorld;
    private long _lastUpdateMilliseconds;
    private double _accumulatorMilliseconds;
    private long _lastPhysicsDiagnosticsMilliseconds;
    private long _fieldDeadlockSinceMilliseconds;
    private long _lastFieldDeadlockRecoveryMilliseconds;
    private readonly long[] _immobilizedSinceMilliseconds = new long[byte.MaxValue + 1];
    private long _lastImmobilizedRecoveryMilliseconds;

    public AiUpdater(EntryCarManager entryCarManager, ACServer server, ACServerConfiguration configuration,
        SessionManager sessionManager, RaceBotPhysicsWorld? racePhysicsWorld = null)
    {
        _entryCarManager = entryCarManager;
        _configuration = configuration;
        _sessionManager = sessionManager;
        _racePhysicsWorld = racePhysicsWorld;
        server.Update += OnUpdate;
    }

    private void OnUpdate(object sender, EventArgs args)
    {
        if (_configuration.Extra.AiParams.Behavior == AiBehaviorMode.Race)
        {
            UpdateRaceBots();
            return;
        }

        for (var i = 0; i < _entryCarManager.EntryCars.Length; i++)
        {
            var entryCar = _entryCarManager.EntryCars[i];
            if (entryCar.AiControlled)
            {
                entryCar.AiUpdate();
            }
        }
    }

    private void UpdateRaceBots()
    {
        var now = _sessionManager.ServerTimeMilliseconds;
        if (_lastUpdateMilliseconds == 0)
        {
            _lastUpdateMilliseconds = now;
            return;
        }

        var stepMilliseconds = 1000d / _configuration.Extra.AiParams.Race.UpdateHz;
        _accumulatorMilliseconds += Math.Clamp(now - _lastUpdateMilliseconds, 0, 250);
        _lastUpdateMilliseconds = now;

        int steps = 0;
        while (_accumulatorMilliseconds >= stepMilliseconds && steps++ < 8)
        {
            var stepSeconds = (float)(stepMilliseconds / 1000d);
            if (_racePhysicsWorld == null)
                throw new InvalidOperationException("Race bot rigid-body world is not registered");

            for (var i = 0; i < _entryCarManager.EntryCars.Length; i++)
            {
                var entryCar = _entryCarManager.EntryCars[i];
                if (!entryCar.AiControlled && entryCar.Client?.HasSentFirstUpdate == true)
                {
                    _racePhysicsWorld.SynchronizeHuman(entryCar.SessionId, entryCar.Model,
                        entryCar.Status.Position, entryCar.Status.Rotation, entryCar.Status.Velocity);
                }
                else if (!entryCar.AiControlled && entryCar.Client == null)
                {
                    _racePhysicsWorld.RemoveBody(entryCar.SessionId);
                }
            }
            for (var i = 0; i < _entryCarManager.EntryCars.Length; i++)
            {
                var entryCar = _entryCarManager.EntryCars[i];
                if (entryCar.AiControlled)
                {
                    entryCar.AiPrepareRacePhysics(stepSeconds);
                }
            }
            _racePhysicsWorld.Step(stepSeconds);
            for (var i = 0; i < _entryCarManager.EntryCars.Length; i++)
            {
                var entryCar = _entryCarManager.EntryCars[i];
                if (entryCar.AiControlled)
                    entryCar.AiCompleteRacePhysics(stepSeconds);
            }
            UpdateFieldDeadlockRecovery(now);
            if (now - _lastPhysicsDiagnosticsMilliseconds >= 5000)
            {
                var diagnostics = _racePhysicsWorld.GetDiagnostics();
                var contactPair = _racePhysicsWorld.GetMostFrequentVehicleContactPair();
                float maximumLaneOffset = 0;
                float maximumPassSeparation = 0;
                int passCommits = 0;
                int separatedPasses = 0;
                int completedPasses = 0;
                int stoppedPassCommits = 0;
                int stoppedPassesCompleted = 0;
                foreach (var entryCar in _entryCarManager.EntryCars)
                {
                    var raceAi = entryCar.GetRaceAiDiagnostics();
                    maximumLaneOffset = Math.Max(maximumLaneOffset,
                        raceAi.MaximumAbsoluteLateralOffsetMeters);
                    maximumPassSeparation = Math.Max(maximumPassSeparation,
                        raceAi.MaximumPassSeparationMeters);
                    passCommits += raceAi.PassCommitCount;
                    separatedPasses += raceAi.SeparatedPassCount;
                    completedPasses += raceAi.CompletedPassCount;
                    stoppedPassCommits += raceAi.StoppedObstaclePassCommitCount;
                    stoppedPassesCompleted += raceAi.StoppedObstaclePassCompletedCount;
                }
                Log.Debug("Race physics: {BotCount} bots, Y {MinimumY:F2}..{MaximumY:F2} m, max speed {MaximumSpeed:F1} m/s, "
                          + "max rise {MaximumUpwardSpeed:F1} m/s, height error {MaximumSplineHeightError:F2} m, "
                          + "excess rise {MaximumExcessUpwardSpeed:F1} m/s, grounded {MinimumGroundedWheels}/4, "
                          + "suspension {MaximumSuspensionCompression:F2} m, slip {MaximumSlipAngleDegrees:F1} deg, "
                          + "steer {MaximumSteeringAngleDegrees:F1} deg, upright {MinimumUprightDot:F2}, "
                          + "overturned {OverturnedBots}, recoveries {TotalRecoveries}, track corrections {TotalTrackCorrections}, "
                          + "surface discontinuities {TotalSurfaceDiscontinuities}, "
                          + "launched {LaunchedBots}/{BotCount}, launch spread {LaunchStepSpread} ticks, "
                          + "lane offset {MaximumLaneOffset:F2} m, pass separation {MaximumPassSeparation:F2} m, "
                          + "passes {PassCommits}/{SeparatedPasses}/{CompletedPasses} committed/separated/completed, "
                          + "stopped-obstacle passes {StoppedPassCommits}/{StoppedPassesCompleted} committed/completed, "
                          + "vehicle contacts {VehicleManifolds}, contact pair {ContactA}/{ContactB} ({ContactCount}), "
                          + "static pairs {StaticPairTests}, manifolds {StaticManifolds}",
                    diagnostics.BotCount, diagnostics.MinimumY, diagnostics.MaximumY, diagnostics.MaximumSpeed,
                    diagnostics.MaximumUpwardSpeed, diagnostics.MaximumSplineHeightError,
                    diagnostics.MaximumExcessUpwardSpeed, diagnostics.MinimumGroundedWheelCount,
                    diagnostics.MaximumSuspensionCompression,
                    diagnostics.MaximumSlipAngleDegrees, diagnostics.MaximumSteeringAngleDegrees,
                    diagnostics.MinimumUprightDot, diagnostics.OverturnedBots, diagnostics.TotalRecoveries,
                    diagnostics.TotalTrackCorrections, diagnostics.TotalSurfaceDiscontinuities,
                    diagnostics.LaunchedBots, diagnostics.BotCount,
                    diagnostics.LaunchStepSpread,
                    maximumLaneOffset, maximumPassSeparation, passCommits, separatedPasses,
                    completedPasses, stoppedPassCommits, stoppedPassesCompleted,
                    diagnostics.VehicleManifolds,
                    contactPair.A, contactPair.B, contactPair.Count,
                    diagnostics.StaticPairTests, diagnostics.StaticManifolds);
                _lastPhysicsDiagnosticsMilliseconds = now;
            }
            _accumulatorMilliseconds -= stepMilliseconds;
        }

        if (steps >= 8 && _accumulatorMilliseconds >= stepMilliseconds)
        {
            // Bound catch-up work after a pause so one slow tick cannot create a permanent backlog.
            _accumulatorMilliseconds = 0;
        }
    }

    private void UpdateFieldDeadlockRecovery(long now)
    {
        var session = _sessionManager.CurrentSession;
        if (session.Configuration.Type != AssettoServer.Shared.Model.SessionType.Race
            || session.IsStoppedByRaceControl
            || session.EndTimeMilliseconds != 0
            || session.HasSentRaceOverPacket
            || now < session.StartTimeMilliseconds)
        {
            _fieldDeadlockSinceMilliseconds = 0;
            Array.Clear(_immobilizedSinceMilliseconds);
            return;
        }

        int activeBots = 0;
        int stoppedBots = 0;
        float maximumSpeed = 0;
        EntryCar? fallbackCandidate = null;
        EntryCar? rootCandidate = null;
        EntryCar? immobilizedCandidate = null;
        long longestImmobilizedMilliseconds = 0;
        foreach (var car in _entryCarManager.EntryCars)
        {
            if (!car.AiControlled
                || car.GetRaceControlMode() != RaceControlBotControlMode.Automatic)
            {
                _immobilizedSinceMilliseconds[car.SessionId] = 0;
                continue;
            }
            if (session.Results?.TryGetValue(car.SessionId, out var result) == true
                && (result.IsDnf || result.HasCompletedLastLap))
            {
                _immobilizedSinceMilliseconds[car.SessionId] = 0;
                continue;
            }
            var snapshot = car.GetRaceAiStateSnapshot();
            if (!snapshot.HasValue)
            {
                _immobilizedSinceMilliseconds[car.SessionId] = 0;
                continue;
            }

            activeBots++;
            maximumSpeed = Math.Max(maximumSpeed, snapshot.Value.CurrentSpeed);
            if (snapshot.Value.CurrentSpeed > RaceBotMath.FieldDeadlockSpeedMetersPerSecond)
            {
                _immobilizedSinceMilliseconds[car.SessionId] = 0;
                continue;
            }
            stoppedBots++;
            fallbackCandidate ??= car;
            if (rootCandidate == null
                && snapshot.Value.ClosestObstacleMeters < 0
                && !snapshot.Value.IsOvertaking)
                rootCandidate = car;

            if (snapshot.Value.CurrentSpeed
                > RaceBotMath.ImmobilizedRaceBotSpeedMetersPerSecond)
            {
                _immobilizedSinceMilliseconds[car.SessionId] = 0;
                continue;
            }
            ref long immobilizedSince = ref _immobilizedSinceMilliseconds[car.SessionId];
            if (immobilizedSince == 0)
            {
                immobilizedSince = now;
                continue;
            }
            long immobilizedMilliseconds = now - immobilizedSince;
            if (RaceBotMath.IsRaceBotImmobilized(snapshot.Value.CurrentSpeed,
                    immobilizedMilliseconds)
                && immobilizedMilliseconds > longestImmobilizedMilliseconds)
            {
                longestImmobilizedMilliseconds = immobilizedMilliseconds;
                immobilizedCandidate = car;
            }
        }

        if (immobilizedCandidate != null
            && now - _lastImmobilizedRecoveryMilliseconds
            >= RaceBotMath.ImmobilizedRaceBotRecoverySpacingMilliseconds
            && immobilizedCandidate.TryRecoverRaceDeadlock())
        {
            _immobilizedSinceMilliseconds[immobilizedCandidate.SessionId] = 0;
            _lastImmobilizedRecoveryMilliseconds = now;
            _fieldDeadlockSinceMilliseconds = 0;
            Log.Warning("Race immobilization recovery moved bot {SessionId} after {StoppedSeconds:F1} s below {Speed:F2} m/s",
                immobilizedCandidate.SessionId,
                longestImmobilizedMilliseconds / 1000d,
                RaceBotMath.ImmobilizedRaceBotSpeedMetersPerSecond);
            return;
        }

        if (!RaceBotMath.IsFieldStalled(activeBots, stoppedBots, maximumSpeed))
        {
            _fieldDeadlockSinceMilliseconds = 0;
            return;
        }

        if (_fieldDeadlockSinceMilliseconds == 0)
        {
            _fieldDeadlockSinceMilliseconds = now;
            return;
        }
        if (!RaceBotMath.IsFieldDeadlocked(activeBots, stoppedBots, maximumSpeed,
                now - _fieldDeadlockSinceMilliseconds)
            || now - _lastFieldDeadlockRecoveryMilliseconds
            < RaceBotMath.FieldDeadlockRecoveryCooldownMilliseconds)
            return;

        var recoveryCandidate = rootCandidate ?? fallbackCandidate;
        if (recoveryCandidate?.TryRecoverRaceDeadlock() != true)
            return;

        _lastFieldDeadlockRecoveryMilliseconds = now;
        _fieldDeadlockSinceMilliseconds = now;
        Log.Warning("Race field deadlock recovery moved bot {SessionId}: {StoppedBots}/{ActiveBots} automatic bots were below {Speed:F2} m/s",
            recoveryCandidate.SessionId, stoppedBots, activeBots,
            RaceBotMath.FieldDeadlockSpeedMetersPerSecond);
    }
}
