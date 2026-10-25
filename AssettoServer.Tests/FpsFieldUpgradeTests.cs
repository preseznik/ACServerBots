using System.Numerics;
using AssettoServer.Network.ClientMessages;
using AssettoServer.Server.Configuration.Extra;
using AssettoServer.Server.Configuration.Kunos;
using AssettoServer.Server.Ai.Physics;
using AssettoServer.Server.Fps;

namespace AssettoServer.Tests;

public sealed class FpsFieldUpgradeTests
{
    private static FpsLoadout Loadout(FpsFieldUpgrade field = FpsFieldUpgrade.MunitionsBox,
        FpsPerk first = FpsPerk.None, FpsPerk second = FpsPerk.None, FpsPerk third = FpsPerk.None,
        FpsLethalType lethal = FpsLethalType.FragGrenade) =>
        new(FpsWeaponType.AssaultRifle, lethal, FpsWeaponType.Colt1911, field, first, second, third);

    private static FpsSimulation Create(FpsLoadout? first = null, FpsLoadout? second = null,
        FpsMatchType match = FpsMatchType.Deathmatch, FpsArenaSurface? surface = null, bool includeThird = false)
    {
        var slots = new List<FpsSimulationSlot> { new(0, "One", FpsSlotRole.Human), new(1, "Two", FpsSlotRole.Human) };
        if (includeThird) slots.Add(new(2, "Three", FpsSlotRole.Human));
        var sim = new FpsSimulation(new FpsConfiguration
        {
            Enabled = true, StartWithBotsOnly = true, MatchType = match,
            KillLimit = 100, TimeLimitMinutes = 10, RespawnSeconds = 0.2f,
            SpawnProtectionSeconds = 0, DisableHealthRegeneration = true,
            Arena = new FpsArenaConfiguration
            {
                BoundsMin = new(-50, -2, -50), BoundsMax = new(50, 10, 50),
                SpawnPoints = [new() { Position = Vector3.Zero }, new() { Position = new(20, 0, 0) }],
            },
        }, slots, surface: surface);
        for (byte id = 0; id < slots.Count; id++)
        {
            sim.ClaimHuman(id, true);
            Assert.That(sim.SelectLoadout(id, (id == 0 ? first : second) ?? Loadout()), Is.EqualTo(FpsLoadoutResultCode.Applied));
            Actor(sim, id).Position = new Vector3(id * 20, 0, 0);
        }
        return sim;
    }

    private static FpsActorState Actor(FpsSimulation sim, byte id = 0) => sim.Actors.Single(a => a.Id == id);
    private static void Input(FpsSimulation sim, byte id = 0, FpsInputButtons buttons = FpsInputButtons.None,
        Vector2 move = default, byte slot = 0, float yaw = 0, float pitch = 0)
    {
        var actor = Actor(sim, id);
        Assert.That(sim.ApplyInput(id, new FpsInputCommand(actor.LastInputSequence + 1,
            move, yaw, pitch, buttons, slot)), Is.True);
    }
    private static void Advance(FpsSimulation sim, float seconds)
    {
        for (int tick = 0; tick < (int)MathF.Ceiling(seconds / 0.05f); tick++) sim.Step(0.05f);
    }
    private static FpsFieldDevice Deploy(FpsSimulation sim)
    {
        // Device-mechanics tests begin with an earned charge; charging has separate tests.
        Actor(sim).FieldUpgradeCooldown = 0;
        Actor(sim).FieldUpgradeMinimumRemaining = 0;
        Input(sim, buttons: FpsInputButtons.FieldUpgrade);
        sim.Step(0.01f);
        var device = sim.FieldDevices.Single();
        Input(sim);
        Advance(sim, 0.5f);
        return device;
    }
    private static FpsGrenadeState Throw(FpsSimulation sim, byte id = 1)
    {
        Input(sim, id, FpsInputButtons.ThrowLethal);
        sim.Step(0.01f);
        Input(sim, id);
        Advance(sim, 0.30f);
        return sim.Grenades.Single();
    }

