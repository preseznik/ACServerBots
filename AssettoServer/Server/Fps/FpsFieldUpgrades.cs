using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AssettoServer.Network.ClientMessages;
using AssettoServer.Server.Configuration.Extra;

namespace AssettoServer.Server.Fps;

internal sealed class FpsFieldDevice
{
    public required uint Id { get; init; }
    public required byte OwnerId { get; init; }
    public required FpsFieldUpgrade Type { get; init; }
    public required Vector3 Position { get; init; }
    public int Health { get; set; } = 100;
    public int UsesRemaining { get; set; }
    public float RemainingSeconds { get; set; } = 60;
    public HashSet<(byte ActorId, uint SpawnCount)> Resupplied { get; } = [];
}

// State: 1 spawn/update, 2 remove, 3 interception, 4 resupply.
internal readonly record struct FpsFieldDeviceEvent(uint Id, byte OwnerId,
    FpsFieldUpgrade Type, byte State, Vector3 Position, int Health, int UsesRemaining,
    float RemainingSeconds, byte ActorId = byte.MaxValue, uint GrenadeId = 0,
    Vector3 Target = default);

internal sealed partial class FpsSimulation
{
    internal const float FieldUpgradeRechargeSeconds = 120;
    internal const float FieldUpgradeMinimumChargeSeconds = 60;
    internal const float FieldUpgradeSecondsPerScore = 0.15f;
    internal const float DeadSilenceSeconds = 15;
    internal const float TrophyRadius = 6;
    internal const float MunitionsUseRadius = 2;
    private readonly List<FpsFieldDevice> _fieldDevices = [];
    private readonly List<FpsFieldDeviceEvent> _fieldDeviceEvents = [];
    private uint _nextFieldDeviceId = 1;
    public IReadOnlyList<FpsFieldDevice> FieldDevices => _fieldDevices;
    public IReadOnlyList<FpsFieldDeviceEvent> FieldDeviceEvents => _fieldDeviceEvents;

    private void StepFieldActor(FpsActorState actor, float dt)
    {
        if (!actor.Dead && actor.LoadoutConfirmed)
        {
            actor.FieldUpgradeCooldown = Math.Max(0, actor.FieldUpgradeCooldown - dt);
            actor.FieldUpgradeMinimumRemaining = Math.Max(0, actor.FieldUpgradeMinimumRemaining - dt);
        }
        actor.DeadSilenceRemaining = actor.Dead ? 0 : Math.Max(0, actor.DeadSilenceRemaining - dt);
        actor.VisionRemaining = actor.Dead ? 0 : Math.Max(0, actor.VisionRemaining - dt);
    }

    private static void ResetFieldCharge(FpsActorState actor)
    {
        actor.FieldUpgradeCooldown = FieldUpgradeRechargeSeconds;
        actor.FieldUpgradeMinimumRemaining = FieldUpgradeMinimumChargeSeconds;
    }

    private static void AddScore(FpsActorState actor, uint score)
    {
        actor.Score += score;
        // One minute is the fastest recharge, even with a burst of score awards.
        actor.FieldUpgradeCooldown = Math.Max(actor.FieldUpgradeMinimumRemaining,
            actor.FieldUpgradeCooldown - score * FieldUpgradeSecondsPerScore);
    }

    private void TryFieldActions(FpsActorState actor)
    {
        bool activate = actor.Input.Buttons.HasFlag(FpsInputButtons.FieldUpgrade);
        if (activate && !actor.FieldUpgradeHeld) TryActivateFieldUpgrade(actor);
        actor.FieldUpgradeHeld = activate;
        bool interact = actor.Input.Buttons.HasFlag(FpsInputButtons.Interact);
        if (!interact) actor.ResupplyHandled = false;
        else if (!actor.FieldInteractHeld) actor.ResupplyHandled = TryResupply(actor);
        actor.FieldInteractHeld = interact;
    }

