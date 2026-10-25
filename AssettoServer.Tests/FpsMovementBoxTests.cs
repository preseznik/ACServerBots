using System.Numerics;
using AssettoServer.Network.ClientMessages;
using AssettoServer.Server.Ai.Physics;
using AssettoServer.Server.Configuration.Extra;
using AssettoServer.Server.Configuration.Kunos;
using AssettoServer.Server.Fps;
using NUnit.Framework;

namespace AssettoServer.Tests;

public sealed class FpsMovementBoxTests
{
    private static FpsMovementBox Box() => new(new(0, 16, 0), new(2, 0, 0),
        new(0, 13.5f, 0), new(0, 0, 4)); // starts at 2.5 m, well above street headroom
    private static Kn5Triangle[] Floor(float y = 0) =>
    [
        new(new(-8, y, -8), new(-8, y, 8), new(8, y, 8)),
        new(new(-8, y, -8), new(8, y, 8), new(8, y, -8)),
    ];
    private static FpsArenaSpawn Spawn(float x) => new(new(x, 0, 0), 0);

    [Test]
    public void ShotsIgnoreMovementVolumeButStillHitPhysicalBody()
    {
        var physical = Floor().Concat(Floor(2)).ToArray();
        var plain = new FpsArenaSurface(physical);
        var blocked = new FpsArenaSurface(physical, [Box()]);
        Assert.Multiple(() =>
        {
            Assert.That(blocked.IsPositionBlocked(new(0, 3, 0), 3, 1.8f), Is.True);
            Assert.That(blocked.TryRaycast(new(-5, 3, 0), Vector3.UnitX, 10, out _), Is.False);
            Assert.That(blocked.TryRaycast(new(0, 5, 0), -Vector3.UnitY, 10, out float d), Is.True);
            Assert.That(plain.TryRaycast(new(0, 5, 0), -Vector3.UnitY, 10, out float old), Is.True);
            Assert.That(d, Is.EqualTo(old));
            Assert.That(blocked.TriangleCount, Is.EqualTo(plain.TriangleCount));
        });
    }

    [TestCase(.55f)]
    [TestCase(1.2f)]
    [TestCase(1.8f)]
    public void RoofUnavailableForEveryStanceWhileStreetRemainsClear(float height)
    {
        var surface = new FpsArenaSurface(Floor(), [Box()]);
        Assert.That(surface.IsPositionBlocked(new(0, 3, 0), 3, height), Is.True);
        Assert.That(surface.IsPositionBlocked(Vector3.Zero, 0, height), Is.False);
        surface.TryResolveMove(new(-5, 0, 0), new(5, 0, 0), 0, height, out var end, out _);
        Assert.That(end.X, Is.EqualTo(5).Within(.001));
    }

    [Test]
    public void AirSweepCannotTunnelThroughOrDropIntoVolume()
    {
        var surface = new FpsArenaSurface(Floor(), [Box()]);
        surface.TryResolveAirMove(new(-5, 3, 0), new(5, 3, 0), 1.8f, out var end, out _);
        Assert.That(end.X, Is.LessThan(-2));
        Assert.That(surface.IsMovementSegmentBlocked(new(0, 35, 0), new(0, 1, 0), 1.8f), Is.True);
        Assert.That(surface.IsMovementSegmentBlocked(new(-5, 0, 0), new(5, 0, 0), 1.8f), Is.False);
    }

    [Test]
    public void RotatedVolumeUsesItsOwnFootprint()
    {
        var rotation = Matrix4x4.CreateRotationY(.7f);
        var box = new FpsMovementBox(new(10, 16, 20),
            Vector3.TransformNormal(new(2, 0, 0), rotation), new(0, 13.5f, 0),
            Vector3.TransformNormal(new(0, 0, 4), rotation));
        var inside = box.Center + Vector3.TransformNormal(new(1, -12, 3), rotation);
        var outside = box.Center + Vector3.TransformNormal(new(3, -12, 3), rotation);
        Assert.That(box.ContainsCapsule(inside, 1.8f, .3f), Is.True);
        Assert.That(box.ContainsCapsule(outside, 1.8f, .3f), Is.False);
        Assert.That(box.IntersectsSegment(outside, inside, 1.8f, .3f), Is.True);
    }

