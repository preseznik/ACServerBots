using System.Numerics;
using AssettoServer.Network.ClientMessages;
using AssettoServer.Server.Ai.Physics;
using AssettoServer.Server.Configuration.Extra;
using AssettoServer.Server.Configuration.Kunos;
using AssettoServer.Server.Fps;
using NUnit.Framework;

namespace AssettoServer.Tests;

public sealed class FpsSimulationTests
{
    [Test]
    public void BotSlotsSpawnAsActiveStationaryActors()
    {
        var simulation = CreateSimulation(FpsSlotRole.Bot, FpsSlotRole.Bot);
        var initial = simulation.Actors.OrderBy(actor => actor.Id)
            .Select(actor => actor.Position).ToArray();

        for (int tick = 0; tick < 40; tick++) simulation.Step(0.05f);

        Assert.Multiple(() =>
        {
            Assert.That(simulation.Actors, Has.Count.EqualTo(2));
            Assert.That(simulation.Actors, Has.All.Matches<FpsActorState>(actor =>
                actor.Active && !actor.HumanControlled && !actor.Dead && actor.SpawnCount == 1));
            Assert.That(simulation.Actors.OrderBy(actor => actor.Id).Select(actor => actor.Position),
                Is.EqualTo(initial));
            Assert.That(simulation.ShotEvents, Is.Empty);
        });
    }

    [Test]
    public void AutoSlotTransfersToHumanAndReturnsToBotOnDisconnect()
    {
        var simulation = CreateSimulation(FpsSlotRole.Auto, FpsSlotRole.Bot);

        Assert.Multiple(() =>
        {
            Assert.That(simulation.ClaimHuman(0), Is.True);
            Assert.That(simulation.ClaimHuman(1), Is.False);
            Assert.That(simulation.Actors.Single(actor => actor.Id == 0).HumanControlled, Is.True);
        });

        simulation.ReleaseHuman(0);
        var actor = simulation.Actors.Single(candidate => candidate.Id == 0);
        Assert.Multiple(() =>
        {
            Assert.That(actor.Active, Is.True);
            Assert.That(actor.HumanControlled, Is.False);
        });
    }

    [Test]
    public void RejectsStaleAndImpossibleInputAndKeepsMovementInsideArena()
    {
        var simulation = CreateSimulation(FpsSlotRole.Human, FpsSlotRole.Bot);
        Assert.That(simulation.ClaimHuman(0), Is.True);
        var actor = simulation.Actors.Single(candidate => candidate.Id == 0);
        var start = actor.Position;
        Assert.That(simulation.ApplyInput(0, new FpsInputCommand(2, new Vector2(0, 1), 0, 0,
            FpsInputButtons.Sprint)), Is.True);
        Assert.That(simulation.ApplyInput(0, new FpsInputCommand(1, new Vector2(0, 1), 0, 0,
            FpsInputButtons.None)), Is.False);
        Assert.That(simulation.ApplyInput(0, new FpsInputCommand(3, new Vector2(4, 0), 0, 0,
            FpsInputButtons.None)), Is.False);

        for (int i = 0; i < 10; i++) simulation.Step(0.05f);
        Assert.That(Vector3.Distance(actor.Position, start), Is.GreaterThan(0.1f),
            "An accepted non-neutral command must move the authoritative actor");
        for (int i = 0; i < 990; i++) simulation.Step(0.05f);
        Assert.That(actor.Position.Z, Is.InRange(-10, 10));
    }

    [Test]
    public void SpawnCountIdentifiesInitialSpawnClaimAndRespawn()
    {
        var simulation = CreateSimulation(FpsSlotRole.Auto, FpsSlotRole.Bot);
        var actor = simulation.Actors.Single(candidate => candidate.Id == 0);
        uint initialSpawn = actor.SpawnCount;

        Assert.That(simulation.ClaimHuman(0), Is.True);
        Assert.That(actor.SpawnCount, Is.EqualTo(initialSpawn + 1));

        simulation.ReleaseHuman(0);
        Assert.That(actor.SpawnCount, Is.EqualTo(initialSpawn + 2));
    }

    [Test]
    public void JumpIsEdgeTriggeredAndReturnsActorToGround()
    {
        var simulation = new FpsSimulation(Configuration(),
            [new(0, "Jumper", FpsSlotRole.Human)]);
        Assert.That(simulation.ClaimHuman(0), Is.True);
        var actor = simulation.Actors.Single();
        float groundY = actor.Position.Y;

        Assert.That(simulation.ApplyInput(0, new FpsInputCommand(1, Vector2.Zero, 0, 0,
            FpsInputButtons.Jump)), Is.True);
        simulation.Step(0.05f);
        Assert.That(actor.Position.Y, Is.GreaterThan(groundY));

        for (int tick = 0; tick < 40; tick++) simulation.Step(0.05f);
        Assert.Multiple(() =>
        {
            Assert.That(actor.Position.Y, Is.EqualTo(groundY).Within(0.001f));
            Assert.That(actor.VerticalVelocity, Is.Zero);
        });

        simulation.ApplyInput(0, new FpsInputCommand(2, Vector2.Zero, 0, 0,
            FpsInputButtons.None));
        simulation.Step(0.05f);
        simulation.ApplyInput(0, new FpsInputCommand(3, Vector2.Zero, 0, 0,
            FpsInputButtons.Jump));
        simulation.Step(0.05f);
        Assert.That(actor.Position.Y, Is.GreaterThan(groundY));
    }

