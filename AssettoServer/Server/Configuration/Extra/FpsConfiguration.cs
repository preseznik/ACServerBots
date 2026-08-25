using System.Collections.Generic;
using System.Numerics;
using YamlDotNet.Serialization;

namespace AssettoServer.Server.Configuration.Extra;

public sealed class FpsConfiguration
{
    public bool Enabled { get; init; }
    public FpsMatchType MatchType { get; init; } = FpsMatchType.Deathmatch;
    public int TimeLimitMinutes { get; init; } = 10;
    public int KillLimit { get; init; } = 20;
    public float RespawnSeconds { get; init; } = 3;
    public float SpawnProtectionSeconds { get; init; } = 1;
    public FpsBotConfiguration Bots { get; init; } = new();
    public FpsArenaConfiguration Arena { get; init; } = new();
}

public enum FpsMatchType
{
    Deathmatch,
}

public sealed class FpsBotConfiguration
{
    public float Difficulty { get; init; } = 0.75f;
    public float DifficultyVariancePercent { get; init; } = 10;
    public float Aggression { get; init; } = 0.5f;
    public float AggressionVariancePercent { get; init; } = 15;
    public int Health { get; init; } = 100;
}

public sealed class FpsArenaConfiguration
{
    public Vector3 BoundsMin { get; init; }
    public Vector3 BoundsMax { get; init; }
    public List<FpsSpawnConfiguration> SpawnPoints { get; init; } = [];
}

public sealed class FpsSpawnConfiguration
{
    public Vector3 Position { get; init; }
    public float YawRadians { get; init; }
}
