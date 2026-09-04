using System;
using System.Numerics;

namespace AssettoServer.Network.ClientMessages;

[Flags]
public enum FpsInputButtons : ushort
{
    None = 0,
    Fire = 1,
    Sprint = 2,
    Jump = 4,
    Crouch = 8,
    Reload = 16,
    Aim = 32,
    // The input packet still carries the physical crouch button. This flag tells the
    // authoritative stance state machine whether a short press should latch crouch.
    CrouchToggleMode = 64,
    ThrowLethal = 128,
}

[OnlineEvent(Key = "ASRC_FpsInput", Udp = true)]
public sealed class FpsInputPacket : OnlineEvent<FpsInputPacket>
{
    [OnlineEventField(Name = "sequence")] public uint Sequence;
    [OnlineEventField(Name = "move")] public Vector2 Move;
    [OnlineEventField(Name = "yaw")] public float Yaw;
    [OnlineEventField(Name = "pitch")] public float Pitch;
    [OnlineEventField(Name = "buttons")] public FpsInputButtons Buttons;
    [OnlineEventField(Name = "selectedSlot")] public byte SelectedSlot;
}

[OnlineEvent(Key = "ASRC_FpsReady")]
public sealed class FpsReadyPacket : OnlineEvent<FpsReadyPacket>
{
    [OnlineEventField(Name = "protocol")] public ushort Protocol = 2;
}

[OnlineEvent(Key = "ASRC_FpsLoadoutSelect")]
public sealed class FpsLoadoutSelectPacket : OnlineEvent<FpsLoadoutSelectPacket>
{
    [OnlineEventField(Name = "mainWeapon")] public byte MainWeapon;
    [OnlineEventField(Name = "lethal")] public byte Lethal;
    [OnlineEventField(Name = "secondaryWeapon")] public byte SecondaryWeapon;
}

[OnlineEvent(Key = "ASRC_FpsLoadoutCatalog")]
public sealed class FpsLoadoutCatalogPacket : OnlineEvent<FpsLoadoutCatalogPacket>
{
    [OnlineEventField(Name = "allowedMainWeapons")] public uint AllowedMainWeapons;
    [OnlineEventField(Name = "allowedLethals")] public uint AllowedLethals;
    [OnlineEventField(Name = "allowedSecondaryWeapons")] public uint AllowedSecondaryWeapons;
    [OnlineEventField(Name = "defaultMainWeapon")] public byte DefaultMainWeapon;
    [OnlineEventField(Name = "defaultLethal")] public byte DefaultLethal;
    [OnlineEventField(Name = "defaultSecondaryWeapon")] public byte DefaultSecondaryWeapon;
}

public enum FpsLoadoutResultCode : byte
{
    Applied = 1,
    QueuedForRespawn = 2,
    InvalidSelection = 3,
    NotAvailable = 4,
}

[OnlineEvent(Key = "ASRC_FpsLoadoutResult")]
public sealed class FpsLoadoutResultPacket : OnlineEvent<FpsLoadoutResultPacket>
{
    [OnlineEventField(Name = "result")] public FpsLoadoutResultCode Result;
    [OnlineEventField(Name = "mainWeapon")] public byte MainWeapon;
    [OnlineEventField(Name = "lethal")] public byte Lethal;
    [OnlineEventField(Name = "secondaryWeapon")] public byte SecondaryWeapon;
}

[OnlineEvent(Key = "ASRC_FpsLoadoutState")]
public sealed class FpsLoadoutStatePacket : OnlineEvent<FpsLoadoutStatePacket>
{
    [OnlineEventField(Name = "actorID")] public byte ActorId;
    [OnlineEventField(Name = "mainWeapon")] public byte MainWeapon;
    [OnlineEventField(Name = "lethal")] public byte Lethal;
    [OnlineEventField(Name = "secondaryWeapon")] public byte SecondaryWeapon;
    [OnlineEventField(Name = "activeSlot")] public byte ActiveSlot;
    [OnlineEventField(Name = "lethalsRemaining")] public byte LethalsRemaining;
}

