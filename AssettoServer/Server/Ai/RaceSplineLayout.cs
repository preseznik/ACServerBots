using System;
using System.Collections.Generic;
using System.Numerics;
using AssettoServer.Server.Ai.Splines;
using AssettoServer.Server.Configuration;

namespace AssettoServer.Server.Ai;

internal readonly record struct RaceSplineProjection(
    float DistanceFromStart, float LateralOffsetMeters, Vector3 LateralDirection);

public sealed class RaceSplineLayout
{
    private const float StartGridWindowMeters = 150;
    private const float GridBranchDistanceToleranceMeters = 0.5f;
    private const float GridBranchHeightToleranceMeters = 1.5f;

    public IReadOnlyList<int> Route { get; }
    public float LengthMeters { get; }
    public bool IsPointToPoint { get; }
    private readonly IReadOnlyDictionary<int, float> _pointDistances;
    private readonly Dictionary<int, float> _startFinishBrakingRadii = new();

    private RaceSplineLayout(ReadOnlySpan<SplinePoint> points, List<int> route, float lengthMeters,
        Dictionary<int, float> pointDistances, bool pointToPoint)
    {
        Route = route;
        LengthMeters = lengthMeters;
        _pointDistances = pointDistances;
        IsPointToPoint = pointToPoint;
        if (!pointToPoint)
            CorrectStartFinishBrakingRadii(points);
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

        return new RaceSplineLayout(points, route, length, pointDistances, pointToPoint);
    }

    internal float GetFullPaceBrakingRadius(int pointId, float authoredRadius) =>
        _startFinishBrakingRadii.GetValueOrDefault(pointId, authoredRadius);

    private void CorrectStartFinishBrakingRadii(ReadOnlySpan<SplinePoint> points)
    {
        const float startFinishWindowMeters = 35;
        const float maximumSuspectRadiusMeters = 200;
        const float minimumStraightRadiusMeters = 350;
        for (int i = 0; i < Route.Count; i++)
        {
            int pointId = Route[i];
            ref readonly var point = ref points[pointId];
            float distance = _pointDistances[pointId];
            if (Math.Min(distance, LengthMeters - distance) > startFinishWindowMeters
                || point.Radius is not (> 0 and < maximumSuspectRadiusMeters)
                || RaceBotMath.IsTightBankedBend(point.Radius, point.Camber))
                continue;

            // Some loops record a tight radius where the spline closes on a straight.
            // Both distances must agree that the actual line is nearly straight.
            float radius = Math.Min(MeasureHorizontalRadius(points, i, 6),
                MeasureHorizontalRadius(points, i, 12));
            if (radius > minimumStraightRadiusMeters)
                _startFinishBrakingRadii.Add(pointId, radius);
        }
    }

    private float MeasureHorizontalRadius(ReadOnlySpan<SplinePoint> points, int index, float sampleDistance)
    {
        var center = points[Route[index]].Position;
        var before = center - SampleRoutePosition(points, index, -sampleDistance);
        var after = SampleRoutePosition(points, index, sampleDistance) - center;
        var incoming = new Vector2(before.X, before.Z);
        var outgoing = new Vector2(after.X, after.Z);
        if (Vector2.Dot(incoming, outgoing) <= 0)
            return 0;

        float twiceCross = 2 * Math.Abs(incoming.X * outgoing.Y - incoming.Y * outgoing.X);
        return Math.Min(1000, incoming.Length() * outgoing.Length() * (incoming + outgoing).Length()
                              / Math.Max(1e-6f, twiceCross));
    }

    private Vector3 SampleRoutePosition(ReadOnlySpan<SplinePoint> points, int index, float distance)
    {
        int direction = Math.Sign(distance);
        float remaining = Math.Abs(distance);
        while (true)
        {
            int next = (index + direction + Route.Count) % Route.Count;
            float length = points[Route[direction > 0 ? index : next]].Length;
            if (remaining <= length)
                return Vector3.Lerp(points[Route[index]].Position, points[Route[next]].Position,
                    remaining / length);

            remaining -= length;
            index = next;
        }
    }

    public float SignedDistanceAhead(int fromPointId, float fromSegmentProgress,
        int toPointId, float toSegmentProgress, ReadOnlySpan<SplinePoint> points)
    {
        float fromDistance = DistanceFromStart(fromPointId, fromSegmentProgress, points);
        float toDistance = DistanceFromStart(toPointId, toSegmentProgress, points);
        return SignedDistanceAhead(fromDistance, toDistance);
    }

    internal float SignedDistanceAhead(float fromDistance, float toDistance)
    {
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

    internal RaceSplineProjection? ProjectPosition(ReadOnlySpan<SplinePoint> points, int nearestPointId, Vector3 position)
    {
        if ((uint)nearestPointId >= (uint)points.Length)
            return null;

        var next = ProjectSegment(points, nearestPointId, position);
        var previous = ProjectSegment(points, points[nearestPointId].PreviousId, position);
        return previous is { } before && (next is null || before.DistanceSquared < next.Value.DistanceSquared)
            ? before.Projection
            : next?.Projection;
    }

    private (RaceSplineProjection Projection, float DistanceSquared)? ProjectSegment(
        ReadOnlySpan<SplinePoint> points, int pointId, Vector3 position)
    {
        if (!_pointDistances.TryGetValue(pointId, out float distance) || (uint)points[pointId].NextId >= (uint)points.Length)
            return null;

        ref readonly var start = ref points[pointId];
        ref readonly var end = ref points[start.NextId];
        var tangent = (end.Position - start.Position) with { Y = 0 };
        float lengthSquared = tangent.LengthSquared();
        if (lengthSquared < 1e-6f)
            return null;

        float progress = Math.Clamp(Vector3.Dot(position - start.Position, tangent) / lengthSquared, 0, 1);
        var center = Vector3.Lerp(start.Position, end.Position, progress);
        var offset = position - center;
        var forward = tangent / MathF.Sqrt(lengthSquared);
        var right = Vector3.Cross(Vector3.UnitY, forward);
        float lateral = Vector3.Dot(offset, right);
        float halfWidth = lateral < 0 ? start.SideLeft : start.SideRight;
        const float roadEdgeAllowanceMeters = 1.5f;
        const float maximumHeightErrorMeters = 2;
        const float maximumSegmentEndErrorMeters = 3;
        if (!float.IsFinite(lateral) || !float.IsFinite(offset.Y) || Math.Abs(offset.Y) > maximumHeightErrorMeters
            || Math.Abs(Vector3.Dot(offset, forward)) > maximumSegmentEndErrorMeters
            || Math.Abs(lateral) > Math.Max(0, halfWidth) + roadEdgeAllowanceMeters)
            return null;

        return (new RaceSplineProjection(distance + progress * start.Length, lateral, right), offset.LengthSquared());
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