    [Test]
    public void MovementFollowsPreparedTrackSlopeInsteadOfSpawnPlane()
    {
        var surface = new FpsArenaSurface(Slope(-10, 10));
        var simulation = new FpsSimulation(Configuration(),
            [new(0, "Walker", FpsSlotRole.Human)], surface: surface);
        Assert.That(simulation.ClaimHuman(0), Is.True);
        Assert.That(simulation.ApplyInput(0, new FpsInputCommand(1, new Vector2(1, 0), 0, 0,
            FpsInputButtons.None)), Is.True);

        for (int tick = 0; tick < 10; tick++) simulation.Step(0.05f);

        var actor = simulation.Actors.Single();
        Assert.Multiple(() =>
        {
            Assert.That(actor.Position.X, Is.GreaterThan(2.5f));
            Assert.That(actor.Position.Y, Is.EqualTo(actor.Position.X * 0.2f).Within(0.01f));
            Assert.That(actor.GroundY, Is.EqualTo(actor.Position.Y).Within(0.001f));
            Assert.That(actor.IsGrounded, Is.True);
        });
    }

    [Test]
    public void MovementClimbsInclinesWithinDefinedWalkableAngle()
    {
        const float inclineDegrees = 35;
        var simulation = new FpsSimulation(Configuration(),
            [new(0, "Climber", FpsSlotRole.Human)],
            surface: new FpsArenaSurface(Incline(inclineDegrees)));
        simulation.ClaimHuman(0);
        simulation.ApplyInput(0, new FpsInputCommand(1, new Vector2(1, 0), 0, 0,
            FpsInputButtons.None));

        for (int tick = 0; tick < 10; tick++) simulation.Step(0.05f);

        var actor = simulation.Actors.Single();
        Assert.Multiple(() =>
        {
            Assert.That(FpsArenaSurface.MaximumWalkableSlopeDegrees, Is.EqualTo(45));
            Assert.That(actor.Position.X, Is.GreaterThan(2.5f));
            Assert.That(actor.Position.Y, Is.EqualTo(actor.Position.X
                * MathF.Tan(inclineDegrees * MathF.PI / 180)).Within(0.02f));
        });
    }

    [Test]
    public void MovementRejectsInclinesBeyondDefinedWalkableAngle()
    {
        var simulation = new FpsSimulation(Configuration(),
            [new(0, "Climber", FpsSlotRole.Human)],
            surface: new FpsArenaSurface(Incline(55)));
        simulation.ClaimHuman(0);
        simulation.ApplyInput(0, new FpsInputCommand(1, new Vector2(1, 0), 0, 0,
            FpsInputButtons.None));

        for (int tick = 0; tick < 10; tick++) simulation.Step(0.05f);

        Assert.That(simulation.Actors.Single().Position.X, Is.LessThan(0.1f));
    }

    [Test]
    public void MovementStepsOverCurbsWithinDefinedHeight()
    {
        const float curbHeight = 0.18f;
        var triangles = new List<Kn5Triangle>
        {
            new(new Vector3(-10, 0, -10), new Vector3(-10, 0, 10), new Vector3(0, 0, 10)),
            new(new Vector3(-10, 0, -10), new Vector3(0, 0, 10), new Vector3(0, 0, -10)),
            new(new Vector3(0, curbHeight, -10), new Vector3(0, curbHeight, 10),
                new Vector3(10, curbHeight, 10)),
            new(new Vector3(0, curbHeight, -10), new Vector3(10, curbHeight, 10),
                new Vector3(10, curbHeight, -10)),
            new(new Vector3(0, 0, -10), new Vector3(0, curbHeight, 10),
                new Vector3(0, curbHeight, -10)),
            new(new Vector3(0, 0, -10), new Vector3(0, 0, 10),
                new Vector3(0, curbHeight, 10)),
        };
        var simulation = new FpsSimulation(Configuration(),
            [new(0, "Walker", FpsSlotRole.Human)],
            surface: new FpsArenaSurface(triangles));
        simulation.ClaimHuman(0);
        var actor = simulation.Actors.Single();
        actor.Position = new Vector3(-1, 0, 0);
        actor.GroundY = 0;
        simulation.ApplyInput(0, new FpsInputCommand(1, new Vector2(1, 0), 0, 0,
            FpsInputButtons.None));

        for (int tick = 0; tick < 10; tick++) simulation.Step(0.05f);

        Assert.Multiple(() =>
        {
            Assert.That(FpsArenaSurface.MaximumStepHeight, Is.EqualTo(0.48f));
            Assert.That(actor.Position.X, Is.GreaterThan(1.5f));
            Assert.That(actor.Position.Y, Is.EqualTo(curbHeight).Within(0.01f));
        });
    }

