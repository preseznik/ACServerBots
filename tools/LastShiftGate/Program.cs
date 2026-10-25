using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using AssettoServer.Server.Ai.Physics;
using AssettoServer.Server.Fps;

const float ActorHeight = 1.8f;
const float PositionTolerance = 0.12f;
var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
var options = new Dictionary<string, string>(StringComparer.Ordinal);
for (int i = 0; i < args.Length; i += 2)
{
    if (i + 1 == args.Length || args[i] is not ("--ac-root" or "--output" or "--probes")
        || !options.TryAdd(args[i], args[i + 1]))
        throw new ArgumentException("Usage: LastShiftGate --ac-root <staged AC root> --output <directory> --probes <layout.json>");
}
if (options.Count != 3)
    throw new ArgumentException("Required: --ac-root <staged AC root> --output <directory> --probes <layout.json>");

string output = Path.GetFullPath(options["--output"]);
string probeFile = Path.GetFullPath(options["--probes"]);
Directory.CreateDirectory(output);
var stopwatch = Stopwatch.StartNew();
var checks = new List<GateCheck>();
object? preparation = null;
string? error = null;
var models = new List<object>();
void Check(string name, bool passed, object? detail = null) => checks.Add(new(name, passed, detail));

try
{
    using var document = JsonDocument.Parse(File.ReadAllText(probeFile));
    var layout = document.RootElement;
    var root = layout;
    if (root.TryGetProperty("probes", out var nested)) root = nested;
    var probes = root.Deserialize<ProbeManifest>(jsonOptions)
        ?? throw new InvalidDataException("Missing probe manifest");
    if (probes.TrackId != "last_shift" || probes.ExpectedSpawnCount != 12
        || probes.WalkRoutes.Length == 0 || probes.ShotProbes.Length == 0
        || probes.StandingPoints.Length == 0)
        throw new InvalidDataException("Last Shift probes require trackId=last_shift, expectedSpawnCount=12, walkRoutes, shotProbes and standingPoints");

    string acRoot = Path.GetFullPath(options["--ac-root"]);
    string trackRoot = Path.Combine(acRoot, "content", "tracks", probes.TrackId);
    var namedTransforms = new List<Kn5NamedTransform>();
    var fullPhysicalTriangles = new List<Kn5Triangle>();
    foreach (string model in RacePhysicsAssetBuilder.ReadModelFiles(
                 Path.Combine(trackRoot, "models.ini"), trackRoot, probes.TrackId))
    {
        models.Add(new { file = Path.GetFileName(model), sha256 = HashFile(model) });
        var data = Kn5CollisionReader.Read(model, RacePhysicsAssetBuilder.IsPhysicalTrackMesh);
        namedTransforms.AddRange(data.NamedTransforms);
        fullPhysicalTriangles.AddRange(data.Triangles);
    }

    string arenaFile = Path.Combine(output, "fps_arena.json");
    string geometryFile = Path.Combine(output, "fps_geometry.bin");
    string navigationFile = Path.Combine(output, "fps_navigation.bin");
    preparation = FpsArenaAssetBuilder.Build(acRoot, probes.TrackId, null,
        arenaFile, geometryFile, navigationFile);
    var arena = JsonSerializer.Deserialize<FpsArenaAsset>(File.ReadAllText(arenaFile), jsonOptions)!;
    var geometry = FpsArenaGeometryAsset.Load(geometryFile);
    var surface = new FpsArenaSurface(geometry.Triangles, geometry.MovementBoxes);
    var navigation = FpsArenaNavigationAsset.Load(navigationFile);
    var boundary = arena.PlayableBoundary.Select(p => new Vector3(p.X, p.Y, p.Z)).ToArray();
    Check("authored playable boundary exists", boundary.Length >= 3);
    Check("expected spawn count", arena.SpawnPoints.Count == probes.ExpectedSpawnCount,
        new { expected = probes.ExpectedSpawnCount, actual = arena.SpawnPoints.Count });
    Check("all spawns share primary navigation component",
        arena.Navigation.ConnectedSpawnCount == arena.SpawnPoints.Count, arena.Navigation);
    Check("navigation stays within playable boundary", navigation.Nodes.All(n =>
        FpsPlayableArea.Contains(boundary, n.Position.X, n.Position.Z)));
    var starts = Markers("AC_START_");
    var pits = Markers("AC_PIT_");
    var crews = Markers("AC_CREW_");
    var allPhysical = new FpsArenaSurface(fullPhysicalTriangles);
    foreach (var carrier in pits.Concat(crews))
    {
        Vector3 position = carrier.Transform.Translation;
        float separation = DistanceFromBoundary(position);
        Check("remote carrier: " + carrier.Name,
            !FpsPlayableArea.Contains(boundary, position.X, position.Z) && separation >= 150,
            new { position = Coordinates(position), separation });
        if (carrier.Name.StartsWith("AC_PIT_", StringComparison.Ordinal))
            Check("carrier support: " + carrier.Name,
                allPhysical.TryGetGroundHeight(position.X, position.Z, position.Y, out float groundY)
                && MathF.Abs(groundY - position.Y) < 0.3f,
                new { groundY });
    }
    for (int i = 0; i < starts.Length; i++)
    for (int j = i + 1; j < starts.Length; j++)
        Check($"separate starts: {starts[i].Name}/{starts[j].Name}",
            Vector3.Distance(starts[i].Transform.Translation, starts[j].Transform.Translation) >= 1);
    using (var ui = JsonDocument.Parse(File.ReadAllText(Path.Combine(trackRoot, "ui", "ui_track.json"))))
        Check("UI exposes 12 carrier slots", ui.RootElement.GetProperty("pitboxes").ToString() == "12");

    if (layout.TryGetProperty("spawns", out var authoredSpawns))
    {
        var expected = authoredSpawns.Deserialize<float[][]>(jsonOptions)!;
        Check("authored spawn count", expected.Length == 12);
        for (int i = 0; i < expected.Length; i++)
        {
            var marker = starts.Where(n => n.Name == $"AC_START_{i}").ToArray();
            if (marker.Length != 1 || expected[i].Length != 4)
            {
                Check($"authored start {i}", false, "Missing/duplicate marker or invalid [x,y,z,yaw] specification");
                continue;
            }
            var pose = RaceGridPose.FromMatrix(marker[0].Transform);
            Vector3 target = new(expected[i][0], expected[i][1], expected[i][2]);
            Vector3 forward = Vector3.Transform(Vector3.UnitZ, pose.Orientation);
            float yaw = MathF.Atan2(forward.X, forward.Z);
            float desiredYaw = expected[i][3] * MathF.PI / 180;
            float yawDifference = MathF.Abs(MathF.IEEERemainder(yaw - desiredYaw, 2 * MathF.PI));
            Check($"authored start {i}", Vector3.Distance(pose.Position, target) < 0.01f && yawDifference < 0.01f,
                new { position = Coordinates(pose.Position), expected = Coordinates(target), yaw, desiredYaw });
        }
    }
    else Check("authored spawn coordinates supplied", false);

    if (layout.TryGetProperty("movementVolumes", out var authoredVolumes))
    {
        var expected = authoredVolumes.Deserialize<VolumeManifest[]>(jsonOptions)!;
        Check("authored movement volume count", expected.Length == geometry.MovementBoxes.Count,
            new { expected = expected.Length, actual = geometry.MovementBoxes.Count });
        for (int i = 0; i < Math.Min(expected.Length, geometry.MovementBoxes.Count); i++)
        {
            var box = geometry.MovementBoxes[i];
            float yaw = expected[i].Yaw * MathF.PI / 180;
            var size = Vector(expected[i].Size);
            var halfX = new Vector3(MathF.Cos(yaw) * size.X / 2, 0, -MathF.Sin(yaw) * size.X / 2);
            var halfZ = new Vector3(MathF.Sin(yaw) * size.Z / 2, 0, MathF.Cos(yaw) * size.Z / 2);
            Check($"authored movement volume {i}", Vector3.Distance(box.Center, Vector(expected[i].Center)) < 0.01f
                && Vector3.Distance(box.HalfX, halfX) < 0.01f
                && Vector3.Distance(box.HalfY, Vector3.UnitY * size.Y / 2) < 0.01f
                && Vector3.Distance(box.HalfZ, halfZ) < 0.01f);
        }
    }
    else Check("authored movement volumes supplied", false);
    Check("roof and bus exclusion volumes present", geometry.MovementBoxes.Count >= 8, geometry.MovementBoxes.Count);
    for (int i = 0; i < geometry.MovementBoxes.Count; i++)
    {
        var box = geometry.MovementBoxes[i];
        int inside = navigation.Nodes.Count(n => box.ContainsCapsule(n.Position, ActorHeight, FpsArenaSurface.ActorRadius));
        int crossing = navigation.Nodes.Sum(n => n.Edges.Count(edge => box.IntersectsSegment(
            n.Position, navigation.Nodes[edge.TargetNode].Position, ActorHeight, FpsArenaSurface.ActorRadius)));
        Check($"exclusion {i}: no navigation nodes or crossing edges", inside == 0 && crossing == 0,
            new { inside, crossing });
    }

    Kn5NamedTransform[] Markers(string prefix)
    {
        var found = namedTransforms.Where(n => n.Name.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        bool contiguous = found.Length == 12 && Enumerable.Range(0, 12)
            .All(i => found.Count(n => n.Name == prefix + i) == 1);
        Check(prefix + "0..11 unique contiguous markers", contiguous, found.Select(n => n.Name).ToArray());
        return found;
    }

    float DistanceFromBoundary(Vector3 position)
    {
        if (boundary.Length < 3) return 0;
        var point = new Vector2(position.X, position.Z);
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < boundary.Length; i++)
        {
            var first = new Vector2(boundary[i].X, boundary[i].Z);
            var next = boundary[(i + 1) % boundary.Length];
            var delta = new Vector2(next.X, next.Z) - first;
            float t = Math.Clamp(Vector2.Dot(point - first, delta) / delta.LengthSquared(), 0, 1);
            nearest = MathF.Min(nearest, Vector2.Distance(point, first + delta * t));
        }
        return nearest;
    }

    var spawnPositions = arena.SpawnPoints.Select(s =>
        new Vector3(s.Position.X, s.Position.Y, s.Position.Z)).ToArray();
    for (int i = 0; i < spawnPositions.Length; i++)
    {
        Standing($"spawn {i}", spawnPositions[i]);
        if (i > 0) Navigation($"spawn 0 to {i}", spawnPositions[0], spawnPositions[i], true);
    }
    foreach (var point in probes.StandingPoints) Standing(point.Name, Vector(point.Position));
    foreach (var route in probes.WalkRoutes)
    {
        if (route.Points.Length < 2) throw new InvalidDataException($"Route {route.Name} needs at least two points");
        Vector3[] points = route.Points.Select(Vector).ToArray();
        Walk(route.Name, points);
        if (route.Bidirectional) Walk(route.Name + " reverse", points.Reverse().ToArray());
        Navigation(route.Name, points[0], points[^1], route.Bidirectional);
    }
    foreach (var shot in probes.ShotProbes)
    {
        Vector3 from = Vector(shot.From);
        Vector3 to = Vector(shot.To);
        float length = Vector3.Distance(from, to);
        if (length < 0.01f) throw new InvalidDataException($"Shot {shot.Name} has zero length");
        bool hit = surface.TryRaycast(from, (to - from) / length, length, out float distance);
        Check("shot: " + shot.Name, hit == shot.Blocked,
            new { expectedBlocked = shot.Blocked, hit, distance = hit ? (float?)distance : null, length });
    }
    foreach (var movement in probes.BlockedMoves)
    {
        Vector3 from = Vector(movement.From);
        Vector3 to = Vector(movement.To);
        if (!float.IsFinite(movement.Height) || movement.Height is < 0.5f or > 2.5f
            || Vector3.Distance(from, to) < 0.5f)
            throw new InvalidDataException($"Blocked movement {movement.Name} has invalid dimensions");
        bool supported = surface.TryGetGroundHeight(from.X, from.Z, from.Y, out float groundY);
        bool clearStart = supported && MathF.Abs(groundY - from.Y) <= PositionTolerance
            && !surface.IsPositionBlocked(from, groundY, movement.Height);
        bool moved = surface.TryResolveMove(from, to, groundY, movement.Height,
            out var resolved, out float nextGround, out bool lostSupport);
        // Falling off an unguarded edge must not count as a successful barrier.
        bool stopped = Vector3.Distance(resolved, to) > PositionTolerance && !lostSupport
            && MathF.Abs(nextGround - from.Y) <= PositionTolerance;
        Check("blocked movement: " + movement.Name, clearStart && stopped,
            new { clearStart, moved, desired = Coordinates(to), resolved = Coordinates(resolved),
                groundY, nextGround, lostSupport });
    }

    void Standing(string name, Vector3 point)
    {
        bool ground = surface.TryGetGroundHeight(point.X, point.Z, point.Y, out float groundY);
        bool blocked = surface.IsPositionBlocked(point with { Y = groundY }, groundY, ActorHeight);
        int node = navigation.FindClosestNode(point, maximumDistance: 1);
        bool close = node >= 0 && Vector3.Distance(navigation.Nodes[node].Position, point) < 1;
        Check("standing: " + name, ground && MathF.Abs(groundY - point.Y) <= PositionTolerance
            && !blocked && close && navigation.Nodes[node].Component == navigation.PrimaryComponent,
            new { position = Coordinates(point), ground, groundY, blocked, navigationNode = node });
    }

    void Navigation(string name, Vector3 from, Vector3 to, bool bothDirections)
    {
        int fromNode = navigation.FindClosestNode(from, maximumDistance: 1);
        int toNode = navigation.FindClosestNode(to, maximumDistance: 1);
        bool close = fromNode >= 0 && toNode >= 0
            && Vector3.Distance(navigation.Nodes[fromNode].Position, from) < 1
            && Vector3.Distance(navigation.Nodes[toNode].Position, to) < 1;
        var forward = navigation.FindPath(from, to);
        var reverse = bothDirections ? navigation.FindPath(to, from) : [];
        bool forwardWalk = close && HasWalkPath(fromNode, toNode);
        bool reverseWalk = close && (!bothDirections || HasWalkPath(toNode, fromNode));
        Check("navigation: " + name, close && forward.Count > 0 && (!bothDirections || reverse.Count > 0)
            && forwardWalk && reverseWalk,
            new { forwardSteps = forward.Count, reverseSteps = reverse.Count,
                fromNode, toNode, forwardWalk, reverseWalk,
                forwardTraversalSteps = forward.Count(s => s.Kind != FpsNavigationLinkKind.Walk),
                reverseTraversalSteps = reverse.Count(s => s.Kind != FpsNavigationLinkKind.Walk) });
    }

    bool HasWalkPath(int from, int to)
    {
        var visited = new HashSet<int> { from };
        var pending = new Queue<int>();
        pending.Enqueue(from);
        while (pending.TryDequeue(out int current))
        {
            if (current == to) return true;
            foreach (var edge in navigation.Nodes[current].Edges)
                if (edge.Kind == FpsNavigationLinkKind.Walk && visited.Add(edge.TargetNode))
                    pending.Enqueue(edge.TargetNode);
        }
        return false;
    }

    void Walk(string name, IReadOnlyList<Vector3> points)
    {
        Vector3 current = points[0];
        bool supported = surface.TryGetGroundHeight(current.X, current.Z, current.Y, out float groundY);
        if (!supported || MathF.Abs(groundY - current.Y) > PositionTolerance)
        {
            Check("walk: " + name, false, new { reason = "start has no matching floor", start = Coordinates(current), groundY });
            return;
        }
        current.Y = groundY;
        for (int i = 1; i < points.Count; i++)
        {
            bool moved = surface.TryResolveMove(current, points[i], groundY, ActorHeight,
                out var resolved, out float nextGround, out bool lostSupport);
            bool clear = moved && !lostSupport && Vector3.Distance(resolved, points[i]) <= PositionTolerance
                && !surface.IsPositionBlocked(resolved, nextGround, ActorHeight);
            Check($"walk: {name} segment {i}", clear,
                new { desired = Coordinates(points[i]), resolved = Coordinates(resolved), lostSupport });
            if (!clear) return;
            current = resolved;
            groundY = nextGround;
        }
    }
}
catch (Exception exception)
{
    error = exception.ToString();
    Check("preparation and probe execution", false, exception.Message);
}

