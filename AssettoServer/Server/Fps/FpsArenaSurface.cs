using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    internal const float MaximumWalkableSlopeDegrees = 45;
    // AC track stairs and deep kerbs can have individual risers above 35 cm. A candidate
    // still needs a walkable top, so this does not turn vertical walls into climbable ramps.
    internal const float MaximumStepHeight = 0.48f;
    internal const float MaximumMantleHeight = 1.75f;
    private const float MaximumMantleDistance = 1.8f;
    internal const float MaximumVaultHeight = 1.15f;
    private const float MaximumVaultDistance = 2.2f;
    private const float SurfaceContactTolerance = 0.08f;
    // Imported terrain transitions can contain narrow, steep side strips even though the
    // total rise remains within the normal capsule auto-step limit. Treat those strips as
    // walkable seams. Taller risers and segmented walls remain blocked by their upper faces.
    internal const float MaximumTerrainSeamRise = MaximumStepHeight;
    // Keep this below the ordinary contact tolerance: it only closes tiny cracks caused
    // by adjacent imported terrain strips not sharing exactly the same projected edge.
    private const float MaximumTerrainSupportSnapDistance = 0.06f;
    // A drop is not a step. Keeping this close to the upward step limit lets stairs and
    // kerbs descend smoothly, while larger drops transition into an airborne fall.
    internal const float MaximumStepDown = 0.48f;
    private const float MaximumFallDistance = 100;
    // Keep a small visual clearance from walls as well as preventing mathematical
    // intersection. This avoids the first-person near plane cutting into rough meshes.
    internal const float ActorRadius = 0.39f;
    private const float StandingHeight = 1.8f;
    private const float CrouchingHeight = 1.15f;
    private const float ProneHeight = 0.65f;
    private const float MaximumSweepStep = 0.08f;
    // Decorative imported floor bands can alternate among overlapping support planes even
    // though the rendered lane is visually flat. Limit only those sub-knee-height changes per
    // sweep so the authoritative capsule converges to the real support without a one-frame pop.
    internal const float MaximumGroundContinuityHeight = 0.22f;
    internal const float MaximumGroundContinuityChangePerSweep = 0.025f;
    // Some imported AC meshes omit collision from a visually continuous floor strip. Probe
    // a few bounded radii so grounded movement can bridge Nuketown's 45-50 cm colored ring
    // and its measured 1.05 m side-yard void, but only when comparable support exists on both
    // opposing sides. A real ledge still has no opposing support and remains a fall.
    internal const float MaximumImportedGroundGapProbeRadius = 1.2f;
    // A grounded actor can momentarily lose direct support on fragmented imported meshes.
    // Only cross that unsupported strip when a real landing exists ahead; this prevents
    // shallow decorative islands from becoming traps without allowing movement beyond
    // the physical end of an arena.
    internal const float MaximumUnsupportedLandingLookahead = 0.65f;
    private const int MaximumSlideIterations = 3;
    internal const int MaximumDepenetrationProbes = 64;
    private static readonly float MinimumWalkableNormalY =
        MathF.Cos(MaximumWalkableSlopeDegrees * MathF.PI / 180);
    // Small imported bevel ribbons can be much steeper than the surrounding terrain even
    // though their total rise is only a few centimetres. They are ground support, not a
    // climbable general-purpose slope; the height cap below keeps tall 45+ degree faces solid.
    private const float MinimumTerrainSeamSupportNormalY = 0.25f;
    private readonly SurfaceTriangle[] _triangles;
    private readonly FpsTriangleBvh _bvh;
    private readonly Stack<List<int>> _candidatePool = [];
    private int _broadphaseQueries;
    private long _broadphaseCandidates;
    private int _broadphaseMaximumCandidates;
    private int _depenetrationProbes;

    public int TriangleCount => _triangles.Length;
    public int BvhNodeCount => _bvh.NodeCount;
    public int BvhLeafCount => _bvh.LeafCount;
    public int BvhMaximumLeafTriangles => _bvh.MaximumLeafSize;
    internal FpsSurfaceDiagnostics TickDiagnostics => new(_broadphaseQueries,
        _broadphaseCandidates, _broadphaseMaximumCandidates, _depenetrationProbes);

    public FpsArenaSurface(IReadOnlyList<Kn5Triangle> triangles)
    {
        _triangles = new SurfaceTriangle[triangles.Count];
        var primitives = new FpsBvhPrimitive[triangles.Count];
        for (int index = 0; index < triangles.Count; index++)
        {
            var triangle = triangles[index];
            var cross = Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A);
            var normal = cross.LengthSquared() > 1e-8f ? Vector3.Normalize(cross) : Vector3.Zero;
            float minY = MathF.Min(triangle.A.Y, MathF.Min(triangle.B.Y, triangle.C.Y));
            float maxY = MathF.Max(triangle.A.Y, MathF.Max(triangle.B.Y, triangle.C.Y));
            float normalY = MathF.Abs(normal.Y);
            bool terrainSeamSupport = normalY < MinimumWalkableNormalY
                && normalY >= MinimumTerrainSeamSupportNormalY
                && maxY - minY <= MaximumTerrainSeamRise;
            bool walkable = normalY >= MinimumWalkableNormalY || terrainSeamSupport;
            var item = new SurfaceTriangle(triangle, normal,
                MathF.Min(triangle.A.X, MathF.Min(triangle.B.X, triangle.C.X)),
                MathF.Max(triangle.A.X, MathF.Max(triangle.B.X, triangle.C.X)),
                MathF.Min(triangle.A.Z, MathF.Min(triangle.B.Z, triangle.C.Z)),
                MathF.Max(triangle.A.Z, MathF.Max(triangle.B.Z, triangle.C.Z)),
                minY, maxY, walkable, terrainSeamSupport);
            _triangles[index] = item;
            primitives[index] = new FpsBvhPrimitive(index,
                new Vector3(item.MinX, item.MinY, item.MinZ),
                new Vector3(item.MaxX, item.MaxY, item.MaxZ));
        }
        _bvh = new FpsTriangleBvh(primitives);
    }

    internal void BeginTickDiagnostics()
    {
        _broadphaseQueries = 0;
        _broadphaseCandidates = 0;
        _broadphaseMaximumCandidates = 0;
        _depenetrationProbes = 0;
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
            if (TryCandidate(candidate, groundY, actorHeight, increment,
                    out var next, out float nextGround, out _))
            {
                resolved = next;
                groundY = nextGround;
                moved = true;
                continue;
            }

            if (!TrySlideGround(resolved, increment, groundY, actorHeight,
                    out next, out nextGround)) break;
            resolved = next;
            groundY = nextGround;
            moved = true;
        }
        return moved || planarDelta.LengthSquared() < 1e-8f;
    }

    public bool TryResolveAirMove(Vector3 current, Vector3 desired, float actorHeight,
        out Vector3 resolved, out float groundY, bool allowUnsupportedGround = true,
        float unsupportedGroundY = float.NaN)
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
            if (TryAirCandidate(candidate, actorHeight, increment, allowUnsupportedGround,
                    unsupportedGroundY,
                    out float nextGround, out _))
            {
                resolved = candidate;
                groundY = nextGround;
                moved = true;
                continue;
            }

            if (!TrySlideAir(resolved, increment, actorHeight, allowUnsupportedGround,
                    unsupportedGroundY,
                    out var next, out nextGround)) break;
            resolved = next;
            groundY = nextGround;
            moved = true;
        }
        return moved || planarDelta.LengthSquared() < 1e-8f;
    }

    public bool HasWalkableLandingAhead(Vector3 current, Vector3 desired,
        float currentGroundY, float actorHeight)
    {
        var direction = new Vector2(desired.X - current.X, desired.Z - current.Z);
        if (direction.LengthSquared() < 1e-8f) return false;
        direction = Vector2.Normalize(direction);
        for (float distance = MaximumSweepStep;
             distance <= MaximumUnsupportedLandingLookahead + 0.001f;
             distance += MaximumSweepStep)
        {
            float x = current.X + direction.X * distance;
            float z = current.Z + direction.Y * distance;
            if (!TryGetGroundHeight(x, z, currentGroundY, -MaximumFallDistance,
                    MaximumStepHeight, out float landingGroundY))
                continue;
            var landing = new Vector3(x, landingGroundY, z);
            if (!IsPositionBlocked(landing, landingGroundY, actorHeight)) return true;
        }
        return false;
    }

    public bool TryGetGroundHeight(float x, float z, float referenceY, out float height)
        => TryGetGroundHeight(x, z, referenceY, -MaximumStepDown, MaximumStepHeight,
            out height);

    internal void CollectWalkableHeights(float x, float z, float minimumY, float maximumY,
        List<float> heights)
    {
        heights.Clear();
        var candidates = RentCandidates(new Vector3(x - 0.001f, minimumY - 0.001f,
                z - 0.001f),
            new Vector3(x + 0.001f, maximumY + 0.001f, z + 0.001f));
        try
        {
            foreach (int index in candidates)
            {
                var item = _triangles[index];
                if (!item.Walkable || x < item.MinX - 0.001f || x > item.MaxX + 0.001f
                    || z < item.MinZ - 0.001f || z > item.MaxZ + 0.001f
                    || !TryHeight(item.Triangle, x, z, out float height)
                    || height < minimumY || height > maximumY)
                    continue;
                if (heights.Any(existing => MathF.Abs(existing - height) < 0.05f)) continue;
                heights.Add(height);
            }
        }
        finally
        {
            ReturnCandidates(candidates);
        }
        heights.Sort();
    }

    public bool IsPositionBlocked(Vector3 position, float groundY, float actorHeight) =>
        TryGetBlockingNormal(position, groundY, actorHeight, Vector3.Zero, out _);

    public bool TryDepenetrate(Vector3 position, float groundY, float actorHeight,
        out Vector3 resolved, out float resolvedGroundY)
    {
        resolved = position;
        resolvedGroundY = groundY;
        if (!IsPositionBlocked(position, groundY, actorHeight)) return true;

        const int directions = 8;
        const int rings = MaximumDepenetrationProbes / directions;
        for (int ring = 0; ring < rings; ring++)
        for (int direction = 0; direction < directions; direction++)
        {
            _depenetrationProbes++;
            float radius = 0.05f + (1.25f - 0.05f) * ring / (rings - 1);
            float angle = direction * MathF.Tau / directions;
            float x = position.X + MathF.Cos(angle) * radius;
            float z = position.Z + MathF.Sin(angle) * radius;
            if (!TryGetGroundHeight(x, z, groundY, -MaximumStepDown,
                    MaximumStepHeight, out float candidateGround)) continue;
            var candidate = new Vector3(x, candidateGround, z);
            if (IsPositionBlocked(candidate, candidateGround, actorHeight)) continue;
            resolved = candidate;
            resolvedGroundY = candidateGround;
            return true;
        }
        return false;
    }

    public bool TryDepenetrateAir(Vector3 position, float actorHeight,
        out Vector3 resolved, out float resolvedGroundY)
    {
        resolved = position;
        resolvedGroundY = position.Y;
        if (!TryGetGroundHeight(position.X, position.Z, position.Y, -MaximumFallDistance,
                SurfaceContactTolerance, out resolvedGroundY))
            return false;
        if (!IsPositionBlocked(position, position.Y, actorHeight)) return true;

        // A descending capsule can overlap the side of the ledge it just left. Moving it
        // back to its last mid-air pose makes gravity and rollback fight forever. Find the
        // nearest horizontal clearance instead and let the existing vertical step continue.
        const int directions = 8;
        const int rings = MaximumDepenetrationProbes / directions;
        for (int ring = 0; ring < rings; ring++)
        for (int direction = 0; direction < directions; direction++)
        {
            _depenetrationProbes++;
            float radius = 0.05f + (ActorRadius * 2 - 0.05f) * ring / (rings - 1);
            float angle = direction * MathF.Tau / directions;
            float x = position.X + MathF.Cos(angle) * radius;
            float z = position.Z + MathF.Sin(angle) * radius;
            if (!TryGetGroundHeight(x, z, position.Y, -MaximumFallDistance,
                    SurfaceContactTolerance, out float candidateGround)) continue;
            var candidate = new Vector3(x, position.Y, z);
            if (IsPositionBlocked(candidate, position.Y, actorHeight)) continue;
            resolved = candidate;
            resolvedGroundY = candidateGround;
            return true;
        }
        return false;
    }

    public bool TryRaycast(Vector3 origin, Vector3 direction, float maximumDistance,
        out float distance)
    {
        distance = maximumDistance;
        if (!float.IsFinite(maximumDistance) || maximumDistance <= 0
            || direction.LengthSquared() < 1e-8f) return false;
        direction = Vector3.Normalize(direction);
        bool hit = false;
        var candidates = RentRayCandidates(origin, direction, maximumDistance);
        try
        {
            foreach (int index in candidates)
            {
                if (!RayIntersectsTriangle(origin, direction, _triangles[index].Triangle,
                        out float candidate)
                    || candidate < 0.01f || candidate >= distance) continue;
                distance = candidate;
                hit = true;
            }
        }
        finally
        {
            ReturnCandidates(candidates);
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
        => TryGetGroundHeight(x, z, referenceY, minimumDelta, maximumDelta, out height, out _);

    private bool TryGetGroundHeight(float x, float z, float referenceY, float minimumDelta,
        float maximumDelta, out float height, out int supportIndex)
    {
        bool found = false;
        float bestHeight = float.NegativeInfinity;
        height = referenceY;
        supportIndex = -1;
        float minimumY = referenceY + minimumDelta - 0.001f;
        float maximumY = referenceY + maximumDelta + 0.001f;
        var candidates = RentCandidates(new Vector3(x - 0.001f, minimumY, z - 0.001f),
            new Vector3(x + 0.001f, maximumY, z + 0.001f));
        try
        {
            foreach (int index in candidates)
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
                supportIndex = index;
            }
        }
        finally
        {
            ReturnCandidates(candidates);
        }
        return found;
    }

    private bool TrySlideGround(Vector3 current, Vector3 increment, float currentGroundY,
        float actorHeight, out Vector3 resolved, out float groundY)
    {
        resolved = current;
        groundY = currentGroundY;
        var remaining = increment;
        for (int iteration = 0; iteration < MaximumSlideIterations; iteration++)
        {
            var candidate = resolved + remaining;
            if (TryCandidate(candidate, groundY, actorHeight, remaining,
                    out var next, out float nextGround, out var normal))
            {
                resolved = next;
                groundY = nextGround;
                return true;
            }
            if (normal.LengthSquared() < 1e-8f) return false;
            remaining = ProjectAlongSurface(remaining, normal);
            if (new Vector2(remaining.X, remaining.Z).LengthSquared() < 1e-8f) return false;
        }
        return false;
    }

    private bool TrySlideAir(Vector3 current, Vector3 increment, float actorHeight,
        bool allowUnsupportedGround, float unsupportedGroundY,
        out Vector3 resolved, out float groundY)
    {
        resolved = current;
        groundY = current.Y;
        var remaining = increment;
        for (int iteration = 0; iteration < MaximumSlideIterations; iteration++)
        {
            var candidate = resolved + remaining;
            if (TryAirCandidate(candidate, actorHeight, remaining, allowUnsupportedGround,
                    unsupportedGroundY,
                    out float nextGround, out var normal))
            {
                resolved = candidate;
                groundY = nextGround;
                return true;
            }
            if (normal.LengthSquared() < 1e-8f) return false;
            remaining = ProjectAlongSurface(remaining, normal);
            if (new Vector2(remaining.X, remaining.Z).LengthSquared() < 1e-8f) return false;
        }
        return false;
    }

    private static Vector3 ProjectAlongSurface(Vector3 movement, Vector2 normal)
    {
        if (normal.LengthSquared() < 1e-8f) return Vector3.Zero;
        normal = Vector2.Normalize(normal);
        var planar = new Vector2(movement.X, movement.Z);
        planar -= normal * Vector2.Dot(planar, normal);
        return new Vector3(planar.X, movement.Y, planar.Y);
    }

    private bool TryCandidate(Vector3 position, float currentGroundY, float actorHeight,
        Vector3 movement, out Vector3 resolved, out float groundY, out Vector2 blockingNormal)
    {
        blockingNormal = default;
        if (!TryGetStepGroundHeight(position, movement, currentGroundY, out groundY))
        {
            resolved = default;
            return false;
        }
        groundY = PreserveMinorGroundContinuity(currentGroundY, groundY);
        if (TryGetBlockingNormal(position, groundY, actorHeight, movement,
                out blockingNormal))
        {
            resolved = default;
            return false;
        }
        resolved = position;
        return true;
    }

    private static float PreserveMinorGroundContinuity(float currentGroundY,
        float candidateGroundY)
    {
        float delta = candidateGroundY - currentGroundY;
        if (MathF.Abs(delta) > MaximumGroundContinuityHeight) return candidateGroundY;
        return currentGroundY + Math.Clamp(delta,
            -MaximumGroundContinuityChangePerSweep,
            MaximumGroundContinuityChangePerSweep);
    }

    private bool TryGetStepGroundHeight(Vector3 position, Vector3 movement,
        float currentGroundY, out float groundY)
    {
        bool found = TryGetGroundHeight(position.X, position.Z, currentGroundY,
            -MaximumStepDown, MaximumStepHeight, out groundY, out int centerSupport);
        var planarMovement = new Vector2(movement.X, movement.Z);
        if (planarMovement.LengthSquared() < 1e-8f) return found;
        planarMovement = Vector2.Normalize(planarMovement);
        float footprintProbeRadius = ActorRadius - SurfaceContactTolerance * 0.25f;

        // Footprint contact can smooth a transition between two reachable supports, but
        // it must not keep an actor grounded after its centre has left a ledge entirely.
        // Imported terrain can also have cracks narrower than the capsule. Bridge one only
        // when both ends of the travel footprint remain supported; a real ledge or wider
        // hole has at most one such support and still transitions to an airborne fall.
        if (!found)
        {
            if (TryGetNearbyWalkableGroundHeight(position.X, position.Z, currentGroundY,
                    out groundY))
                return true;
            var perpendicular = new Vector2(-planarMovement.Y, planarMovement.X);
            Span<Vector2> bridgeAxes =
            [
                planarMovement,
                perpendicular,
                Vector2.Normalize(planarMovement + perpendicular),
                Vector2.Normalize(planarMovement - perpendicular),
            ];
            Span<float> bridgeProbeRadii =
            [
                ActorRadius + MaximumSweepStep + 0.01f,
                0.8f,
                MaximumImportedGroundGapProbeRadius,
            ];
            foreach (var axis in bridgeAxes)
            {
                foreach (float radius in bridgeProbeRadii)
                {
                    bool hasFront = TryGetGroundHeight(
                        position.X + axis.X * radius,
                        position.Z + axis.Y * radius,
                        currentGroundY, -MaximumStepDown, MaximumStepHeight,
                        out float frontGround, out _);
                    bool hasRear = TryGetGroundHeight(
                        position.X - axis.X * radius,
                        position.Z - axis.Y * radius,
                        currentGroundY, -MaximumStepDown, MaximumStepHeight,
                        out float rearGround, out _);
                    if (!hasFront || !hasRear
                        || MathF.Abs(frontGround - rearGround) > MaximumStepHeight)
                        continue;
                    groundY = MathF.Max(frontGround, rearGround);
                    return true;
                }
            }
            return false;
        }

        // The capsule footprint along its travel corridor participates in a step. The
        // front edge discovers an ascending tread before the centre penetrates its riser;
        // the rear edge retains the old tread until the capsule has completely crossed it. Using
        // only the front edge made narrow walls alternate between two support heights and
        // made the lowered capsule collide with stair risers while descending.
        Span<Vector2> directions =
        [
            planarMovement,
            -planarMovement,
        ];
        foreach (var direction in directions)
        {
            float probeX = position.X + direction.X * footprintProbeRadius;
            float probeZ = position.Z + direction.Y * footprintProbeRadius;
            if (!TryGetGroundHeight(probeX, probeZ, currentGroundY,
                    -MaximumStepDown, MaximumStepHeight, out float footprintGround,
                    out int footprintSupport))
                continue;
            if (footprintGround > groundY
                && !SupportsSamePlane(centerSupport, footprintSupport))
            {
                groundY = footprintGround;
            }
        }
        return found;
    }

    private bool TryGetNearbyWalkableGroundHeight(float x, float z, float referenceY,
        out float groundY)
    {
        groundY = referenceY;
        bool found = false;
        float bestHeight = float.NegativeInfinity;
        float radiusSquared = MaximumTerrainSupportSnapDistance
            * MaximumTerrainSupportSnapDistance;
        var point = new Vector3(x, referenceY, z);
        var candidates = RentCandidates(
            new Vector3(x - MaximumTerrainSupportSnapDistance,
                referenceY - MaximumStepDown, z - MaximumTerrainSupportSnapDistance),
            new Vector3(x + MaximumTerrainSupportSnapDistance,
                referenceY + MaximumStepHeight, z + MaximumTerrainSupportSnapDistance));
        try
        {
            foreach (int index in candidates)
            {
                var candidate = _triangles[index];
                if (!candidate.TerrainSeamSupport) continue;
                var closest = ClosestPoint(point, candidate.Triangle);
                float dx = closest.X - x;
                float dz = closest.Z - z;
                if (dx * dx + dz * dz > radiusSquared) continue;
                float delta = closest.Y - referenceY;
                if (delta < -MaximumStepDown || delta > MaximumStepHeight
                    || closest.Y <= bestHeight)
                    continue;
                found = true;
                bestHeight = closest.Y;
                groundY = closest.Y;
            }
            return found;
        }
        finally
        {
            ReturnCandidates(candidates);
        }
    }

    private bool SupportsSamePlane(int firstIndex, int secondIndex)
    {
        if (firstIndex < 0 || secondIndex < 0) return false;
        if (firstIndex == secondIndex) return true;
        var first = _triangles[firstIndex];
        var second = _triangles[secondIndex];
        if (MathF.Abs(Vector3.Dot(first.Normal, second.Normal)) < 0.999f) return false;
        float firstPlane = Vector3.Dot(first.Normal, first.Triangle.A);
        float secondPlane = Vector3.Dot(first.Normal, second.Triangle.A);
        return MathF.Abs(firstPlane - secondPlane) < 0.01f;
    }

    private bool TryAirCandidate(Vector3 position, float actorHeight, Vector3 movement,
        bool allowUnsupportedGround, float unsupportedGroundY,
        out float groundY, out Vector2 blockingNormal)
    {
        blockingNormal = default;
        // Horizontal movement in the air must not require support at every intermediate
        // X/Z sample. Imported arenas commonly have narrow cracks between otherwise
        // adjacent terrain strips; treating "no ground directly below this sample" as a
        // wall froze the capsule for the whole jump arc. Keep collision authoritative,
        // but allow the actor to cross unsupported space and reacquire a landing surface
        // on a later sweep sample.
        if (!TryGetGroundHeight(position.X, position.Z, position.Y, -MaximumFallDistance,
                SurfaceContactTolerance, out groundY))
        {
            if (!allowUnsupportedGround) return false;
            // The caller owns the last authoritative support. Reusing it keeps a narrow
            // crack traversable without recursively lowering the support plane on every
            // airborne tick and eventually dropping actors far below the arena.
            groundY = float.IsFinite(unsupportedGroundY)
                ? unsupportedGroundY
                : position.Y - MaximumFallDistance;
        }
        if (TryGetBlockingNormal(position, position.Y, actorHeight, movement,
                out blockingNormal))
            return false;
        return true;
    }

    private bool IsBlocked(Vector3 position, float groundY, float actorHeight) =>
        TryGetBlockingNormal(position, groundY, actorHeight, Vector3.Zero, out _);

    private bool TryGetBlockingNormal(Vector3 position, float groundY, float actorHeight,
        Vector3 movement, out Vector2 blockingNormal)
    {
        blockingNormal = default;
        float baseY = MathF.Max(position.Y, groundY);
        float height = Math.Clamp(actorHeight, ProneHeight, StandingHeight);
        bool blocked = false;
        float bestScore = float.NegativeInfinity;
        var candidates = RentCandidates(
            new Vector3(position.X - ActorRadius, baseY, position.Z - ActorRadius),
            new Vector3(position.X + ActorRadius, baseY + height,
                position.Z + ActorRadius));
        try
        {
            foreach (int index in candidates)
            {
                if (IsNonBlockingSurface(_triangles[index], position.X, position.Z, baseY))
                    continue;
                if (IntersectsCapsule(position.X, position.Z, baseY, height,
                        _triangles[index].Triangle))
                    SelectBlockingNormal(_triangles[index], movement, ref blocked,
                        ref bestScore, ref blockingNormal);
            }
        }
        finally
        {
            ReturnCandidates(candidates);
        }
        return blocked;
    }

    private static void SelectBlockingNormal(SurfaceTriangle triangle, Vector3 movement,
        ref bool blocked, ref float bestScore, ref Vector2 blockingNormal)
    {
        blocked = true;
        var horizontal = new Vector2(triangle.Normal.X, triangle.Normal.Z);
        if (horizontal.LengthSquared() < 1e-8f) return;
        horizontal = Vector2.Normalize(horizontal);
        var planarMovement = new Vector2(movement.X, movement.Z);
        float score = planarMovement.LengthSquared() < 1e-8f
            ? 0
            : MathF.Abs(Vector2.Dot(Vector2.Normalize(planarMovement), horizontal));
        if (score <= bestScore) return;
        bestScore = score;
        blockingNormal = horizontal;
    }

    private bool IsNonBlockingSurface(SurfaceTriangle triangle, float x, float z,
        float baseY)
    {
        if (triangle.MaxY <= baseY + SurfaceContactTolerance)
            return true;
        if (!triangle.Walkable)
        {
            // Some AC stairs use treads which are shallower than the player capsule and
            // back every riser down to a retained base floor. A capsule standing on one
            // tread then overlaps the neighboring vertical face even though the next
            // tread is a valid reachable support. Low terrain/decorative faces within the
            // normal auto-step height must not become invisible capsule walls. Taller
            // fences and ordinary walls remain solid.
            float rise = triangle.MaxY - baseY;
            return rise <= MaximumTerrainSeamRise + SurfaceContactTolerance;
        }
        // Nearby floor triangles often share the same spatial cell but do not contain the
        // actor's exact X/Z point. They are not barriers. More importantly, a walkable top
        // inside the normal auto-step range is support, never a capsule wall. Validation can
        // run for one tick with the previous support height while crossing thin imported
        // paint/kerb layers; treating the new top as a ceiling restores the last safe pose on
        // every tick and permanently traps the actor at an otherwise trivial ground detail.
        return !TryHeight(triangle.Triangle, x, z, out float height)
            || height <= baseY + MaximumStepHeight + SurfaceContactTolerance;
    }

    private bool HasWalkableStepTop(float topY, float x, float z)
    {
        float maximumDistance = ActorRadius + SurfaceContactTolerance;
        float maximumDistanceSquared = maximumDistance * maximumDistance;
        var point = new Vector3(x, topY, z);
        var candidates = RentCandidates(
            new Vector3(x - maximumDistance, topY - SurfaceContactTolerance,
                z - maximumDistance),
            new Vector3(x + maximumDistance, topY + SurfaceContactTolerance,
                z + maximumDistance));
        try
        {
            foreach (int index in candidates)
            {
                var candidate = _triangles[index];
                if (!candidate.Walkable) continue;
                var closest = ClosestPoint(point, candidate.Triangle);
                if (MathF.Abs(closest.Y - topY) > SurfaceContactTolerance) continue;
                float dx = closest.X - x;
                float dz = closest.Z - z;
                if (dx * dx + dz * dz <= maximumDistanceSquared) return true;
            }
            return false;
        }
        finally
        {
            ReturnCandidates(candidates);
        }
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

    private List<int> RentCandidates(Vector3 minimum, Vector3 maximum)
    {
        var candidates = _candidatePool.Count > 0 ? _candidatePool.Pop() : [];
        _bvh.Collect(minimum, maximum, candidates);
        _broadphaseQueries++;
        _broadphaseCandidates += candidates.Count;
        _broadphaseMaximumCandidates = Math.Max(_broadphaseMaximumCandidates,
            candidates.Count);
        return candidates;
    }

    private List<int> RentRayCandidates(Vector3 origin, Vector3 direction,
        float maximumDistance)
    {
        var candidates = _candidatePool.Count > 0 ? _candidatePool.Pop() : [];
        _bvh.CollectRay(origin, direction, maximumDistance, candidates);
        _broadphaseQueries++;
        _broadphaseCandidates += candidates.Count;
        _broadphaseMaximumCandidates = Math.Max(_broadphaseMaximumCandidates,
            candidates.Count);
        return candidates;
    }

    private void ReturnCandidates(List<int> candidates)
    {
        candidates.Clear();
        _candidatePool.Push(candidates);
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

    private readonly record struct SurfaceTriangle(Kn5Triangle Triangle, Vector3 Normal,
        float MinX, float MaxX, float MinZ, float MaxZ, float MinY, float MaxY,
        bool Walkable, bool TerrainSeamSupport);
}

internal readonly record struct FpsSurfaceDiagnostics(int BroadphaseQueries,
    long BroadphaseCandidates, int MaximumCandidatesPerQuery, int DepenetrationProbes);