    private bool TryActivateFieldUpgrade(FpsActorState actor)
    {
        if (actor.Dead || !actor.Active || !actor.LoadoutConfirmed || actor.IsMantling
            || actor.FieldUpgradeCooldown > 0 || actor.ActionLockRemaining > 0
            || actor.PendingLethal is not null || actor.ReloadRemaining > 0) return false;
        if (actor.Loadout.FieldUpgrade == FpsFieldUpgrade.DeadSilence)
        {
            actor.DeadSilenceRemaining = DeadSilenceSeconds;
        }
        else
        {
            var direction = new Vector3(MathF.Sin(actor.Yaw), 0, MathF.Cos(actor.Yaw));
            var position = actor.Position + direction * 1.2f;
            if (_surface is not null)
            {
                var origin = actor.Position + Vector3.UnitY * 0.7f;
                if (_surface.TryRaycast(origin, direction, 1.3f, out _)) return false;
                if (!_surface.TryGetGroundHeight(position.X, position.Z, actor.Position.Y,
                        out float ground) || MathF.Abs(ground - actor.Position.Y) > 1.2f) return false;
                position.Y = ground;
            }
            RemoveOwnedDevices(actor.Id);
            var device = new FpsFieldDevice
            {
                Id = _nextFieldDeviceId++, OwnerId = actor.Id, Type = actor.Loadout.FieldUpgrade,
                Position = position, UsesRemaining = actor.Loadout.FieldUpgrade == FpsFieldUpgrade.TrophySystem ? 3 : 4,
            };
            if (_nextFieldDeviceId == 0) _nextFieldDeviceId = 1;
            _fieldDevices.Add(device);
            RecordDevice(device, 1);
        }
        ResetFieldCharge(actor);
        actor.ActionLockRemaining = Math.Max(actor.ActionLockRemaining, 0.45f);
        actor.FireHeld = true;
        return true;
    }

    private bool DeviceFriendly(FpsActorState actor, FpsFieldDevice device) =>
        actor.Id == device.OwnerId || (_actors.TryGetValue(device.OwnerId, out var owner)
                                      && AreTeammates(actor, owner));

    private bool DeviceLineOfSight(Vector3 from, Vector3 to)
    {
        var offset = to - from;
        float length = offset.Length();
        return length < 0.05f || _surface is null
            || !_surface.TryRaycast(from, offset / length, length, out float obstacle)
            || obstacle >= length - 0.05f;
    }

    private bool TryResupply(FpsActorState actor)
    {
        if (actor.Dead || actor.PendingLethal is not null || actor.IsMantling) return false;
        var device = _fieldDevices.Where(d => d.Type == FpsFieldUpgrade.MunitionsBox
                && DeviceFriendly(actor, d) && d.UsesRemaining > 0
                && !d.Resupplied.Contains((actor.Id, actor.SpawnCount))
                && Vector3.DistanceSquared(actor.Position, d.Position) <= MunitionsUseRadius * MunitionsUseRadius
                && DeviceLineOfSight(actor.Position + Vector3.UnitY * EyeHeight(actor.Stance),
                    d.Position + Vector3.UnitY * 0.25f))
            .OrderBy(d => Vector3.DistanceSquared(actor.Position, d.Position)).FirstOrDefault();
        if (device is null) return false;
        SaveActiveWeaponState(actor);
        var primary = FpsItems.Firearm(actor.Loadout.MainWeapon);
        var secondary = FpsItems.Firearm(actor.Loadout.SecondaryWeapon);
        if (actor.PrimaryAmmoInMagazine == primary.MagazineCapacity
            && actor.PrimaryReserveMagazines == primary.MaximumReserveMagazines
            && actor.SecondaryAmmoInMagazine == secondary.MagazineCapacity
            && actor.SecondaryReserveMagazines == secondary.MaximumReserveMagazines
            && actor.LethalsRemaining == actor.Loadout.LethalCapacity) return false;
        actor.PrimaryAmmoInMagazine = primary.MagazineCapacity;
        actor.PrimaryReserveMagazines = primary.MaximumReserveMagazines;
        actor.SecondaryAmmoInMagazine = secondary.MagazineCapacity;
        actor.SecondaryReserveMagazines = secondary.MaximumReserveMagazines;
        actor.AmmoInMagazine = actor.ActiveWeaponSlot == 0 ? actor.PrimaryAmmoInMagazine : actor.SecondaryAmmoInMagazine;
        actor.ReserveMagazines = actor.ActiveWeaponSlot == 0 ? actor.PrimaryReserveMagazines : actor.SecondaryReserveMagazines;
        actor.ReloadRemaining = 0;
        actor.LethalsRemaining = actor.Loadout.LethalCapacity;
        device.Resupplied.Add((actor.Id, actor.SpawnCount));
        device.UsesRemaining--;
        RecordDevice(device, 4, actor.Id);
        if (device.UsesRemaining == 0) RemoveDevice(device);
        return true;
    }

