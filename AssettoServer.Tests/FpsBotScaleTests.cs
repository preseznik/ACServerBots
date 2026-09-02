using System.Diagnostics;
using System.Numerics;
using AssettoServer.Server.Ai.Physics;
using AssettoServer.Server.Configuration.Extra;
using AssettoServer.Server.Configuration.Kunos;
using AssettoServer.Server.Fps;
using NUnit.Framework;

namespace AssettoServer.Tests;

public sealed class FpsBotScaleTests
{
    [TestCase(8)]
    [TestCase(16)]
    [TestCase(32)]
    public void HeadlessCombatGridRunsWithoutBacklog(int actorCount)
    {
        var (simulation, _) = Create(actorCount, 23);
        var samples = new double[600];
        int shots = 0;
        int hits = 0;
        int maximumPathPlans = 0;
        for (int tick = 0; tick < samples.Length; tick++)
        {
            long started = Stopwatch.GetTimestamp();
            simulation.Step(1f / 60);
            shots += simulation.ShotEvents.Count;
            hits += simulation.HitEvents.Count;
            maximumPathPlans = Math.Max(maximumPathPlans, simulation.BotPathPlansLastStep);
            samples[tick] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }
        Array.Sort(samples);
        double p95 = samples[(int)(samples.Length * 0.95)];
        TestContext.Progress.WriteLine(
            $"FPS {actorCount}-bot headless run: p95={p95:F3} ms, max={samples[^1]:F3} ms, shots={shots}, hits={hits}, kills={simulation.Actors.Sum(actor => actor.Kills)}");

        Assert.Multiple(() =>
        {
            Assert.That(simulation.Actors, Has.Count.EqualTo(actorCount));
            Assert.That(simulation.Actors, Has.All.Matches<FpsActorState>(actor => actor.Active));
            Assert.That(simulation.Actors.Sum(actor => actor.Kills), Is.GreaterThan(0));
            Assert.That(maximumPathPlans, Is.LessThanOrEqualTo(1),
                "Path planning must be spread across simulation ticks.");
            if (actorCount == 8)
                Assert.That(p95, Is.LessThan(8.3),
                    "Eight bots must leave at least half of the 60 Hz tick budget free.");
        });
    }

    [Test]
    public void HeadlessCombatIsDeterministicForSameSeed()
    {
        var (first, _) = Create(8, 71);
        var (second, _) = Create(8, 71);
        for (int tick = 0; tick < 600; tick++)
        {
            first.Step(1f / 60);
            second.Step(1f / 60);
        }

        Assert.That(first.Actors.OrderBy(actor => actor.Id).Select(actor => new
            {
                actor.Position,
                actor.Health,
                actor.Kills,
                actor.Deaths,
                actor.SpawnCount,
            }), Is.EqualTo(second.Actors.OrderBy(actor => actor.Id).Select(actor => new
            {
                actor.Position,
                actor.Health,
                actor.Kills,
                actor.Deaths,
                actor.SpawnCount,
            })));
    }

    private static (FpsSimulation Simulation, FpsArenaNavigationAsset Navigation) Create(
        int actorCount, int seed)
    {
        var triangles = new Kn5Triangle[]
        {
            new(new Vector3(-20, 0, -20), new Vector3(-20, 0, 20),
                new Vector3(20, 0, 20)),
            new(new Vector3(-20, 0, -20), new Vector3(20, 0, 20),
                new Vector3(20, 0, -20)),
        };
        var surface = new FpsArenaSurface(triangles);
        var spawnMetadata = new List<FpsArenaSpawn>();
        var spawnConfiguration = new List<FpsSpawnConfiguration>();
        for (int index = 0; index < actorCount; index++)
        {
            float angle = index * MathF.Tau / actorCount;
            var position = new Vector3(MathF.Cos(angle) * 12, 0, MathF.Sin(angle) * 12);
            spawnMetadata.Add(new FpsArenaSpawn(FpsArenaPoint.From(position), angle + MathF.PI));
            spawnConfiguration.Add(new FpsSpawnConfiguration
            {
                Position = position,
                YawRadians = angle + MathF.PI,
            });
        }
        var navigation = FpsArenaNavigationBuilder.Build(surface,
            new FpsArenaPoint(-20, -1, -20), new FpsArenaPoint(20, 4, 20),
            spawnMetadata).Asset;
        var configuration = new FpsConfiguration
        {
            Enabled = true,
            TimeLimitMinutes = 10,
            KillLimit = 999,
            RespawnSeconds = 0.2f,
            SpawnProtectionSeconds = 0.1f,
            Bots = new FpsBotConfiguration
            {
                Difficulty = 0.75f,
                DifficultyVariancePercent = 10,
                Aggression = 0.5f,
                AggressionVariancePercent = 15,
                Health = 100,
            },
            Arena = new FpsArenaConfiguration
            {
                BoundsMin = new Vector3(-20, -1, -20),
                BoundsMax = new Vector3(20, 4, 20),
                SpawnPoints = spawnConfiguration,
            },
        };
        var slots = Enumerable.Range(0, actorCount)
            .Select(index => new FpsSimulationSlot((byte)index, $"Bot {index + 1}",
                FpsSlotRole.Bot));
        return (new FpsSimulation(configuration, slots, seed, surface, navigation), navigation);
    }
}
