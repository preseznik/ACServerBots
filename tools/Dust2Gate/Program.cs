using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using AssettoServer.Server.Ai.Physics;
using AssettoServer.Server.Fps;

if (args.Length != 3) throw new ArgumentException("Dust2Gate <staged AC root> <output directory> <layout.json>");
string acRoot = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
var json = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
var layout = JsonSerializer.Deserialize<Layout>(File.ReadAllText(args[2]), json)!;
var checks = new List<object>();
int failures = 0;
void Check(string name, bool pass, object? detail = null)
{
    checks.Add(new { name, pass, detail });
    if (!pass) { failures++; Console.Error.WriteLine($"FAIL {name}: {JsonSerializer.Serialize(detail)}"); }
}
string track = Path.Combine(acRoot, "content", "tracks", "css_dust2");
var files = RacePhysicsAssetBuilder.ReadModelFiles(Path.Combine(track, "models.ini"), track, "css_dust2").ToArray();
var models = files.Select(path => new { file = Path.GetFileName(path), sha256 = Hash(path) }).ToArray();
Console.WriteLine("Preparing Dust II from KN5 with the current server geometry/navigation code...");
var preparation = FpsArenaAssetBuilder.Build(acRoot, "css_dust2", null,
    Path.Combine(output, "fps_arena.json"), Path.Combine(output, "fps_geometry.bin"),
    Path.Combine(output, "fps_navigation.bin"), boundsPaddingMeters: 65);
var arena = JsonSerializer.Deserialize<FpsArenaAsset>(File.ReadAllText(Path.Combine(output, "fps_arena.json")), json)!;
var geometry = FpsArenaGeometryAsset.Load(Path.Combine(output, "fps_geometry.bin"));
var surface = new FpsArenaSurface(geometry.Triangles, geometry.MovementBoxes);
// Dust II's 20 cm treads need a denser grid than the generic 60 cm default.
// This is map-owned prepared data; it does not change global movement settings.
var refined = FpsArenaNavigationBuilder.Build(surface, arena.BoundsMin, arena.BoundsMax, arena.SpawnPoints,
    arena.PlayableBoundary.Select(p => new Vector3(p.X, p.Y, p.Z)).ToArray(), cellSize: .3f);
var nav = refined.Asset;
var stableWalkEdges = new Dictionary<long, bool>();
nav.Save(Path.Combine(output, "fps_navigation.bin"));
var metadata = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "fps_arena.json")))!;
metadata["Navigation"] = JsonSerializer.SerializeToNode(new FpsArenaNavigationSummary(1, nav.CellSize,
    nav.Nodes.Count, nav.ComponentCount, refined.ConnectedSpawnPoints, refined.WalkLinks, refined.TraversalLinks));
File.WriteAllText(Path.Combine(output, "fps_arena.json"), metadata.ToJsonString(json));
arena = metadata.Deserialize<FpsArenaAsset>(json)!;
preparation = preparation with { NavigationNodes = nav.Nodes.Count, NavigationComponents = nav.ComponentCount,
    ConnectedNavigationSpawns = refined.ConnectedSpawnPoints, NavigationWalkLinks = refined.WalkLinks,
    NavigationTraversalLinks = refined.TraversalLinks };