[OnlineEvent(Key = "ASRC_FpsEnvironmentRequest")]
public sealed class FpsEnvironmentRequestPacket : OnlineEvent<FpsEnvironmentRequestPacket>
{
    [OnlineEventField(Name = "weatherType")] public byte WeatherType;
    [OnlineEventField(Name = "timeOfDaySeconds")] public uint TimeOfDaySeconds;
}

[Flags]
public enum FpsClientDiagnosticFlags : ushort
{
    None = 0,
    GameplayActive = 1,
    AssetCached = 2,
    ModelLoaded = 4,
    ActorAvailable = 8,
    CameraActive = 16,
    DirectRenderReady = 32,
    ThirdPerson = 64,
    LocalAvatarReady = 128,
    RemoteActorsAvailable = 256,
    RemoteActorsRendered = 512,
    ShotEventsReceived = 1024,
    ShotEffectsRendered = 2048,
}

[OnlineEvent(Key = "ASRC_FpsClientDiagnostic")]
public sealed class FpsClientDiagnosticPacket : OnlineEvent<FpsClientDiagnosticPacket>
{
    [OnlineEventField(Name = "pipeline")] public byte Pipeline;
    [OnlineEventField(Name = "flags")] public FpsClientDiagnosticFlags Flags;
    [OnlineEventField(Name = "attempts")] public uint Attempts;
    [OnlineEventField(Name = "completions")] public uint Completions;
    [OnlineEventField(Name = "frameBeginCalls")] public uint FrameBeginCalls;
    [OnlineEventField(Name = "draw3DCalls")] public uint Draw3DCalls;
    [OnlineEventField(Name = "drawUICalls")] public uint DrawUiCalls;
    [OnlineEventField(Name = "directDrawAttempts")] public uint DirectDrawAttempts;
    [OnlineEventField(Name = "directDrawCompletions")] public uint DirectDrawCompletions;
    [OnlineEventField(Name = "directDrawPending")] public uint DirectDrawPending;
    [OnlineEventField(Name = "directDrawFailures")] public uint DirectDrawFailures;
    [OnlineEventField(Name = "position")] public Vector3 Position;
    [OnlineEventField(Name = "remoteActorID")] public byte RemoteActorId = byte.MaxValue;
    [OnlineEventField(Name = "remoteTarget")] public Vector3 RemoteTarget;
    [OnlineEventField(Name = "remoteRender")] public Vector3 RemoteRender;
    [OnlineEventField(Name = "remoteTargetYaw")] public float RemoteTargetYaw;
    [OnlineEventField(Name = "remoteRenderYaw")] public float RemoteRenderYaw;
    [OnlineEventField(Name = "stage", Size = 48)] public string Stage = string.Empty;
}

[OnlineEvent(Key = "ASRC_FpsSnapshot", Udp = true)]
public sealed class FpsSnapshotPacket : OnlineEvent<FpsSnapshotPacket>
{
    public const int Capacity = 16;