    private void StepFieldDevices(float dt)
    {
        foreach (var device in _fieldDevices.ToArray())
        {
            device.RemainingSeconds -= dt;
            if (device.RemainingSeconds <= 0) RemoveDevice(device);
        }
    }

    private bool TryInterceptGrenade(FpsGrenadeState grenade, Vector3 previousPosition)
    {
        if (!_actors.TryGetValue(grenade.OwnerId, out var thrower)) return false;
        foreach (var device in _fieldDevices.ToArray())
        {
            if (device.Type != FpsFieldUpgrade.TrophySystem || device.UsesRemaining <= 0
                || DeviceFriendly(thrower, device)) continue;
            var center = device.Position + Vector3.UnitY * 0.4f;
            var segment = grenade.Position - previousPosition;
            float t = segment.LengthSquared() > 0.0001f
                ? Math.Clamp(Vector3.Dot(center - previousPosition, segment) / segment.LengthSquared(), 0, 1) : 0;
            var closest = previousPosition + segment * t;
            if (Vector3.DistanceSquared(center, closest) > TrophyRadius * TrophyRadius
                || !DeviceLineOfSight(center, closest)) continue;
            device.UsesRemaining--;
            RecordDevice(device, 3, grenade.OwnerId, grenade.Id, closest);
            if (device.UsesRemaining == 0) RemoveDevice(device);
            return true;
        }
        return false;
    }

    private FpsFieldDevice? FindShotDevice(FpsActorState attacker, Vector3 origin,
        Vector3 direction, ref float hitDistance)
    {
        FpsFieldDevice? hit = null;
        foreach (var device in _fieldDevices)
        {
            if (device.OwnerId != attacker.Id && DeviceFriendly(attacker, device)) continue;
            // Tight upright collision proxy follows the physical gadget rather than a large pickup radius.
            float height = device.Type == FpsFieldUpgrade.TrophySystem ? 0.6f : 0.54f;
            if (!TryRaycastActorCapsule(origin, direction, device.Position, height,
                    out float distance, out _) || distance >= hitDistance) continue;
            hit = device;
            hitDistance = distance;
        }
        return hit;
    }

    private void DamageDevice(FpsFieldDevice device, int damage)
    {
        device.Health -= damage;
        if (device.Health <= 0) RemoveDevice(device);
        else RecordDevice(device, 1);
    }

    private void RemoveOwnedDevices(byte ownerId)
    {
        foreach (var device in _fieldDevices.Where(d => d.OwnerId == ownerId).ToArray()) RemoveDevice(device);
    }

    private void RemoveDevice(FpsFieldDevice device)
    {
        if (_fieldDevices.Remove(device)) RecordDevice(device, 2);
    }

    private void RecordDevice(FpsFieldDevice device, byte state, byte actorId = byte.MaxValue,
        uint grenadeId = 0, Vector3 target = default) =>
        _fieldDeviceEvents.Add(new(device.Id, device.OwnerId, device.Type, state,
            device.Position, Math.Max(0, device.Health), device.UsesRemaining,
            Math.Max(0, device.RemainingSeconds), actorId, grenadeId, target));
}
