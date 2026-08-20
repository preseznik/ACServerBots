using System;
using AssettoServer.Server.Configuration;
using AssettoServer.Server.Configuration.Extra;

namespace AssettoServer.Server.Ai;

public class AiUpdater
{
    private readonly EntryCarManager _entryCarManager;
    private readonly ACServerConfiguration _configuration;
    private readonly SessionManager _sessionManager;
    private long _lastUpdateMilliseconds;
    private double _accumulatorMilliseconds;

    public AiUpdater(EntryCarManager entryCarManager, ACServer server, ACServerConfiguration configuration, SessionManager sessionManager)
    {
        _entryCarManager = entryCarManager;
        _configuration = configuration;
        _sessionManager = sessionManager;
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
            for (var i = 0; i < _entryCarManager.EntryCars.Length; i++)
            {
                var entryCar = _entryCarManager.EntryCars[i];
                if (entryCar.AiControlled)
                {
                    entryCar.AiUpdate(stepSeconds);
                }
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