    [Test]
    public void SelectionRejectsUnknownOrDuplicatePerksAndRespectsAllowLists()
    {
        var config = new FpsLoadoutConfiguration();
        Assert.That(FpsItems.IsAllowed(config, Loadout(first: FpsPerk.Ninja, second: FpsPerk.Ninja)), Is.False);
        Assert.That(FpsItems.IsAllowed(config, Loadout(first: (FpsPerk)8)), Is.False);
        Assert.That(FpsItems.IsAllowed(config, Loadout(field: (FpsFieldUpgrade)99)), Is.False);
        Assert.That(FpsItems.IsAllowed(config, Loadout(first: FpsPerk.Ninja, second: FpsPerk.Vision, third: FpsPerk.Engineer)), Is.True);
        config.AllowedPerks.Remove(FpsPerk.Ninja);
        Assert.That(FpsItems.IsAllowed(config, Loadout(first: FpsPerk.Ninja)), Is.False);
        config.AllowedFieldUpgrades.Remove(FpsFieldUpgrade.TrophySystem);
        Assert.That(FpsItems.IsAllowed(config, Loadout(FpsFieldUpgrade.TrophySystem)), Is.False);
    }

    [Test]
    public void QueuedPerksDoNotApplyUntilRespawn()
    {
        var sim = Create();
        var actor = Actor(sim);
        var selected = Loadout(FpsFieldUpgrade.DeadSilence, FpsPerk.GearHead, FpsPerk.Vision, FpsPerk.Ninja);
        Assert.That(sim.SelectLoadout(0, selected), Is.EqualTo(FpsLoadoutResultCode.QueuedForRespawn));
        Assert.That(actor.Loadout.HasPerk(FpsPerk.GearHead), Is.False);
        actor.Dead = true; actor.Health = 0; actor.RespawnRemaining = 0.01f;
        sim.Step(0.05f);
        Assert.Multiple(() =>
        {
            Assert.That(actor.Loadout, Is.EqualTo(selected));
            Assert.That(actor.LethalsRemaining, Is.EqualTo(2));
            Assert.That(actor.VisionRemaining, Is.EqualTo(2).Within(0.001));
            Assert.That(actor.FieldUpgradeCooldown, Is.EqualTo(120));
        });
    }

    [TestCase(FpsInputButtons.None)]
    [TestCase(FpsInputButtons.Sprint)]
    [TestCase(FpsInputButtons.Aim)]
    public void SlipstreamAddsExactlyFifteenPercentToMovement(FpsInputButtons buttons)
    {
        var normal = Create();
        var fast = Create(Loadout(first: FpsPerk.Slipstream));
        Input(normal, buttons: buttons, move: Vector2.UnitY);
        Input(fast, buttons: buttons, move: Vector2.UnitY);
        normal.Step(0.05f); fast.Step(0.05f);
        Assert.That(Actor(fast).Position.Z, Is.EqualTo(Actor(normal).Position.Z * 1.15f).Within(0.001));
    }

    [Test]
    public void NewPlayerStartsEmptyAndMustChargeBeforeFirstUse()
    {
        var sim = Create(Loadout(FpsFieldUpgrade.DeadSilence));
        var actor = Actor(sim);
        Assert.That(actor.FieldUpgradeCooldown, Is.EqualTo(120));
        Input(sim, buttons: FpsInputButtons.FieldUpgrade); sim.Step(0.01f);
        Assert.That(actor.DeadSilenceRemaining, Is.Zero);
        Input(sim); Advance(sim, 119);
        Assert.That(actor.FieldUpgradeCooldown, Is.GreaterThan(0.9));
        Advance(sim, 1.1f);
        Assert.That(actor.FieldUpgradeCooldown, Is.Zero);
        Input(sim, buttons: FpsInputButtons.FieldUpgrade); sim.Step(0.01f);
        Assert.That(actor.DeadSilenceRemaining, Is.EqualTo(15));
        Assert.That(actor.FieldUpgradeCooldown, Is.EqualTo(120));
        Assert.That(actor.FieldUpgradeMinimumRemaining, Is.EqualTo(60));
    }

