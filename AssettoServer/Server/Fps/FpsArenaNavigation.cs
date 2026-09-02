using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace AssettoServer.Server.Fps;

internal enum FpsNavigationLinkKind : byte
{
    Walk,
    Drop,
    Jump,
    Vault,
    Mantle,
}

internal readonly record struct FpsNavigationEdge(int TargetNode,
    FpsNavigationLinkKind Kind, float Cost);

internal sealed class FpsNavigationNode
{
    public required Vector3 Position { get; init; }
    public int Component { get; set; }
    public List<FpsNavigationEdge> Edges { get; } = [];
}

internal readonly record struct FpsNavigationStep(int NodeIndex, Vector3 Position,
    FpsNavigationLinkKind Kind);

internal sealed class FpsArenaNavigationAsset
{
    private const uint Magic = 0x4E535046; // FPSN
    public const int CurrentVersion = 1;
    public const float DefaultCellSize = 0.6f;

    private readonly PriorityQueue<PathCandidate, float> _openNodes = new();
    private Dictionary<long, List<int>>? _nodeCells;
    private float[]? _pathCosts;
    private int[]? _pathPrevious;
    private FpsNavigationLinkKind[]? _pathPreviousKinds;
    private int[]? _pathSeenGeneration;
    private int[]? _pathClosedGeneration;
    private int _pathGeneration;

    public required float CellSize { get; init; }
    public required IReadOnlyList<FpsNavigationNode> Nodes { get; init; }
    public required IReadOnlyList<int> SpawnNodes { get; init; }
    public required int ComponentCount { get; init; }
    public required int PrimaryComponent { get; init; }
    internal int LastPathExpandedNodes { get; private set; }

