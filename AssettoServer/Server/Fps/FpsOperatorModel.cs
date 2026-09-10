using AssettoServer.Server.Configuration.Extra;

namespace AssettoServer.Server.Fps;

public enum FpsOperatorModel : byte
{
    Officer = 0,
    Ghost = 1,
}

internal static class FpsOperatorModels
{
    public static uint AllowedMask(FpsVisualTheme theme) =>
        theme == FpsVisualTheme.Modern ? 3u : 1u;

    public static bool IsAllowed(FpsVisualTheme theme, FpsOperatorModel model) =>
        (byte)model < 32 && (AllowedMask(theme) & (1u << (byte)model)) != 0;
}
