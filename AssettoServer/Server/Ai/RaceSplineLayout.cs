using System;
using System.Collections.Generic;
using AssettoServer.Server.Ai.Splines;
using AssettoServer.Server.Configuration;

namespace AssettoServer.Server.Ai;

public sealed class RaceSplineLayout
{
    public IReadOnlyList<int> Route { get; }
    public float LengthMeters { get; }
    private readonly IReadOnlyDictionary<int, float> _pointDistances;

    private RaceSplineLayout(List<int> route, float lengthMeters,
        Dictionary<int, float> pointDistances)
    {
        Route = route;
        LengthMeters = lengthMeters;
        _pointDistances = pointDistances;
    }

    public static RaceSplineLayout Create(ReadOnlySpan<SplinePoint> points, int startPointId)
    {
        if ((uint)startPointId >= (uint)points.Length)
            throw new ConfigurationException($"Race StartSplinePointId {startPointId} is outside the AI spline");

        var route = new List<int>(points.Length);
        var pointDistances = new Dictionary<int, float>(points.Length);
        var visited = new HashSet<int>();
        var current = startPointId;
        float length = 0;

        while (visited.Add(current))
        {
            route.Add(current);
            pointDistances[current] = length;
            ref readonly var point = ref points[current];
            if (point.Length <= 0 || (uint)point.NextId >= (uint)points.Length)
                throw new ConfigurationException("Race behavior requires a closed usable fast_lane.ai spline");

            length += point.Length;
            current = point.NextId;
        }

        if (current != startPointId || route.Count < 20 || length < 100)
            throw new ConfigurationException("Race behavior requires a closed usable fast_lane.ai spline");

        return new RaceSplineLayout(route, length, pointDistances);
    }

    public float SignedDistanceAhead(int fromPointId, float fromSegmentProgress,
        int toPointId, float toSegmentProgress, ReadOnlySpan<SplinePoint> points)
    {
        float fromDistance = DistanceFromStart(fromPointId, fromSegmentProgress, points);
        float toDistance = DistanceFromStart(toPointId, toSegmentProgress, points);
        float distance = toDistance - fromDistance;
        if (distance > LengthMeters * 0.5f)
            distance -= LengthMeters;
        else if (distance < -LengthMeters * 0.5f)
            distance += LengthMeters;
        return distance;
    }

    public float DistanceFromStart(int pointId, float segmentProgress, ReadOnlySpan<SplinePoint> points)
    {
        if (!_pointDistances.TryGetValue(pointId, out var distance))
            throw new ArgumentOutOfRangeException(nameof(pointId),
                "Spline point is not part of the configured race route");
        return distance + Math.Clamp(segmentProgress, 0, 1) * points[pointId].Length;
    }

    public static int GetPointBehind(ReadOnlySpan<SplinePoint> points, int startPointId, float distanceMeters)
    {
        var current = startPointId;
        float traversed = 0;
        int guard = points.Length + 1;

        while (traversed < distanceMeters && guard-- > 0)
        {
            var previous = points[current].PreviousId;
            if ((uint)previous >= (uint)points.Length)
                throw new ConfigurationException("Race grid cannot be placed on an open AI spline");

            current = previous;
            traversed += Math.Max(points[current].Length, 0.01f);
        }

        if (guard <= 0)
            throw new ConfigurationException("Race grid spacing exceeds the usable AI spline length");

        return current;
    }
}
