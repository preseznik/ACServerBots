using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace AssettoServer.Server.Ai.Physics;

internal readonly record struct RaceSurfaceSample(float Height, Vector3 Normal,
    float DistanceFromCenter, float HalfWidth);

/// <summary>
/// A cheap spatial guide to the selected route deck. Physical raycasts remain authoritative;
/// this atlas chooses the correct nearby deck and bridges only short collision-mesh seams.
/// </summary>
internal sealed class RaceSurfaceAtlas
{
    private const float CellSizeMeters = 10;
    private const int QueryCellRadius = 2;
    private readonly RaceRoutePoint[] _route;
    private readonly Dictionary<long, List<int>> _segments = [];

    public RaceSurfaceAtlas(IReadOnlyList<RaceRoutePoint> route)
    {
        _route = route.ToArray();
        if (_route.Length < 2)
            return;
        for (int index = 0; index < _route.Length; index++)
        {
            int next = (index + 1) % _route.Length;
            Vector3 from = _route[index].Position;
            Vector3 to = _route[next].Position;
            float length = HorizontalDistance(from, to);
            int samples = Math.Max(1, (int)MathF.Ceiling(length / (CellSizeMeters * 0.5f)));
            for (int sample = 0; sample <= samples; sample++)
            {
                Vector3 point = Vector3.Lerp(from, to, sample / (float)samples);
                long cell = CellKey(point.X, point.Z);
                if (!_segments.TryGetValue(cell, out var list))
                {
                    list = [];
                    _segments.Add(cell, list);
                }
                if (list.Count == 0 || list[^1] != index)
                    list.Add(index);
            }
        }
    }

    public bool TrySample(Vector3 position, out RaceSurfaceSample sample)
    {
        sample = default;
        if (_route.Length < 2)
            return false;
        int centerX = CellCoordinate(position.X);
        int centerZ = CellCoordinate(position.Z);
        float bestDistanceSquared = float.PositiveInfinity;
        int bestIndex = -1;
        float bestProgress = 0;
        for (int z = centerZ - QueryCellRadius; z <= centerZ + QueryCellRadius; z++)
        for (int x = centerX - QueryCellRadius; x <= centerX + QueryCellRadius; x++)
        {
            if (!_segments.TryGetValue(CellKey(x, z), out var candidates))
                continue;
            foreach (int index in candidates)
            {
                int next = (index + 1) % _route.Length;
                float progress = ClosestHorizontalProgress(position,
                    _route[index].Position, _route[next].Position);
                Vector3 center = Vector3.Lerp(_route[index].Position, _route[next].Position, progress);
                float distanceSquared = HorizontalDistanceSquared(position, center);
                if (distanceSquared >= bestDistanceSquared)
                    continue;
                bestDistanceSquared = distanceSquared;
                bestIndex = index;
                bestProgress = progress;
            }
        }
        if (bestIndex < 0)
            return false;

        int bestNext = (bestIndex + 1) % _route.Length;
        var from = _route[bestIndex];
        var to = _route[bestNext];
        Vector3 centerPoint = Vector3.Lerp(from.Position, to.Position, bestProgress);
        Vector3 normal = NormalizeUp(Vector3.Lerp(from.SurfaceNormal, to.SurfaceNormal, bestProgress));
        Vector3 delta = position - centerPoint;
        float height = centerPoint.Y - (normal.X * delta.X + normal.Z * delta.Z) / normal.Y;
        float halfWidth = MathF.Max(2,
            MathF.Max(Lerp(from.SideLeft, to.SideLeft, bestProgress),
                Lerp(from.SideRight, to.SideRight, bestProgress)));
        sample = new RaceSurfaceSample(height, normal, MathF.Sqrt(bestDistanceSquared), halfWidth);
        return float.IsFinite(height);
    }

    private static float ClosestHorizontalProgress(Vector3 point, Vector3 from, Vector3 to)
    {
        Vector2 segment = new(to.X - from.X, to.Z - from.Z);
        float lengthSquared = segment.LengthSquared();
        return lengthSquared <= 1e-8f ? 0 : Math.Clamp(Vector2.Dot(
            new Vector2(point.X - from.X, point.Z - from.Z), segment) / lengthSquared, 0, 1);
    }

    private static Vector3 NormalizeUp(Vector3 normal)
    {
        if (!float.IsFinite(normal.X) || !float.IsFinite(normal.Y) || !float.IsFinite(normal.Z)
            || normal.LengthSquared() < 1e-6f)
            return Vector3.UnitY;
        normal = Vector3.Normalize(normal);
        if (normal.Y < 0)
            normal = -normal;
        return normal.Y >= 0.15f ? normal : Vector3.UnitY;
    }

    private static float Lerp(float from, float to, float progress) => from + (to - from) * progress;
    private static float HorizontalDistance(Vector3 first, Vector3 second) =>
        MathF.Sqrt(HorizontalDistanceSquared(first, second));
    private static float HorizontalDistanceSquared(Vector3 first, Vector3 second)
    {
        float x = first.X - second.X;
        float z = first.Z - second.Z;
        return x * x + z * z;
    }
    private static int CellCoordinate(float value) => (int)MathF.Floor(value / CellSizeMeters);
    private static long CellKey(float x, float z) => CellKey(CellCoordinate(x), CellCoordinate(z));
    private static long CellKey(int x, int z) => ((long)x << 32) ^ (uint)z;
}
