using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace AssettoServer.Server.Ai.Physics;

internal readonly record struct RaceRoutePoint(Vector3 Position, float SideLeft, float SideRight,
    Vector3 SurfaceNormal);

internal readonly record struct RaceRouteSurfaceResult(List<Kn5Triangle> Triangles,
    List<Kn5Triangle> GridGroundingTriangles, int SourceTriangles,
    double CenterlineCoverage, bool UsesSplineRibbon);

internal readonly record struct RaceRouteBarrierResult(List<Kn5Triangle> Triangles,
    int SourceTriangles);

/// <summary>
/// Limits suspension geometry to the selected layout's road layer. This is important for
/// multilevel tracks where a vertical ray can otherwise see several decks and undersides.
/// </summary>
internal static class RaceRouteSurfaceBuilder
{
    private const float CellSize = 2.5f;
    private const float SamplingStepMeters = 1.5f;
    private const float MinimumSideWidthMeters = 4f;
    private const float MaximumSideWidthMeters = 20f;
    private const float CorridorMarginMeters = 3f;
    private const float MaximumSurfaceHeightErrorMeters = 0.75f;
    private const float MaximumRouteProjectionErrorMeters = 1.25f;
    private const float ProjectionCellSizeMeters = 5f;
    // Lane planning still obeys the authored widths. This extra non-raceable support shoulder
    // keeps an outside suspension ray from falling into empty space during a bounded correction
    // on synthetic multilevel ribbons, where the raw barrier mesh is intentionally suppressed.
    private const float SplineRibbonSupportMarginMeters = 1f;
    private const float MinimumSurfaceUpwardness = 0.2f;
    // Chassis contacts only need wall-like faces. Sloped floors belong to the suspension surface;
    // accepting them here makes some mod-track COLLIDER meshes apply a second, conflicting floor.
    private const float MaximumBarrierUpwardness = 0.25f;
    private const float BarrierBandBelowRoadMeters = 1f;
    private const float BarrierBandAboveRoadMeters = 6f;
    private const float ProtectedLaneWidthFactor = 0.55f;
    private const float MinimumProtectedLaneHalfWidthMeters = 2.25f;
    private const float MaximumProtectedLaneHalfWidthMeters = 5.5f;
    private const int MaximumSplinePoints = 2_000_000;

    public static string FindFastLane(string trackRoot, string? trackConfig)
    {
        string[] candidates = string.IsNullOrWhiteSpace(trackConfig)
            ? [Path.Combine(trackRoot, "ai", "fast_lane.ai")]
            :
            [
                Path.Combine(trackRoot, trackConfig, "ai", "fast_lane.ai"),
                Path.Combine(trackRoot, "ai", trackConfig, "fast_lane.ai")
            ];
        return candidates.FirstOrDefault(File.Exists)
               ?? throw new FileNotFoundException("Selected track layout has no fast_lane.ai",
                   candidates[0]);
    }

    public static RaceRoutePoint[] ReadFastLane(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        int version = reader.ReadInt32();
        int count = ReadCount(reader, "spline point");
        if (count < 20)
            throw new InvalidDataException($"Race spline contains too few points: {count}");

        if (version == -1)
        {
            var legacy = new RaceRoutePoint[count];
            for (int i = 0; i < count; i++)
            {
                legacy[i] = new RaceRoutePoint(ReadVector3(reader), 6, 6, Vector3.UnitY);
                _ = reader.ReadSingle(); // Radius
                _ = reader.ReadSingle(); // Camber
            }
            ValidateClosed(legacy, path);
            return legacy;
        }

        if (version != 7)
            throw new InvalidDataException($"Unsupported fast_lane.ai version {version}: {path}");

        _ = reader.ReadInt32(); // Lap time
        _ = reader.ReadInt32(); // Sample count
        var positions = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            positions[i] = ReadVector3(reader);
            _ = reader.ReadSingle(); // Length
            _ = reader.ReadInt32(); // ID
        }
        int extraCount = ReadCount(reader, "extra spline point");
        if (extraCount != count)
            throw new InvalidDataException("fast_lane.ai point and extra-point counts differ");