var allTriangles = new List<Kn5Triangle>();
var transforms = new List<Kn5NamedTransform>();
foreach (string path in files)
{
    var model = Kn5CollisionReader.Read(path, RacePhysicsAssetBuilder.IsPhysicalTrackMesh);
    allTriangles.AddRange(model.Triangles); transforms.AddRange(model.NamedTransforms);
}
var fullSurface = new FpsArenaSurface(allTriangles);
var boundary = arena.PlayableBoundary.Select(p => new Vector3(p.X, p.Y, p.Z)).ToArray();
Check("preparation version 5", arena.PreparationVersion == 5);
Check("spawn count matches layout", arena.SpawnPoints.Count == layout.Spawns.Length);
Check("all spawns connected", arena.Navigation.ConnectedSpawnCount == layout.Spawns.Length, arena.Navigation);
Check("collision is separate physical geometry", preparation.SupplementalTriangles == 0 && preparation.PhysicalTriangles > 1000, preparation);
Check("all navigation inside boundary", nav.Nodes.All(n => FpsPlayableArea.Contains(boundary, n.Position.X, n.Position.Z)));
Check("Source player clips present", geometry.MovementBoxes.Count is > 60 and <= 256, geometry.MovementBoxes.Count);
foreach (string prefix in new[] { "AC_START_", "AC_PIT_", "AC_CREW_" })
{
    var found = transforms.Where(t => t.Name.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
    Check(prefix + " unique contiguous", found.Length == layout.Spawns.Length &&
        Enumerable.Range(0, layout.Spawns.Length).All(i => found.Count(t => t.Name == prefix + i) == 1));
}
foreach (var pit in transforms.Where(t => t.Name.StartsWith("AC_PIT_", StringComparison.Ordinal)))
{
    var p = pit.Transform.Translation;
    Check(pit.Name + " remote and supported", p.X > 230 && !FpsPlayableArea.Contains(boundary, p.X, p.Z)
        && fullSurface.TryGetGroundHeight(p.X, p.Z, p.Y, out var y) && MathF.Abs(p.Y - y) < .3f);
}
for (int i = 0; i < arena.SpawnPoints.Count; i++)
{
    var p = arena.SpawnPoints[i].Position;
    Standing("spawn " + i, new(p.X, p.Y, p.Z));
    var authored = layout.Spawns[i];
    Check("spawn marker " + i, Vector3.Distance(transforms.Single(t => t.Name == "AC_START_" + i).Transform.Translation,
        new(authored[0], authored[1], authored[2])) < .001f);
}
for (int i = 0; i < geometry.MovementBoxes.Count; i++)
{
    var box = geometry.MovementBoxes[i];
    Check("clip " + i + " excludes navigation", nav.Nodes.All(n => !box.ContainsCapsule(n.Position, 1.8f, FpsArenaSurface.ActorRadius)));
}
foreach (var point in layout.Landmarks) Standing(point.Key, V(point.Value));
foreach (var route in layout.Routes)
{
    var from = V(layout.Landmarks[route[0]]); var to = V(layout.Landmarks[route[1]]);
    foreach (var (a, b) in new[] { (from, to), (to, from) })
    {
        var path = WalkPath(a, b);
        var fallback = path.Count == 0 ? nav.FindPath(a, b) : [];
        Check("route " + route[0] + " / " + route[1], path.Count > 0, new { steps = path.Count,
            traversal = fallback.Select((s, i) => new { s.Kind, from = P(i > 0 ? fallback[i - 1].Position : a), to = P(s.Position) })
                .Where(s => s.Kind != FpsNavigationLinkKind.Walk).ToArray() });
        var current = a;
        if (surface.TryGetGroundHeight(a.X, a.Z, a.Y, out var ground)) current.Y = ground;
        int walked = 0;
        foreach (var step in path)
        {
            surface.TryResolveMove(current, step, current.Y, 1.8f,
                out var next, out var nextY, out bool lostSupport);
            if (lostSupport || Vector3.Distance(next, step) > .15f) break;
            current = next with { Y = nextY }; walked++;
        }
        Check("walk route " + route[0] + " / " + route[1], path.Count > 0 && walked == path.Count,
            new { walked, steps = path.Count, stopped = P(current), next = walked < path.Count ? P(path[walked]) : null });
    }
}
foreach (var shot in layout.Shots)
{
    var a = V(shot.From); var delta = V(shot.To) - a;
    bool hit = surface.TryRaycast(a, Vector3.Normalize(delta), delta.Length(), out float distance);
    Check("shot " + shot.Name, hit == shot.Blocked, new { hit, expected = shot.Blocked, distance });
}
bool complete = layout.Landmarks.Count >= 8 && layout.Routes.Length >= 8 && layout.Shots.Length >= 8;
Check("route and cover probes supplied", complete);
File.WriteAllText(Path.Combine(output, "navigation-points.json"), JsonSerializer.Serialize(new {
    primary = nav.PrimaryComponent, nodes = nav.Nodes.Select((n, i) => new {
        id = i, p = P(n.Position), component = n.Component, edges = n.Edges.Count }) }, json));
var report = new { passed = failures == 0, runtimeValidated = false,
    scope = "Offline KN5, collision, spawn, movement, navigation and cover checks. AC/CSP acceptance is separate.",
    models, preparation, checkCount = checks.Count, failedChecks = failures, checks };
File.WriteAllText(Path.Combine(output, "validation.json"), JsonSerializer.Serialize(report, json));
Console.WriteLine($"Dust II: {checks.Count - failures}/{checks.Count} checks passed; {output}");
return failures == 0 ? 0 : 1;

List<Vector3> WalkPath(Vector3 from, Vector3 to)
{
    int start = nav.FindClosestNode(from, maximumDistance: 1), end = nav.FindClosestNode(to, maximumDistance: 1);
    if (start < 0 || end < 0) return [];
    var previous = new Dictionary<int, int> { [start] = -1 };
    var queue = new Queue<int>(); queue.Enqueue(start);
    while (queue.TryDequeue(out int current))
    {
        if (current == end)
        {
            var path = new List<Vector3>();
            for (int node = end; node >= 0; node = previous[node]) path.Add(nav.Nodes[node].Position);
            path.Reverse(); return path;
        }
        foreach (var edge in nav.Nodes[current].Edges)
        {
            var delta = nav.Nodes[edge.TargetNode].Position - nav.Nodes[current].Position;
            if (edge.Kind != FpsNavigationLinkKind.Walk) continue;
            // Avoid tolerance-only diagonal corner cuts in this route replay.
            // Require them to arrive even with a 2 mm starting-position change.
            if (MathF.Abs(delta.X) > .01f && MathF.Abs(delta.Z) > .01f)
            {
                long key = ((long)current << 32) | (uint)edge.TargetNode;
                if (!stableWalkEdges.TryGetValue(key, out bool stable))
                {
                    stable = true;
                    foreach (var offset in new[] { new Vector3(.002f, 0, .002f), new Vector3(-.002f, 0, -.002f) })
                    {
                        var target = nav.Nodes[edge.TargetNode].Position;
                        var startPosition = nav.Nodes[current].Position + offset;
                        surface.TryResolveMove(startPosition, target, startPosition.Y, 1.8f,
                            out var resolved, out _, out bool lostSupport);
                        if (lostSupport || Vector2.Distance(new(resolved.X, resolved.Z), new(target.X, target.Z)) > .02f) stable = false;
                    }
                    stableWalkEdges[key] = stable;
                }
                if (!stable) continue;
            }
            if (previous.TryAdd(edge.TargetNode, current)) queue.Enqueue(edge.TargetNode);
        }
    }
    return [];
}

void Standing(string name, Vector3 point)
{
    bool supported = surface.TryGetGroundHeight(point.X, point.Z, point.Y, out float ground);
    int node = nav.FindClosestNode(point, maximumDistance: 1);
    var heights = new List<float>();
    surface.CollectWalkableHeights(point.X, point.Z, -10, 25, heights);
    Check("standing " + name, supported && MathF.Abs(ground - point.Y) < .15f
        && !surface.IsPositionBlocked(point with { Y = ground }, ground, 1.8f)
        && node >= 0 && nav.Nodes[node].Component == nav.PrimaryComponent,
        new { p = P(point), supported, ground, node, heights = heights.Select(y => new {
            y, blocked = surface.IsPositionBlocked(point with { Y = y }, y, 1.8f),
            solidBlocked = fullSurface.IsPositionBlocked(point with { Y = y }, y, 1.8f) }) });
}
static Vector3 V(float[] p) => new(p[0], p[1], p[2]);
static float[] P(Vector3 p) => [p.X, p.Y, p.Z];
static string Hash(string p) { using var f = File.OpenRead(p); return Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant(); }
internal sealed class Layout
{
    public float[][] Spawns { get; init; } = [];
    public Dictionary<string, float[]> Landmarks { get; init; } = [];
    public string[][] Routes { get; init; } = [];
    public Shot[] Shots { get; init; } = [];
}
internal sealed record Shot(string Name, float[] From, float[] To, bool Blocked);
