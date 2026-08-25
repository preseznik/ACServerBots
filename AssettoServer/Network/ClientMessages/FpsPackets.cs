using System;
using System.Numerics;

namespace AssettoServer.Network.ClientMessages;

[Flags]
public enum FpsInputButtons : byte
{
    None = 0,
    Fire = 1,
    Sprint = 2,
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

[OnlineEvent(Key = "ASRC_FpsSnapshot", Udp = true)]
public sealed class FpsSnapshotPacket : OnlineEvent<FpsSnapshotPacket>
{
    public const int Capacity = 16;

    [OnlineEventField(Name = "sequence")] public uint Sequence;
    [OnlineEventField(Name = "count")] public byte Count;
    [OnlineEventField(Name = "actorIDs", Size = Capacity)] public byte[] ActorIds = new byte[Capacity];
    [OnlineEventField(Name = "flags", Size = Capacity)] public byte[] Flags = new byte[Capacity];
    [OnlineEventField(Name = "positions", Size = Capacity)] public Vector3[] Positions = new Vector3[Capacity];
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