    [Test]
    public void MovementStepsUpRepeatedStairRisers()
    {
        const float riserHeight = 0.24f;
        const float treadDepth = 0.42f;
        var triangles = new List<Kn5Triangle>
        {
            // Fire Pit-style geometry retains a floor below the modeled stair treads.
            new(new Vector3(-10, 0, -2), new Vector3(-10, 0, 2), new Vector3(10, 0, 2)),
            new(new Vector3(-10, 0, -2), new Vector3(10, 0, 2), new Vector3(10, 0, -2)),
        };
        for (int step = 0; step < 5; step++)
        {
            float x0 = step * treadDepth;
            float x1 = (step + 1) * treadDepth;
            float y0 = step * riserHeight;
            float y1 = (step + 1) * riserHeight;
            triangles.Add(new Kn5Triangle(new Vector3(x0, y1, -2), new Vector3(x0, y1, 2),
                new Vector3(x1, y1, 2)));
            triangles.Add(new Kn5Triangle(new Vector3(x0, y1, -2), new Vector3(x1, y1, 2),
                new Vector3(x1, y1, -2)));
            triangles.Add(new Kn5Triangle(new Vector3(x0, y0, -2), new Vector3(x0, y1, 2),
                new Vector3(x0, y1, -2)));
            triangles.Add(new Kn5Triangle(new Vector3(x0, y0, -2), new Vector3(x0, y0, 2),
                new Vector3(x0, y1, 2)));
        }
        float topY = 5 * riserHeight;
        triangles.Add(new Kn5Triangle(new Vector3(5 * treadDepth, topY, -2),
            new Vector3(5 * treadDepth, topY, 2), new Vector3(10, topY, 2)));
        triangles.Add(new Kn5Triangle(new Vector3(5 * treadDepth, topY, -2),
            new Vector3(10, topY, 2), new Vector3(10, topY, -2)));

        var simulation = new FpsSimulation(Configuration(),
            [new(0, "Stair walker", FpsSlotRole.Human)],
            surface: new FpsArenaSurface(triangles));
        simulation.ClaimHuman(0);
        var actor = simulation.Actors.Single();
        actor.Position = new Vector3(-1, 0, 0);
        actor.GroundY = 0;
        simulation.ApplyInput(0, new FpsInputCommand(1, new Vector2(1, 0), 0, 0,
            FpsInputButtons.None));

        for (int tick = 0; tick < 24; tick++) simulation.Step(0.05f);

        Assert.Multiple(() =>
        {
            Assert.That(actor.Position.X, Is.GreaterThan(2.5f));
            Assert.That(actor.Position.Y, Is.EqualTo(topY).Within(0.01f));
        });
    }

    [Test]
    public void MovementCannotWalkPastPhysicalSurfaceEdge()
    {
        var surface = new FpsArenaSurface(Slope(-1, 1));
        var simulation = new FpsSimulation(Configuration(),
            [new(0, "Walker", FpsSlotRole.Human)], surface: surface);
        simulation.ClaimHuman(0);
        simulation.ApplyInput(0, new FpsInputCommand(1, new Vector2(1, 0), 0, 0,
            FpsInputButtons.None));

        for (int tick = 0; tick < 40; tick++) simulation.Step(0.05f);

        Assert.That(simulation.Actors.Single().Position.X, Is.LessThanOrEqualTo(1.01f));
    }

    [Test]
    public void MovementOffLedgeTransitionsToGravityDrivenFall()
    {
        var triangles = new List<Kn5Triangle>
        {
            new(new Vector3(-10, 2, -5), new Vector3(-10, 2, 5), new Vector3(0, 2, 5)),
            new(new Vector3(-10, 2, -5), new Vector3(0, 2, 5), new Vector3(0, 2, -5)),
            new(new Vector3(0, 0, -5), new Vector3(0, 0, 5), new Vector3(10, 0, 5)),
            new(new Vector3(0, 0, -5), new Vector3(10, 0, 5), new Vector3(10, 0, -5)),
        };
        var simulation = new FpsSimulation(Configuration(),
            [new(0, "Drop runner", FpsSlotRole.Human)],
            surface: new FpsArenaSurface(triangles));
        simulation.ClaimHuman(0);
        var actor = simulation.Actors.Single();
        actor.Position = new Vector3(-1, 2, 0);
        actor.GroundY = 2;
        simulation.ApplyInput(0, new FpsInputCommand(1, new Vector2(1, 0), 0, 0,
            FpsInputButtons.None));

        float firstAirborneY = float.NaN;
        for (int tick = 0; tick < 35; tick++)
        {
            simulation.Step(0.05f);
            if (!actor.IsGrounded && float.IsNaN(firstAirborneY)) firstAirborneY = actor.Position.Y;
        }

        Assert.Multiple(() =>
        {
            Assert.That(FpsArenaSurface.MaximumStepDown, Is.EqualTo(0.48f));
            Assert.That(firstAirborneY, Is.GreaterThan(1.8f),
                "A ledge must begin a fall instead of snapping two metres to the lower floor");
            Assert.That(actor.Position.X, Is.GreaterThan(2));
            Assert.That(actor.Position.Y, Is.Zero.Within(0.01f));
            Assert.That(actor.IsGrounded, Is.True);
        });
    }

