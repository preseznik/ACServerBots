using System;
using AssettoServer.Network.ClientMessages;
using AssettoServer.Server.Configuration.Extra;

namespace AssettoServer.Server.Fps;

internal readonly record struct FpsLoadout(FpsWeaponType MainWeapon,
    FpsLethalType Lethal, FpsWeaponType SecondaryWeapon);

internal readonly record struct FpsEffectiveRange(float MaximumDamageEndMeters,
    float MediumDamageEndMeters, float MediumDamageMultiplier,
    float MinimumDamageMultiplier)
{
    public int DamageAtDistance(int maximumDamage, float distance)
    {
        float multiplier = distance <= MaximumDamageEndMeters ? 1
            : distance <= MediumDamageEndMeters ? MediumDamageMultiplier
            : MinimumDamageMultiplier;
        return Math.Max(1, (int)MathF.Round(maximumDamage * multiplier,
            MidpointRounding.AwayFromZero));
    }
}

internal readonly record struct FpsFirearmDefinition(int MagazineCapacity,
    int InitialReserveMagazines, int MaximumReserveMagazines, int Damage,
    FpsEffectiveRange EffectiveRange, float FireInterval, float Range,
    float ReloadSeconds, bool Automatic, float MaximumSpreadRadians,
    float HeatPerShot)
{
    public int DamageAtDistance(float distance) =>
        EffectiveRange.DamageAtDistance(Damage, distance);
}

internal readonly record struct FpsLethalDefinition(float FuseSeconds,
    float ThrowSpeed, float DamageRadius, int MaximumDamage, int EdgeDamage,
    bool Sticky);

internal static class FpsItems
{
    private static readonly FpsEffectiveRange RifleEffectiveRange =
        new(20, 45, 0.70f, 0.50f);
    private static readonly FpsEffectiveRange PistolEffectiveRange =
        new(8, 15, 0.70f, 0.50f);

    public static FpsFirearmDefinition Firearm(FpsWeaponType weapon) => weapon switch
    {
        FpsWeaponType.AssaultRifle => new(40, 4, 4, 34, RifleEffectiveRange,
            0.12f, 120, 1.8f, true, 0.018f, 0.18f),
        FpsWeaponType.CompactSmg => new(30, 5, 5, 25, RifleEffectiveRange,
            0.075f, 65, 1.55f, true, 0.027f, 0.22f),
        FpsWeaponType.DesertEagle => new(7, 4, 4, 55, PistolEffectiveRange,
            0.30f, 80, 1.65f, false, 0.021f, 0.32f),
        FpsWeaponType.Colt1911 => new(8, 5, 5, 34, PistolEffectiveRange,
            0.17f, 55, 1.35f, false, 0.019f, 0.24f),
        _ => throw new ArgumentOutOfRangeException(nameof(weapon), weapon,
            "Unknown FPS firearm"),
    };

    public static FpsLethalDefinition Lethal(FpsLethalType lethal) => lethal switch
    {
        FpsLethalType.FragGrenade => new(3, 16, 7.5f, 125, 20, false),
        FpsLethalType.StickyGrenade => new(2.2f, 18, 6.5f, 120, 15, true),
        _ => throw new ArgumentOutOfRangeException(nameof(lethal), lethal,
            "Unknown FPS lethal"),
    };

    public static FpsLoadout FromConfiguration(FpsLoadoutSelectionConfiguration selection) =>
        new((FpsWeaponType)selection.MainWeapon, (FpsLethalType)selection.Lethal,
            (FpsWeaponType)selection.SecondaryWeapon);

    public static bool IsAllowed(FpsLoadoutConfiguration configuration,
        in FpsLoadout loadout) =>
        configuration.AllowedMainWeapons.Contains((FpsMainWeapon)loadout.MainWeapon)
        && configuration.AllowedLethals.Contains((FpsLethalEquipment)loadout.Lethal)
        && configuration.AllowedSecondaryWeapons.Contains(
            (FpsSecondaryWeapon)loadout.SecondaryWeapon)
        && loadout.MainWeapon is FpsWeaponType.AssaultRifle or FpsWeaponType.CompactSmg
        && loadout.SecondaryWeapon is FpsWeaponType.DesertEagle or FpsWeaponType.Colt1911
        && loadout.Lethal is FpsLethalType.FragGrenade or FpsLethalType.StickyGrenade;
}
