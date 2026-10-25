using System;
using System.IO;
using System.Numerics;

namespace AssettoServer.Server.Fps;

// Map-owned, upright volume. Never added to the physical/bullet triangle BVH.
internal sealed class FpsMovementBox
{
    public Vector3 Center { get; }
    public Vector3 HalfX { get; }
    public Vector3 HalfY { get; }
    public Vector3 HalfZ { get; }
    private readonly Vector3 _extent;
    private readonly Vector3 _x;
    private readonly Vector3 _z;

    public FpsMovementBox(Vector3 center, Vector3 halfX, Vector3 halfY, Vector3 halfZ)
    {
        Center = center; HalfX = halfX; HalfY = halfY; HalfZ = halfZ;
        _extent = new(halfX.Length(), halfY.Length(), halfZ.Length());
        if (!Finite(center) || !Finite(_extent) || !Finite(halfX) || !Finite(halfY)
            || !Finite(halfZ) || _extent.X < .01f || _extent.Y < .01f || _extent.Z < .01f
            || _extent.X > 1000 || _extent.Y > 1000 || _extent.Z > 1000)
            throw new InvalidDataException("Invalid FPS_MOVEBOX volume dimensions");
        _x = halfX / _extent.X;
        _z = halfZ / _extent.Z;
        if (MathF.Abs(_x.Y) > .001f || MathF.Abs(_z.Y) > .001f
            || Vector3.Distance(halfY / _extent.Y, Vector3.UnitY) > .001f
            || MathF.Abs(Vector3.Dot(_x, _z)) > .001f)
            throw new InvalidDataException("FPS_MOVEBOX axes must be upright and orthogonal");
    }

    private static bool Finite(Vector3 p) => float.IsFinite(p.X)
        && float.IsFinite(p.Y) && float.IsFinite(p.Z);

    private Vector3 Local(Vector3 point)
    {
        var d = point - Center;
        return new(Vector3.Dot(d, _x), d.Y, Vector3.Dot(d, _z));
    }

    public bool ContainsCapsule(Vector3 feet, float height, float radius)
    {
        var p = Local(feet);
        return MathF.Abs(p.X) < _extent.X + radius
            && MathF.Abs(p.Z) < _extent.Z + radius
            && p.Y < _extent.Y && p.Y + height > -_extent.Y;
    }

    public Vector2 BlockingNormal(Vector3 feet)
    {
        var p = Local(feet);
        var axis = _extent.X - MathF.Abs(p.X) < _extent.Z - MathF.Abs(p.Z) ? _x : _z;
        return new(axis.X, axis.Z);
    }

    // Sweep the whole capsule, including vertical movement and fast traversal arcs.
    public bool IntersectsSegment(Vector3 from, Vector3 to, float height, float radius)
    {
        var a = Local(from);
        var d = Local(to) - a;
        var min = -_extent - new Vector3(radius, height, radius);
        var max = _extent + new Vector3(radius, 0, radius);
        float enter = 0, exit = 1;
        for (int axis = 0; axis < 3; axis++)
        {
            if (MathF.Abs(d[axis]) < 1e-8f)
            {
                if (a[axis] <= min[axis] || a[axis] >= max[axis]) return false;
                continue;
            }
            float t0 = (min[axis] - a[axis]) / d[axis];
            float t1 = (max[axis] - a[axis]) / d[axis];
            enter = MathF.Max(enter, MathF.Min(t0, t1));
            exit = MathF.Min(exit, MathF.Max(t0, t1));
            if (enter >= exit) return false;
        }
        return enter < 1 && exit > 0;
    }
}
