using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace AssettoServer.Server.Fps;

internal static class FpsPlayableArea
{
    public static bool Contains(IReadOnlyList<Vector3> boundary, float x, float z)
    {
        if (boundary.Count < 3) return true;
        bool inside = false;
        int previous = boundary.Count - 1;
        for (int current = 0; current < boundary.Count; current++)
        {
            var a = boundary[previous];
            var b = boundary[current];
            if (OnSegment(a.X, a.Z, b.X, b.Z, x, z)) return true;
            bool crosses = (a.Z > z) != (b.Z > z)
                           && x < (b.X - a.X) * (z - a.Z) / (b.Z - a.Z) + a.X;
            if (crosses) inside = !inside;
            previous = current;
        }
        return inside;
    }

    public static bool IsValid(IReadOnlyList<Vector3> boundary)
    {
        if (boundary.Count == 0) return true;
        if (boundary.Count < 3 || boundary.Any(point => !float.IsFinite(point.X)
                                                        || !float.IsFinite(point.Z)))
            return false;
        double twiceArea = 0;
        for (int index = 0; index < boundary.Count; index++)
        {
            var current = boundary[index];
            var next = boundary[(index + 1) % boundary.Count];
            twiceArea += (double)current.X * next.Z - (double)next.X * current.Z;
        }
        return Math.Abs(twiceArea) >= 0.02;
    }

    private static bool OnSegment(float ax, float az, float bx, float bz, float x, float z)
    {
        float cross = (x - ax) * (bz - az) - (z - az) * (bx - ax);
        if (MathF.Abs(cross) > 0.001f) return false;
        return x >= MathF.Min(ax, bx) - 0.001f && x <= MathF.Max(ax, bx) + 0.001f
               && z >= MathF.Min(az, bz) - 0.001f && z <= MathF.Max(az, bz) + 0.001f;
    }
}
