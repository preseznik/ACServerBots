using AssettoServer.Server.Configuration.Extra;
using AssettoServer.Server.Configuration.Kunos;

namespace AssettoServer.Server.Fps;

public enum FpsOperatorModel : byte
{
    Officer = 0,
    Ghost = 1,
}

public enum FpsOperatorSkin : byte
{
    Standard = 0,
    BlueGrey = 1,
    DesertTan = 2,
}

internal static class FpsOperatorModels
{
    public static FpsOperatorModel DefaultModel(FpsVisualTheme theme, FpsMatchType matchType,
        FpsTeamAssignment team) => theme == FpsVisualTheme.Modern
        && (matchType is FpsMatchType.TeamDeathmatch or FpsMatchType.HardcoreTeamDeathmatch)
        && team == FpsTeamAssignment.Team2 ? FpsOperatorModel.Ghost : FpsOperatorModel.Officer;

    public static uint AllowedMask(FpsVisualTheme theme,
        FpsMatchType matchType = FpsMatchType.Deathmatch, FpsTeamAssignment team = FpsTeamAssignment.Auto) =>
        theme != FpsVisualTheme.Modern ? 1u
        : matchType is FpsMatchType.TeamDeathmatch or FpsMatchType.HardcoreTeamDeathmatch
            ? 1u << (byte)DefaultModel(theme, matchType, team) : 3u;

    public static uint AllowedSkins(FpsVisualTheme theme, FpsOperatorModel model) =>
        theme != FpsVisualTheme.Modern ? 1u : model == FpsOperatorModel.Ghost ? 7u : 3u;

    public static bool IsAllowed(FpsVisualTheme theme, FpsMatchType matchType, FpsTeamAssignment team,
        FpsOperatorModel model, FpsOperatorSkin skin) =>
        (byte)model < 32 && (AllowedMask(theme, matchType, team) & (1u << (byte)model)) != 0
        && (byte)skin < 32 && (AllowedSkins(theme, model) & (1u << (byte)skin)) != 0;
}