    [Test]
    public void MantleTargetAndNavigationRoofNodesAreExcluded()
    {
        var triangles = Floor().Concat(Floor(1.2f)).ToArray();
        var box = new FpsMovementBox(new(0, 10, 0), new(2, 0, 0), new(0, 9, 0), new(0, 0, 4));
        var surface = new FpsArenaSurface(triangles, [box]);
        Assert.That(surface.TryFindMantle(new(-2.1f, 0, 0), Vector2.UnitX, 0, out _, out _), Is.False);
        // Separate raised bus roof and street floor; retain paths outside the footprint.
        var navSurface = new FpsArenaSurface(Floor().Concat(Floor(3)).ToArray(), [Box()]);
        var nav = FpsArenaNavigationBuilder.Build(navSurface, new(-7, -1, -7), new(7, 35, 7),
            [Spawn(-6), Spawn(6)]);
        Assert.That(nav.Asset.Nodes.Any(n => n.Position.Y > 2.5f
            && MathF.Abs(n.Position.X) < 2 && MathF.Abs(n.Position.Z) < 4), Is.False);
        Assert.That(nav.ConnectedSpawnPoints, Is.EqualTo(2));
        Assert.That(nav.Asset.FindPath(new(-6, 0, 0), new(6, 0, 0)), Is.Not.Empty);
    }

    [Test]
    public void JumpIsStoppedByMovementOnlyCeilingWithoutGettingOntoRoof()
    {
        var surface = new FpsArenaSurface(Floor(), [Box()]);
        var config = new FpsConfiguration
        {
            Enabled = true, StartWithBotsOnly = true,
            Arena = new FpsArenaConfiguration { BoundsMin = new(-8, -1, -8), BoundsMax = new(8, 40, 8),
                SpawnPoints = [new() { Position = Vector3.Zero }] },
        };
        var sim = new FpsSimulation(config, [new(0, "Jumper", FpsSlotRole.Human)], surface: surface);
        sim.ClaimHuman(0);
        sim.ApplyInput(0, new FpsInputCommand(1, Vector2.Zero, 0, 0, FpsInputButtons.Jump));
        bool blocked = false;
        for (int tick = 0; tick < 120; tick++)
        {
            sim.Step(1f / 60);
            var actor = sim.Actors.Single();
            blocked |= actor.GeometryBlocked;
            Assert.That(actor.Position.Y, Is.LessThan(.71f));
        }
        Assert.That(blocked, Is.True);
    }

    [Test]
    public void GeometryRoundTripKeepsBoxesSeparateFromTrianglesAndReadsLegacy()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"movebox-{Guid.NewGuid():N}.bin");
        try
        {
            new FpsArenaGeometryAsset { Triangles = Floor(), MovementBoxes = [Box()] }.Save(path);
            var loaded = FpsArenaGeometryAsset.Load(path);
            Assert.That(loaded.Triangles, Is.EqualTo(Floor()));
            Assert.That(loaded.MovementBoxes.Single().HalfZ, Is.EqualTo(Box().HalfZ));
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(0x47535046); writer.Write(1); writer.Write(2);
                foreach (var tri in Floor()) foreach (var p in new[] { tri.A, tri.B, tri.C })
                { writer.Write(p.X); writer.Write(p.Y); writer.Write(p.Z); }
            }
            Assert.That(FpsArenaGeometryAsset.Load(path).MovementBoxes, Is.Empty);
        }
        finally { File.Delete(path); }
    }

    private static (string Name, Vector3 Position)[] Nodes() =>
    [
        ("FPS_MOVEBOX_0_CENTER", Box().Center), ("FPS_MOVEBOX_0_X", Box().Center + Box().HalfX),
        ("FPS_MOVEBOX_0_Y", Box().Center + Box().HalfY), ("FPS_MOVEBOX_0_Z", Box().Center + Box().HalfZ),
    ];

    [Test]
    public void MapNodeContractRejectsPartialDuplicateGappedAndInvalidVolumes()
    {
        Assert.That(FpsArenaAssetBuilder.ParseMovementBoxes(Nodes()), Has.Count.EqualTo(1));
        Assert.Throws<InvalidDataException>(() => FpsArenaAssetBuilder.ParseMovementBoxes(Nodes().Take(3)));
        Assert.Throws<InvalidDataException>(() => FpsArenaAssetBuilder.ParseMovementBoxes(Nodes().Concat(Nodes())));
        Assert.Throws<InvalidDataException>(() => FpsArenaAssetBuilder.ParseMovementBoxes(
            Nodes().Select(n => (n.Name.Replace("_0_", "_1_"), n.Position))));
        Assert.Throws<InvalidDataException>(() => new FpsMovementBox(Vector3.Zero, Vector3.Zero, Vector3.UnitY, Vector3.UnitZ));
        Assert.Throws<InvalidDataException>(() => new FpsMovementBox(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitX));
        Assert.That(FpsArenaAssetBuilder.ShouldIncludeMesh("FPS_MOVEBOX_0_CENTER", true, ["*"]), Is.False);
    }
}