    [Test]
    public void JumpCarriesMomentumAcrossLedgeAndLandsOnLowerFloor()
    {
        var triangles = new List<Kn5Triangle>
        {
            new(new Vector3(-10, 2, -5), new Vector3(-10, 2, 5), new Vector3(0, 2, 5)),
            new(new Vector3(-10, 2, -5), new Vector3(0, 2, 5), new Vector3(0, 2, -5)),
            new(new Vector3(0, 0, -5), new Vector3(0, 0, 5), new Vector3(10, 0, 5)),
            new(new Vector3(0, 0, -5), new Vector3(10, 0, 5), new Vector3(10, 0, -5)),
        };
        var simulation = new FpsSimulation(Configuration(),
            [new(0, "Ledge jumper", FpsSlotRole.Human)],
            surface: new FpsArenaSurface(triangles));
        simulation.ClaimHuman(0);
        var actor = simulation.Actors.Single();
        actor.Position = new Vector3(-1, 2, 0);
        actor.GroundY = 2;
        simulation.ApplyInput(0, new FpsInputCommand(1, new Vector2(1, 0), 0, 0,
            FpsInputButtons.Sprint | FpsInputButtons.Jump));

        float maximumY = actor.Position.Y;
        for (int tick = 0; tick < 45; tick++)
        {
            simulation.Step(0.05f);
            maximumY = MathF.Max(maximumY, actor.Position.Y);
        }

        Assert.Multiple(() =>
        {
            Assert.That(maximumY, Is.GreaterThan(3.5f));
            Assert.That(actor.Position.X, Is.GreaterThan(4));
            Assert.That(actor.Position.Y, Is.Zero.Within(0.01f));
            Assert.That(actor.IsGrounded, Is.True);
        });
    }

    [Test]
    public void SprintMovementCannotTunnelIntoPhysicalBarrier()
    {
        var triangles = Slope(-10, 10).ToList();
        triangles.AddRange(
        [
            new Kn5Triangle(new Vector3(1, -2, -10), new Vector3(1, 3, 10),
                new Vector3(1, 3, -10)),
            new Kn5Triangle(new Vector3(1, -2, -10), new Vector3(1, -2, 10),
                new Vector3(1, 3, 10)),
        ]);
        var simulation = new FpsSimulation(Configuration(),
            [new(0, "Runner", FpsSlotRole.Human)], surface: new FpsArenaSurface(triangles));
        simulation.ClaimHuman(0);
        simulation.ApplyInput(0, new FpsInputCommand(1, new Vector2(1, 0), 0, 0,
            FpsInputButtons.Sprint));

        for (int tick = 0; tick < 20; tick++) simulation.Step(0.05f);

        var actor = simulation.Actors.Single();
        Assert.Multiple(() =>
        {
            Assert.That(actor.Position.X, Is.LessThanOrEqualTo(0.7f));
            Assert.That(actor.GeometryBlocked, Is.True);
        });
    }

    [Test]
    public void JumpPreservesTakeoffMomentumAfterMovementInputIsReleased()
    {
        var simulation = new FpsSimulation(Configuration(),
            [new(0, "Runner", FpsSlotRole.Human)]);
        simulation.ClaimHuman(0);
        simulation.ApplyInput(0, new FpsInputCommand(1, new Vector2(0, 1), 0, 0,
            FpsInputButtons.Sprint | FpsInputButtons.Jump));
        simulation.Step(0.05f);
        float takeoffZ = simulation.Actors.Single().Position.Z;
        simulation.ApplyInput(0, new FpsInputCommand(2, Vector2.Zero, 0, 0,
            FpsInputButtons.Jump));

        for (int tick = 0; tick < 5; tick++) simulation.Step(0.05f);

        var actor = simulation.Actors.Single();
        Assert.Multiple(() =>
        {
            Assert.That(actor.Position.Z, Is.GreaterThan(takeoffZ + 1.5f));
            Assert.That(actor.Position.Y, Is.GreaterThan(actor.GroundY));
        });
    }

    [Test]
    public void CrouchSetsActorStateAndReducesGroundMovementSpeed()
    {
        FpsActorState Run(FpsInputButtons buttons)
        {
            var simulation = new FpsSimulation(Configuration(),
                [new(0, "Player", FpsSlotRole.Human)]);
            simulation.ClaimHuman(0);
            simulation.ApplyInput(0, new FpsInputCommand(1, new Vector2(0, 1), 0, 0, buttons));
            for (int tick = 0; tick < 10; tick++) simulation.Step(0.05f);
            return simulation.Actors.Single();
        }

        var standing = Run(FpsInputButtons.None);
        var crouching = Run(FpsInputButtons.Crouch);
        Assert.Multiple(() =>
        {
            Assert.That(crouching.IsCrouching, Is.True);
            Assert.That(crouching.Position.Z, Is.LessThan(standing.Position.Z * 0.7f));
        });
    }

    [Test]
    public void CrouchingCanClearLowGeometryThatBlocksStandingCapsule()
    {
        var triangles = new List<Kn5Triangle>
        {
            new(new Vector3(-10, 0, -10), new Vector3(-10, 0, 10), new Vector3(10, 0, 10)),
            new(new Vector3(-10, 0, -10), new Vector3(10, 0, 10), new Vector3(10, 0, -10)),
            new(new Vector3(0.8f, 1.3f, -2), new Vector3(0.8f, 1.3f, 2),
                new Vector3(4, 1.3f, 2)),
            new(new Vector3(0.8f, 1.3f, -2), new Vector3(4, 1.3f, 2),
                new Vector3(4, 1.3f, -2)),
        };

        float Run(FpsInputButtons buttons)
        {
            var simulation = new FpsSimulation(Configuration(),
                [new(0, "Player", FpsSlotRole.Human)],
                surface: new FpsArenaSurface(triangles));
            simulation.ClaimHuman(0);
            simulation.ApplyInput(0, new FpsInputCommand(1, new Vector2(1, 0), 0, 0, buttons));
            for (int tick = 0; tick < 30; tick++) simulation.Step(0.05f);
            return simulation.Actors.Single().Position.X;
        }

        Assert.Multiple(() =>
        {
            Assert.That(Run(FpsInputButtons.None), Is.LessThan(0.8f));
            Assert.That(Run(FpsInputButtons.Crouch), Is.GreaterThan(2f));
        });
    }

