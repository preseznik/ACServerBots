using System.Numerics;
using AssettoServer.Server.Ai.Physics;
using AssettoServer.Server.Fps;
using NUnit.Framework;

namespace AssettoServer.Tests;

public sealed class FpsNavigationTests
{
    [Test]
    public void LayeredGridPreservesDistinctWalkableFloors()
    {
        var triangles = new List<Kn5Triangle>();
        triangles.AddRange(Floor(-4, 4, -4, 4, 0));
        triangles.AddRange(Floor(-4, 4, -4, 4, 3));
        var surface = new FpsArenaSurface(triangles);
        var result = FpsArenaNavigationBuilder.Build(surface,
            new FpsArenaPoint(-4, -1, -4), new FpsArenaPoint(4, 5, 4),
            [Spawn(-2, 0, 0), Spawn(2, 0, 0)]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Asset.Nodes.Any(node => MathF.Abs(node.Position.Y) < 0.05f),
                Is.True);
            Assert.That(result.Asset.Nodes.Any(node => MathF.Abs(node.Position.Y - 3) < 0.05f),
                Is.True);
            Assert.That(result.Asset.ComponentCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(result.ConnectedSpawnPoints, Is.EqualTo(2));
        });
    }

    [Test]
    public void NavigationRoutesAroundSolidWallAndRoundTripsBinaryAsset()
    {
        var triangles = new List<Kn5Triangle>();
        triangles.AddRange(Floor(-5, 5, -4, 4, 0));
        triangles.AddRange(Wall(0, -1.2f, 1.2f, 0, 2.5f));
        var result = FpsArenaNavigationBuilder.Build(new FpsArenaSurface(triangles),
            new FpsArenaPoint(-5, -1, -4), new FpsArenaPoint(5, 4, 4),
            [Spawn(-3, 0, 0), Spawn(3, 0, 0)]);
        var path = result.Asset.FindPath(new Vector3(-3, 0, 0), new Vector3(3, 0, 0));
        string temporary = Path.Combine(TestContext.CurrentContext.WorkDirectory,
            $"fps-navigation-{Guid.NewGuid():N}.bin");
        try
        {
            result.Asset.Save(temporary);
            var loaded = FpsArenaNavigationAsset.Load(temporary);
            Assert.Multiple(() =>
            {
                Assert.That(path, Is.Not.Empty);
                Assert.That(path.Any(step => MathF.Abs(step.Position.Z) > 1.25f), Is.True,
                    "The route must go around the wall instead of crossing it.");
                Assert.That(loaded.Nodes, Has.Count.EqualTo(result.Asset.Nodes.Count));
                Assert.That(loaded.SpawnNodes, Is.EqualTo(result.Asset.SpawnNodes));
                Assert.That(loaded.FindPath(new Vector3(-3, 0, 0), new Vector3(3, 0, 0)),
                    Is.Not.Empty);
            });
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    [Test]
    public void PreparationConnectsRaisedPlatformWithTraversalLink()
    {
        var triangles = new List<Kn5Triangle>();
        triangles.AddRange(Floor(-4, -0.3f, -3, 3, 0));
        triangles.AddRange(Floor(0.3f, 4, -3, 3, 1));
        triangles.AddRange(Wall(0.3f, -3, 3, 0, 1));
        var result = FpsArenaNavigationBuilder.Build(new FpsArenaSurface(triangles),
            new FpsArenaPoint(-4, -1, -3), new FpsArenaPoint(4, 4, 3),
            [Spawn(-2, 0, 0), Spawn(2, 1, 0)]);

        Assert.Multiple(() =>
        {
            Assert.That(result.TraversalLinks, Is.GreaterThan(0));
            Assert.That(result.ConnectedSpawnPoints, Is.EqualTo(2));
            Assert.That(result.Asset.FindPath(new Vector3(-2, 0, 0),
                new Vector3(2, 1, 0)), Is.Not.Empty);
        });
    }

    [Test]
    public void OneWayDropRemainsUsableWithoutCreatingAReverseClimb()
    {
        var triangles = new List<Kn5Triangle>();
        triangles.AddRange(Floor(-3, -0.3f, -2, 2, 2));
        triangles.AddRange(Floor(0.3f, 3, -2, 2, 0));
        var result = FpsArenaNavigationBuilder.Build(new FpsArenaSurface(triangles),
            new FpsArenaPoint(-3, -1, -2), new FpsArenaPoint(3, 4, 2),
            [Spawn(-2, 2, 0), Spawn(2, 0, 0)]);

        var down = result.Asset.FindPath(new Vector3(-2, 2, 0),
            new Vector3(2, 0, 0));
        var up = result.Asset.FindPath(new Vector3(2, 0, 0),
            new Vector3(-2, 2, 0));
        Assert.Multiple(() =>
        {
            Assert.That(down.Any(step => step.Kind == FpsNavigationLinkKind.Drop), Is.True);
            Assert.That(up, Is.Empty,
                "A drop link must not let a bot climb a wall above its jump limit.");
        });
    }

    [Test]
    public void PathDoesNotSnapTargetOntoCallersDisconnectedComponent()
    {
        var first = new FpsNavigationNode
        {
            Position = Vector3.Zero,
            Component = 0,
        };
        var second = new FpsNavigationNode
        {
            Position = new Vector3(2, 0, 0),
            Component = 0,
        };
        var disconnected = new FpsNavigationNode
        {
            Position = new Vector3(2.2f, 0, 0),
            Component = 1,
        };
        first.Edges.Add(new FpsNavigationEdge(1, FpsNavigationLinkKind.Walk, 2));
        second.Edges.Add(new FpsNavigationEdge(0, FpsNavigationLinkKind.Walk, 2));
        var navigation = new FpsArenaNavigationAsset
        {
            CellSize = 0.6f,
            Nodes = [first, second, disconnected],
            SpawnNodes = [0, 2],
            ComponentCount = 2,
            PrimaryComponent = 0,
        };

        Assert.Multiple(() =>
        {
            Assert.That(navigation.AreConnected(Vector3.Zero,
                disconnected.Position), Is.False);
            Assert.That(navigation.FindPath(Vector3.Zero,
                disconnected.Position), Is.Empty,
                "The destination must resolve independently before components are compared.");
        });
    }

    [Test]
    public void FlatWalkableGridDoesNotGenerateRedundantTraversalLinks()
    {
        var surface = new FpsArenaSurface(Floor(-6, 6, -6, 6, 0).ToArray());
        var result = FpsArenaNavigationBuilder.Build(surface,
            new FpsArenaPoint(-6, -1, -6), new FpsArenaPoint(6, 3, 6),
            [Spawn(-4, 0, 0), Spawn(4, 0, 0)]);

        Assert.Multiple(() =>
        {
            Assert.That(result.WalkLinks, Is.GreaterThan(0));
            Assert.That(result.TraversalLinks, Is.Zero,
                "Ordinary walk neighbors must not also receive jump or mantle links.");
            Assert.That(result.Asset.FindPath(new Vector3(-4, 0, 0),
                new Vector3(4, 0, 0)), Is.Not.Empty);
        });
    }

    [Test]
    public void LargeGridPathSearchExpandsEachNodeAtMostOnceAndReusesState()
    {
        const int width = 80;
        var nodes = new FpsNavigationNode[width * width];
        for (int z = 0; z < width; z++)
        for (int x = 0; x < width; x++)
            nodes[z * width + x] = new FpsNavigationNode
            {
                Position = new Vector3(x * 0.6f, 0, z * 0.6f),
                Component = 0,
            };
        for (int z = 0; z < width; z++)
        for (int x = 0; x < width; x++)
        {
            int index = z * width + x;
            if (x + 1 < width)
                nodes[index].Edges.Add(new FpsNavigationEdge(index + 1,
                    FpsNavigationLinkKind.Walk, 0.6f));
            if (x > 0)
                nodes[index].Edges.Add(new FpsNavigationEdge(index - 1,
                    FpsNavigationLinkKind.Walk, 0.6f));
            if (z + 1 < width)
                nodes[index].Edges.Add(new FpsNavigationEdge(index + width,
                    FpsNavigationLinkKind.Walk, 0.6f));
            if (z > 0)
                nodes[index].Edges.Add(new FpsNavigationEdge(index - width,
                    FpsNavigationLinkKind.Walk, 0.6f));
        }
        var navigation = new FpsArenaNavigationAsset
        {
            CellSize = 0.6f,
            Nodes = nodes,
            SpawnNodes = [0, nodes.Length - 1],
            ComponentCount = 1,
            PrimaryComponent = 0,
        };

        var first = navigation.FindPath(nodes[0].Position, nodes[^1].Position);
        int firstExpanded = navigation.LastPathExpandedNodes;
        var second = navigation.FindPath(nodes[0].Position, nodes[^1].Position);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Empty);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(firstExpanded, Is.LessThanOrEqualTo(nodes.Length));
            Assert.That(navigation.LastPathExpandedNodes, Is.LessThanOrEqualTo(nodes.Length));
        });
    }

    [Test]
    public void PathSearchCanQuarantineOneFailedTraversalEdge()
    {
        var nodes = new[]
        {
            Node(0, 0),
            Node(1, 0),
            Node(0, 2),
            Node(2, 0),
        };
        nodes[0].Edges.Add(new FpsNavigationEdge(1, FpsNavigationLinkKind.Mantle, 1));
        nodes[1].Edges.Add(new FpsNavigationEdge(3, FpsNavigationLinkKind.Walk, 1));
        nodes[0].Edges.Add(new FpsNavigationEdge(2, FpsNavigationLinkKind.Walk, 2));
        nodes[2].Edges.Add(new FpsNavigationEdge(3, FpsNavigationLinkKind.Walk, 2));
        var navigation = new FpsArenaNavigationAsset
        {
            CellSize = 0.6f,
            Nodes = nodes,
            SpawnNodes = [0, 3],
            ComponentCount = 1,
            PrimaryComponent = 0,
        };

        var ordinary = navigation.FindPath(nodes[0].Position, nodes[3].Position);
        var quarantined = navigation.FindPath(nodes[0].Position, nodes[3].Position,
            0, 1, FpsNavigationLinkKind.Mantle);

        Assert.Multiple(() =>
        {
            Assert.That(ordinary.Select(step => step.NodeIndex), Is.EqualTo(new[] { 1, 3 }));
            Assert.That(quarantined.Select(step => step.NodeIndex), Is.EqualTo(new[] { 2, 3 }));
        });

        static FpsNavigationNode Node(float x, float z) => new()
        {
            Position = new Vector3(x, 0, z),
            Component = 0,
        };
    }

    private static FpsArenaSpawn Spawn(float x, float y, float z) =>
        new(new FpsArenaPoint(x, y, z), 0);

    private static IEnumerable<Kn5Triangle> Floor(float minX, float maxX, float minZ,
        float maxZ, float y)
    {
        yield return new Kn5Triangle(new Vector3(minX, y, minZ),
            new Vector3(minX, y, maxZ), new Vector3(maxX, y, maxZ));
        yield return new Kn5Triangle(new Vector3(minX, y, minZ),
            new Vector3(maxX, y, maxZ), new Vector3(maxX, y, minZ));
    }

    private static IEnumerable<Kn5Triangle> Wall(float x, float minZ, float maxZ,
        float minY, float maxY)
    {
        yield return new Kn5Triangle(new Vector3(x, minY, minZ),
            new Vector3(x, maxY, minZ), new Vector3(x, maxY, maxZ));
        yield return new Kn5Triangle(new Vector3(x, minY, minZ),
            new Vector3(x, maxY, maxZ), new Vector3(x, minY, maxZ));
    }
}
