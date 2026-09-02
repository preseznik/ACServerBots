using System;
using System.Collections.Generic;
using System.Numerics;

namespace AssettoServer.Server.Fps;

internal readonly record struct FpsBvhPrimitive(int Index, Vector3 Min, Vector3 Max)
{
    public Vector3 Center => (Min + Max) * 0.5f;
}

/// <summary>
/// Deterministic static BVH for prepared FPS collision geometry. The tree is built once
/// when an arena is loaded and replaces the old fixed 4 m buckets, whose worst cells could
/// contain thousands of overlapping KN5 triangles.
/// </summary>
internal sealed class FpsTriangleBvh
{
    private const int MaximumLeafPrimitives = 8;
    private readonly FpsBvhPrimitive[] _primitives;
    private readonly Node[] _nodes;

    public int NodeCount { get; }
    public int LeafCount { get; private set; }
    public int MaximumLeafSize { get; private set; }

    public FpsTriangleBvh(IReadOnlyList<FpsBvhPrimitive> primitives)
    {
        if (primitives.Count == 0) throw new ArgumentException("A BVH needs primitives.", nameof(primitives));
        _primitives = new FpsBvhPrimitive[primitives.Count];
        for (int index = 0; index < primitives.Count; index++)
            _primitives[index] = primitives[index];
        _nodes = new Node[Math.Max(1, primitives.Count * 2)];
        int nodeCount = 0;
        Build(0, _primitives.Length, ref nodeCount);
        NodeCount = nodeCount;
    }

    public void Collect(Vector3 minimum, Vector3 maximum, List<int> results)
    {
        results.Clear();
        Span<int> stack = stackalloc int[64];
        int count = 0;
        stack[count++] = 0;
        while (count > 0)
        {
            ref readonly var node = ref _nodes[stack[--count]];
            if (!Intersects(node.Minimum, node.Maximum, minimum, maximum)) continue;
            if (node.Count > 0)
            {
                for (int index = node.Start; index < node.Start + node.Count; index++)
                    results.Add(_primitives[index].Index);
                continue;
            }
            stack[count++] = node.Left;
            stack[count++] = node.Right;
        }
    }

    public void CollectRay(Vector3 origin, Vector3 direction, float maximumDistance,
        List<int> results)
    {
        results.Clear();
        Span<int> stack = stackalloc int[64];
        int count = 0;
        stack[count++] = 0;
        while (count > 0)
        {
            ref readonly var node = ref _nodes[stack[--count]];
            if (!IntersectsRay(node.Minimum, node.Maximum, origin, direction,
                    maximumDistance)) continue;
            if (node.Count > 0)
            {
                for (int index = node.Start; index < node.Start + node.Count; index++)
                    results.Add(_primitives[index].Index);
                continue;
            }
            stack[count++] = node.Left;
            stack[count++] = node.Right;
        }
    }

    private int Build(int start, int count, ref int nodeCount)
    {
        int nodeIndex = nodeCount++;
        var minimum = new Vector3(float.PositiveInfinity);
        var maximum = new Vector3(float.NegativeInfinity);
        var centerMinimum = new Vector3(float.PositiveInfinity);
        var centerMaximum = new Vector3(float.NegativeInfinity);
        for (int index = start; index < start + count; index++)
        {
            var primitive = _primitives[index];
            minimum = Vector3.Min(minimum, primitive.Min);
            maximum = Vector3.Max(maximum, primitive.Max);
            centerMinimum = Vector3.Min(centerMinimum, primitive.Center);
            centerMaximum = Vector3.Max(centerMaximum, primitive.Center);
        }

        if (count <= MaximumLeafPrimitives)
        {
            _nodes[nodeIndex] = new Node(minimum, maximum, -1, -1, start, count);
            LeafCount++;
            MaximumLeafSize = Math.Max(MaximumLeafSize, count);
            return nodeIndex;
        }

        var centerExtent = centerMaximum - centerMinimum;
        int axis = centerExtent.X >= centerExtent.Y && centerExtent.X >= centerExtent.Z ? 0
            : centerExtent.Y >= centerExtent.Z ? 1 : 2;
        int middle = start + count / 2;
        SelectMedian(start, start + count - 1, middle, axis);
        int left = Build(start, middle - start, ref nodeCount);
        int right = Build(middle, start + count - middle, ref nodeCount);
        _nodes[nodeIndex] = new Node(minimum, maximum, left, right, 0, 0);
        return nodeIndex;
    }

    private void SelectMedian(int left, int right, int target, int axis)
    {
        while (left < right)
        {
            int pivot = Partition(left, right, (left + right) / 2, axis);
            if (target == pivot) return;
            if (target < pivot) right = pivot - 1;
            else left = pivot + 1;
        }
    }

    private int Partition(int left, int right, int pivot, int axis)
    {
        var pivotValue = _primitives[pivot];
        Swap(pivot, right);
        int store = left;
        for (int index = left; index < right; index++)
        {
            if (Compare(_primitives[index], pivotValue, axis) >= 0) continue;
            Swap(store++, index);
        }
        Swap(store, right);
        return store;
    }

    private void Swap(int first, int second)
    {
        if (first == second) return;
        (_primitives[first], _primitives[second]) = (_primitives[second], _primitives[first]);
    }

    private static int Compare(FpsBvhPrimitive first, FpsBvhPrimitive second, int axis)
    {
        float firstCenter = Axis(first.Center, axis);
        float secondCenter = Axis(second.Center, axis);
        int result = firstCenter.CompareTo(secondCenter);
        return result != 0 ? result : first.Index.CompareTo(second.Index);
    }

    private static float Axis(Vector3 value, int axis) => axis switch
    {
        0 => value.X,
        1 => value.Y,
        _ => value.Z,
    };

    private static bool Intersects(Vector3 firstMinimum, Vector3 firstMaximum,
        Vector3 secondMinimum, Vector3 secondMaximum) =>
        firstMaximum.X >= secondMinimum.X && firstMinimum.X <= secondMaximum.X
        && firstMaximum.Y >= secondMinimum.Y && firstMinimum.Y <= secondMaximum.Y
        && firstMaximum.Z >= secondMinimum.Z && firstMinimum.Z <= secondMaximum.Z;

    private static bool IntersectsRay(Vector3 minimum, Vector3 maximum, Vector3 origin,
        Vector3 direction, float maximumDistance)
    {
        float near = 0;
        float far = maximumDistance;
        return ClipAxis(minimum.X, maximum.X, origin.X, direction.X, ref near, ref far)
               && ClipAxis(minimum.Y, maximum.Y, origin.Y, direction.Y, ref near, ref far)
               && ClipAxis(minimum.Z, maximum.Z, origin.Z, direction.Z, ref near, ref far);
    }

    private static bool ClipAxis(float minimum, float maximum, float origin, float direction,
        ref float near, ref float far)
    {
        if (MathF.Abs(direction) < 1e-8f) return origin >= minimum && origin <= maximum;
        float inverse = 1 / direction;
        float first = (minimum - origin) * inverse;
        float second = (maximum - origin) * inverse;
        if (first > second) (first, second) = (second, first);
        near = MathF.Max(near, first);
        far = MathF.Min(far, second);
        return near <= far;
    }

    private readonly record struct Node(Vector3 Minimum, Vector3 Maximum, int Left, int Right,
        int Start, int Count);
}