    [Test]
    public void HoldingCrouchEntersProneAndReleaseKeepsProneUntilJumpExitsToCrouch()
    {
        var simulation = new FpsSimulation(Configuration(),
            [new(0, "Player", FpsSlotRole.Human)]);
        simulation.ClaimHuman(0);
        simulation.ApplyInput(0, new FpsInputCommand(1, Vector2.Zero, 0, 0,
            FpsInputButtons.Crouch));

        for (int tick = 0; tick < 14; tick++) simulation.Step(0.05f);

        var actor = simulation.Actors.Single();
        Assert.That(actor.Stance, Is.EqualTo(FpsStance.Prone));
        simulation.ApplyInput(0, new FpsInputCommand(2, Vector2.Zero, 0, 0,
            FpsInputButtons.None));
        simulation.Step(0.05f);
        Assert.That(actor.Stance, Is.EqualTo(FpsStance.Prone),
            "Releasing crouch must leave prone latched");

        simulation.ApplyInput(0, new FpsInputCommand(3, Vector2.Zero, 0, 0,
            FpsInputButtons.Jump));
        simulation.Step(0.05f);
        Assert.Multiple(() =>
        {
            Assert.That(actor.Stance, Is.EqualTo(FpsStance.Crouching));
            Assert.That(actor.IsGrounded, Is.True, "The prone-exit press must not also jump");
        });
        simulation.ApplyInput(0, new FpsInputCommand(4, Vector2.Zero, 0, 0,
            FpsInputButtons.None));
        simulation.Step(0.05f);
        Assert.That(actor.Stance, Is.EqualTo(FpsStance.Crouching));
    }

    [Test]
    public void PressingCrouchAgainExitsProneToLatchedCrouch()
    {
        var simulation = new FpsSimulation(Configuration(),
            [new(0, "Player", FpsSlotRole.Human)]);
        simulation.ClaimHuman(0);
        simulation.ApplyInput(0, new FpsInputCommand(1, Vector2.Zero, 0, 0,
            FpsInputButtons.Crouch));
        for (int tick = 0; tick < 14; tick++) simulation.Step(0.05f);
        simulation.ApplyInput(0, new FpsInputCommand(2, Vector2.Zero, 0, 0,
            FpsInputButtons.None));
        simulation.Step(0.05f);
        simulation.ApplyInput(0, new FpsInputCommand(3, Vector2.Zero, 0, 0,
            FpsInputButtons.Crouch));
        simulation.Step(0.05f);
        simulation.ApplyInput(0, new FpsInputCommand(4, Vector2.Zero, 0, 0,
            FpsInputButtons.None));
        simulation.Step(0.05f);

        Assert.That(simulation.Actors.Single().Stance, Is.EqualTo(FpsStance.Crouching));
    }

    [Test]
    public void JumpClearsObstacleAboveStepHeight()
    {
        const float obstacleHeight = 0.55f;
        var triangles = new List<Kn5Triangle>
        {
            new(new Vector3(-10, 0, -10), new Vector3(-10, 0, 10), new Vector3(10, 0, 10)),
            new(new Vector3(-10, 0, -10), new Vector3(10, 0, 10), new Vector3(10, 0, -10)),
        };
        triangles.AddRange(
        [
            new Kn5Triangle(new Vector3(0, 0, -2), new Vector3(0, obstacleHeight, 2),
                new Vector3(0, obstacleHeight, -2)),
            new Kn5Triangle(new Vector3(0, 0, -2), new Vector3(0, 0, 2),
                new Vector3(0, obstacleHeight, 2)),
        ]);
        var simulation = new FpsSimulation(Configuration(),
            [new(0, "Jumper", FpsSlotRole.Human)],
            surface: new FpsArenaSurface(triangles));
        simulation.ClaimHuman(0);
        var actor = simulation.Actors.Single();
        actor.Position = new Vector3(-1, 0, 0);
        actor.GroundY = 0;
        simulation.ApplyInput(0, new FpsInputCommand(1, new Vector2(1, 0), 0, 0,
            FpsInputButtons.Sprint | FpsInputButtons.Jump));

        for (int tick = 0; tick < 14; tick++) simulation.Step(0.05f);

        Assert.That(actor.Position.X, Is.GreaterThan(1),
            "An airborne capsule should pass over an obstacle below its feet");
    }

