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
            if (now - _lastPhysicsDiagnosticsMilliseconds >= 5000)
            {
                var diagnostics = _racePhysicsWorld.GetDiagnostics();
                var contactPair = _racePhysicsWorld.GetMostFrequentVehicleContactPair();
                float maximumLaneOffset = 0;
                float maximumPassSeparation = 0;
                int passCommits = 0;
                int separatedPasses = 0;
                int completedPasses = 0;
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
                }
                Log.Debug("Race physics: {BotCount} bots, Y {MinimumY:F2}..{MaximumY:F2} m, max speed {MaximumSpeed:F1} m/s, "
                          + "max rise {MaximumUpwardSpeed:F1} m/s, height error {MaximumSplineHeightError:F2} m, "
                          + "suspension {MaximumSuspensionCompression:F2} m, slip {MaximumSlipAngleDegrees:F1} deg, "
                          + "steer {MaximumSteeringAngleDegrees:F1} deg, upright {MinimumUprightDot:F2}, "
                          + "overturned {OverturnedBots}, recoveries {TotalRecoveries}, track corrections {TotalTrackCorrections}, "
                          + "launched {LaunchedBots}/{BotCount}, launch spread {LaunchStepSpread} ticks, "
                          + "lane offset {MaximumLaneOffset:F2} m, pass separation {MaximumPassSeparation:F2} m, "
                          + "passes {PassCommits}/{SeparatedPasses}/{CompletedPasses} committed/separated/completed, "
                          + "vehicle contacts {VehicleManifolds}, contact pair {ContactA}/{ContactB} ({ContactCount}), "
                          + "static pairs {StaticPairTests}, manifolds {StaticManifolds}",
                    diagnostics.BotCount, diagnostics.MinimumY, diagnostics.MaximumY, diagnostics.MaximumSpeed,
                    diagnostics.MaximumUpwardSpeed, diagnostics.MaximumSplineHeightError,
                    diagnostics.MaximumSuspensionCompression,
                    diagnostics.MaximumSlipAngleDegrees, diagnostics.MaximumSteeringAngleDegrees,
                    diagnostics.MinimumUprightDot, diagnostics.OverturnedBots, diagnostics.TotalRecoveries,
                    diagnostics.TotalTrackCorrections, diagnostics.LaunchedBots, diagnostics.BotCount,
                    diagnostics.LaunchStepSpread,
                    maximumLaneOffset, maximumPassSeparation, passCommits, separatedPasses,
                    completedPasses,
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
}
