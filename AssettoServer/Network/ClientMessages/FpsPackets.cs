using System;
using System.Numerics;

namespace AssettoServer.Network.ClientMessages;

[Flags]
public enum FpsInputButtons : byte
{
    None = 0,
    Fire = 1,
    Sprint = 2,
    Jump = 4,
    Crouch = 8,
}

[OnlineEvent(Key = "ASRC_FpsInput", Udp = true)]
public sealed class FpsInputPacket : OnlineEvent<FpsInputPacket>
{
    [OnlineEventField(Name = "sequence")] public uint Sequence;
    [OnlineEventField(Name = "move")] public Vector2 Move;
    [OnlineEventField(Name = "yaw")] public float Yaw;
    [OnlineEventField(Name = "pitch")] public float Pitch;
    [OnlineEventField(Name = "buttons")] public FpsInputButtons Buttons;
}

[OnlineEvent(Key = "ASRC_FpsReady")]
public sealed class FpsReadyPacket : OnlineEvent<FpsReadyPacket>
{
    [OnlineEventField(Name = "protocol")] public ushort Protocol = 1;
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
    [OnlineEventField(Name = "positions", Size = Capacity)] public Vector3[] Positions = new Vector3[Capacity];
    [OnlineEventField(Name = "groundYs", Size = Capacity)] public float[] GroundYs = new float[Capacity];
    [OnlineEventField(Name = "yaws", Size = Capacity)] public float[] Yaws = new float[Capacity];
    [OnlineEventField(Name = "pitches", Size = Capacity)] public float[] Pitches = new float[Capacity];
    [OnlineEventField(Name = "health", Size = Capacity)] public ushort[] Health = new ushort[Capacity];
    [OnlineEventField(Name = "kills", Size = Capacity)] public ushort[] Kills = new ushort[Capacity];
    [OnlineEventField(Name = "deaths", Size = Capacity)] public ushort[] Deaths = new ushort[Capacity];
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
    [OnlineEventField(Name = "winnerID")] public byte WinnerId = byte.MaxValue;
}

[OnlineEvent(Key = "ASRC_FpsKill")]
public sealed class FpsKillPacket : OnlineEvent<FpsKillPacket>
{
    [OnlineEventField(Name = "killerID")] public byte KillerId = byte.MaxValue;
    [OnlineEventField(Name = "victimID")] public byte VictimId;
    [OnlineEventField(Name = "killerKills")] public ushort KillerKills;
    [OnlineEventField(Name = "victimDeaths")] public ushort VictimDeaths;
}

[OnlineEvent(Key = "ASRC_FpsHit")]
public sealed class FpsHitPacket : OnlineEvent<FpsHitPacket>
{
    [OnlineEventField(Name = "attackerID")] public byte AttackerId;
    [OnlineEventField(Name = "victimID")] public byte VictimId;
    [OnlineEventField(Name = "remainingHealth")] public ushort RemainingHealth;
}

[OnlineEvent(Key = "ASRC_FpsShot", Udp = true)]
public sealed class FpsShotPacket : OnlineEvent<FpsShotPacket>
{
    [OnlineEventField(Name = "shooterID")] public byte ShooterId;
    [OnlineEventField(Name = "sequence")] public uint Sequence;
    [OnlineEventField(Name = "origin")] public Vector3 Origin;
    [OnlineEventField(Name = "direction")] public Vector3 Direction;
    [OnlineEventField(Name = "distance")] public float Distance;
}