        var points = new RaceRoutePoint[count];
        for (int i = 0; i < count; i++)
        {
            SkipSingles(reader, 5); // Speed, gas, brake, obsolete lateral G, radius
            float sideLeft = reader.ReadSingle();
            float sideRight = reader.ReadSingle();
            SkipSingles(reader, 2); // Camber and direction
            var normal = ReadVector3(reader);
            SkipSingles(reader, 6); // Length, forward XYZ, tag, grade
            if (!IsFinite(normal) || normal.LengthSquared() < 1e-6f)
                normal = Vector3.UnitY;
            else
            {
                normal = Vector3.Normalize(normal);
                if (normal.Y < 0)
                    normal = -normal;
                if (normal.Y < 0.15f)
                    normal = Vector3.UnitY;
            }
            points[i] = new RaceRoutePoint(positions[i], SanitizeWidth(sideLeft),
                SanitizeWidth(sideRight), normal);
        }
        ValidateClosed(points, path);
        return points;
    }

    public static RaceRouteSurfaceResult Filter(IReadOnlyList<Kn5Triangle> source,
        IReadOnlyList<RaceRoutePoint> route)
    {
        var map = new RouteHeightMap(route);
        var result = new List<Kn5Triangle>(Math.Min(source.Count, 1_000_000));
        var matchedCells = new HashSet<long>();
        var separatedPhysicalCells = new HashSet<long>();
        Span<Vector3> samples = stackalloc Vector3[7];
        foreach (var triangle in source)
        {
            if (!IsUsableSurface(triangle))
                continue;
            SetSamples(triangle, samples);
            bool include = false;
            foreach (var sample in samples)
            {
                if (!map.TryGetNearestSurfaceError(sample, out long cell, out float error))
                    continue;
                if (error >= 3f && error <= 50f)
                    separatedPhysicalCells.Add(cell);
                if (error > MaximumSurfaceHeightErrorMeters)
                    continue;
                include = true;
                matchedCells.Add(cell);
            }
            if (include)
                result.Add(triangle);
        }

        double coverage = map.GetCenterlineCoverage(matchedCells);
        if (coverage < 0.70)
        {
            throw new InvalidDataException($"Selected fast_lane.ai could only be matched to "
                                           + $"{coverage:P1} of the physical road surface");
        }
        int separatedLayerThreshold = Math.Max(20, map.CenterlineCellCount / 1000);
        bool useSplineRibbon = map.HasSeparatedElevationLayers
                               || separatedPhysicalCells.Count >= separatedLayerThreshold;
        var ribbonRoute = useSplineRibbon ? ProjectRouteToPhysicalSurface(route, result) : null;
        return new RaceRouteSurfaceResult(useSplineRibbon ? BuildSplineRibbon(ribbonRoute!) : result,
            result, source.Count, coverage, useSplineRibbon);
    }

    internal static RaceRoutePoint[] ProjectRouteToPhysicalSurface(
        IReadOnlyList<RaceRoutePoint> route, IReadOnlyList<Kn5Triangle> physicalSurface)
    {
        if (route.Count == 0 || physicalSurface.Count == 0)
            return route.ToArray();

        var index = new SurfaceTriangleIndex(physicalSurface);
        var corrections = new float[route.Count];
        // The physical triangle is authoritative for deck height, but not necessarily for road
        // attitude: mod-track collision meshes can contain steep helper faces and inconsistent
        // normals. Keep the smoothed fast_lane.ai normal so projecting the centre height cannot
        // twist the wide synthetic ribbon around one bad triangle.
        var normals = route.Select(point => NormalizeUp(point.SurfaceNormal)).ToArray();
        var valid = new List<int>(route.Count);
        for (int i = 0; i < route.Count; i++)
        {
            if (!index.TryProject(route[i].Position, out float height, out _))
                continue;
            corrections[i] = height - route[i].Position.Y;
            valid.Add(i);
        }

        if (valid.Count == 0)
            return route.ToArray();

        if (valid.Count == 1)
        {
            float correction = corrections[valid[0]];
            for (int i = 0; i < route.Count; i++)
                corrections[i] = correction;
        }

        for (int validIndex = 0; valid.Count > 1 && validIndex < valid.Count; validIndex++)
        {
            int from = valid[validIndex];
            int to = valid[(validIndex + 1) % valid.Count];
            int span = (to - from + route.Count) % route.Count;
            for (int step = 1; step < span; step++)
            {
                int point = (from + step) % route.Count;
                float blend = step / (float)span;
                corrections[point] = corrections[from]
                                     + (corrections[to] - corrections[from]) * blend;
            }
        }

        var projected = new RaceRoutePoint[route.Count];
        for (int i = 0; i < route.Count; i++)
        {
            float correction = 0;
            const int correctionRadius = 5;
            for (int sample = -correctionRadius; sample <= correctionRadius; sample++)
            {
                int point = (i + sample + route.Count) % route.Count;
                correction += corrections[point];
            }
            correction /= correctionRadius * 2 + 1;

            var normal = Vector3.Zero;
            for (int sample = -2; sample <= 2; sample++)
            {
                int point = (i + sample + route.Count) % route.Count;
                normal += normals[point];
            }
            projected[i] = route[i] with
            {
                Position = route[i].Position with { Y = route[i].Position.Y + correction },
                SurfaceNormal = NormalizeUp(normal)
            };
        }
        return projected;
    }

    private static List<Kn5Triangle> BuildSplineRibbon(IReadOnlyList<RaceRoutePoint> route)
    {
        var result = new List<Kn5Triangle>(route.Count * 2);
        for (int i = 0; i < route.Count; i++)
        {
            int previousIndex = (i - 1 + route.Count) % route.Count;
            int nextIndex = (i + 1) % route.Count;
            var from = route[i];
            var to = route[nextIndex];
            var fromForward = (route[nextIndex].Position - route[previousIndex].Position) with { Y = 0 };
            var toForward = (route[(nextIndex + 1) % route.Count].Position - route[i].Position) with { Y = 0 };
            if (fromForward.LengthSquared() < 1e-6f || toForward.LengthSquared() < 1e-6f)
                continue;
            var fromLeft = new Vector3(-fromForward.Z, 0, fromForward.X) / fromForward.Length();
            var toLeft = new Vector3(-toForward.Z, 0, toForward.X) / toForward.Length();
            var a = OffsetOnSurface(from, fromLeft,
                from.SideLeft + SplineRibbonSupportMarginMeters);
            var b = OffsetOnSurface(from, fromLeft,
                -(from.SideRight + SplineRibbonSupportMarginMeters));
            var c = OffsetOnSurface(to, toLeft,
                -(to.SideRight + SplineRibbonSupportMarginMeters));
            var d = OffsetOnSurface(to, toLeft,
                to.SideLeft + SplineRibbonSupportMarginMeters);
            AddUpwardTriangle(result, a, b, c);
            AddUpwardTriangle(result, a, c, d);
        }
        return result;
    }

    private static Vector3 OffsetOnSurface(RaceRoutePoint point, Vector3 left, float distance)
    {
        var offset = left * distance;
        if (Math.Abs(point.SurfaceNormal.Y) >= 0.15f)
            offset.Y = -(point.SurfaceNormal.X * offset.X + point.SurfaceNormal.Z * offset.Z)
                       / point.SurfaceNormal.Y;
        return point.Position + offset;
    }

    private static void AddUpwardTriangle(List<Kn5Triangle> result, Vector3 a, Vector3 b, Vector3 c)
    {
        var triangle = Vector3.Cross(b - a, c - a).Y >= 0
            ? new Kn5Triangle(a, b, c)
            : new Kn5Triangle(a, c, b);
        if (Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A).LengthSquared() >= 1e-8f)
            result.Add(triangle);
    }

    private static bool IsUsableSurface(Kn5Triangle triangle)
    {
        var cross = Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A);
        float length = cross.Length();
        return length >= 1e-5f && Math.Abs(cross.Y) / length >= MinimumSurfaceUpwardness;
    }

    private static Vector3 NormalizeUp(Vector3 normal)
    {
        if (!IsFinite(normal) || normal.LengthSquared() < 1e-6f)
            return Vector3.UnitY;
        normal = Vector3.Normalize(normal);
        return normal.Y < 0 ? -normal : normal;
    }

    private static bool TryGetSurfaceHeight(Kn5Triangle triangle, float x, float z,
        out float height)
    {
        float denominator = (triangle.B.Z - triangle.C.Z) * (triangle.A.X - triangle.C.X)
                            + (triangle.C.X - triangle.B.X) * (triangle.A.Z - triangle.C.Z);
        if (Math.Abs(denominator) < 1e-8f)
        {
            height = 0;
            return false;
        }
        float a = ((triangle.B.Z - triangle.C.Z) * (x - triangle.C.X)
                   + (triangle.C.X - triangle.B.X) * (z - triangle.C.Z)) / denominator;
        float b = ((triangle.C.Z - triangle.A.Z) * (x - triangle.C.X)
                   + (triangle.A.X - triangle.C.X) * (z - triangle.C.Z)) / denominator;
        float c = 1 - a - b;
        const float edgeTolerance = -1e-4f;
        if (a < edgeTolerance || b < edgeTolerance || c < edgeTolerance)
        {
            height = 0;
            return false;
        }
        height = a * triangle.A.Y + b * triangle.B.Y + c * triangle.C.Y;
        return float.IsFinite(height);
    }

    private static void SetSamples(Kn5Triangle triangle, Span<Vector3> samples)
    {
        samples[0] = triangle.A;
        samples[1] = triangle.B;
        samples[2] = triangle.C;
        samples[3] = (triangle.A + triangle.B + triangle.C) / 3;
        samples[4] = (triangle.A + triangle.B) * 0.5f;
        samples[5] = (triangle.B + triangle.C) * 0.5f;
        samples[6] = (triangle.C + triangle.A) * 0.5f;
    }

    /// <summary>
    /// Keeps walls and obstacles beside the selected route, but rejects horizontal collision
    /// floors and geometry belonging to another deck. Some mod tracks put both in meshes named
    /// COLLIDER; feeding those floors to the chassis contact solver causes launches and twitching.
    /// </summary>
    public static RaceRouteBarrierResult FilterBarriers(IReadOnlyList<Kn5Triangle> source,
        IReadOnlyList<RaceRoutePoint> route)
    {
        var map = new RouteHeightMap(route);
        var result = new List<Kn5Triangle>(Math.Min(source.Count, 500_000));
        Span<Vector3> samples = stackalloc Vector3[7];
        foreach (var triangle in source)
        {
            var cross = Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A);
            float length = cross.Length();
            if (length < 1e-5f || Math.Abs(cross.Y) / length > MaximumBarrierUpwardness)
                continue;

            float minimumY = Math.Min(triangle.A.Y, Math.Min(triangle.B.Y, triangle.C.Y));
            float maximumY = Math.Max(triangle.A.Y, Math.Max(triangle.B.Y, triangle.C.Y));
            samples[0] = triangle.A;
            samples[1] = triangle.B;
            samples[2] = triangle.C;
            samples[3] = (triangle.A + triangle.B + triangle.C) / 3;
            samples[4] = (triangle.A + triangle.B) * 0.5f;
            samples[5] = (triangle.B + triangle.C) * 0.5f;
            samples[6] = (triangle.C + triangle.A) * 0.5f;

            bool include = false;
            bool intersectsProtectedLane = false;
            foreach (var sample in samples)
            {
                if (!map.TryClassifyBarrier(sample.X, sample.Z, minimumY, maximumY,
                        BarrierBandBelowRoadMeters, BarrierBandAboveRoadMeters,
                        out bool insideProtectedLane))
                    continue;
                if (insideProtectedLane)
                {
                    intersectsProtectedLane = true;
                    break;
                }
                include = true;
            }
            if (include && !intersectsProtectedLane)
                result.Add(triangle);
        }
        return new RaceRouteBarrierResult(result, source.Count);
    }

    private static float SanitizeWidth(float value) => !float.IsFinite(value) || value <= 0
        ? 6f
        : Math.Clamp(value, MinimumSideWidthMeters, MaximumSideWidthMeters);

    private static void ValidateClosed(IReadOnlyList<RaceRoutePoint> points, string path)
    {
        if (Vector3.Distance(points[0].Position, points[^1].Position) >= 50)
            throw new InvalidDataException($"Race physics requires a closed fast_lane.ai: {path}");
    }

    private static int ReadCount(BinaryReader reader, string label)
    {
        int value = reader.ReadInt32();
        if (value < 0 || value > MaximumSplinePoints)
            throw new InvalidDataException($"Invalid {label} count: {value}");
        return value;
    }

    private static Vector3 ReadVector3(BinaryReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    private static void SkipSingles(BinaryReader reader, int count)
    {
        for (int i = 0; i < count; i++)
            _ = reader.ReadSingle();
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private readonly record struct HeightLayer(float ExpectedY, float DistanceSquared,
        float LateralDistance, float ProtectedLaneHalfWidth);

    private sealed class RouteHeightMap
    {
        private readonly Dictionary<long, List<HeightLayer>> _layers = [];
        private readonly HashSet<long> _centerlineCells = [];
        public bool HasSeparatedElevationLayers { get; private set; }
        public int CenterlineCellCount => _centerlineCells.Count;

        public RouteHeightMap(IReadOnlyList<RaceRoutePoint> route)
        {
            for (int i = 0; i < route.Count; i++)
            {
                var from = route[i];
                var to = route[(i + 1) % route.Count];
                float distance = Vector3.Distance(from.Position, to.Position);
                int steps = Math.Max(1, (int)Math.Ceiling(distance / SamplingStepMeters));
                for (int step = 0; step < steps; step++)
                {
                    float t = step / (float)steps;
                    var position = Vector3.Lerp(from.Position, to.Position, t);
                    var normal = Vector3.Lerp(from.SurfaceNormal, to.SurfaceNormal, t);
                    normal = normal.LengthSquared() > 1e-6f ? Vector3.Normalize(normal) : Vector3.UnitY;
                    if (normal.Y < 0)
                        normal = -normal;
                    float trackLeftWidth = from.SideLeft + (to.SideLeft - from.SideLeft) * t;
                    float trackRightWidth = from.SideRight + (to.SideRight - from.SideRight) * t;
                    float leftWidth = trackLeftWidth + CorridorMarginMeters;
                    float rightWidth = trackRightWidth + CorridorMarginMeters;
                    var forward = (to.Position - from.Position) with { Y = 0 };
                    if (forward.LengthSquared() < 1e-6f)
                        continue;
                    forward = Vector3.Normalize(forward);
                    var left = new Vector3(-forward.Z, 0, forward.X);
                    Rasterize(position, normal, forward, left, leftWidth, rightWidth,
                        trackLeftWidth, trackRightWidth);
                    _centerlineCells.Add(ToKey(position.X, position.Z));
                }
            }
        }

        private void Rasterize(Vector3 position, Vector3 normal, Vector3 forward, Vector3 left,
            float leftWidth, float rightWidth, float trackLeftWidth, float trackRightWidth)
        {
            float radius = Math.Max(leftWidth, rightWidth) + CellSize;
            int minX = ToCell(position.X - radius);
            int maxX = ToCell(position.X + radius);
            int minZ = ToCell(position.Z - radius);
            int maxZ = ToCell(position.Z + radius);
            for (int x = minX; x <= maxX; x++)
            for (int z = minZ; z <= maxZ; z++)
            {
                var cellCenter = new Vector3((x + 0.5f) * CellSize, position.Y,
                    (z + 0.5f) * CellSize);
                var delta = cellCenter - position;
                float longitudinal = Vector3.Dot(delta, forward);
                if (Math.Abs(longitudinal) > CellSize * 1.25f)
                    continue;
                float lateral = Vector3.Dot(delta, left);
                if (lateral > leftWidth || lateral < -rightWidth)
                    continue;
                float expectedY = Math.Abs(normal.Y) < 0.15f
                    ? position.Y
                    : position.Y - (normal.X * delta.X + normal.Z * delta.Z) / normal.Y;
                float sideWidth = lateral >= 0 ? trackLeftWidth : trackRightWidth;
                float protectedHalfWidth = Math.Clamp(sideWidth * ProtectedLaneWidthFactor,
                    MinimumProtectedLaneHalfWidthMeters, MaximumProtectedLaneHalfWidthMeters);
                AddLayer(Pack(x, z), new HeightLayer(expectedY,
                    longitudinal * longitudinal + lateral * lateral, Math.Abs(lateral),
                    protectedHalfWidth));
            }
        }

        private void AddLayer(long key, HeightLayer candidate)
        {
            if (!_layers.TryGetValue(key, out var layers))
            {
                _layers.Add(key, [candidate]);
                return;
            }
            for (int i = 0; i < layers.Count; i++)
            {
                if (Math.Abs(layers[i].ExpectedY - candidate.ExpectedY) >= 0.35f)
                    continue;
                if (candidate.DistanceSquared < layers[i].DistanceSquared)
                    layers[i] = candidate;
                return;
            }
            layers.Add(candidate);
            float minimum = layers.Min(layer => layer.ExpectedY);
            float maximum = layers.Max(layer => layer.ExpectedY);
            if (maximum - minimum >= 3f)
                HasSeparatedElevationLayers = true;
        }

        public bool TryGetNearestSurfaceError(Vector3 sample, out long cell, out float error)
        {
            cell = ToKey(sample.X, sample.Z);
            error = float.PositiveInfinity;
            if (!_layers.TryGetValue(cell, out var layers))
                return false;
            foreach (var layer in layers)
            {
                float candidate = Math.Abs(sample.Y - layer.ExpectedY);
                if (candidate < error)
                    error = candidate;
            }
            return true;
        }

        public bool TryClassifyBarrier(float x, float z, float minimumY, float maximumY,
            float belowRoad, float aboveRoad, out bool insideProtectedLane)
        {
            insideProtectedLane = false;
            if (!_layers.TryGetValue(ToKey(x, z), out var layers))
                return false;
            bool overlaps = false;
            foreach (var layer in layers)
            {
                if (maximumY < layer.ExpectedY - belowRoad
                    || minimumY > layer.ExpectedY + aboveRoad)
                    continue;
                overlaps = true;
                if (layer.LateralDistance < layer.ProtectedLaneHalfWidth)
                {
                    insideProtectedLane = true;
                    return true;
                }
            }
            return overlaps;
        }

        public double GetCenterlineCoverage(HashSet<long> matchedCells)
        {
            if (_centerlineCells.Count == 0)
                return 0;
            int matched = 0;
            foreach (long key in _centerlineCells)
            {
                Unpack(key, out int x, out int z);
                bool found = false;
                for (int dx = -1; dx <= 1 && !found; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!matchedCells.Contains(Pack(x + dx, z + dz)))
                        continue;
                    found = true;
                    break;
                }
                if (found)
                    matched++;
            }
            return matched / (double)_centerlineCells.Count;
        }

        private static long ToKey(float x, float z) => Pack(ToCell(x), ToCell(z));
        private static int ToCell(float value) => (int)MathF.Floor(value / CellSize);
        private static long Pack(int x, int z) => ((long)x << 32) | (uint)z;
        private static void Unpack(long key, out int x, out int z)
        {
            x = (int)(key >> 32);
            z = (int)key;
        }
    }

    private sealed class SurfaceTriangleIndex
    {
        private readonly Dictionary<long, List<Kn5Triangle>> _cells = [];
        private readonly List<Kn5Triangle> _largeTriangles = [];

        public SurfaceTriangleIndex(IReadOnlyList<Kn5Triangle> triangles)
        {
            foreach (var triangle in triangles)
            {
                int minimumX = ToProjectionCell(Math.Min(triangle.A.X,
                    Math.Min(triangle.B.X, triangle.C.X)));
                int maximumX = ToProjectionCell(Math.Max(triangle.A.X,
                    Math.Max(triangle.B.X, triangle.C.X)));
                int minimumZ = ToProjectionCell(Math.Min(triangle.A.Z,
                    Math.Min(triangle.B.Z, triangle.C.Z)));
                int maximumZ = ToProjectionCell(Math.Max(triangle.A.Z,
                    Math.Max(triangle.B.Z, triangle.C.Z)));
                long cellCount = (long)(maximumX - minimumX + 1)
                                 * (maximumZ - minimumZ + 1);
                if (cellCount > 4096)
                {
                    _largeTriangles.Add(triangle);
                    continue;
                }
                for (int x = minimumX; x <= maximumX; x++)
                for (int z = minimumZ; z <= maximumZ; z++)
                {
                    long key = PackProjectionCell(x, z);
                    if (!_cells.TryGetValue(key, out var cell))
                        _cells.Add(key, cell = []);
                    cell.Add(triangle);
                }
            }
        }

        public bool TryProject(Vector3 position, out float height, out Vector3 normal)
        {
            height = 0;
            normal = Vector3.UnitY;
            float bestError = float.PositiveInfinity;
            Span<Vector2> samples = stackalloc Vector2[5]
            {
                new(position.X, position.Z),
                new(position.X - 0.2f, position.Z),
                new(position.X + 0.2f, position.Z),
                new(position.X, position.Z - 0.2f),
                new(position.X, position.Z + 0.2f)
            };
            foreach (var sample in samples)
            {
                long key = PackProjectionCell(ToProjectionCell(sample.X),
                    ToProjectionCell(sample.Y));
                if (_cells.TryGetValue(key, out var cell))
                    Check(cell, sample.X, sample.Y, position.Y, ref height, ref normal, ref bestError);
                Check(_largeTriangles, sample.X, sample.Y, position.Y,
                    ref height, ref normal, ref bestError);
            }
            return bestError <= MaximumRouteProjectionErrorMeters;
        }

        private static void Check(IEnumerable<Kn5Triangle> triangles, float x, float z,
            float expectedHeight, ref float height, ref Vector3 normal, ref float bestError)
        {
            foreach (var triangle in triangles)
            {
                if (!TryGetSurfaceHeight(triangle, x, z, out float candidateHeight))
                    continue;
                float error = Math.Abs(candidateHeight - expectedHeight);
                if (error >= bestError)
                    continue;
                var candidateNormal = Vector3.Cross(triangle.B - triangle.A,
                    triangle.C - triangle.A);
                if (candidateNormal.LengthSquared() < 1e-8f)
                    continue;
                bestError = error;
                height = candidateHeight;
                normal = NormalizeUp(candidateNormal);
            }
        }

        private static int ToProjectionCell(float value) =>
            (int)MathF.Floor(value / ProjectionCellSizeMeters);

        private static long PackProjectionCell(int x, int z) => ((long)x << 32) | (uint)z;
    }
}
