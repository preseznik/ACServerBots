using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using AssettoServer.Server.Ai.Physics;

namespace AssettoServer.Server.Fps;

internal sealed class FpsArenaGeometryAsset
{
    private const uint Magic = 0x47535046; // FPSG
    private const int Version = 1;
    public required IReadOnlyList<Kn5Triangle> Triangles { get; init; }

    public void Save(string path)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(Triangles.Count);
        foreach (var triangle in Triangles)
        {
            Write(writer, triangle.A);
            Write(writer, triangle.B);
            Write(writer, triangle.C);
        }
    }

    public static FpsArenaGeometryAsset Load(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(Path.GetFullPath(path)));
        if (reader.ReadUInt32() != Magic)
            throw new InvalidDataException("FPS arena geometry has an invalid header");
        if (reader.ReadInt32() != Version)
            throw new InvalidDataException("FPS arena geometry uses an unsupported version");
        int count = reader.ReadInt32();
        if (count is <= 0 or > 5_000_000)
            throw new InvalidDataException($"FPS arena geometry has an invalid triangle count: {count}");
        var triangles = new Kn5Triangle[count];
        for (int index = 0; index < count; index++)
            triangles[index] = new Kn5Triangle(Read(reader), Read(reader), Read(reader));
        if (reader.BaseStream.Position != reader.BaseStream.Length)
            throw new InvalidDataException("FPS arena geometry has unexpected trailing data");
        return new FpsArenaGeometryAsset { Triangles = triangles };
    }

    private static void Write(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }

    private static Vector3 Read(BinaryReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
}

internal sealed class FpsArenaSurface
{
    private const float CellSize = 4;
    internal const float MaximumWalkableSlopeDegrees = 45;
    // AC track stairs and deep kerbs can have individual risers above 35 cm. A candidate
    // still needs a walkable top, so this does not turn vertical walls into climbable ramps.
    internal const float MaximumStepHeight = 0.48f;
    internal const float MaximumMantleHeight = 1.75f;
    private const float MaximumMantleDistance = 1.8f;
    internal const float MaximumVaultHeight = 1.15f;
    private const float MaximumVaultDistance = 2.2f;
    private const float SurfaceContactTolerance = 0.08f;
    // A drop is not a step. Keeping this close to the upward step limit lets stairs and
    // kerbs descend smoothly, while larger drops transition into an airborne fall.
    internal const float MaximumStepDown = 0.48f;
    private const float MaximumFallDistance = 100;
    private const float ActorRadius = 0.35f;
    private const float StandingHeight = 1.8f;
    private const float CrouchingHeight = 1.15f;
    private const float ProneHeight = 0.65f;
    private const float MaximumSweepStep = 0.08f;
    private static readonly float MinimumWalkableNormalY =
        MathF.Cos(MaximumWalkableSlopeDegrees * MathF.PI / 180);
    private readonly SurfaceTriangle[] _triangles;
    private readonly Dictionary<long, List<int>> _cells = [];
    private readonly List<int> _largeTriangles = [];

    public int TriangleCount => _triangles.Length;