    public void Save(string path)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(Magic);
        writer.Write(CurrentVersion);
        writer.Write(CellSize);
        writer.Write(ComponentCount);
        writer.Write(PrimaryComponent);
        writer.Write(Nodes.Count);
        foreach (var node in Nodes)
        {
            Write(writer, node.Position);
            writer.Write(node.Component);
            writer.Write(node.Edges.Count);
            foreach (var edge in node.Edges)
            {
                writer.Write(edge.TargetNode);
                writer.Write((byte)edge.Kind);
                writer.Write(edge.Cost);
            }
        }
        writer.Write(SpawnNodes.Count);
        foreach (int node in SpawnNodes) writer.Write(node);
    }

    public static FpsArenaNavigationAsset Load(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(Path.GetFullPath(path)));
        if (reader.ReadUInt32() != Magic)
            throw new InvalidDataException("FPS arena navigation has an invalid header");
        if (reader.ReadInt32() != CurrentVersion)
            throw new InvalidDataException("FPS arena navigation uses an unsupported version");
        float cellSize = reader.ReadSingle();
        int componentCount = reader.ReadInt32();
        int primaryComponent = reader.ReadInt32();
        int count = reader.ReadInt32();
        if (!float.IsFinite(cellSize) || cellSize is < 0.25f or > 2
            || count is <= 0 or > 1_000_000
            || componentCount <= 0 || componentCount > count
            || primaryComponent is < 0 || primaryComponent >= componentCount)
            throw new InvalidDataException("FPS arena navigation metadata is invalid");
        var nodes = new FpsNavigationNode[count];
        for (int index = 0; index < count; index++)
        {
            var node = new FpsNavigationNode
            {
                Position = Read(reader),
                Component = reader.ReadInt32(),
            };
            int edgeCount = reader.ReadInt32();
            if (node.Component is < 0 || node.Component >= componentCount
                || edgeCount is < 0 or > 64)
                throw new InvalidDataException("FPS arena navigation node is invalid");
            for (int edgeIndex = 0; edgeIndex < edgeCount; edgeIndex++)
            {
                int target = reader.ReadInt32();
                var kind = (FpsNavigationLinkKind)reader.ReadByte();
                float cost = reader.ReadSingle();
                if (target is < 0 || target >= count
                    || !Enum.IsDefined(kind) || !float.IsFinite(cost) || cost <= 0)
                    throw new InvalidDataException("FPS arena navigation edge is invalid");
                node.Edges.Add(new FpsNavigationEdge(target, kind, cost));
            }
            nodes[index] = node;
        }
        int spawnCount = reader.ReadInt32();
        if (spawnCount is < 0 or > 254)
            throw new InvalidDataException("FPS arena navigation spawn mapping is invalid");
        var spawnNodes = new int[spawnCount];
        for (int index = 0; index < spawnCount; index++)
        {
            spawnNodes[index] = reader.ReadInt32();
            if (spawnNodes[index] is < 0 || spawnNodes[index] >= count)
                throw new InvalidDataException("FPS arena navigation spawn node is invalid");
        }
        if (reader.BaseStream.Position != reader.BaseStream.Length)
            throw new InvalidDataException("FPS arena navigation has unexpected trailing data");
        return new FpsArenaNavigationAsset
        {
            CellSize = cellSize,
            Nodes = nodes,
            SpawnNodes = spawnNodes,
            ComponentCount = componentCount,
            PrimaryComponent = primaryComponent,
        };
    }

    public int FindClosestNode(Vector3 position, int component = -1,
        float maximumDistance = 4)
    {
        EnsureNodeIndex();
        int best = -1;
        float bestDistance = maximumDistance * maximumDistance;
        int centerX = Cell(position.X);
        int centerZ = Cell(position.Z);
        int radius = (int)MathF.Ceiling(maximumDistance / CellSize) + 1;
        for (int ring = 0; ring <= radius; ring++)
        {
            for (int x = centerX - ring; x <= centerX + ring; x++)
            for (int z = centerZ - ring; z <= centerZ + ring; z++)
            {
                if (ring > 0 && Math.Abs(x - centerX) != ring
                             && Math.Abs(z - centerZ) != ring)
                    continue;
                if (!_nodeCells!.TryGetValue(Key(x, z), out var candidates)) continue;
                foreach (int index in candidates)
                {
                    var node = Nodes[index];
                    if (component >= 0 && node.Component != component) continue;
                    float vertical = MathF.Abs(node.Position.Y - position.Y);
                    if (vertical > MathF.Max(2, maximumDistance)) continue;
                    var delta = node.Position - position;
                    float distance = delta.X * delta.X + delta.Z * delta.Z
                                     + vertical * vertical * 0.25f;
                    if (distance >= maximumDistance * maximumDistance
                        || distance > bestDistance
                        || distance == bestDistance && best >= 0 && index >= best)
                        continue;
                    bestDistance = distance;
                    best = index;
                }
            }
            float nextRingMinimum = (ring + 0.5f) * CellSize;
            if (best >= 0 && nextRingMinimum * nextRingMinimum > bestDistance) break;
        }
        return best;
    }

    public IReadOnlyList<FpsNavigationStep> FindPath(Vector3 start, Vector3 target,
        int excludedFromNode = -1, int excludedTargetNode = -1,
        FpsNavigationLinkKind excludedKind = FpsNavigationLinkKind.Walk)
    {
        LastPathExpandedNodes = 0;
        int startNode = FindClosestNode(start);
        if (startNode < 0) return [];
        int targetNode = FindClosestNode(target);
        if (targetNode < 0 || Nodes[targetNode].Component != Nodes[startNode].Component)
            return [];
        if (startNode == targetNode)
            return [new FpsNavigationStep(targetNode, Nodes[targetNode].Position,
                FpsNavigationLinkKind.Walk)];

        int generation = BeginPathSearch();
        _pathCosts![startNode] = 0;
        _pathPrevious![startNode] = -1;
        _pathSeenGeneration![startNode] = generation;
        _openNodes.Enqueue(new PathCandidate(startNode, 0), 0);
        while (_openNodes.TryDequeue(out var candidate, out _))
        {
            int current = candidate.Node;
            if (_pathClosedGeneration![current] == generation
                || _pathSeenGeneration[current] != generation
                || candidate.Cost > _pathCosts![current] + 0.0001f)
                continue;
            _pathClosedGeneration[current] = generation;
            LastPathExpandedNodes++;
            if (current == targetNode) break;
            float currentCost = _pathCosts[current];
            foreach (var edge in Nodes[current].Edges)
            {
                if (current == excludedFromNode && edge.TargetNode == excludedTargetNode
                    && edge.Kind == excludedKind) continue;
                if (Nodes[edge.TargetNode].Component != Nodes[startNode].Component) continue;
                float cost = currentCost + edge.Cost;
                if (_pathSeenGeneration![edge.TargetNode] == generation
                    && cost >= _pathCosts[edge.TargetNode]) continue;
                _pathSeenGeneration[edge.TargetNode] = generation;
                _pathCosts[edge.TargetNode] = cost;
                _pathPrevious![edge.TargetNode] = current;
                _pathPreviousKinds![edge.TargetNode] = edge.Kind;
                float heuristic = Vector3.Distance(Nodes[edge.TargetNode].Position,
                    Nodes[targetNode].Position);
                _openNodes.Enqueue(new PathCandidate(edge.TargetNode, cost), cost + heuristic);
            }
        }
        if (_pathSeenGeneration![targetNode] != generation
            || _pathPrevious![targetNode] < 0) return [];
        var reversed = new List<FpsNavigationStep>();
        for (int current = targetNode; current != startNode; current = _pathPrevious[current])
            reversed.Add(new FpsNavigationStep(current, Nodes[current].Position,
                _pathPreviousKinds![current]));
        reversed.Reverse();
        return reversed;
    }

    public bool AreConnected(Vector3 first, Vector3 second)
    {
        int firstNode = FindClosestNode(first);
        int secondNode = FindClosestNode(second);
        return firstNode >= 0 && secondNode >= 0
                              && Nodes[firstNode].Component == Nodes[secondNode].Component;
    }

    private void EnsureNodeIndex()
    {
        if (_nodeCells is not null) return;
        _nodeCells = new Dictionary<long, List<int>>();
        for (int index = 0; index < Nodes.Count; index++)
        {
            var position = Nodes[index].Position;
            long key = Key(Cell(position.X), Cell(position.Z));
            if (!_nodeCells.TryGetValue(key, out var nodes))
                _nodeCells[key] = nodes = [];
            nodes.Add(index);
        }
    }

    private int BeginPathSearch()
    {
        if (_pathCosts?.Length != Nodes.Count)
        {
            _pathCosts = new float[Nodes.Count];
            _pathPrevious = new int[Nodes.Count];
            _pathPreviousKinds = new FpsNavigationLinkKind[Nodes.Count];
            _pathSeenGeneration = new int[Nodes.Count];
            _pathClosedGeneration = new int[Nodes.Count];
            _pathGeneration = 0;
        }
        if (_pathGeneration == int.MaxValue)
        {
            Array.Clear(_pathSeenGeneration!);
            Array.Clear(_pathClosedGeneration!);
            _pathGeneration = 0;
        }
        _openNodes.Clear();
        return ++_pathGeneration;
    }

    private int Cell(float value) => (int)MathF.Round(value / CellSize);
    private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
    private readonly record struct PathCandidate(int Node, float Cost);

    private static void Write(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }

    private static Vector3 Read(BinaryReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
}