    [OnlineEventField(Name = "sequence")] public uint Sequence;
    [OnlineEventField(Name = "count")] public byte Count;
    [OnlineEventField(Name = "actorIDs", Size = Capacity)] public byte[] ActorIds = new byte[Capacity];
    [OnlineEventField(Name = "flags", Size = Capacity)] public byte[] Flags = new byte[Capacity];
    // Two traversal bits per actor: active in bits 0..15 and vault in bits 16..31.
    // The compact bitfield keeps the UDP event under CSP's silent payload-drop threshold.
    [OnlineEventField(Name = "actionStates")] public uint ActionStates;
    [OnlineEventField(Name = "spawnCounts", Size = Capacity)] public uint[] SpawnCounts = new uint[Capacity];
    [OnlineEventField(Name = "positions", Size = Capacity)] public Vector3[] Positions = new Vector3[Capacity];
    [OnlineEventField(Name = "groundYs", Size = Capacity)] public float[] GroundYs = new float[Capacity];
    // Quantized planar direction (0..254); 255 means no collision constraint. Keeping this
    // byte-sized is important because CSP silently drops oversized UDP online events.
    [OnlineEventField(Name = "collisionDirections", Size = Capacity)] public byte[] CollisionDirections = new byte[Capacity];
    [OnlineEventField(Name = "yaws", Size = Capacity)] public float[] Yaws = new float[Capacity];
    [OnlineEventField(Name = "pitches", Size = Capacity)] public float[] Pitches = new float[Capacity];
    // Low byte is health (configured maximum is 200); high byte is stamina (0..100).
    // Packing both vitals keeps this UDP event below CSP's silent payload-drop ceiling.
    [OnlineEventField(Name = "vitals", Size = Capacity)] public ushort[] Vitals = new ushort[Capacity];
    [OnlineEventField(Name = "kills", Size = Capacity)] public ushort[] Kills = new ushort[Capacity];
    [OnlineEventField(Name = "deaths", Size = Capacity)] public ushort[] Deaths = new ushort[Capacity];
    [OnlineEventField(Name = "ammo", Size = Capacity)] public byte[] Ammo = new byte[Capacity];
    [OnlineEventField(Name = "reserveMagazines", Size = Capacity)] public byte[] ReserveMagazines = new byte[Capacity];
    [OnlineEventField(Name = "reloadRemaining", Size = Capacity)] public float[] ReloadRemaining = new float[Capacity];
}

[OnlineEvent(Key = "ASRC_FpsBoundary")]
public sealed class FpsBoundaryPacket : OnlineEvent<FpsBoundaryPacket>
{
    [OnlineEventField(Name = "outside")] public byte Outside;
    [OnlineEventField(Name = "remainingSeconds")] public float RemainingSeconds;
}

[OnlineEvent(Key = "ASRC_FpsRoster")]
public sealed class FpsRosterPacket : OnlineEvent<FpsRosterPacket>
{
    [OnlineEventField(Name = "actorID")] public byte ActorId;
    [OnlineEventField(Name = "role")] public byte Role;
    [OnlineEventField(Name = "name", Size = 32)] public string Name = string.Empty;
}

[OnlineEvent(Key = "ASRC_FpsMatch")]
public sealed class FpsMatchPacket : OnlineEvent<FpsMatchPacket>
{
    [OnlineEventField(Name = "state")] public byte State;
    [OnlineEventField(Name = "remainingSeconds")] public float RemainingSeconds;
    [OnlineEventField(Name = "killLimit")] public ushort KillLimit;
    [OnlineEventField(Name = "maximumHealth")] public ushort MaximumHealth;
    [OnlineEventField(Name = "winnerID")] public byte WinnerId = byte.MaxValue;
    [OnlineEventField(Name = "weatherType")] public byte WeatherType;
    [OnlineEventField(Name = "timeOfDaySeconds")] public uint TimeOfDaySeconds;
}

[OnlineEvent(Key = "ASRC_FpsKill")]
public sealed class FpsKillPacket : OnlineEvent<FpsKillPacket>
{
    [OnlineEventField(Name = "killerID")] public byte KillerId = byte.MaxValue;
    [OnlineEventField(Name = "victimID")] public byte VictimId;
    [OnlineEventField(Name = "killerKills")] public ushort KillerKills;
    [OnlineEventField(Name = "victimDeaths")] public ushort VictimDeaths;
    [OnlineEventField(Name = "itemID")] public byte ItemId;
}

[OnlineEvent(Key = "ASRC_FpsHit")]
public sealed class FpsHitPacket : OnlineEvent<FpsHitPacket>
{
    [OnlineEventField(Name = "attackerID")] public byte AttackerId;
    [OnlineEventField(Name = "victimID")] public byte VictimId;
    [OnlineEventField(Name = "remainingHealth")] public ushort RemainingHealth;
    [OnlineEventField(Name = "itemID")] public byte ItemId;
}

