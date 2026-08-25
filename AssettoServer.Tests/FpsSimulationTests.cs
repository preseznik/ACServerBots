using System.Numerics;
using AssettoServer.Network.ClientMessages;
using AssettoServer.Server.Configuration.Extra;
using AssettoServer.Server.Configuration.Kunos;
using AssettoServer.Server.Fps;
using NUnit.Framework;

namespace AssettoServer.Tests;

public sealed class FpsSimulationTests
{
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
