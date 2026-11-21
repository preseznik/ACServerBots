using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace AssettoServer.Server.Ai.Physics;

internal readonly record struct RaceTimingGate(Vector3 Left, Vector3 Right, Vector3 Forward)
{
    public static RaceTimingGate Create(Vector3 left, Vector3 right, Vector3 routeEnd, Vector3 tangent)
    {
        var across = (right - left) with { Y = 0 };
        float width = across.Length();
        var center = (left + right) * 0.5f;
        var normal = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, across));
        float alignment = Vector3.Dot(normal, Vector3.Normalize(tangent with { Y = 0 }));
        if (!(width is >= 2 and <= 25) || !(Vector3.Distance(center, routeEnd) <= 15)
            || !(Math.Abs(alignment) >= 0.5f))
            throw new InvalidDataException("The hillclimb timing gate does not cross the road at its spline endpoint");
        return new(left, right, alignment >= 0 ? normal : -normal);
    }

    public bool CrossedForward(Vector3 previous, Vector3 current)
    {
        float before = Vector3.Dot(previous - Left, Forward);
        float after = Vector3.Dot(current - Left, Forward);
        if (!(before < 0 && after >= 0))
            return false;

        var crossing = Vector3.Lerp(previous, current, -before / (after - before));
        var across = (Right - Left) with { Y = 0 };
        float offset = Vector3.Dot(crossing - Left, across) / across.LengthSquared();
        float gateHeight = Left.Y + (Right.Y - Left.Y) * offset;
        return offset is >= 0 and <= 1 && Math.Abs(crossing.Y - gateHeight) <= 3f;
    }
}

internal readonly record struct RaceHillclimbCourse(RaceTimingGate Start, RaceTimingGate Finish)
{
    public static RaceHillclimbCourse Create(IReadOnlyList<RaceRoutePoint> route,
        Vector3 startLeft, Vector3 startRight, Vector3 finishLeft, Vector3 finishRight)
    {
        if (route.Count < 2)
            throw new InvalidDataException("The hillclimb requires an open spline with two endpoints");
        return new(
            RaceTimingGate.Create(startLeft, startRight, route[0].Position, route[1].Position - route[0].Position),
            RaceTimingGate.Create(finishLeft, finishRight, route[^1].Position, route[^1].Position - route[^2].Position));
    }
}