[OnlineEvent(Key = "ASRC_FpsAward")]
public sealed class FpsAwardPacket : OnlineEvent<FpsAwardPacket>
{
    [OnlineEventField(Name = "actorID")] public byte ActorId;
    [OnlineEventField(Name = "victimID")] public byte VictimId = byte.MaxValue;
    [OnlineEventField(Name = "points")] public ushort Points;
    [OnlineEventField(Name = "totalScore")] public uint TotalScore;
    [OnlineEventField(Name = "flags")] public byte Flags;
}

public enum FpsWeaponType : byte
{
    AssaultRifle = 1,
    CompactSmg = 2,
    DesertEagle = 3,
    Colt1911 = 4,
}

public enum FpsLethalType : byte
{
    FragGrenade = 16,
    StickyGrenade = 17,
}

public enum FpsPickupState : byte
{
    Spawned = 1,
    Removed = 2,
}

[OnlineEvent(Key = "ASRC_FpsPickup")]
public sealed class FpsPickupPacket : OnlineEvent<FpsPickupPacket>
{
    [OnlineEventField(Name = "pickupID")] public uint PickupId;
    [OnlineEventField(Name = "state")] public FpsPickupState State;
    [OnlineEventField(Name = "weaponType")] public FpsWeaponType WeaponType;
    [OnlineEventField(Name = "collectorID")] public byte CollectorId = byte.MaxValue;
    [OnlineEventField(Name = "position")] public Vector3 Position;
}

[OnlineEvent(Key = "ASRC_FpsShot", Udp = true)]
public sealed class FpsShotPacket : OnlineEvent<FpsShotPacket>
{
    [OnlineEventField(Name = "shooterID")] public byte ShooterId;
    [OnlineEventField(Name = "sequence")] public uint Sequence;
    [OnlineEventField(Name = "origin")] public Vector3 Origin;
    [OnlineEventField(Name = "direction")] public Vector3 Direction;
    [OnlineEventField(Name = "distance")] public float Distance;
    [OnlineEventField(Name = "impact")] public byte Impact;
    [OnlineEventField(Name = "targetID")] public byte TargetId = byte.MaxValue;
    [OnlineEventField(Name = "weaponType")] public FpsWeaponType WeaponType;
}

[OnlineEvent(Key = "ASRC_FpsGrenadeSnapshot", Udp = true)]
public sealed class FpsGrenadeSnapshotPacket : OnlineEvent<FpsGrenadeSnapshotPacket>
{
    public const int Capacity = 8;

    [OnlineEventField(Name = "sequence")] public uint Sequence;
    [OnlineEventField(Name = "count")] public byte Count;
    [OnlineEventField(Name = "grenadeIDs", Size = Capacity)] public uint[] GrenadeIds = new uint[Capacity];
    [OnlineEventField(Name = "ownerIDs", Size = Capacity)] public byte[] OwnerIds = new byte[Capacity];
    [OnlineEventField(Name = "types", Size = Capacity)] public byte[] Types = new byte[Capacity];
    [OnlineEventField(Name = "flags", Size = Capacity)] public byte[] Flags = new byte[Capacity];
    [OnlineEventField(Name = "positions", Size = Capacity)] public Vector3[] Positions = new Vector3[Capacity];
    [OnlineEventField(Name = "velocities", Size = Capacity)] public Vector3[] Velocities = new Vector3[Capacity];
    [OnlineEventField(Name = "remaining", Size = Capacity)] public float[] Remaining = new float[Capacity];
}

[OnlineEvent(Key = "ASRC_FpsGrenadeExploded")]
public sealed class FpsGrenadeExplodedPacket : OnlineEvent<FpsGrenadeExplodedPacket>
{
    [OnlineEventField(Name = "grenadeID")] public uint GrenadeId;
    [OnlineEventField(Name = "ownerID")] public byte OwnerId;
    [OnlineEventField(Name = "type")] public FpsLethalType Type;
    [OnlineEventField(Name = "position")] public Vector3 Position;
}