    [Test]
    public void JumpMantlesEyeLevelPlatformAndFinishesCrouchedOnTop()
    {
        const float platformHeight = 1.25f;
        var triangles = new List<Kn5Triangle>
        {
            new(new Vector3(-10, 0, -5), new Vector3(-10, 0, 5), new Vector3(0, 0, 5)),
            new(new Vector3(-10, 0, -5), new Vector3(0, 0, 5), new Vector3(0, 0, -5)),
            new(new Vector3(0, platformHeight, -5), new Vector3(0, platformHeight, 5),
                new Vector3(10, platformHeight, 5)),
            new(new Vector3(0, platformHeight, -5), new Vector3(10, platformHeight, 5),
                new Vector3(10, platformHeight, -5)),
            new(new Vector3(0, 0, -5), new Vector3(0, platformHeight, 5),
                new Vector3(0, platformHeight, -5)),
            new(new Vector3(0, 0, -5), new Vector3(0, 0, 5),
                new Vector3(0, platformHeight, 5)),
        };
        var simulation = new FpsSimulation(Configuration(),
            [new(0, "Climber", FpsSlotRole.Human)],
            surface: new FpsArenaSurface(triangles));
        simulation.ClaimHuman(0);
        var actor = simulation.Actors.Single();
        actor.Position = new Vector3(-0.5f, 0, 0);
        actor.GroundY = 0;
        simulation.ApplyInput(0, new FpsInputCommand(1, new Vector2(1, 0), 0, 0,
            FpsInputButtons.Jump));

        simulation.Step(0.05f);
        Assert.That(actor.IsMantling, Is.True);
        for (int tick = 0; tick < 10; tick++) simulation.Step(0.05f);

        Assert.Multiple(() =>
        {
            Assert.That(FpsArenaSurface.MaximumMantleHeight, Is.EqualTo(1.75f));
            Assert.That(actor.IsMantling, Is.False);
            Assert.That(actor.Position.X, Is.GreaterThan(0));
            Assert.That(actor.Position.Y, Is.EqualTo(platformHeight).Within(0.01f));
            Assert.That(actor.Stance, Is.EqualTo(FpsStance.Crouching));
            Assert.That(actor.IsGrounded, Is.True);
        });
    }

    [Test]
    public void HoldingJumpMantlesNearbyPlatformWithoutMovementInput()
    {
        const float platformHeight = 1.55f;
        var triangles = new List<Kn5Triangle>
        {
            new(new Vector3(-10, 0, -5), new Vector3(-10, 0, 5), new Vector3(0, 0, 5)),
            new(new Vector3(-10, 0, -5), new Vector3(0, 0, 5), new Vector3(0, 0, -5)),
            new(new Vector3(0, platformHeight, -5), new Vector3(0, platformHeight, 5),
                new Vector3(10, platformHeight, 5)),
            new(new Vector3(0, platformHeight, -5), new Vector3(10, platformHeight, 5),
                new Vector3(10, platformHeight, -5)),
            new(new Vector3(0, 0, -5), new Vector3(0, platformHeight, 5),
                new Vector3(0, platformHeight, -5)),
            new(new Vector3(0, 0, -5), new Vector3(0, 0, 5),
                new Vector3(0, platformHeight, 5)),
        };
        var simulation = new FpsSimulation(Configuration(),
            [new(0, "Climber", FpsSlotRole.Human)],
            surface: new FpsArenaSurface(triangles));
        simulation.ClaimHuman(0);
        var actor = simulation.Actors.Single();
        actor.Position = new Vector3(-0.5f, 0, 0);
        actor.GroundY = 0;
        simulation.ApplyInput(0, new FpsInputCommand(1, Vector2.Zero, MathF.PI / 2, 0,
            FpsInputButtons.Jump));

        simulation.Step(0.05f);

        Assert.That(actor.IsMantling, Is.True,
            "Held jump must probe the viewed ledge even before movement reports a collision");
    }

    [Test]
    public void JumpReachesAboveOneAndAHalfMetres()
    {
        var simulation = new FpsSimulation(Configuration(),
            [new(0, "Jumper", FpsSlotRole.Human)]);
        simulation.ClaimHuman(0);
        var actor = simulation.Actors.Single();
        float startY = actor.Position.Y;
        simulation.ApplyInput(0, new FpsInputCommand(1, Vector2.Zero, 0, 0,
            FpsInputButtons.Jump));

        float maximumY = startY;
        for (int tick = 0; tick < 30; tick++)
        {
            simulation.Step(0.05f);
            maximumY = MathF.Max(maximumY, actor.Position.Y);
        }

        Assert.That(maximumY - startY, Is.GreaterThan(1.5f));
    }

    [Test]
    public void JumpVaultsThinWaistHighBarrierAndFinishesStandingBeyondIt()
    {
        const float barrierHeight = 0.9f;
        var triangles = new List<Kn5Triangle>
        {
            new(new Vector3(-10, 0, -10), new Vector3(-10, 0, 10), new Vector3(10, 0, 10)),
            new(new Vector3(-10, 0, -10), new Vector3(10, 0, 10), new Vector3(10, 0, -10)),
        };
        triangles.AddRange(
        [
            new Kn5Triangle(new Vector3(0, 0, -2), new Vector3(0, barrierHeight, 2),
                new Vector3(0, barrierHeight, -2)),
            new Kn5Triangle(new Vector3(0, 0, -2), new Vector3(0, 0, 2),
                new Vector3(0, barrierHeight, 2)),
        ]);
        var simulation = new FpsSimulation(Configuration(),
            [new(0, "Vaulter", FpsSlotRole.Human)],
            surface: new FpsArenaSurface(triangles));
        simulation.ClaimHuman(0);
        var actor = simulation.Actors.Single();
        actor.Position = new Vector3(-0.5f, 0, 0);
        actor.GroundY = 0;
        simulation.ApplyInput(0, new FpsInputCommand(1, new Vector2(1, 0), 0, 0,
            FpsInputButtons.Jump));

        simulation.Step(0.05f);
        Assert.That(actor.IsMantling, Is.True);
        for (int tick = 0; tick < 10; tick++) simulation.Step(0.05f);

        Assert.Multiple(() =>
        {
            Assert.That(FpsArenaSurface.MaximumVaultHeight, Is.EqualTo(1.15f));
            Assert.That(actor.IsMantling, Is.False);
            Assert.That(actor.Position.X, Is.GreaterThan(0.35f));
            Assert.That(actor.Position.Y, Is.Zero.Within(0.01f));
            Assert.That(actor.Stance, Is.EqualTo(FpsStance.Standing));
        });
    }

