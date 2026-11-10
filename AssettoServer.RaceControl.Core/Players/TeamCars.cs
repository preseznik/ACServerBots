namespace AssettoServer.RaceControl.Core.Players;

/// <summary>A grid row as the one-car-per-team rule sees it.</summary>
public sealed record TeamCarRow(int Number, string TeamName, bool IsSpectator);

/// <summary>
/// In a driver-change ("hot-seat") race a team is the drivers sharing one car, so each team name may
/// be on only one car. The bots' default label "Race Control" is exempt.
/// </summary>
public static class TeamCars
{
    /// <summary>The default team label of bot and new rows; it never makes a team.</summary>
    public const string BotTeamName = "Race Control";

    public static bool IsTeam(string? name)
        => !string.IsNullOrWhiteSpace(name) && !string.Equals(name.Trim(), BotTeamName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Team names that are on more than one racing car, with those cars' numbers.</summary>
    public static IReadOnlyList<(string Team, IReadOnlyList<int> Cars)> Conflicts(IEnumerable<TeamCarRow> rows)
        => rows
            .Where(row => !row.IsSpectator && IsTeam(row.TeamName))
            .GroupBy(row => row.TeamName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => (group.Key, (IReadOnlyList<int>)group.Select(row => row.Number).ToArray()))
            .ToArray();

    /// <summary>The car that already has this row's team, or null when the team name is free.</summary>
    public static int? ConflictFor(TeamCarRow row, IEnumerable<TeamCarRow> others)
    {
        if (row.IsSpectator || !IsTeam(row.TeamName))
            return null;
        return others
            .Where(other => other.Number != row.Number && !other.IsSpectator
                            && string.Equals(other.TeamName?.Trim(), row.TeamName.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(other => (int?)other.Number)
            .FirstOrDefault();
    }
}
