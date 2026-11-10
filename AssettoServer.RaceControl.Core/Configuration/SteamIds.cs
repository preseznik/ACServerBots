namespace AssettoServer.RaceControl.Core.Configuration;

/// <summary>
/// Steam IDs entered for reserved (team) cars. Assetto Corsa identifies drivers by SteamID64,
/// a 17-digit number starting with 7656119.
/// </summary>
public static class SteamIds
{
    private static readonly char[] Separators = [';', ',', ' ', '\t', '\r', '\n'];

    public static List<string> Parse(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToList();

    public static string Format(IEnumerable<string> ids) => string.Join("; ", ids);

    public static IEnumerable<string> Normalize(IEnumerable<string> ids)
        => ids.Select(id => id.Trim()).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal);

    public static bool IsValid(string id)
        => id.Length == 17 && id.StartsWith("7656119", StringComparison.Ordinal) && id.All(char.IsAsciiDigit);
}