    [Test]
    public void JumpCannotVaultBarrierAboveMaximumVaultHeight()
    {
        const float barrierHeight = 2f;
        var triangles = new List<Kn5Triangle>
        {
            new(new Vector3(-10, 0, -10), new Vector3(-10, 0, 10), new Vector3(10, 0, 10)),
            new(new Vector3(-10, 0, -10), new Vector3(10, 0, 10), new Vector3(10, 0, -10)),
        };
        triangles.AddRange(
        [
            new Kn5Triangle(new Vector3(0, 0, -2), new Vector3(0, barrierHeight, 2),
                new Vector3(0, barrierHeight, -2)),
            new Kn5Triangle(new Vector3(0, 0, -2), new Vector3(0, 0, 2),
                new Vector3(0, barrierHeight, 2)),
        ]);
        var simulation = new FpsSimulation(Configuration(),
            [new(0, "Jumper", FpsSlotRole.Human)],
            surface: new FpsArenaSurface(triangles));
        simulation.ClaimHuman(0);
        var actor = simulation.Actors.Single();
        actor.Position = new Vector3(-0.5f, 0, 0);
        actor.GroundY = 0;
        simulation.ApplyInput(0, new FpsInputCommand(1, new Vector2(1, 0), 0, 0,
            FpsInputButtons.Jump));

        simulation.Step(0.05f);

        Assert.That(actor.IsMantling, Is.False);
    }

    [Test]
    public void ServerValidatedHitsKillRespawnAndEndScoreLimitedMatch()
    {
        var configuration = Configuration(killLimit: 1);
        var simulation = new FpsSimulation(configuration,
        [
            new(0, "Human", FpsSlotRole.Human),
            new(1, "Target", FpsSlotRole.Human),
        ]);
        simulation.ClaimHuman(0);
        simulation.ClaimHuman(1);
        const float pitchToChest = -0.129f;
        Assert.That(simulation.ApplyInput(0, new FpsInputCommand(1, Vector2.Zero, 0,
            pitchToChest, FpsInputButtons.Fire)), Is.True);
        for (int tick = 0; tick < 8; tick++) simulation.Step(0.05f);

        var attacker = simulation.Actors.Single(actor => actor.Id == 0);
        var victim = simulation.Actors.Single(actor => actor.Id == 1);
        Assert.Multiple(() =>
        {
            Assert.That(attacker.Kills, Is.EqualTo(1));
            Assert.That(victim.Deaths, Is.EqualTo(1));
            Assert.That(victim.Dead, Is.True);
            Assert.That(simulation.MatchState, Is.EqualTo(FpsMatchState.Finished));
            Assert.That(simulation.WinnerId, Is.EqualTo(0));
        });
    }

    [Test]
    public void AuthoritativeRifleEmitsShotsForMissesAndBuildsDeterministicSpread()
    {
        var simulation = new FpsSimulation(Configuration(),
            [new(0, "Shooter", FpsSlotRole.Human)]);
        simulation.ClaimHuman(0);
        simulation.ApplyInput(0, new FpsInputCommand(1, Vector2.Zero, 0, 0,
            FpsInputButtons.Fire));

        simulation.Step(0.05f);
        var first = simulation.ShotEvents.Single();
        FpsShotEvent second = default;
        for (int tick = 0; tick < 4; tick++)
        {
            simulation.Step(0.05f);
            if (simulation.ShotEvents.Count > 0) second = simulation.ShotEvents.Single();
        }

        Assert.Multiple(() =>
        {
            Assert.That(first.ShooterId, Is.Zero);
            Assert.That(first.Sequence, Is.EqualTo(1));
            Assert.That(first.Distance, Is.EqualTo(120).Within(0.001f));
            Assert.That(second.Sequence, Is.EqualTo(2));
            Assert.That(second.Direction, Is.Not.EqualTo(first.Direction),
                "Sustained automatic fire should accumulate server-side spread");
        });
    }

    [Test]
    public void PreparedArenaGeometryBlocksRifleDamageAndShortensTracer()
    {
        var triangles = new List<Kn5Triangle>
        {
            new(new Vector3(-10, 0, -10), new Vector3(-10, 0, 10), new Vector3(10, 0, 10)),
            new(new Vector3(-10, 0, -10), new Vector3(10, 0, 10), new Vector3(10, 0, -10)),
            new(new Vector3(-2, 0, 2), new Vector3(2, 3, 2), new Vector3(-2, 3, 2)),
            new(new Vector3(-2, 0, 2), new Vector3(2, 0, 2), new Vector3(2, 3, 2)),
        };
        var simulation = new FpsSimulation(Configuration(),
        [
            new(0, "Shooter", FpsSlotRole.Human),
            new(1, "Target", FpsSlotRole.Human),
        ], surface: new FpsArenaSurface(triangles));
        simulation.ClaimHuman(0);
        simulation.ClaimHuman(1);
        simulation.ApplyInput(0, new FpsInputCommand(1, Vector2.Zero, 0, 0,
            FpsInputButtons.Fire));

        simulation.Step(0.05f);

        Assert.Multiple(() =>
        {
            Assert.That(simulation.Actors.Single(actor => actor.Id == 1).Health,
                Is.EqualTo(100));
            Assert.That(simulation.ShotEvents.Single().Distance, Is.EqualTo(2).Within(0.02f));
            Assert.That(simulation.HitEvents, Is.Empty);
        });
    }