    public FpsArenaSurface(IReadOnlyList<Kn5Triangle> triangles)
    {
        _triangles = new SurfaceTriangle[triangles.Count];
        for (int index = 0; index < triangles.Count; index++)
        {
            var triangle = triangles[index];
            var cross = Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A);
            var normal = cross.LengthSquared() > 1e-8f ? Vector3.Normalize(cross) : Vector3.Zero;
            var item = new SurfaceTriangle(triangle, normal,
                MathF.Min(triangle.A.X, MathF.Min(triangle.B.X, triangle.C.X)),
                MathF.Max(triangle.A.X, MathF.Max(triangle.B.X, triangle.C.X)),
                MathF.Min(triangle.A.Z, MathF.Min(triangle.B.Z, triangle.C.Z)),
                MathF.Max(triangle.A.Z, MathF.Max(triangle.B.Z, triangle.C.Z)),
                MathF.Max(triangle.A.Y, MathF.Max(triangle.B.Y, triangle.C.Y)),
                MathF.Abs(normal.Y) >= MinimumWalkableNormalY);
            _triangles[index] = item;
            Index(index, item);
        }
    }

    public bool TryResolveMove(Vector3 current, Vector3 desired, float currentGroundY,
        float actorHeight,
        out Vector3 resolved, out float groundY)
    {
        resolved = current;
        groundY = currentGroundY;
        var planarDelta = new Vector2(desired.X - current.X, desired.Z - current.Z);
        int steps = Math.Max(1, (int)MathF.Ceiling(planarDelta.Length() / MaximumSweepStep));
        var increment = (desired - current) / steps;
        bool moved = false;
        for (int step = 0; step < steps; step++)
        {
            var candidate = resolved + increment;
            if (TryCandidate(candidate, groundY, actorHeight, out var next, out float nextGround))
            {
                resolved = next;
                groundY = nextGround;
                moved = true;
                continue;
            }

            bool slid = false;
            var xOnly = new Vector3(resolved.X + increment.X, resolved.Y, resolved.Z);
            if (MathF.Abs(increment.X) > 0.0001f
                && TryCandidate(xOnly, groundY, actorHeight, out next, out nextGround))
            {
                resolved = next;
                groundY = nextGround;
                moved = slid = true;
            }
            var zOnly = new Vector3(resolved.X, resolved.Y, resolved.Z + increment.Z);
            if (MathF.Abs(increment.Z) > 0.0001f
                && TryCandidate(zOnly, groundY, actorHeight, out next, out nextGround))
            {
                resolved = next;
                groundY = nextGround;
                moved = slid = true;
            }
            if (!slid) break;
        }
        return moved || planarDelta.LengthSquared() < 1e-8f;
    }

    public bool TryResolveAirMove(Vector3 current, Vector3 desired, float actorHeight,
        out Vector3 resolved, out float groundY)
    {
        resolved = current;
        groundY = current.Y;
        var planarDelta = new Vector2(desired.X - current.X, desired.Z - current.Z);
        int steps = Math.Max(1, (int)MathF.Ceiling(planarDelta.Length() / MaximumSweepStep));
        var increment = (desired - current) / steps;
        bool moved = false;
        for (int step = 0; step < steps; step++)
        {
            var candidate = resolved + increment;
            if (TryAirCandidate(candidate, actorHeight, out float nextGround))
            {
                resolved = candidate;
                groundY = nextGround;
                moved = true;
                continue;
            }

            bool slid = false;
            var xOnly = new Vector3(resolved.X + increment.X, resolved.Y, resolved.Z);
            if (MathF.Abs(increment.X) > 0.0001f
                && TryAirCandidate(xOnly, actorHeight, out nextGround))
            {
                resolved = xOnly;
                groundY = nextGround;
                moved = slid = true;
            }
            var zOnly = new Vector3(resolved.X, resolved.Y, resolved.Z + increment.Z);
            if (MathF.Abs(increment.Z) > 0.0001f
                && TryAirCandidate(zOnly, actorHeight, out nextGround))
            {
                resolved = zOnly;
                groundY = nextGround;
                moved = slid = true;
            }
            if (!slid) break;
        }
        return moved || planarDelta.LengthSquared() < 1e-8f;
    }

    public bool TryGetGroundHeight(float x, float z, float referenceY, out float height)
        => TryGetGroundHeight(x, z, referenceY, -MaximumStepDown, MaximumStepHeight,
            out height);

    public bool TryRaycast(Vector3 origin, Vector3 direction, float maximumDistance,
        out float distance)
    {
        distance = maximumDistance;
        if (!float.IsFinite(maximumDistance) || maximumDistance <= 0
            || direction.LengthSquared() < 1e-8f) return false;
        direction = Vector3.Normalize(direction);
        var checkedTriangles = new HashSet<int>();
        float sampleStep = CellSize * 0.25f;
        bool hit = false;
        for (float along = 0; along <= maximumDistance + sampleStep; along += sampleStep)
        {
            var sample = origin + direction * MathF.Min(along, maximumDistance);
            foreach (int index in Candidates(sample.X, sample.Z))
            {
                if (!checkedTriangles.Add(index)
                    || !RayIntersectsTriangle(origin, direction, _triangles[index].Triangle,
                        out float candidate)
                    || candidate < 0.01f || candidate >= distance) continue;
                distance = candidate;
                hit = true;
            }
        }
        return hit;
    }

    public bool TryFindMantle(Vector3 current, Vector2 direction, float currentGroundY,
        out Vector3 target, out float groundY)
    {
        target = current;
        groundY = currentGroundY;
        if (direction.LengthSquared() < 0.01f) return false;
        direction = Vector2.Normalize(direction);
        for (float distance = 0.35f; distance <= MaximumMantleDistance; distance += 0.1f)
        {
            float x = current.X + direction.X * distance;
            float z = current.Z + direction.Y * distance;
            if (!TryGetGroundHeight(x, z, currentGroundY,
                    MaximumStepHeight + 0.02f, MaximumMantleHeight, out float candidateGround))
                continue;
            var candidate = new Vector3(x, candidateGround, z);
            if (IsBlocked(candidate, candidateGround, CrouchingHeight)) continue;
            target = candidate;
            groundY = candidateGround;
            return true;
        }
        return false;
    }

    public bool TryFindVault(Vector3 current, Vector2 direction, float currentGroundY,
        out Vector3 target, out float groundY)
    {
        target = current;
        groundY = currentGroundY;
        if (direction.LengthSquared() < 0.01f) return false;
        direction = Vector2.Normalize(direction);
        bool foundBarrier = false;
        for (float distance = 0.2f; distance <= MaximumVaultDistance; distance += 0.1f)
        {
            float x = current.X + direction.X * distance;
            float z = current.Z + direction.Y * distance;
            var groundPosition = new Vector3(x, currentGroundY, z);
            bool blockedAtBodyHeight = IsBlocked(groundPosition, currentGroundY, StandingHeight);
            var raisedPosition = groundPosition with { Y = currentGroundY + MaximumVaultHeight };
            bool blockedAboveVault = IsBlocked(raisedPosition, currentGroundY, CrouchingHeight);

            if (!foundBarrier)
            {
                if (!blockedAtBodyHeight) continue;
                if (blockedAboveVault) return false;
                foundBarrier = true;
                continue;
            }

            // Validate the entire elevated corridor, not just the landing point. This keeps
            // vaulting useful for thin rails without allowing it through walls or ceilings.
            if (blockedAboveVault) return false;
            if (blockedAtBodyHeight || distance < 0.8f) continue;
            if (!TryGetGroundHeight(x, z, currentGroundY, out float candidateGround)) continue;
            var candidate = new Vector3(x, candidateGround, z);
            if (IsBlocked(candidate, candidateGround, StandingHeight)) continue;
            target = candidate;
            groundY = candidateGround;
            return true;
        }
        return false;
    }

    private bool TryGetGroundHeight(float x, float z, float referenceY, float minimumDelta,
        float maximumDelta, out float height)
    {
        bool found = false;
        float bestHeight = float.NegativeInfinity;
        height = referenceY;
        foreach (int index in Candidates(x, z))
        {
            var item = _triangles[index];
            if (!item.Walkable || x < item.MinX - 0.001f || x > item.MaxX + 0.001f
                || z < item.MinZ - 0.001f || z > item.MaxZ + 0.001f
                || !TryHeight(item.Triangle, x, z, out float candidate)) continue;
            float delta = candidate - referenceY;
            if (delta > maximumDelta || delta < minimumDelta) continue;
            // Track collision meshes commonly retain a base floor underneath modeled stairs.
            // Prefer the highest reachable support so that a tread wins over the hidden floor.
            if (candidate <= bestHeight) continue;
            found = true;
            bestHeight = candidate;
            height = candidate;
        }
        return found;
    }

    private bool TryCandidate(Vector3 position, float currentGroundY, float actorHeight,
        out Vector3 resolved,
        out float groundY)
    {
        if (!TryGetGroundHeight(position.X, position.Z, currentGroundY, out groundY)
            || IsBlocked(position, groundY, actorHeight))
        {
            resolved = default;
            return false;
        }
        resolved = position;
        return true;
    }

    private bool TryAirCandidate(Vector3 position, float actorHeight, out float groundY)
    {
        if (!TryGetGroundHeight(position.X, position.Z, position.Y, -MaximumFallDistance,
                SurfaceContactTolerance, out groundY)
            || IsBlocked(position, position.Y, actorHeight))
            return false;
        return true;
    }

    private bool IsBlocked(Vector3 position, float groundY, float actorHeight)
    {
        float baseY = MathF.Max(position.Y, groundY);
        float height = Math.Clamp(actorHeight, ProneHeight, StandingHeight);
        int minX = Cell(position.X - ActorRadius);
        int maxX = Cell(position.X + ActorRadius);
        int minZ = Cell(position.Z - ActorRadius);
        int maxZ = Cell(position.Z + ActorRadius);
        var checkedTriangles = new HashSet<int>();
        for (int cellX = minX; cellX <= maxX; cellX++)
        for (int cellZ = minZ; cellZ <= maxZ; cellZ++)
        {
            if (!_cells.TryGetValue(Key(cellX, cellZ), out var cell)) continue;
            foreach (int index in cell)
            {
                if (!checkedTriangles.Add(index)
                    || IsNonBlockingSurface(_triangles[index], position.X, position.Z, baseY))
                    continue;
                if (IntersectsCapsule(position.X, position.Z, baseY, height,
                        _triangles[index].Triangle))
                    return true;
            }
        }
        foreach (int index in _largeTriangles)
        {
            if (IsNonBlockingSurface(_triangles[index], position.X, position.Z, baseY)) continue;
            if (IntersectsCapsule(position.X, position.Z, baseY, height,
                    _triangles[index].Triangle))
                return true;
        }
        return false;
    }

    private static bool IsNonBlockingSurface(SurfaceTriangle triangle, float x, float z,
        float baseY)
    {
        if (triangle.MaxY <= baseY + MaximumStepHeight + SurfaceContactTolerance)
            return true;
        if (!triangle.Walkable) return false;
        // Nearby floor triangles often share the same spatial cell but do not contain the
        // actor's exact X/Z point. They are not barriers. A horizontal triangle directly
        // above the capsule, however, is a ceiling and remains solid.
        return !TryHeight(triangle.Triangle, x, z, out float height)
            || height <= baseY + SurfaceContactTolerance;
    }

    private static bool IntersectsCapsule(float x, float z, float baseY, float height,
        Kn5Triangle triangle)
    {
        float bottom = baseY + ActorRadius;
        float top = baseY + MathF.Max(ActorRadius, height - ActorRadius);
        int samples = Math.Max(1, (int)MathF.Ceiling((top - bottom) / (ActorRadius * 1.5f)));
        for (int sample = 0; sample <= samples; sample++)
        {
            float y = bottom + (top - bottom) * (sample / (float)samples);
            var center = new Vector3(x, y, z);
            if (Vector3.DistanceSquared(center, ClosestPoint(center, triangle))
                < ActorRadius * ActorRadius)
                return true;
        }
        return false;
    }

    private IEnumerable<int> Candidates(float x, float z)
    {
        if (_cells.TryGetValue(Key(Cell(x), Cell(z)), out var cell))
            foreach (int index in cell) yield return index;
        foreach (int index in _largeTriangles) yield return index;
    }

    private void Index(int index, SurfaceTriangle triangle)
    {
        int minX = Cell(triangle.MinX);
        int maxX = Cell(triangle.MaxX);
        int minZ = Cell(triangle.MinZ);
        int maxZ = Cell(triangle.MaxZ);
        long cells = (long)(maxX - minX + 1) * (maxZ - minZ + 1);
        if (cells > 64)
        {
            _largeTriangles.Add(index);
            return;
        }
        for (int x = minX; x <= maxX; x++)
        for (int z = minZ; z <= maxZ; z++)
        {
            long key = Key(x, z);
            if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = [];
            list.Add(index);
        }
    }

    private static bool TryHeight(Kn5Triangle triangle, float x, float z, out float height)
    {
        float x1 = triangle.A.X, z1 = triangle.A.Z;
        float x2 = triangle.B.X, z2 = triangle.B.Z;
        float x3 = triangle.C.X, z3 = triangle.C.Z;
        float denominator = (z2 - z3) * (x1 - x3) + (x3 - x2) * (z1 - z3);
        if (MathF.Abs(denominator) < 1e-7f)
        {
            height = 0;
            return false;
        }
        float a = ((z2 - z3) * (x - x3) + (x3 - x2) * (z - z3)) / denominator;
        float b = ((z3 - z1) * (x - x3) + (x1 - x3) * (z - z3)) / denominator;
        float c = 1 - a - b;
        if (a < -0.001f || b < -0.001f || c < -0.001f)
        {
            height = 0;
            return false;
        }
        height = a * triangle.A.Y + b * triangle.B.Y + c * triangle.C.Y;
        return float.IsFinite(height);
    }

    private static Vector3 ClosestPoint(Vector3 point, Kn5Triangle triangle)
    {
        var ab = triangle.B - triangle.A;
        var ac = triangle.C - triangle.A;
        var ap = point - triangle.A;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return triangle.A;
        var bp = point - triangle.B;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return triangle.B;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return triangle.A + ab * (d1 / (d1 - d3));
        var cp = point - triangle.C;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return triangle.C;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return triangle.A + ac * (d2 / (d2 - d6));
        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
            return triangle.B + (triangle.C - triangle.B) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
        float inverse = 1 / (va + vb + vc);
        return triangle.A + ab * (vb * inverse) + ac * (vc * inverse);
    }

    private static bool RayIntersectsTriangle(Vector3 origin, Vector3 direction,
        Kn5Triangle triangle, out float distance)
    {
        var edge1 = triangle.B - triangle.A;
        var edge2 = triangle.C - triangle.A;
        var cross = Vector3.Cross(direction, edge2);
        float determinant = Vector3.Dot(edge1, cross);
        if (MathF.Abs(determinant) < 1e-7f)
        {
            distance = 0;
            return false;
        }
        float inverse = 1 / determinant;
        var offset = origin - triangle.A;
        float u = Vector3.Dot(offset, cross) * inverse;
        if (u is < 0 or > 1)
        {
            distance = 0;
            return false;
        }
        var q = Vector3.Cross(offset, edge1);
        float v = Vector3.Dot(direction, q) * inverse;
        if (v < 0 || u + v > 1)
        {
            distance = 0;
            return false;
        }
        distance = Vector3.Dot(edge2, q) * inverse;
        return distance >= 0;
    }

    private static int Cell(float value) => (int)MathF.Floor(value / CellSize);
    private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

    private readonly record struct SurfaceTriangle(Kn5Triangle Triangle, Vector3 Normal,
        float MinX, float MaxX, float MinZ, float MaxZ, float MaxY, bool Walkable);
}