bool passed = checks.Count > 0 && checks.All(check => check.Passed);
var report = new
{
    passed,
    scope = "Offline KN5 preparation, standing capsule, navigation and shot collision checks; no in-game rendering or human playtest acceptance.",
    createdUtc = DateTimeOffset.UtcNow,
    elapsedSeconds = stopwatch.Elapsed.TotalSeconds,
    probes = new { file = probeFile, sha256 = File.Exists(probeFile) ? HashFile(probeFile) : null },
    models,
    preparation,
    checkCount = checks.Count,
    failedChecks = checks.Count(check => !check.Passed),
    checks,
    error
};
File.WriteAllText(Path.Combine(output, "validation.json"), JsonSerializer.Serialize(report, jsonOptions));
Console.WriteLine($"Last Shift: {checks.Count(c => c.Passed)}/{checks.Count} checks passed; {Path.Combine(output, "validation.json")}");
foreach (var check in checks.Where(c => !c.Passed)) Console.Error.WriteLine($"FAIL: {check.Name} {JsonSerializer.Serialize(check.Detail)}");
if (error != null) Console.Error.WriteLine(error);
return passed ? 0 : 1;

static Vector3 Vector(float[] point) => point.Length == 3 && point.All(float.IsFinite)
    ? new(point[0], point[1], point[2]) : throw new InvalidDataException("Coordinates must contain three finite numbers: [x,y,z]");
static float[] Coordinates(Vector3 point) => [point.X, point.Y, point.Z];
static string HashFile(string path)
{
    using var file = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
}

internal sealed record GateCheck(string Name, bool Passed, object? Detail);
internal sealed class ProbeManifest
{
    public string TrackId { get; init; } = "";
    public int ExpectedSpawnCount { get; init; }
    public WalkRoute[] WalkRoutes { get; init; } = [];
    public ShotProbe[] ShotProbes { get; init; } = [];
    public StandingPoint[] StandingPoints { get; init; } = [];
    public BlockedMove[] BlockedMoves { get; init; } = [];
}
internal sealed record WalkRoute(string Name, float[][] Points, bool Bidirectional = true);
internal sealed record ShotProbe(string Name, float[] From, float[] To, bool Blocked);
internal sealed record StandingPoint(string Name, float[] Position);
internal sealed record BlockedMove(string Name, float[] From, float[] To, float Height = 1.8f);
internal sealed record VolumeManifest(float[] Center, float[] Size, float Yaw);
