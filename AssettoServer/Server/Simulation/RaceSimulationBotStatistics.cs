using System;

namespace AssettoServer.Server.RaceSimulation;

internal sealed class RaceSimulationBotStatistics
{
    private const float FullyStoppedMetersPerSecond = 0.5f;
    private long? _lastObservedAt;
    private float _lastSpeedMetersPerSecond;
    private double _speedIntegral;
    private bool _hasMoved;
    private bool _isFullyStopped;
    private bool _isInContact;
    private long _lastContactManifolds;

    public long ObservedMilliseconds { get; private set; }
    public float TopSpeedMetersPerSecond { get; private set; }
    public int FullStopCount { get; private set; }
    public long FullyStoppedMilliseconds { get; private set; }
    public int RecoveryCount { get; private set; }
    public int ContactEpisodeCount { get; private set; }
    public long ContactManifolds { get; private set; }
    public double AverageSpeedKilometersPerHour => ObservedMilliseconds <= 0
        ? 0
        : _speedIntegral / ObservedMilliseconds * 3.6;
    public double TopSpeedKilometersPerHour => TopSpeedMetersPerSecond * 3.6;

    public void Observe(long simulatedMilliseconds, float speedMetersPerSecond,
        int recoveryCount, long contactManifolds = 0)
    {
        speedMetersPerSecond = float.IsFinite(speedMetersPerSecond)
            ? Math.Max(0, speedMetersPerSecond)
            : 0;
        if (_lastObservedAt.HasValue)
        {
            long elapsed = Math.Max(0, simulatedMilliseconds - _lastObservedAt.Value);
            ObservedMilliseconds += elapsed;
            _speedIntegral += _lastSpeedMetersPerSecond * elapsed;
            if (_isFullyStopped)
                FullyStoppedMilliseconds += elapsed;
        }

        bool stopped = speedMetersPerSecond < FullyStoppedMetersPerSecond;
        if (!stopped)
        {
            _hasMoved = true;
            _isFullyStopped = false;
        }
        else if (_hasMoved && !_isFullyStopped)
        {
            FullStopCount++;
            _isFullyStopped = true;
        }

        TopSpeedMetersPerSecond = Math.Max(TopSpeedMetersPerSecond, speedMetersPerSecond);
        RecoveryCount = Math.Max(RecoveryCount, recoveryCount);
        long newContacts = Math.Max(0, contactManifolds - _lastContactManifolds);
        if (newContacts > 0)
        {
            ContactManifolds += newContacts;
            if (!_isInContact)
                ContactEpisodeCount++;
            _isInContact = true;
        }
        else
        {
            _isInContact = false;
        }
        _lastContactManifolds = Math.Max(_lastContactManifolds, contactManifolds);
        _lastSpeedMetersPerSecond = speedMetersPerSecond;
        _lastObservedAt = simulatedMilliseconds;
    }
}