    [Test]
    public void SpawnProtectionBlocksDamageThenVictimRespawnsAtSafeSpawn()
    {
        var configuration = Configuration(killLimit: 20);
        configuration = configuration.WithProtection(0.5f);
        var simulation = new FpsSimulation(configuration,
        [
            new(0, "Shooter", FpsSlotRole.Human),
            new(1, "Victim", FpsSlotRole.Human),
        ]);
        simulation.ClaimHuman(0);
        simulation.ClaimHuman(1);
        simulation.ApplyInput(0, new FpsInputCommand(1, Vector2.Zero, 0, -0.129f,
            FpsInputButtons.Fire));

        for (int tick = 0; tick < 9; tick++) simulation.Step(0.05f);
        Assert.That(simulation.Actors.Single(actor => actor.Id == 1).Health, Is.EqualTo(100));
        for (int tick = 0; tick < 16 && !simulation.Actors.Single(actor => actor.Id == 1).Dead; tick++)
            simulation.Step(0.05f);
        Assert.That(simulation.Actors.Single(actor => actor.Id == 1).Dead, Is.True);
        for (int tick = 0; tick < 6; tick++) simulation.Step(0.05f);
        var respawned = simulation.Actors.Single(actor => actor.Id == 1);
        Assert.Multiple(() =>
        {
            Assert.That(respawned.Dead, Is.False);
            Assert.That(respawned.Health, Is.EqualTo(100));
            Assert.That(respawned.SpawnProtectionRemaining, Is.GreaterThan(0));
        });
    }

    [Test]
    public void AutomaticRacecraftVarianceIsStableAndPerSlotOverridesWin()
    {
        var configuration = Configuration(difficulty: 0.7f, difficultyVariance: 20,
            aggression: 0.5f, aggressionVariance: 30);
        FpsSimulation Create() => new(configuration,
        [
            new(0, "Automatic", FpsSlotRole.Bot),
            new(1, "Override", FpsSlotRole.Bot, 0.99f, 0.12f),
        ]);

        var first = Create().Actors.OrderBy(actor => actor.Id).ToArray();
        var second = Create().Actors.OrderBy(actor => actor.Id).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(first[0].Difficulty, Is.EqualTo(second[0].Difficulty));
            Assert.That(first[0].Aggression, Is.EqualTo(second[0].Aggression));
            Assert.That(first[0].Difficulty, Is.InRange(0.56f, 0.84f));
            Assert.That(first[0].Aggression, Is.InRange(0.35f, 0.65f));
            Assert.That(first[1].Difficulty, Is.EqualTo(0.99f));
            Assert.That(first[1].Aggression, Is.EqualTo(0.12f));
        });
    }

    private static FpsSimulation CreateSimulation(FpsSlotRole first, FpsSlotRole second) =>
        new(Configuration(), [new(0, "First", first), new(1, "Second", second)]);

    private static Kn5Triangle[] Slope(float minX, float maxX)
    {
        float minY = minX * 0.2f;
        float maxY = maxX * 0.2f;
        return
        [
            new(new Vector3(minX, minY, -10), new Vector3(minX, minY, 10),
                new Vector3(maxX, maxY, 10)),
            new(new Vector3(minX, minY, -10), new Vector3(maxX, maxY, 10),
                new Vector3(maxX, maxY, -10)),
        ];
    }

    private static Kn5Triangle[] Incline(float degrees)
    {
        float grade = MathF.Tan(degrees * MathF.PI / 180);
        return
        [
            new(new Vector3(-10, -10 * grade, -10), new Vector3(-10, -10 * grade, 10),
                new Vector3(10, 10 * grade, 10)),
            new(new Vector3(-10, -10 * grade, -10), new Vector3(10, 10 * grade, 10),
                new Vector3(10, 10 * grade, -10)),
        ];
    }

    private static FpsConfiguration Configuration(int killLimit = 20, float difficulty = 0,
        float difficultyVariance = 0, float aggression = 0, float aggressionVariance = 0) => new()
    {
        Enabled = true,
        TimeLimitMinutes = 10,
        KillLimit = killLimit,
        RespawnSeconds = 0.2f,
        SpawnProtectionSeconds = 0,
        Bots = new FpsBotConfiguration
        {
            Health = 100,
            Difficulty = difficulty,
            DifficultyVariancePercent = difficultyVariance,
            Aggression = aggression,
            AggressionVariancePercent = aggressionVariance,
        },
        Arena = new FpsArenaConfiguration
        {
            BoundsMin = new Vector3(-10, -2, -10),
            BoundsMax = new Vector3(10, 10, 10),
            SpawnPoints =
            [
                new FpsSpawnConfiguration { Position = Vector3.Zero, YawRadians = 0 },
                new FpsSpawnConfiguration { Position = new Vector3(0, 0, 5), YawRadians = MathF.PI },
            ],
        },
    };
}

internal static class FpsConfigurationTestExtensions
{
    public static FpsConfiguration WithProtection(this FpsConfiguration source, float seconds) => new()
    {
        Enabled = source.Enabled,
        TimeLimitMinutes = source.TimeLimitMinutes,
        KillLimit = source.KillLimit,
        RespawnSeconds = source.RespawnSeconds,
        SpawnProtectionSeconds = seconds,
        Bots = source.Bots,
        Arena = source.Arena,
    };
}
