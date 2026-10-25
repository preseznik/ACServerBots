using System.Collections.Generic;
using System.Numerics;
using YamlDotNet.Serialization;

namespace AssettoServer.Server.Configuration.Extra;

public sealed class FpsConfiguration
{
    public bool Enabled { get; init; }
    public FpsVisualTheme Theme { get; init; } = FpsVisualTheme.Blocks;
    public FpsMatchType MatchType { get; init; } = FpsMatchType.Deathmatch;
    public int TimeLimitMinutes { get; init; } = 10;
    public int KillLimit { get; init; } = 20;
    public float RespawnSeconds { get; init; } = 3;
    public float SpawnProtectionSeconds { get; init; } = 1;
    public bool HeadshotsOnly { get; init; }
    public bool InfiniteSprint { get; init; }
    public bool DisableHealthRegeneration { get; init; }
    public bool StartWithBotsOnly { get; init; }
    public FpsBotConfiguration Bots { get; init; } = new();
    public FpsLoadoutConfiguration Loadouts { get; init; } = new();
    public FpsArenaConfiguration Arena { get; init; } = new();
}

public enum FpsMainWeapon : byte
{
    AssaultRifle = 1,
    CompactSmg = 2,
}

public enum FpsLethalEquipment : byte
{
    FragGrenade = 16,
    StickyGrenade = 17,
}

public enum FpsSecondaryWeapon : byte
{
    DesertEagle = 3,
    Colt1911 = 4,
}

public enum FpsFieldUpgrade : byte
{
    MunitionsBox = 1,
    DeadSilence = 2,
    TrophySystem = 3,
}

public enum FpsPerk : byte
{
    None = 0,
    Slipstream = 1,
    Ninja = 2,
    Vision = 3,
    Engineer = 4,
    FlackJacket = 5,
    FastHands = 6,
    GearHead = 7,
}

public sealed class FpsLoadoutSelectionConfiguration
{
    public FpsMainWeapon MainWeapon { get; init; } = FpsMainWeapon.AssaultRifle;
    public FpsLethalEquipment Lethal { get; init; } = FpsLethalEquipment.FragGrenade;
    public FpsSecondaryWeapon SecondaryWeapon { get; init; } = FpsSecondaryWeapon.Colt1911;
    public FpsFieldUpgrade FieldUpgrade { get; init; } = FpsFieldUpgrade.MunitionsBox;
    public List<FpsPerk> Perks { get; init; } = [];
}

public sealed class FpsLoadoutConfiguration
{
    public List<FpsMainWeapon> AllowedMainWeapons { get; init; } =
        [FpsMainWeapon.AssaultRifle, FpsMainWeapon.CompactSmg];
    public List<FpsLethalEquipment> AllowedLethals { get; init; } =
        [FpsLethalEquipment.FragGrenade, FpsLethalEquipment.StickyGrenade];
    public List<FpsSecondaryWeapon> AllowedSecondaryWeapons { get; init; } =
        [FpsSecondaryWeapon.DesertEagle, FpsSecondaryWeapon.Colt1911];
    public List<FpsFieldUpgrade> AllowedFieldUpgrades { get; init; } =
        [FpsFieldUpgrade.MunitionsBox, FpsFieldUpgrade.DeadSilence, FpsFieldUpgrade.TrophySystem];
    public List<FpsPerk> AllowedPerks { get; init; } =
        [FpsPerk.Slipstream, FpsPerk.Ninja, FpsPerk.Vision, FpsPerk.Engineer,
            FpsPerk.FlackJacket, FpsPerk.FastHands, FpsPerk.GearHead];
    public FpsLoadoutSelectionConfiguration HumanDefault { get; init; } = new();
    public FpsLoadoutSelectionConfiguration BotDefault { get; init; } = new();
}

public enum FpsVisualTheme
{
    Blocks,
    Modern,
}

public enum FpsMatchType
{
    Deathmatch,
    TeamDeathmatch,
    HardcoreDeathmatch,
    HardcoreTeamDeathmatch,
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
    public string GeometryPath { get; init; } = "fps-arena-geometry.bin";
    public string NavigationPath { get; init; } = "fps-arena-navigation.bin";
    public Vector3 BoundsMin { get; init; }
    public Vector3 BoundsMax { get; init; }
    public List<Vector3> PlayableBoundary { get; init; } = [];
    public float OutOfBoundsSeconds { get; init; } = 3;
    public List<FpsSpawnConfiguration> SpawnPoints { get; init; } = [];
}

public sealed class FpsSpawnConfiguration
{
    public Vector3 Position { get; init; }
    public float YawRadians { get; init; }
}
