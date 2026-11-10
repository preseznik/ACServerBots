namespace AssettoServer.RaceControl.Core.Players;

/// <summary>A grid row as the assigner sees it.</summary>
public sealed record AssignableSlot(int Index, string CarId, IReadOnlyList<string> SteamIds, bool IsSpectator);

/// <summary>Put these drivers in this grid row.</summary>
public sealed record SlotAssignment(int SlotIndex, IReadOnlyList<string> SteamIds, string DriverName, string TeamName);

public sealed record AutoAssignResult(IReadOnlyList<SlotAssignment> Assignments,
    IReadOnlyList<IReadOnlyList<PlayerRegistration>> Unplaced);

/// <summary>
/// Turns registrations into grid assignments. Players who entered the same team name share one car
/// (a team car with driver changes); everyone else gets a car of their own.
/// </summary>
public static class RegistrationAssigner
{
    /// <summary>Teams (same team name, ignoring case) and solo players, in registration order.</summary>
    public static IReadOnlyList<IReadOnlyList<PlayerRegistration>> Groups(IEnumerable<PlayerRegistration> registrations)
    {
        var groups = new List<List<PlayerRegistration>>();
        var teams = new Dictionary<string, List<PlayerRegistration>>(StringComparer.OrdinalIgnoreCase);
        foreach (var registration in registrations.OrderBy(registration => registration.RegisteredAt))
        {
            string team = registration.TeamName.Trim();
            if (team.Length == 0)
            {
                groups.Add([registration]);
                continue;
            }
            if (!teams.TryGetValue(team, out var members))
            {
                members = [];
                teams[team] = members;
                groups.Add(members);
            }
            members.Add(registration);
        }
        return groups;
    }

    /// <summary>The selected players plus their teammates, as one group per team or solo player.</summary>
    public static IReadOnlyList<IReadOnlyList<PlayerRegistration>> ExpandToTeams(
        IEnumerable<PlayerRegistration> selected, IReadOnlyList<PlayerRegistration> all)
    {
        var selectedIds = selected.Select(registration => registration.SteamId).ToHashSet(StringComparer.Ordinal);
        return Groups(all).Where(group => group.Any(member => selectedIds.Contains(member.SteamId))).ToArray();
    }

    public static SlotAssignment ToSlot(int slotIndex, IReadOnlyList<PlayerRegistration> group)
    {
        if (group.Count == 0)
            throw new ArgumentException("A group needs at least one player.", nameof(group));
        string team = group.Select(member => member.TeamName.Trim()).FirstOrDefault(name => name.Length > 0) ?? string.Empty;
        return new SlotAssignment(slotIndex,
            group.Select(member => member.SteamId).Distinct(StringComparer.Ordinal).ToArray(),
            group.Count > 1 && team.Length > 0 ? team : group[0].DisplayName,
            team);
    }

    /// <summary>
    /// Places every group that isn't on the grid yet into a free racing row (no Steam IDs), preferring a
    /// row with the car most of the group asked for. Groups that don't fit are returned as unplaced.
    /// </summary>
    public static AutoAssignResult AutoAssign(IReadOnlyList<AssignableSlot> slots, IReadOnlyList<PlayerRegistration> registrations)
    {
        var assigned = slots.SelectMany(slot => slot.SteamIds).ToHashSet(StringComparer.Ordinal);
        var free = slots.Where(slot => !slot.IsSpectator && slot.SteamIds.Count == 0).ToList();
        var assignments = new List<SlotAssignment>();
        var unplaced = new List<IReadOnlyList<PlayerRegistration>>();

        foreach (var group in Groups(registrations))
        {
            if (group.Any(member => assigned.Contains(member.SteamId)))
                continue;
            string? preferred = group
                .Select(member => member.PreferredCarId)
                .Where(car => !string.IsNullOrWhiteSpace(car))
                .GroupBy(car => car, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(cars => cars.Count())
                .Select(cars => cars.Key)
                .FirstOrDefault();
            var slot = free.FirstOrDefault(candidate => preferred is not null
                           && string.Equals(candidate.CarId, preferred, StringComparison.OrdinalIgnoreCase))
                       ?? free.FirstOrDefault();
            if (slot is null)
            {
                unplaced.Add(group);
                continue;
            }
            free.Remove(slot);
            assignments.Add(ToSlot(slot.Index, group));
        }

        return new AutoAssignResult(assignments, unplaced);
    }
}
