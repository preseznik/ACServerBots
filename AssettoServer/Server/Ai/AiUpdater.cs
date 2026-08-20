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
                Log.Debug("Race physics: {BotCount} bots, Y {MinimumY:F2}..{MaximumY:F2} m, max speed {MaximumSpeed:F1} m/s",
                    diagnostics.BotCount, diagnostics.MinimumY, diagnostics.MaximumY, diagnostics.MaximumSpeed);
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