    [Test]
    public void DeathAndChangingLoadoutPreserveEarnedCharge()
    {
        var sim = Create();
        var actor = Actor(sim);
        Advance(sim, 30);
        float remaining = actor.FieldUpgradeCooldown;
        sim.SelectLoadout(0, Loadout(FpsFieldUpgrade.DeadSilence, FpsPerk.Vision));
        actor.Dead = true; actor.Health = 0; actor.RespawnRemaining = .2f;
        Advance(sim, .3f);
        Assert.That(actor.Dead, Is.False);
        Assert.That(actor.Loadout.FieldUpgrade, Is.EqualTo(FpsFieldUpgrade.DeadSilence));
        Assert.That(actor.FieldUpgradeCooldown, Is.EqualTo(remaining).Within(.11));
        Assert.That(actor.VisionRemaining, Is.GreaterThan(1.8));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void KillAndAssistScoreBoostChargeWithoutBypassingOneMinuteFloor(bool nearFloor)
    {
        var sim = Create(includeThird: true);
        var killer = Actor(sim);
        var victim = Actor(sim, 1);
        var assistant = Actor(sim, 2);
        Advance(sim, 20);
        if (nearFloor)
        {
            killer.FieldUpgradeCooldown = killer.FieldUpgradeMinimumRemaining + 1;
            assistant.FieldUpgradeCooldown = assistant.FieldUpgradeMinimumRemaining + 1;
        }
        float beforeKill = killer.FieldUpgradeCooldown, beforeAssist = assistant.FieldUpgradeCooldown;
        victim.Position = new(0, 0, 5); victim.Health = 1;
        // Represents an earlier nonlethal hit by the third player.
        victim.DamageContributors.Add(2);
        Input(sim, buttons: FpsInputButtons.Fire | FpsInputButtons.Aim); sim.Step(.01f);
        Assert.That(killer.Score, Is.EqualTo(100));
        Assert.That(assistant.Score, Is.EqualTo(50));
        if (nearFloor)
        {
            Assert.That(killer.FieldUpgradeCooldown, Is.EqualTo(killer.FieldUpgradeMinimumRemaining).Within(.02));
            Assert.That(assistant.FieldUpgradeCooldown, Is.EqualTo(assistant.FieldUpgradeMinimumRemaining).Within(.02));
            Assert.That(killer.FieldUpgradeCooldown, Is.GreaterThan(39));
        }
        else
        {
            Assert.That(beforeKill-killer.FieldUpgradeCooldown, Is.EqualTo(15.01).Within(.02));
            Assert.That(beforeAssist-assistant.FieldUpgradeCooldown, Is.EqualTo(7.51).Within(.02));
        }
    }

    [Test]
    public void FastHandsReducesSwitchLockWithoutRefillingAmmo()
    {
        var normal = Create();
        var fast = Create(Loadout(first: FpsPerk.FastHands));
        foreach (var sim in new[] { normal, fast })
        {
            Actor(sim).SecondaryAmmoInMagazine = 2;
            Input(sim, slot: 1); sim.Step(0.01f);
            Assert.That(Actor(sim).AmmoInMagazine, Is.EqualTo(2));
        }
        Assert.That(Actor(normal).ActionLockRemaining, Is.EqualTo(0.35f).Within(0.001));
        Assert.That(Actor(fast).ActionLockRemaining, Is.EqualTo(0.12f).Within(0.001));
    }

    [Test]
    public void DeadSilenceIsTimedEdgeTriggeredAndResetsOnDeath()
    {
        var sim = Create(Loadout(FpsFieldUpgrade.DeadSilence));
        var actor = Actor(sim);
        actor.FieldUpgradeCooldown = 0;
        Input(sim, buttons: FpsInputButtons.FieldUpgrade); sim.Step(0.01f);
        Assert.That(actor.DeadSilenceRemaining, Is.EqualTo(15));
        Assert.That(actor.FieldUpgradeCooldown, Is.EqualTo(120));
        Assert.That(sim.FieldDevices, Is.Empty);
        Advance(sim, 16);
        Assert.That(actor.DeadSilenceRemaining, Is.Zero);
        actor.FieldUpgradeCooldown = 0;
        sim.Step(0.01f);
        Assert.That(actor.DeadSilenceRemaining, Is.Zero, "Holding activation cannot reactivate");
        Input(sim); sim.Step(0.01f);
        Input(sim, buttons: FpsInputButtons.FieldUpgrade); sim.Step(0.01f);
        Assert.That(actor.DeadSilenceRemaining, Is.EqualTo(15));
        actor.Dead = true; actor.RespawnRemaining = 1;
        sim.Step(0.01f);
        Assert.That(actor.DeadSilenceRemaining, Is.Zero);
    }

    [Test]
    public void ResupplyFillsBothWeaponsAndTwoSelectedGrenadesOncePerLife()
    {
        var sim = Create(Loadout(first: FpsPerk.GearHead));
        var actor = Actor(sim);
        var box = Deploy(sim);
        actor.AmmoInMagazine = 1; actor.ReserveMagazines = 0;
        actor.SecondaryAmmoInMagazine = 0; actor.SecondaryReserveMagazines = 0;
        actor.LethalsRemaining = 0;
        Input(sim, buttons: FpsInputButtons.Interact); sim.Step(0.01f);
        Assert.Multiple(() =>
        {
            Assert.That(actor.AmmoInMagazine, Is.EqualTo(40));
            Assert.That(actor.ReserveMagazines, Is.EqualTo(4));
            Assert.That(actor.SecondaryAmmoInMagazine, Is.EqualTo(8));
            Assert.That(actor.SecondaryReserveMagazines, Is.EqualTo(5));
            Assert.That(actor.LethalsRemaining, Is.EqualTo(2));
            Assert.That(box.UsesRemaining, Is.EqualTo(3));
            Assert.That(sim.FieldDeviceEvents.Single().State, Is.EqualTo(4));
        });
        sim.Step(0.01f);
        Assert.That(actor.ResupplyHandled, Is.True, "Keep pickup-swap suppressed until F is released");
        actor.AmmoInMagazine = 2;
        Input(sim); sim.Step(0.01f);
        Input(sim, buttons: FpsInputButtons.Interact); sim.Step(0.01f);
        Assert.That(actor.AmmoInMagazine, Is.EqualTo(2), "Same box cannot be farmed within a life");
        Assert.That(box.UsesRemaining, Is.EqualTo(3));
    }

    [Test]
    public void EnemyAndOutOfRangeActorsCannotResupplyAndDevicesExpireOrLeaveWithOwner()
    {
        var sim = Create();
        var box = Deploy(sim);
        Actor(sim, 1).Position = box.Position + Vector3.UnitX;
        Actor(sim, 1).AmmoInMagazine = 1;
        Input(sim, 1, FpsInputButtons.Interact); sim.Step(0.01f);
        Assert.That(Actor(sim, 1).AmmoInMagazine, Is.EqualTo(1));
        Actor(sim).Position = new(20, 0, 0); Actor(sim).AmmoInMagazine = 1;
        Input(sim, buttons: FpsInputButtons.Interact); sim.Step(0.01f);
        Assert.That(Actor(sim).AmmoInMagazine, Is.EqualTo(1));
        box.RemainingSeconds = 0.01f; sim.Step(0.05f);
        Assert.That(sim.FieldDevices, Is.Empty);
        Actor(sim).FieldUpgradeCooldown = 0; Deploy(sim);
        sim.ReleaseHuman(0);
        Assert.That(sim.FieldDevices, Is.Empty);
    }

    [TestCase(FpsLethalType.FragGrenade)]
    [TestCase(FpsLethalType.StickyGrenade)]
    public void TrophyInterceptsThreeEnemyGrenadesWithoutExploding(FpsLethalType type)
    {
        var sim = Create(Loadout(FpsFieldUpgrade.TrophySystem), Loadout(lethal: type));
        var trophy = Deploy(sim);
        for (int use = 0; use < 3; use++)
        {
            Actor(sim, 1).LethalsRemaining = 1;
            var grenade = Throw(sim);
            grenade.Position = trophy.Position + Vector3.UnitY;
            grenade.Velocity = Vector3.Zero;
            sim.Step(0.01f);
            Assert.That(sim.Grenades, Is.Empty);
            Assert.That(sim.GrenadeExplosionEvents, Is.Empty);
            Assert.That(sim.FieldDeviceEvents.Any(e => e.State == 3 && e.GrenadeId == grenade.Id), Is.True);
            Advance(sim, 0.6f);
        }
        Assert.That(sim.FieldDevices, Is.Empty);
    }

    [Test]
    public void TrophyIgnoresOwnersGrenade()
    {
        var sim = Create(Loadout(FpsFieldUpgrade.TrophySystem));
        var trophy = Deploy(sim);
        var grenade = Throw(sim, 0);
        grenade.Position = trophy.Position + Vector3.UnitY;
        sim.Step(0.01f);
        Assert.That(sim.Grenades, Has.Count.EqualTo(1));
        Assert.That(trophy.UsesRemaining, Is.EqualTo(3));
    }

    [TestCase(FpsPerk.None, 100, true)]
    [TestCase(FpsPerk.FlackJacket, 100, false)]
    [TestCase(FpsPerk.FlackJacket, 10, false)]
    public void FlackJacketCapsBlastDamageAtSurvivableHealth(FpsPerk perk, int health, bool dead)
    {
        var sim = Create(Loadout(first: perk));
        var actor = Actor(sim); actor.Health = health;
        var grenade = Throw(sim);
        grenade.Position = actor.Position + Vector3.UnitY;
        grenade.Velocity = Vector3.Zero; grenade.RemainingSeconds = 0.001f;
        sim.Step(0.01f);
        Assert.That(actor.Dead, Is.EqualTo(dead));
        if (!dead) Assert.That(actor.Health, Is.InRange(1, health - 1));
    }

    [Test]
    public void EnemyCanDestroyDeployedGadgetWithGunfire()
    {
        var sim = Create(Loadout(FpsFieldUpgrade.TrophySystem));
        var trophy = Deploy(sim);
        var shooter = Actor(sim, 1);
        shooter.Position = trophy.Position + new Vector3(0, 0, 4);
        Actor(sim).Position = new(-20, 0, 0);
        Input(sim, 1, FpsInputButtons.Fire | FpsInputButtons.Aim,
            yaw: MathF.PI, pitch: MathF.Atan2(0.3f - 1.62f, 4));
        Advance(sim, 1.1f);
        Assert.That(sim.FieldDevices, Is.Empty);
        Assert.That(shooter.AmmoInMagazine, Is.LessThan(40));
    }

    private static FpsArenaSurface FloorAndWall() => new(new Kn5Triangle[]
    {
        new(new(-50,0,-50), new(-50,0,50), new(50,0,50)),
        new(new(-50,0,-50), new(50,0,50), new(50,0,-50)),
        new(new(-20,0,2), new(20,0,2), new(20,5,2)),
        new(new(-20,0,2), new(20,5,2), new(-20,5,2)),
    });

    [Test]
    public void SolidWallBlocksTrophyInterceptionAndGunfire()
    {
        var sim = Create(Loadout(FpsFieldUpgrade.TrophySystem), surface: FloorAndWall());
        var trophy = Deploy(sim);
        var grenade = Throw(sim);
        grenade.Position = new Vector3(0, 1, 3); grenade.Velocity = Vector3.Zero;
        sim.Step(0.01f);
        Assert.That(sim.Grenades, Has.Count.EqualTo(1));
        Assert.That(trophy.UsesRemaining, Is.EqualTo(3));
        Actor(sim, 1).Position = new(0, 0, 4);
        Input(sim, 1, FpsInputButtons.Fire | FpsInputButtons.Aim,
            yaw: MathF.PI, pitch: MathF.Atan2(0.3f - 1.62f, 2.8f));
        Advance(sim, 0.8f);
        Assert.That(trophy.Health, Is.EqualTo(100));
    }

    [Test]
    public void SolidWallBlocksResupplyAndDeploymentDoesNotConsumeCooldownWhenObstructed()
    {
        var sim = Create(surface: FloorAndWall());
        var box = Deploy(sim);
        Actor(sim).Position = new(0, 0, 2.7f); Actor(sim).AmmoInMagazine = 1;
        Input(sim, buttons: FpsInputButtons.Interact); sim.Step(0.01f);
        Assert.That(Actor(sim).AmmoInMagazine, Is.EqualTo(1));
        Actor(sim).FieldUpgradeCooldown = 0;
        Input(sim, buttons: FpsInputButtons.FieldUpgrade, yaw: MathF.PI); sim.Step(0.01f);
        Assert.That(Actor(sim).FieldUpgradeCooldown, Is.Zero);
        Assert.That(sim.FieldDevices.Single().Id, Is.EqualTo(box.Id));
    }
}
