using System;
using System.Collections.Generic;
using System.Numerics;
using AssettoServer.Server.Ai.Splines;
using AssettoServer.Server.Configuration;

namespace AssettoServer.Server.Ai;

public sealed class RaceSplineLayout
{
    private const float StartGridWindowMeters = 150;
    private const float GridBranchDistanceToleranceMeters = 0.5f;
    private const float GridBranchHeightToleranceMeters = 1.5f;

    public IReadOnlyList<int> Route { get; }
    public float LengthMeters { get; }
    public bool IsPointToPoint { get; }
    private readonly IReadOnlyDictionary<int, float> _pointDistances;

    private RaceSplineLayout(List<int> route, float lengthMeters,
        Dictionary<int, float> pointDistances, bool pointToPoint)
    {
        Route = route;
        LengthMeters = lengthMeters;
        _pointDistances = pointDistances;
        IsPointToPoint = pointToPoint;
    }

    public static RaceSplineLayout Create(ReadOnlySpan<SplinePoint> points, int startPointId, bool pointToPoint = false)
    {
        if ((uint)startPointId >= (uint)points.Length)
            throw new ConfigurationException($"Race StartSplinePointId {startPointId} is outside the AI spline");
        string error = pointToPoint
            ? "Hillclimb behavior requires a usable open fast_lane.ai from its first point"
            : "Race behavior requires a closed usable fast_lane.ai spline";
        if (pointToPoint && points[startPointId].PreviousId >= 0)
            throw new ConfigurationException(error);

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
            if (pointToPoint && point.NextId == -1)
                break;
            if (!float.IsFinite(point.Length) || point.Length <= 0 || (uint)point.NextId >= (uint)points.Length)
                throw new ConfigurationException(error);

            length += point.Length;
            current = point.NextId;
        }

        bool validEnd = pointToPoint ? points[current].NextId == -1 : current == startPointId;
        if (!validEnd || route.Count < 20 || length < 100)
            throw new ConfigurationException(error);

        return new RaceSplineLayout(route, length, pointDistances, pointToPoint);
    }

    public float SignedDistanceAhead(int fromPointId, float fromSegmentProgress,
        int toPointId, float toSegmentProgress, ReadOnlySpan<SplinePoint> points)
    {
        float fromDistance = DistanceFromStart(fromPointId, fromSegmentProgress, points);
        float toDistance = DistanceFromStart(toPointId, toSegmentProgress, points);
        float distance = toDistance - fromDistance;
        if (IsPointToPoint)
            return distance;
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

    internal int ResolveGridPoint(ReadOnlySpan<SplinePoint> points, Vector3 position, Vector3 forward,
        int nearestPointId, float nearestDistanceSquared)
    {
        if (IsPointToPoint || !_pointDistances.TryGetValue(nearestPointId, out float nearestDistance)
            || Math.Min(nearestDistance, LengthMeters - nearestDistance) <= StartGridWindowMeters)
            return nearestPointId;

        // Two parts of a lap can share a straight. Prefer the start branch only when it
        // is nearly as close to the authored grid slot and faces the same direction.
        float distanceLimit = MathF.Sqrt(nearestDistanceSquared) + GridBranchDistanceToleranceMeters;
        float bestDistanceSquared = distanceLimit * distanceLimit;
        float nearestHeight = points[nearestPointId].Position.Y;
        int bestPointId = nearestPointId;
        foreach (int pointId in Route)
        {
            float distanceFromStart = _pointDistances[pointId];
            if (Math.Min(distanceFromStart, LengthMeters - distanceFromStart) > StartGridWindowMeters)
                continue;

            ref readonly var point = ref points[pointId];
            float distanceSquared = Vector3.DistanceSquared(position, point.Position);
            Vector3 tangent = (points[point.NextId].Position - point.Position) with { Y = 0 };
            if (distanceSquared >= bestDistanceSquared
                || Math.Abs(SignedDistanceAhead(nearestPointId, 0, pointId, 0, points)) <= StartGridWindowMeters
                || Math.Abs(point.Position.Y - nearestHeight) > GridBranchHeightToleranceMeters
                || Vector3.Dot(tangent, forward) <= 0.5f * tangent.Length())
                continue;

            bestPointId = pointId;
            bestDistanceSquared = distanceSquared;
        }

        return bestPointId;
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
