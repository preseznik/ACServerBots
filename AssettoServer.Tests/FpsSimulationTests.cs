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
    public void LiveMatchSnapshotUsesAuthoritativeActorsAndNames()
    {
        var simulation = new FpsSimulation(Configuration(difficulty: 0.4f),
            [new(0, "Arena Bot", FpsSlotRole.Bot), new(1, "Human Player", FpsSlotRole.Human)]);
        Assert.That(simulation.ClaimHuman(1), Is.True);
        Assert.That(simulation.ApplyInput(1, new FpsInputCommand(1, Vector2.UnitY, 0.5f, 0,
            FpsInputButtons.Sprint)), Is.True);
        for (int tick = 0; tick < 12; tick++) simulation.Step(0.05f);

        FpsLiveMatchSnapshot snapshot = FpsWorld.CreateLiveMatchSnapshot(simulation, 25);
        FpsLiveActorSnapshot bot = snapshot.Actors.Single(actor => actor.Id == 0);
        FpsLiveActorSnapshot human = snapshot.Actors.Single(actor => actor.Id == 1);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.KillLimit, Is.EqualTo(25));
            Assert.That(snapshot.RemainingSeconds, Is.LessThan(600));
            Assert.That(bot.Name, Is.EqualTo("Arena Bot"));
            Assert.That(bot.IsBot, Is.True);
            Assert.That(bot.Position,
                Is.EqualTo(simulation.Actors.Single(actor => actor.Id == 0).Position));
            Assert.That(human.Name, Is.EqualTo("Human Player"));
            Assert.That(human.IsBot, Is.False);
            Assert.That(human.Active, Is.True);
            Assert.That(human.Velocity.LengthSquared(), Is.GreaterThan(0));
            Assert.That(human.Position,
                Is.EqualTo(simulation.Actors.Single(actor => actor.Id == 1).Position));
        });
    }

    [Test]
    public void SnapshotCollisionDirectionUsesCompactSentinelAndPreservesDirection()
    {
        Assert.That(FpsWorld.EncodeCollisionDirection(Vector2.Zero), Is.EqualTo(byte.MaxValue));

        foreach (var direction in new[]
                 {
                     Vector2.UnitX, Vector2.UnitY, -Vector2.UnitX, -Vector2.UnitY,
                     Vector2.Normalize(new Vector2(1, 1)),
                 })
        {
            byte encoded = FpsWorld.EncodeCollisionDirection(direction);
            float decodedAngle = encoded / 254f * MathF.Tau - MathF.PI;
            var decoded = new Vector2(MathF.Cos(decodedAngle), MathF.Sin(decodedAngle));

            Assert.Multiple(() =>
            {
                Assert.That(encoded, Is.LessThan(byte.MaxValue));
                Assert.That(Vector2.Dot(direction, decoded), Is.GreaterThan(0.999f));
            });
        }
    }

    [Test]
    public void BotSlotsSpawnPursueAndFightAuthoritatively()
    {
        var simulation = new FpsSimulation(Configuration(difficulty: 1),
            [new(0, "First", FpsSlotRole.Bot), new(1, "Second", FpsSlotRole.Bot)]);
        var initial = simulation.Actors.OrderBy(actor => actor.Id)
            .Select(actor => actor.Position).ToArray();
        bool emittedShot = false;

        for (int tick = 0; tick < 120; tick++)
        {
            simulation.Step(0.05f);
            emittedShot |= simulation.ShotEvents.Count > 0;
        }

        Assert.Multiple(() =>
        {
            Assert.That(simulation.Actors, Has.Count.EqualTo(2));
            Assert.That(simulation.Actors, Has.All.Matches<FpsActorState>(actor =>
                actor.Active && !actor.HumanControlled && actor.SpawnCount >= 1));
            Assert.That(simulation.Actors.OrderBy(actor => actor.Id)
                    .Select((actor, index) => Vector3.Distance(actor.Position, initial[index]))
                    .Any(distance => distance > 0.5f), Is.True);
            Assert.That(emittedShot, Is.True);
            Assert.That(simulation.Actors.Sum(actor => actor.Kills), Is.GreaterThan(0));
        });
    }

    [Test]
    public void BotRoutesAroundWallAndOnlyDamagesTargetAfterLineOfSight()
    {
        var triangles = new List<Kn5Triangle>();
        triangles.AddRange(FlatFloor(-5, 5, -4, 4, 0));
        triangles.AddRange(VerticalWall(0, -1.2f, 1.2f, 0, 2.5f));
        var surface = new FpsArenaSurface(triangles);
        var spawns = new[]
        {
            new FpsSpawnConfiguration { Position = new Vector3(-3, 0, 0) },
            new FpsSpawnConfiguration { Position = new Vector3(3, 0, 0), YawRadians = MathF.PI },
        };
        var navigation = FpsArenaNavigationBuilder.Build(surface,
            new FpsArenaPoint(-5, -1, -4), new FpsArenaPoint(5, 4, 4),
            [new(new FpsArenaPoint(-3, 0, 0), 0),
                new(new FpsArenaPoint(3, 0, 0), MathF.PI)]).Asset;
        var configuration = new FpsConfiguration
        {
            Enabled = true,
            TimeLimitMinutes = 10,
            KillLimit = 1,
            RespawnSeconds = 0.2f,
            SpawnProtectionSeconds = 0,
            Bots = new FpsBotConfiguration { Difficulty = 1, Aggression = 0.7f, Health = 100 },
            Arena = new FpsArenaConfiguration
            {
                BoundsMin = new Vector3(-5, -1, -4),
                BoundsMax = new Vector3(5, 4, 4),
                SpawnPoints = [.. spawns],
            },
        };
        var simulation = new FpsSimulation(configuration,
            [new(0, "Hunter", FpsSlotRole.Bot), new(1, "Player", FpsSlotRole.Human)],
            surface: surface, navigation: navigation);
        Assert.That(simulation.ClaimHuman(1), Is.True);

        Vector3? firstHitPosition = null;
        for (int tick = 0; tick < 500 && simulation.MatchState != FpsMatchState.Finished; tick++)
        {
            simulation.Step(0.05f);
            if (firstHitPosition is null && simulation.HitEvents.Count > 0)
                firstHitPosition = simulation.Actors.Single(actor => actor.Id == 0).Position;
        }

        var bot = simulation.Actors.Single(actor => actor.Id == 0);
        var player = simulation.Actors.Single(actor => actor.Id == 1);
        Assert.Multiple(() =>
        {
            Assert.That(bot.Kills, Is.EqualTo(1));
            Assert.That(player.Deaths, Is.EqualTo(1));
            Assert.That(firstHitPosition, Is.Not.Null);
            Assert.That(MathF.Abs(firstHitPosition!.Value.Z), Is.GreaterThan(0.8f),
                "The bot must route toward an end of the wall before it gains a valid shot.");
        });
    }

    [Test]
    public void BlockedCombatStrafeFallsBackToReachablePath()
    {
        var triangles = new List<Kn5Triangle>();
        triangles.AddRange(FlatFloor(-1, 1, -2, 12, 0));
        triangles.AddRange(VerticalWall(-0.55f, -2, 12, 0, 2.5f));
        triangles.AddRange(VerticalWall(0.55f, -2, 12, 0, 2.5f));
        var nodes = Enumerable.Range(0, 5).Select(index => new FpsNavigationNode
        {
            Position = new Vector3(0, 0, index * 2),
            Component = 0,
        }).ToArray();
        for (int index = 0; index < nodes.Length - 1; index++)
        {
            nodes[index].Edges.Add(new FpsNavigationEdge(index + 1,
                FpsNavigationLinkKind.Walk, 2));
            nodes[index + 1].Edges.Add(new FpsNavigationEdge(index,
                FpsNavigationLinkKind.Walk, 2));
        }
        var navigation = new FpsArenaNavigationAsset
        {
            CellSize = 0.6f,
            Nodes = nodes,
            SpawnNodes = [0, 4],
            ComponentCount = 1,
            PrimaryComponent = 0,
        };
        var configuration = Configuration(difficulty: 1, aggression: 1);
        configuration.Arena.SpawnPoints.Clear();
        configuration.Arena.SpawnPoints.AddRange(
        [
            new FpsSpawnConfiguration { Position = Vector3.Zero },
            new FpsSpawnConfiguration { Position = new Vector3(0, 0, 8),
                YawRadians = MathF.PI },
        ]);
        var simulation = new FpsSimulation(configuration,
            [new(0, "Hunter", FpsSlotRole.Bot), new(1, "Target", FpsSlotRole.Human)],
            surface: new FpsArenaSurface(triangles), navigation: navigation);
        Assert.That(simulation.ClaimHuman(1), Is.True);
        var hunter = simulation.Actors.Single(actor => actor.Id == 0);
        var target = simulation.Actors.Single(actor => actor.Id == 1);
        hunter.BotTargetId = target.Id;
        hunter.BotReactionRemaining = 0;
        hunter.BotSearchRemaining = 8;
        target.Health = 10_000;

        for (int tick = 0; tick < 20; tick++) simulation.Step(0.05f);

        Assert.Multiple(() =>
        {
            Assert.That(hunter.Position.Z, Is.GreaterThan(0.5f),
                "A blocked side-step must not strand a bot in combat mode.");
            Assert.That(MathF.Abs(hunter.Position.X), Is.LessThan(0.25f));
            Assert.That(hunter.BotStuckFailures, Is.Zero);
        });
    }

    [Test]
    public void BotDropsTargetWhenAuthoritativePosesAreOnDisconnectedComponents()
    {
        var navigation = new FpsArenaNavigationAsset
        {
            CellSize = 0.6f,
            Nodes =
            [
                new FpsNavigationNode { Position = Vector3.Zero, Component = 0 },
                new FpsNavigationNode { Position = new Vector3(3, 0, 0), Component = 1 },
            ],
            SpawnNodes = [0, 1],
            ComponentCount = 2,
            PrimaryComponent = 0,
        };
        var simulation = new FpsSimulation(Configuration(difficulty: 1),
            [new(0, "Hunter", FpsSlotRole.Bot), new(1, "Target", FpsSlotRole.Human)],
            surface: new FpsArenaSurface(FlatFloor(-5, 5, -5, 5, 0).ToArray()),
            navigation: navigation);
        Assert.That(simulation.ClaimHuman(1), Is.True);
        var hunter = simulation.Actors.Single(actor => actor.Id == 0);
        var target = simulation.Actors.Single(actor => actor.Id == 1);
        hunter.Position = Vector3.Zero;
        hunter.BotTargetId = target.Id;
        target.Position = new Vector3(3, 0, 0);

        simulation.Step(0.05f);

        Assert.Multiple(() =>
        {
            Assert.That(hunter.BotTargetId, Is.EqualTo(byte.MaxValue));
            Assert.That(simulation.ShotEvents, Is.Empty);
        });
    }

    [Test]
    public void FailedBotPathIsRetriedAtAControlledRate()
    {
        var triangles = new List<Kn5Triangle>();
        triangles.AddRange(FlatFloor(-10, 10, -4, 4, 0));
        triangles.AddRange(VerticalWall(0, -4, 4, 0, 3));
        var surface = new FpsArenaSurface(triangles);
        var navigation = new FpsArenaNavigationAsset
        {
            CellSize = 0.6f,
            Nodes =
            [
                new FpsNavigationNode { Position = new Vector3(-4, 0, -1), Component = 0 },
                new FpsNavigationNode { Position = new Vector3(-4, 0, 1), Component = 0 },
                new FpsNavigationNode { Position = new Vector3(9, 0, 0), Component = 0 },
            ],
            SpawnNodes = [0, 1],
            ComponentCount = 1,
            PrimaryComponent = 0,
        };
        var configuration = Configuration();
        configuration.Arena.SpawnPoints.Clear();
        configuration.Arena.SpawnPoints.AddRange(
        [
            new FpsSpawnConfiguration { Position = new Vector3(-4, 0, -1) },
            new FpsSpawnConfiguration { Position = new Vector3(-4, 0, 1) },
        ]);
        var simulation = new FpsSimulation(configuration,
            [new(0, "Hunter", FpsSlotRole.Bot), new(1, "Target", FpsSlotRole.Human)],
            surface: surface, navigation: navigation);
        Assert.That(simulation.ClaimHuman(1), Is.True);
        var hunter = simulation.Actors.Single(actor => actor.Id == 0);
        var target = simulation.Actors.Single(actor => actor.Id == 1);
        hunter.Position = new Vector3(-3, 0, 0);
        hunter.BotTargetId = target.Id;
        hunter.BotSearchRemaining = 8;
        target.Position = new Vector3(9, 0, 0);
        var sightLine = target.Position + Vector3.UnitY * 0.99f
                        - (hunter.Position + Vector3.UnitY * 1.65f);
        Assert.That(surface.TryRaycast(hunter.Position + Vector3.UnitY * 1.65f,
            Vector3.Normalize(sightLine), sightLine.Length(), out _), Is.True);

        simulation.Step(0.05f);
        Assert.That(simulation.BotDiagnosticEvents.Count(item =>
                item.Message.StartsWith("path-failed", StringComparison.Ordinal)), Is.EqualTo(1),
            string.Join("; ", simulation.BotDiagnosticEvents.Select(item => item.Message)));

        int retries = 0;
        for (int tick = 0; tick < 10; tick++)
        {
            simulation.Step(0.05f);
            retries += simulation.BotDiagnosticEvents.Count(item =>
                item.Message.StartsWith("path-failed", StringComparison.Ordinal));
        }
        Assert.That(retries, Is.Zero,
            "An unreachable target must not cause an A* attempt on every 60 Hz tick.");
    }

    [Test]
    public void StationaryBotIsAnAuthoritativeRifleTarget()
    {
        var configuration = Configuration(killLimit: 1);
        var simulation = new FpsSimulation(configuration,
        [
            new(0, "Shooter", FpsSlotRole.Human),
            new(1, "Stationary target", FpsSlotRole.Bot),
        ]);
        Assert.That(simulation.ClaimHuman(0), Is.True);
        Assert.That(simulation.ApplyInput(0, new FpsInputCommand(1, Vector2.Zero, MathF.PI,
            0, FpsInputButtons.Fire)), Is.True);

        for (int tick = 0; tick < 8; tick++) simulation.Step(0.05f);

        var shooter = simulation.Actors.Single(actor => actor.Id == 0);
        var target = simulation.Actors.Single(actor => actor.Id == 1);
        Assert.Multiple(() =>
        {
            Assert.That(target.HumanControlled, Is.False);
            Assert.That(target.Dead, Is.True);
            Assert.That(target.Deaths, Is.EqualTo(1));
            Assert.That(shooter.Kills, Is.EqualTo(1));
            Assert.That(shooter.Pitch, Is.Zero,
                "A standing target under the crosshair must not require artificial downward aim");
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
    public void StairStepRaisesCapsuleWhenLeadingEdgeReachesRiser()
    {
        const float riserHeight = 0.24f;
        var triangles = new List<Kn5Triangle>
        {
            new(new Vector3(-5, 0, -2), new Vector3(-5, 0, 2), new Vector3(0, 0, 2)),
            new(new Vector3(-5, 0, -2), new Vector3(0, 0, 2), new Vector3(0, 0, -2)),
            new(new Vector3(0, riserHeight, -2), new Vector3(0, riserHeight, 2),
                new Vector3(5, riserHeight, 2)),
            new(new Vector3(0, riserHeight, -2), new Vector3(5, riserHeight, 2),
                new Vector3(5, riserHeight, -2)),
            new(new Vector3(0, 0, -2), new Vector3(0, riserHeight, 2),
                new Vector3(0, riserHeight, -2)),
            new(new Vector3(0, 0, -2), new Vector3(0, 0, 2),
                new Vector3(0, riserHeight, 2)),
        };
        var surface = new FpsArenaSurface(triangles);
        var current = new Vector3(-0.45f, 0, 0);
        var desired = new Vector3(-0.35f, 0, 0);

        bool moved = surface.TryResolveMove(current, desired, 0, 1.8f,
            out var resolved, out float groundY);

        Assert.Multiple(() =>
        {
            Assert.That(moved, Is.True);
            Assert.That(resolved.X, Is.EqualTo(desired.X).Within(0.001f));
            Assert.That(resolved.X, Is.LessThan(0),
                "The capsule centre has not reached the tread yet");
            Assert.That(groundY, Is.EqualTo(riserHeight).Within(0.001f),
                "The capsule must step up when its leading edge meets the riser");
            Assert.That(surface.IsPositionBlocked(resolved with { Y = groundY }, groundY, 1.8f),
                Is.False);
        });
    }

    [Test]
    public void MovementStepsDownRepeatedStairRisersWithoutSticking()
    {
        const float riserHeight = 0.24f;
        const float treadDepth = 0.42f;
        var triangles = new List<Kn5Triangle>
        {
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

        var surface = new FpsArenaSurface(triangles);
        var simulation = new FpsSimulation(Configuration(),
            [new(0, "Stair descender", FpsSlotRole.Human)], surface: surface);
        simulation.ClaimHuman(0);
        var actor = simulation.Actors.Single();
        actor.Position = new Vector3(3, topY, 0);
        actor.GroundY = topY;
        simulation.ApplyInput(0, new FpsInputCommand(1, new Vector2(-1, 0), 0, 0,
            FpsInputButtons.None));

        for (int tick = 0; tick < 30; tick++) simulation.Step(0.05f);

        Assert.Multiple(() =>
        {
            Assert.That(actor.Position.X, Is.LessThan(-0.5f));
            Assert.That(actor.Position.Y, Is.Zero.Within(0.01f));
            Assert.That(actor.IsGrounded, Is.True);
        });
    }

    [Test]
    public void DeepBackedShortTreadsRemainValidCapsuleSupports()
    {
        var surface = new FpsArenaSurface(FirePitStairs());
        const float supportY = 2.1363635f;
        var position = new Vector3(0, supportY, -3.736f);

        Assert.That(surface.IsPositionBlocked(position, supportY, 1.8f), Is.False,
            "A valid stair tread must not become a blocked safe pose merely because the "
            + "capsule overlaps the next deep-backed riser");
    }

    [Test]
    public void MovementTraversesDeepBackedShortTreadsInBothDirections()
    {
        var surface = new FpsArenaSurface(FirePitStairs());
        var simulation = new FpsSimulation(Configuration(),
            [new(0, "Fire Pit stair walker", FpsSlotRole.Human)], surface: surface);
        simulation.ClaimHuman(0);
        var actor = simulation.Actors.Single();
        actor.Position = new Vector3(0, 1, -5.4f);
        actor.GroundY = 1;
        simulation.ApplyInput(0, new FpsInputCommand(1, new Vector2(0, 1), 0, 0,
            FpsInputButtons.None));

        for (int tick = 0; tick < 60; tick++) simulation.Step(0.05f);
        float topZ = actor.Position.Z;
        float topY = actor.Position.Y;

        simulation.ApplyInput(0, new FpsInputCommand(2, new Vector2(0, -1), 0, 0,
            FpsInputButtons.None));
        for (int tick = 0; tick < 60; tick++) simulation.Step(0.05f);

        Assert.Multiple(() =>
        {
            Assert.That(topZ, Is.GreaterThan(-2.5f));
            Assert.That(topY, Is.GreaterThan(2.5f));
            Assert.That(actor.Position.Z, Is.LessThan(-5));
            Assert.That(actor.Position.Y, Is.EqualTo(1).Within(0.01f));
            Assert.That(actor.IsGrounded, Is.True);
        });
    }

    [Test]
    public void NarrowStepSupportDoesNotOscillateOrTrapCapsule()
    {
        const float wallHeight = 0.32f;
        const float wallWidth = 0.22f;
        var triangles = new List<Kn5Triangle>
        {
            new(new Vector3(-5, 0, -2), new Vector3(-5, 0, 2), new Vector3(5, 0, 2)),
            new(new Vector3(-5, 0, -2), new Vector3(5, 0, 2), new Vector3(5, 0, -2)),
            new(new Vector3(0, wallHeight, -2), new Vector3(0, wallHeight, 2),
                new Vector3(wallWidth, wallHeight, 2)),
            new(new Vector3(0, wallHeight, -2), new Vector3(wallWidth, wallHeight, 2),
                new Vector3(wallWidth, wallHeight, -2)),
            new(new Vector3(0, 0, -2), new Vector3(0, wallHeight, 2),
                new Vector3(0, wallHeight, -2)),
            new(new Vector3(0, 0, -2), new Vector3(0, 0, 2),
                new Vector3(0, wallHeight, 2)),
            new(new Vector3(wallWidth, 0, -2), new Vector3(wallWidth, wallHeight, -2),
                new Vector3(wallWidth, wallHeight, 2)),
            new(new Vector3(wallWidth, 0, -2), new Vector3(wallWidth, wallHeight, 2),
                new Vector3(wallWidth, 0, 2)),
        };
        var simulation = new FpsSimulation(Configuration(),
            [new(0, "Wall crosser", FpsSlotRole.Human)],
            surface: new FpsArenaSurface(triangles));
        simulation.ClaimHuman(0);
        var actor = simulation.Actors.Single();
        actor.Position = new Vector3(-1, 0, 0);
        actor.GroundY = 0;
        simulation.ApplyInput(0, new FpsInputCommand(1, new Vector2(1, 0), 0, 0,
            FpsInputButtons.None));
        var supports = new List<float> { actor.GroundY };

        for (int tick = 0; tick < 20; tick++)
        {
            simulation.Step(0.05f);
            if (MathF.Abs(supports[^1] - actor.GroundY) > 0.01f) supports.Add(actor.GroundY);
        }

        Assert.Multiple(() =>
        {
            Assert.That(actor.Position.X, Is.GreaterThan(2));
            Assert.That(actor.Position.Y, Is.Zero.Within(0.01f));
            Assert.That(supports, Is.EqualTo(new[] { 0f, wallHeight, 0f }),
                "Crossing one narrow support must produce one rise and one descent");
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
    public void AirborneCapsuleClearsWallSideInsteadOfLoopingAtLastSafeHeight()
    {
        const float wallHalfWidth = 0.2f;
        const float wallHeight = 2;
        var triangles = new List<Kn5Triangle>
        {
            new(new Vector3(-10, 0, -10), new Vector3(-10, 0, 10),
                new Vector3(10, 0, 10)),
            new(new Vector3(-10, 0, -10), new Vector3(10, 0, 10),
                new Vector3(10, 0, -10)),
        };
        triangles.AddRange(
        [
            new Kn5Triangle(new Vector3(-wallHalfWidth, wallHeight, -5),
                new Vector3(-wallHalfWidth, wallHeight, 5),
                new Vector3(wallHalfWidth, wallHeight, 5)),
            new Kn5Triangle(new Vector3(-wallHalfWidth, wallHeight, -5),
                new Vector3(wallHalfWidth, wallHeight, 5),
                new Vector3(wallHalfWidth, wallHeight, -5)),
            new Kn5Triangle(new Vector3(-wallHalfWidth, 0, -5),
                new Vector3(-wallHalfWidth, wallHeight, 5),
                new Vector3(-wallHalfWidth, wallHeight, -5)),
            new Kn5Triangle(new Vector3(-wallHalfWidth, 0, -5),
                new Vector3(-wallHalfWidth, 0, 5),
                new Vector3(-wallHalfWidth, wallHeight, 5)),
            new Kn5Triangle(new Vector3(wallHalfWidth, 0, -5),
                new Vector3(wallHalfWidth, wallHeight, -5),
                new Vector3(wallHalfWidth, wallHeight, 5)),
            new Kn5Triangle(new Vector3(wallHalfWidth, 0, -5),
                new Vector3(wallHalfWidth, wallHeight, 5),
                new Vector3(wallHalfWidth, 0, 5)),
        ]);
        var surface = new FpsArenaSurface(triangles);
        var simulation = new FpsSimulation(Configuration(),
            [new(0, "Wall jumper", FpsSlotRole.Human)], surface: surface);
        simulation.ClaimHuman(0);
        var actor = simulation.Actors.Single();
        actor.Position = new Vector3(0, wallHeight + 0.2f, 0);
        actor.GroundY = 0;
        actor.IsGrounded = false;
        actor.VerticalVelocity = -1;

        for (int tick = 0; tick < 50; tick++) simulation.Step(0.05f);

        Assert.Multiple(() =>
        {
            Assert.That(MathF.Abs(actor.Position.X),
                Is.GreaterThanOrEqualTo(wallHalfWidth + FpsArenaSurface.ActorRadius - 0.01f),
                "The airborne capsule must be pushed clear of the wall side");
            Assert.That(actor.Position.Y, Is.Zero.Within(0.01f));
            Assert.That(actor.IsGrounded, Is.True);
            Assert.That(surface.IsPositionBlocked(actor.Position, actor.GroundY, 1.8f),
                Is.False);
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
            Assert.That(actor.Position.X,
                Is.LessThanOrEqualTo(1 - FpsArenaSurface.ActorRadius + 0.001f));
            Assert.That(actor.GeometryBlocked, Is.True);
            Assert.That(actor.CollisionNormal.X, Is.GreaterThan(0.9f));
            Assert.That(MathF.Abs(actor.CollisionNormal.Y), Is.LessThan(0.1f));
        });
    }

    [Test]
    public void DiagonalBarrierProjectsMovementAlongContactNormal()
    {
        var triangles = Slope(-10, 10).ToList();
        triangles.AddRange(
        [
            new Kn5Triangle(new Vector3(-5, -2, -5), new Vector3(5, 3, 5),
                new Vector3(5, -2, 5)),
            new Kn5Triangle(new Vector3(-5, -2, -5), new Vector3(-5, 3, -5),
                new Vector3(5, 3, 5)),
        ]);
        var surface = new FpsArenaSurface(triangles);

        bool moved = surface.TryResolveMove(new Vector3(-1, -0.2f, 0),
            new Vector3(1, -0.2f, 0), -0.2f, 1.8f,
            out var resolved, out _);

        Assert.Multiple(() =>
        {
            Assert.That(moved, Is.True);
            Assert.That(resolved.Z, Is.GreaterThan(0.25f),
                "A diagonal contact must slide along the wall instead of stopping on a world axis");
            Assert.That(resolved.X - resolved.Z, Is.LessThanOrEqualTo(-0.45f),
                "The capsule must remain outside the diagonal wall");
        });
    }

    [Test]
    public void EmbeddedActorReturnsToLastKnownSafePose()
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
            [new(0, "Recovery", FpsSlotRole.Human)],
            surface: new FpsArenaSurface(triangles));
        simulation.ClaimHuman(0);
        var actor = simulation.Actors.Single();
        var safe = actor.LastSafePosition;
        actor.Position = new Vector3(1, 0.2f, 0);
        actor.GroundY = 0.2f;

        simulation.Step(0.05f);

        Assert.Multiple(() =>
        {
            Assert.That(actor.Position, Is.EqualTo(safe));
            Assert.That(actor.GeometryBlocked, Is.True);
            Assert.That(actor.HorizontalVelocity, Is.EqualTo(Vector2.Zero));
        });
    }

    [Test]
    public void ArenaCollisionSelectionIncludesSolidVisualsAndHonorsOverrides()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FpsArenaAssetBuilder.ShouldIncludeMesh("1ROAD", false), Is.True);
            Assert.That(FpsArenaAssetBuilder.ShouldIncludeMesh("FPV_042_Stair", false), Is.True);
            Assert.That(FpsArenaAssetBuilder.ShouldIncludeMesh("FPB_00_Billboard", false), Is.False);
            Assert.That(FpsArenaAssetBuilder.ShouldIncludeMesh("TREE_LINE", false), Is.False);
            Assert.That(FpsArenaAssetBuilder.ShouldIncludeMesh("custom_crate", false,
                ["custom_*"], []), Is.True);
            Assert.That(FpsArenaAssetBuilder.ShouldIncludeMesh("FPV_042_Stair", false,
                [], ["FPV_042*"]), Is.False);
            Assert.That(FpsArenaAssetBuilder.ShouldIncludeMesh("generic_proxy", true), Is.True);
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
    public void JumpLandsOnRaisedPlatformAboveStepHeight()
    {
        const float platformHeight = 0.75f;
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
        var surface = new FpsArenaSurface(triangles);
        var simulation = new FpsSimulation(Configuration(),
            [new(0, "Platform jumper", FpsSlotRole.Human)], surface: surface);
        simulation.ClaimHuman(0);
        var actor = simulation.Actors.Single();
        actor.Position = new Vector3(-1.3f, 0, 0);
        actor.GroundY = 0;
        simulation.ApplyInput(0, new FpsInputCommand(1, new Vector2(1, 0), 0, 0,
            FpsInputButtons.Jump));
        simulation.Step(0.05f);
        simulation.ApplyInput(0, new FpsInputCommand(2, new Vector2(1, 0), 0, 0,
            FpsInputButtons.None));

        bool landedOnPlatform = false;
        for (int tick = 0; tick < 30; tick++)
        {
            simulation.Step(0.05f);
            if (actor.IsGrounded && actor.Position.X > 0
                && MathF.Abs(actor.Position.Y - platformHeight) < 0.01f)
            {
                landedOnPlatform = true;
                break;
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(landedOnPlatform, Is.True,
                "A normal jump must be able to clear the face and land on a raised platform");
            Assert.That(surface.IsPositionBlocked(actor.Position, actor.GroundY, 1.8f), Is.False);
        });
    }

    [Test]
    public void JumpMantlesEyeLevelPlatformAndRestoresStandingSpeedWhenClear()
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
        Assert.That(actor.IsMantling, Is.False, "A jump tap must not start a mantle");
        for (int tick = 0; tick < 3; tick++) simulation.Step(0.05f);
        Assert.That(actor.IsMantling, Is.True);
        for (int tick = 0; tick < 10; tick++) simulation.Step(0.05f);

        float mantleFinishX = actor.Position.X;
        simulation.ApplyInput(0, new FpsInputCommand(2, new Vector2(1, 0), 0, 0,
            FpsInputButtons.None));
        for (int tick = 0; tick < 20; tick++) simulation.Step(0.05f);

        Assert.Multiple(() =>
        {
            Assert.That(FpsArenaSurface.MaximumMantleHeight, Is.EqualTo(1.75f));
            Assert.That(actor.IsMantling, Is.False);
            Assert.That(actor.Position.X, Is.GreaterThan(0));
            Assert.That(actor.Position.Y, Is.EqualTo(platformHeight).Within(0.01f));
            Assert.That(actor.Stance, Is.EqualTo(FpsStance.Standing));
            Assert.That(actor.IsGrounded, Is.True);
            Assert.That(actor.Position.X - mantleFinishX, Is.GreaterThan(5.5f),
                "A clear mantle landing must resume normal walking speed without a crouch tap");
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
        simulation.ApplyInput(0, new FpsInputCommand(2, Vector2.Zero, MathF.PI / 2, 0,
            FpsInputButtons.None));
        for (int tick = 0; tick < 4; tick++) simulation.Step(0.05f);
        Assert.That(actor.IsMantling, Is.False, "Releasing a tapped jump must cancel mantle intent");

        actor.Position = new Vector3(-0.5f, 0, 0);
        actor.GroundY = 0;
        actor.VerticalVelocity = 0;
        actor.IsGrounded = true;
        simulation.ApplyInput(0, new FpsInputCommand(3, Vector2.Zero, MathF.PI / 2, 0,
            FpsInputButtons.Jump));
        for (int tick = 0; tick < 4; tick++) simulation.Step(0.05f);

        Assert.That(actor.IsMantling, Is.True,
            "Jump must mantle after the hold threshold while looking at a nearby ledge");

        for (int tick = 0; tick < 12; tick++) simulation.Step(0.05f);
        Assert.Multiple(() =>
        {
            Assert.That(actor.IsMantling, Is.False);
            Assert.That(actor.TraversalConsumedForJumpHold, Is.True,
                "One held jump must trigger at most one traversal");
        });

        simulation.ApplyInput(0, new FpsInputCommand(4, Vector2.Zero, MathF.PI / 2, 0,
            FpsInputButtons.None));
        simulation.Step(0.05f);
        Assert.That(actor.TraversalConsumedForJumpHold, Is.False,
            "Releasing jump must arm traversal for a later press");
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
        Assert.That(actor.IsMantling, Is.False, "A jump tap must not start a vault");
        for (int tick = 0; tick < 3; tick++) simulation.Step(0.05f);
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
    public void FinishedMatchClearsTransientCombatEventsOnTheNextStep()
    {
        var simulation = new FpsSimulation(Configuration(killLimit: 1),
        [
            new(0, "Human", FpsSlotRole.Human),
            new(1, "Target", FpsSlotRole.Human),
        ]);
        simulation.ClaimHuman(0);
        simulation.ClaimHuman(1);
        simulation.ApplyInput(0, new FpsInputCommand(1, Vector2.Zero, 0,
            -0.129f, FpsInputButtons.Fire));

        for (int tick = 0; tick < 8 && simulation.MatchState == FpsMatchState.Running; tick++)
            simulation.Step(0.05f);
        Assert.Multiple(() =>
        {
            Assert.That(simulation.MatchState, Is.EqualTo(FpsMatchState.Finished));
            Assert.That(simulation.ShotEvents, Has.Count.EqualTo(1));
            Assert.That(simulation.ShotEvents.Single().Impact, Is.EqualTo(FpsShotImpact.Actor));
            Assert.That(simulation.ShotEvents.Single().TargetId, Is.EqualTo(1));
            Assert.That(simulation.HitEvents, Has.Count.EqualTo(1));
            Assert.That(simulation.KillEvents, Has.Count.EqualTo(1));
        });

        simulation.Step(0.05f);
        Assert.Multiple(() =>
        {
            Assert.That(simulation.ShotEvents, Is.Empty);
            Assert.That(simulation.HitEvents, Is.Empty);
            Assert.That(simulation.KillEvents, Is.Empty);
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
            Assert.That(first.Origin.Y, Is.EqualTo(1.65f).Within(0.001f));
            Assert.That(first.Distance, Is.EqualTo(120).Within(0.001f));
            Assert.That(first.Impact, Is.EqualTo(FpsShotImpact.None));
            Assert.That(first.TargetId, Is.EqualTo(byte.MaxValue));
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
            Assert.That(simulation.ShotEvents.Single().Impact, Is.EqualTo(FpsShotImpact.World));
            Assert.That(simulation.ShotEvents.Single().TargetId, Is.EqualTo(byte.MaxValue));
            Assert.That(simulation.HitEvents, Is.Empty);
        });
    }

    [Test]
    public void RifleAutomaticallyReloadsAfterFortyAuthoritativeShots()
    {
        var simulation = new FpsSimulation(Configuration(),
            [new(0, "Shooter", FpsSlotRole.Human)]);
        simulation.ClaimHuman(0);
        simulation.ApplyInput(0, new FpsInputCommand(1, Vector2.Zero, 0, 0,
            FpsInputButtons.Fire));
        var actor = simulation.Actors.Single();
        int emittedShots = 0;
        for (int shot = 0; shot < FpsSimulation.RifleMagazineCapacity; shot++)
        {
            actor.FireCooldown = 0;
            simulation.Step(0.01f);
            emittedShots += simulation.ShotEvents.Count;
        }

        Assert.Multiple(() =>
        {
            Assert.That(emittedShots, Is.EqualTo(40));
            Assert.That(actor.AmmoInMagazine, Is.Zero);
            Assert.That(actor.ReserveMagazines, Is.EqualTo(4));
            Assert.That(actor.ReloadRemaining, Is.GreaterThan(0));
        });

        simulation.ApplyInput(0, new FpsInputCommand(2, Vector2.Zero, 0, 0,
            FpsInputButtons.None));
        for (int tick = 0; tick < 40; tick++) simulation.Step(0.05f);

        Assert.Multiple(() =>
        {
            Assert.That(actor.AmmoInMagazine, Is.EqualTo(40));
            Assert.That(actor.ReserveMagazines, Is.EqualTo(3));
            Assert.That(actor.ReloadRemaining, Is.Zero);
        });
    }

    [Test]
    public void ReloadButtonSwapsAPartialMagazineOncePerPress()
    {
        var simulation = new FpsSimulation(Configuration(),
            [new(0, "Shooter", FpsSlotRole.Human)]);
        simulation.ClaimHuman(0);
        simulation.ApplyInput(0, new FpsInputCommand(1, Vector2.Zero, 0, 0,
            FpsInputButtons.Fire));
        simulation.Step(0.05f);
        var actor = simulation.Actors.Single();
        Assert.That(actor.AmmoInMagazine, Is.EqualTo(39));

        simulation.ApplyInput(0, new FpsInputCommand(2, Vector2.Zero, 0, 0,
            FpsInputButtons.Reload));
        simulation.Step(0.05f);
        Assert.That(actor.ReloadRemaining, Is.GreaterThan(0));
        simulation.ApplyInput(0, new FpsInputCommand(3, Vector2.Zero, 0, 0,
            FpsInputButtons.None));
        for (int tick = 0; tick < 40; tick++) simulation.Step(0.05f);

        Assert.Multiple(() =>
        {
            Assert.That(actor.AmmoInMagazine, Is.EqualTo(40));
            Assert.That(actor.ReserveMagazines, Is.EqualTo(3));
            Assert.That(actor.ReloadRemaining, Is.Zero);
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

    [Test]
    public void LowDifficultyCombatHasSlowAcquisitionWideErrorAndShortBursts()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FpsSimulation.BotReactionDelaySeconds(0.1f),
                Is.EqualTo(1.272f).Within(0.001f));
            Assert.That(FpsSimulation.BotAimErrorDegrees(0.1f),
                Is.EqualTo(16.35f).Within(0.001f));
            Assert.That(FpsSimulation.BotTrackingDegreesPerSecond(0.1f),
                Is.EqualTo(76.5f).Within(0.001f));
            Assert.That(FpsSimulation.BotBurstShotCount(0.1f), Is.EqualTo(2));
            Assert.That(FpsSimulation.BotBurstPauseSeconds(0.1f),
                Is.EqualTo(1.278f).Within(0.001f));
            Assert.That(FpsSimulation.BotMovementSpeedScale(0.1f),
                Is.EqualTo(0.505f).Within(0.001f));
            Assert.That(FpsSimulation.BotMovementSpeedScale(1), Is.EqualTo(1));
        });
    }

    [Test]
    public void LowDifficultyBotFiresAndHitsMateriallyLessThanHighDifficultyBot()
    {
        (int Shots, int Hits) Run(float difficulty)
        {
            var simulation = new FpsSimulation(Configuration(difficulty: difficulty),
            [
                new(0, "Bot", FpsSlotRole.Bot),
                new(1, "Target", FpsSlotRole.Human),
            ]);
            Assert.That(simulation.ClaimHuman(1), Is.True);
            var bot = simulation.Actors.Single(actor => actor.Id == 0);
            var target = simulation.Actors.Single(actor => actor.Id == 1);
            bot.Position = Vector3.Zero;
            bot.Yaw = 0;
            bot.BotTargetId = target.Id;
            bot.BotReactionRemaining = 0;
            bot.BotSearchRemaining = 8;
            target.Position = new Vector3(0, 0, 20);
            target.Health = 10_000;
            int shots = 0;
            int hits = 0;
            for (int tick = 0; tick < 400; tick++)
            {
                simulation.Step(0.05f);
                shots += simulation.ShotEvents.Count;
                hits += simulation.HitEvents.Count;
            }
            return (shots, hits);
        }

        var low = Run(0.1f);
        var high = Run(1);

        Assert.Multiple(() =>
        {
            Assert.That(low.Shots, Is.LessThan(high.Shots * 0.5f));
            Assert.That(low.Hits, Is.LessThan(high.Hits * 0.35f));
        });
    }

    [Test]
    public void LowDifficultyBotMovesMateriallySlowerThanHighDifficultyBot()
    {
        float Run(float difficulty)
        {
            var simulation = new FpsSimulation(Configuration(difficulty: difficulty),
            [
                new(0, "Bot", FpsSlotRole.Bot),
                new(1, "Target", FpsSlotRole.Human),
            ]);
            Assert.That(simulation.ClaimHuman(1), Is.True);
            var bot = simulation.Actors.Single(actor => actor.Id == 0);
            var target = simulation.Actors.Single(actor => actor.Id == 1);
            bot.Position = Vector3.Zero;
            bot.BotTargetId = target.Id;
            bot.BotReactionRemaining = 10;
            target.Position = new Vector3(0, 0, 30);
            for (int tick = 0; tick < 20; tick++) simulation.Step(0.05f);
            return new Vector2(bot.Position.X, bot.Position.Z).Length();
        }

        float lowDistance = Run(0.1f);
        float highDistance = Run(1);

        Assert.Multiple(() =>
        {
            Assert.That(lowDistance, Is.LessThan(highDistance * 0.6f));
            Assert.That(highDistance, Is.GreaterThan(7));
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

    private static IEnumerable<Kn5Triangle> FlatFloor(float minX, float maxX,
        float minZ, float maxZ, float y)
    {
        yield return new Kn5Triangle(new Vector3(minX, y, minZ),
            new Vector3(minX, y, maxZ), new Vector3(maxX, y, maxZ));
        yield return new Kn5Triangle(new Vector3(minX, y, minZ),
            new Vector3(maxX, y, maxZ), new Vector3(maxX, y, minZ));
    }

    private static IEnumerable<Kn5Triangle> VerticalWall(float x, float minZ,
        float maxZ, float minY, float maxY)
    {
        yield return new Kn5Triangle(new Vector3(x, minY, minZ),
            new Vector3(x, maxY, minZ), new Vector3(x, maxY, maxZ));
        yield return new Kn5Triangle(new Vector3(x, minY, minZ),
            new Vector3(x, maxY, maxZ), new Vector3(x, minY, maxZ));
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

    private static IReadOnlyList<Kn5Triangle> FirePitStairs()
    {
        const float minX = -2;
        const float maxX = 2;
        const float baseY = 1;
        const float firstZ = -5;
        const float treadDepth = 0.27272728f;
        const float riserHeight = 0.22727273f;
        const int stepCount = 11;
        var triangles = new List<Kn5Triangle>
        {
            new(new Vector3(minX, baseY, -7), new Vector3(minX, baseY, 2),
                new Vector3(maxX, baseY, 2)),
            new(new Vector3(minX, baseY, -7), new Vector3(maxX, baseY, 2),
                new Vector3(maxX, baseY, -7)),
        };
        for (int step = 1; step <= stepCount; step++)
        {
            float z0 = firstZ + (step - 1) * treadDepth;
            float z1 = firstZ + step * treadDepth;
            float topY = baseY + step * riserHeight;
            triangles.Add(new Kn5Triangle(new Vector3(minX, topY, z0),
                new Vector3(minX, topY, z1), new Vector3(maxX, topY, z1)));
            triangles.Add(new Kn5Triangle(new Vector3(minX, topY, z0),
                new Vector3(maxX, topY, z1), new Vector3(maxX, topY, z0)));
            // The source mesh backs each visible riser all the way down to its retained
            // base floor instead of only to the preceding tread.
            triangles.Add(new Kn5Triangle(new Vector3(minX, baseY, z1),
                new Vector3(minX, topY, z1), new Vector3(maxX, topY, z1)));
            triangles.Add(new Kn5Triangle(new Vector3(minX, baseY, z1),
                new Vector3(maxX, topY, z1), new Vector3(maxX, baseY, z1)));
        }
        float landingZ = firstZ + stepCount * treadDepth;
        float landingY = baseY + stepCount * riserHeight;
        triangles.Add(new Kn5Triangle(new Vector3(minX, landingY, landingZ),
            new Vector3(minX, landingY, 2), new Vector3(maxX, landingY, 2)));
        triangles.Add(new Kn5Triangle(new Vector3(minX, landingY, landingZ),
            new Vector3(maxX, landingY, 2), new Vector3(maxX, landingY, landingZ)));
        return triangles;
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
