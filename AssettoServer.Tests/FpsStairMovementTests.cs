using System.Numerics;
using AssettoServer.Network.ClientMessages;
using AssettoServer.Server.Ai.Physics;
using AssettoServer.Server.Configuration.Extra;
using AssettoServer.Server.Configuration.Kunos;
using AssettoServer.Server.Fps;
using NUnit.Framework;

namespace AssettoServer.Tests;

public sealed class FpsStairMovementTests
{
    [Test]
    public void PartialStepCannotRetryBelowTreadWhenRaisedCapsuleHitsObstruction()
    {
        // The next tread is reachable, but raising the capsule brings its head into
        // an overhanging beam. Retrying that partial sweep at the old Y tunnels under it.
        var surface = new FpsArenaSurface([
            new(new(-5, 0, -5), new(-5, 0, 5), new(5, 0, 5)),
            new(new(-5, 0, -5), new(5, 0, 5), new(5, 0, -5)),
            new(new(0, .4f, -5), new(0, .4f, 5), new(5, .4f, 5)),
            new(new(0, .4f, -5), new(5, .4f, 5), new(5, .4f, -5)),
            new(new(.3f, 1.9f, -5), new(.3f, 3, 5), new(.3f, 3, -5)),
            new(new(.3f, 1.9f, -5), new(.3f, 1.9f, 5), new(.3f, 3, 5)),
        ]);
        var configuration = new FpsConfiguration
        {
            Enabled = true, StartWithBotsOnly = true,
            Arena = new FpsArenaConfiguration
            {
                BoundsMin = new(-5, -1, -5), BoundsMax = new(5, 5, 5),
                SpawnPoints = [new() { Position = new(-.4f, 0, 0) }],
            },
        };
        var simulation = new FpsSimulation(configuration,
            [new(0, "Step runner", FpsSlotRole.Human)], surface: surface);
        simulation.ClaimHuman(0);
        simulation.ApplyInput(0, new FpsInputCommand(1, Vector2.UnitX, 0, 0, FpsInputButtons.Sprint));
        simulation.Step(.05f);
        var actor = simulation.Actors.Single();
        Assert.Multiple(() =>
        {
            Assert.That(actor.GeometryBlocked, Is.True);
            Assert.That(actor.IsGrounded, Is.True);
            Assert.That(actor.GroundY, Is.EqualTo(.4f).Within(.001f));
            Assert.That(actor.Position.Y, Is.EqualTo(actor.GroundY));
            Assert.That(actor.Position.X, Is.LessThan(0));
        });
    }

    [TestCase(false, -30)]
    [TestCase(false, 30)]
    [TestCase(true, -30)]
    [TestCase(true, 30)]
    public void DiagonalClimbAgainstStairRailCannotFallThroughTreads(bool sprint, float turn)
    {
        var surface = new FpsArenaSurface(OpenStairs());
        var configuration = new FpsConfiguration
        {
            Enabled = true, StartWithBotsOnly = true, InfiniteSprint = true,
            Arena = new FpsArenaConfiguration
            {
                BoundsMin = new(-10, -2, -10), BoundsMax = new(10, 10, 10),
                SpawnPoints = [new() { Position = new(0, 0, -.6f) }],
            },
        };
        var simulation = new FpsSimulation(configuration,
            [new(0, "Stair runner", FpsSlotRole.Human)], surface: surface);
        simulation.ClaimHuman(0);
        var actor = simulation.Actors.Single();
        bool touchedRail = false;
        for (int tick = 0; tick < 60; tick++)
        {
            float angle = (tick is >= 8 and < 24 ? turn : tick is >= 24 and < 30 ? -turn : 0)
                * MathF.PI / 180;
            simulation.ApplyInput(0, new FpsInputCommand((uint)(tick + 1),
                new Vector2(MathF.Sin(angle), MathF.Cos(angle)), 0, 0,
                sprint ? FpsInputButtons.Sprint : FpsInputButtons.None));
            float previousY = actor.Position.Y;
            simulation.Step(.05f);
            touchedRail |= actor.GeometryBlocked;
            Assert.That(actor.IsGrounded, Is.True, $"Lost stair support at tick {tick}: {actor.Position}");
            Assert.That(actor.Position.Y, Is.GreaterThanOrEqualTo(previousY - .08f),
                $"Fell into an ascending tread at tick {tick}");
            if (actor.Position.Z > 5) break;
        }
        Assert.That(touchedRail, Is.True, "The route must actually exercise rail collision");
        Assert.That(actor.Position.Y, Is.GreaterThan(3), "Must finish climbing along the rail");
    }

    // Nuketown's 20.32 cm rise and 30.48 cm pitch with thin rails and posts.
    // Continuous treads isolate rail collision from imported-mesh gap handling.
    private static IReadOnlyList<Kn5Triangle> OpenStairs()
    {
        var triangles = new List<Kn5Triangle>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            triangles.Add(new(a, b, c)); triangles.Add(new(a, c, d));
        }
        Quad(new(-10, 0, -10), new(-10, 0, 10), new(10, 0, 10), new(10, 0, -10));
        const float pitch = .3048f, rise = .2032f, halfWidth = .865f;
        for (int step = 0; step < 16; step++)
        {
            float z = step * pitch, y = (step + 1) * rise;
            Quad(new(-halfWidth, y, z), new(-halfWidth, y, z + pitch),
                new(halfWidth, y, z + pitch), new(halfWidth, y, z));
            Quad(new(-halfWidth, y - .05f, z), new(-halfWidth, y, z),
                new(halfWidth, y, z), new(halfWidth, y - .05f, z));
            if (step % 4 != 0) continue;
            foreach (float x in new[] { -halfWidth, halfWidth })
            {
                Quad(new(x, y, z), new(x, y + 1, z),
                    new(x, y + 1, z + .08f), new(x, y, z + .08f));
                Quad(new(x - .04f, y, z), new(x - .04f, y + 1, z),
                    new(x + .04f, y + 1, z), new(x + .04f, y, z));
            }
        }
        foreach (float x in new[] { -halfWidth, halfWidth })
            Quad(new(x, 1, 0), new(x, 1.08f, 0),
                new(x, 16 * rise + 1.08f, 16 * pitch), new(x, 16 * rise + 1, 16 * pitch));
        Quad(new(-halfWidth, 16 * rise, 16 * pitch), new(-halfWidth, 16 * rise, 10),
            new(halfWidth, 16 * rise, 10), new(halfWidth, 16 * rise, 16 * pitch));
        return triangles;
    }
}
