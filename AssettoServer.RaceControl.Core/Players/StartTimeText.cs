using System.Globalization;

namespace AssettoServer.RaceControl.Core.Players;

/// <summary>Reads an event start time typed as 24-hour HH:mm.</summary>
public static class StartTimeText
{
    /// <summary>Accepts 20:00, 9:30, 2030 and 20.30.</summary>
    public static bool TryParse(string? text, out TimeSpan time)
    {
        string value = (text ?? string.Empty).Trim().Replace('.', ':');
        if (value.Length is 3 or 4 && value.All(char.IsAsciiDigit))
            value = value[..^2] + ":" + value[^2..];
        return TimeSpan.TryParseExact(value, [@"h\:mm", @"hh\:mm"], CultureInfo.InvariantCulture, out time)
               && time < TimeSpan.FromDays(1);
    }
}