internal sealed record FpsArenaNavigationBuildResult(FpsArenaNavigationAsset Asset,
    int WalkLinks, int TraversalLinks, int ConnectedSpawnPoints);

internal static class FpsArenaNavigationBuilder
{
    private const float MaximumDropHeight = 6;
    private const float MaximumJumpRise = 1.5f;
    private const float MaximumJumpDistance = 1.8f;

    public static FpsArenaNavigationBuildResult Build(FpsArenaSurface surface,
        FpsArenaPoint boundsMin, FpsArenaPoint boundsMax,
        IReadOnlyList<FpsArenaSpawn> spawns,
        float cellSize = FpsArenaNavigationAsset.DefaultCellSize)
    {
        if (spawns.Count < 2) throw new InvalidDataException("FPS navigation needs at least two spawns");
        var nodes = new List<FpsNavigationNode>();
        var cells = new Dictionary<long, List<int>>();
        var heights = new List<float>();
        int minimumX = (int)MathF.Ceiling(boundsMin.X / cellSize);
        int maximumX = (int)MathF.Floor(boundsMax.X / cellSize);
        int minimumZ = (int)MathF.Ceiling(boundsMin.Z / cellSize);
        int maximumZ = (int)MathF.Floor(boundsMax.Z / cellSize);
        for (int x = minimumX; x <= maximumX; x++)
        for (int z = minimumZ; z <= maximumZ; z++)
        {
            float worldX = x * cellSize;
            float worldZ = z * cellSize;
            surface.CollectWalkableHeights(worldX, worldZ, boundsMin.Y, boundsMax.Y, heights);
            foreach (float height in heights)
            {
                var position = new Vector3(worldX, height, worldZ);
                if (surface.IsPositionBlocked(position, height, 1.8f)) continue;
                AddNode(nodes, cells, position, x, z);
            }
        }

        foreach (var spawn in spawns)
        {
            var position = new Vector3(spawn.Position.X, spawn.Position.Y, spawn.Position.Z);
            surface.TryGetGroundHeight(position.X, position.Z, position.Y, out float groundY);
            position.Y = groundY;
            if (FindClosest(nodes, position, 0.8f) >= 0) continue;
            if (surface.IsPositionBlocked(position, groundY, 1.8f)) continue;
            AddNode(nodes, cells, position, Cell(position.X, cellSize), Cell(position.Z, cellSize));
        }
        if (nodes.Count == 0)
            throw new InvalidDataException("FPS arena did not produce any walkable navigation nodes");

        int walkLinks = ConnectWalkableNeighbors(surface, nodes, cells, cellSize);
        int traversalLinks = ConnectTraversalLinks(surface, nodes, cells, cellSize);
        var components = AssignComponents(nodes);
        var spawnNodes = new int[spawns.Count];
        var spawnsByComponent = new Dictionary<int, int>();
        for (int index = 0; index < spawns.Count; index++)
        {
            var spawn = spawns[index].Position;
            int node = FindClosest(nodes, new Vector3(spawn.X, spawn.Y, spawn.Z), 3);
            if (node < 0)
                throw new InvalidDataException($"FPS spawn {index + 1} could not be mapped to navigation");
            spawnNodes[index] = node;
            int component = nodes[node].Component;
            spawnsByComponent[component] = spawnsByComponent.GetValueOrDefault(component) + 1;
        }
        var primary = spawnsByComponent.OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key).First();
        if (primary.Value < 2)
            throw new InvalidDataException("FPS arena has fewer than two connected navigation spawns");
        var asset = new FpsArenaNavigationAsset
        {
            CellSize = cellSize,
            Nodes = nodes,
            SpawnNodes = spawnNodes,
            ComponentCount = components,
            PrimaryComponent = primary.Key,
        };
        return new FpsArenaNavigationBuildResult(asset, walkLinks, traversalLinks, primary.Value);
    }

    private static int ConnectWalkableNeighbors(FpsArenaSurface surface,
        IReadOnlyList<FpsNavigationNode> nodes, IReadOnlyDictionary<long, List<int>> cells,
        float cellSize)
    {
        int links = 0;
        for (int index = 0; index < nodes.Count; index++)
        {
            var from = nodes[index];
            int cellX = Cell(from.Position.X, cellSize);
            int cellZ = Cell(from.Position.Z, cellSize);
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dz == 0
                    || !cells.TryGetValue(Key(cellX + dx, cellZ + dz), out var candidates))
                    continue;
                foreach (int targetIndex in candidates)
                {
                    if (targetIndex == index) continue;
                    var target = nodes[targetIndex];
                    if (MathF.Abs(target.Position.Y - from.Position.Y)
                        > FpsArenaSurface.MaximumStepHeight + 0.05f) continue;
                    if (!surface.TryResolveMove(from.Position, target.Position, from.Position.Y,
                            1.8f, out var resolved, out float groundY)
                        || PlanarDistance(resolved, target.Position) > 0.12f
                        || MathF.Abs(groundY - target.Position.Y) > 0.12f)
                        continue;
                    float cost = Vector3.Distance(from.Position, target.Position);
                    if (AddEdge(from, targetIndex, FpsNavigationLinkKind.Walk, cost)) links++;
                }
            }
        }
        return links;
    }

    private static int ConnectTraversalLinks(FpsArenaSurface surface,
        IReadOnlyList<FpsNavigationNode> nodes, IReadOnlyDictionary<long, List<int>> cells,
        float cellSize)
    {
        int links = 0;
        ReadOnlySpan<(int X, int Z)> directions = [(1, 0), (-1, 0), (0, 1), (0, -1)];
        for (int index = 0; index < nodes.Count; index++)
        {
            var from = nodes[index];
            int cellX = Cell(from.Position.X, cellSize);
            int cellZ = Cell(from.Position.Z, cellSize);
            foreach (var directionCell in directions)
            {
                if (HasWalkConnection(from, nodes, cellX + directionCell.X,
                        cellZ + directionCell.Z, cellSize))
                    continue;
                var direction = Vector2.Normalize(new Vector2(directionCell.X, directionCell.Z));
                if (TryAddProbedLink(nodes, from, direction, FpsNavigationLinkKind.Mantle,
                        (Vector3 current, Vector2 move, float ground, out Vector3 target,
                            out float targetGround) => surface.TryFindMantle(current, move, ground,
                            out target, out targetGround)))
                {
                    links++;
                    continue;
                }
                if (TryAddProbedLink(nodes, from, direction, FpsNavigationLinkKind.Vault,
                        (Vector3 current, Vector2 move, float ground, out Vector3 target,
                            out float targetGround) => surface.TryFindVault(current, move, ground,
                            out target, out targetGround)))
                {
                    links++;
                    continue;
                }

                for (int distance = 2; distance <= 3; distance++)
                {
                    if (!cells.TryGetValue(Key(cellX + directionCell.X * distance,
                            cellZ + directionCell.Z * distance), out var candidates)) continue;
                    int targetIndex = candidates.Where(candidate => candidate != index)
                        .OrderBy(candidate => MathF.Abs(nodes[candidate].Position.Y - from.Position.Y))
                        .FirstOrDefault(-1);
                    if (targetIndex < 0) continue;
                    var target = nodes[targetIndex];
                    float planar = PlanarDistance(from.Position, target.Position);
                    float rise = target.Position.Y - from.Position.Y;
                    if (planar > MaximumJumpDistance) continue;
                    FpsNavigationLinkKind kind;
                    if (rise < -FpsArenaSurface.MaximumStepDown - 0.05f
                        && rise >= -MaximumDropHeight)
                        kind = FpsNavigationLinkKind.Drop;
                    else if (rise >= -FpsArenaSurface.MaximumStepDown
                             && rise <= MaximumJumpRise)
                        kind = FpsNavigationLinkKind.Jump;
                    else continue;
                    var rayOrigin = from.Position + Vector3.UnitY * 1.1f;
                    var delta = target.Position + Vector3.UnitY * 1.1f - rayOrigin;
                    if (surface.TryRaycast(rayOrigin, Vector3.Normalize(delta), delta.Length(),
                            out float hitDistance) && hitDistance < delta.Length() - 0.15f)
                        continue;
                    if (AddEdge(from, targetIndex, kind,
                            Vector3.Distance(from.Position, target.Position) + 0.5f)) links++;
                    break;
                }
            }
        }
        return links;
    }

    private static bool HasWalkConnection(FpsNavigationNode from,
        IReadOnlyList<FpsNavigationNode> nodes, int targetCellX, int targetCellZ,
        float cellSize) => from.Edges.Any(edge =>
        edge.Kind == FpsNavigationLinkKind.Walk
        && Cell(nodes[edge.TargetNode].Position.X, cellSize) == targetCellX
        && Cell(nodes[edge.TargetNode].Position.Z, cellSize) == targetCellZ);

    private delegate bool TraversalProbe(Vector3 current, Vector2 direction, float groundY,
        out Vector3 target, out float targetGroundY);

    private static bool TryAddProbedLink(IReadOnlyList<FpsNavigationNode> nodes,
        FpsNavigationNode from, Vector2 direction,
        FpsNavigationLinkKind kind, TraversalProbe probe)
    {
        if (!probe(from.Position, direction, from.Position.Y, out var target, out _)) return false;
        int targetIndex = FindClosest(nodes, target, 1);
        if (targetIndex < 0 || ReferenceEquals(from, nodes[targetIndex])) return false;
        return AddEdge(from, targetIndex, kind,
            Vector3.Distance(from.Position, nodes[targetIndex].Position) + 0.75f);
    }

    private static int AssignComponents(IReadOnlyList<FpsNavigationNode> nodes)
    {
        var connected = new List<int>[nodes.Count];
        for (int index = 0; index < nodes.Count; index++) connected[index] = [];
        for (int index = 0; index < nodes.Count; index++)
        foreach (var edge in nodes[index].Edges)
        {
            connected[index].Add(edge.TargetNode);
            connected[edge.TargetNode].Add(index);
        }

        var visited = new bool[nodes.Count];
        int component = 0;
        for (int start = 0; start < nodes.Count; start++)
        {
            if (visited[start]) continue;
            var queue = new Queue<int>();
            queue.Enqueue(start);
            visited[start] = true;
            while (queue.TryDequeue(out int current))
            {
                nodes[current].Component = component;
                foreach (int target in connected[current])
                {
                    if (visited[target]) continue;
                    visited[target] = true;
                    queue.Enqueue(target);
                }
            }
            component++;
        }
        return component;
    }

    private static void AddNode(List<FpsNavigationNode> nodes,
        Dictionary<long, List<int>> cells, Vector3 position, int cellX, int cellZ)
    {
        int index = nodes.Count;
        nodes.Add(new FpsNavigationNode { Position = position });
        long key = Key(cellX, cellZ);
        if (!cells.TryGetValue(key, out var list)) cells[key] = list = [];
        list.Add(index);
    }

    private static bool AddEdge(FpsNavigationNode node, int target,
        FpsNavigationLinkKind kind, float cost)
    {
        if (node.Edges.Any(edge => edge.TargetNode == target && edge.Kind == kind)) return false;
        node.Edges.Add(new FpsNavigationEdge(target, kind, cost));
        return true;
    }

    private static int FindClosest(IReadOnlyList<FpsNavigationNode> nodes, Vector3 position,
        float maximumDistance)
    {
        int best = -1;
        float bestDistance = maximumDistance * maximumDistance;
        for (int index = 0; index < nodes.Count; index++)
        {
            var delta = nodes[index].Position - position;
            float distance = delta.X * delta.X + delta.Z * delta.Z + delta.Y * delta.Y * 0.25f;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = index;
        }
        return best;
    }

    private static float PlanarDistance(Vector3 first, Vector3 second) =>
        Vector2.Distance(new Vector2(first.X, first.Z), new Vector2(second.X, second.Z));
    private static int Cell(float value, float cellSize) => (int)MathF.Round(value / cellSize);
    private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
}
