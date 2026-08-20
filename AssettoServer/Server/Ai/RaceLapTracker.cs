using System;

namespace AssettoServer.Server.Ai;

public sealed class RaceLapTracker
{
    private const float MinimumLapFraction = 0.85f;
    private readonly int _startPointId;
    private readonly float _minimumLapDistance;
    private float _forwardDistance;

    public int CompletedLaps { get; private set; }

    public RaceLapTracker(int startPointId, float trackLengthMeters)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startPointId);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(trackLengthMeters, 0);
        _startPointId = startPointId;
        _minimumLapDistance = trackLengthMeters * MinimumLapFraction;
    }

    public bool ObservePointTransition(int previousPointId, int currentPointId, float distanceMeters, bool movingForward)
    {
        if (!movingForward || distanceMeters < 0)
            return false;

        _forwardDistance += distanceMeters;
        if (previousPointId == _startPointId || currentPointId != _startPointId || _forwardDistance < _minimumLapDistance)
            return false;

        _forwardDistance = 0;
        CompletedLaps++;
        return true;
    }
}
