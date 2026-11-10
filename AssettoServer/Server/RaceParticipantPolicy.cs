using System.Collections.Generic;
using System.Linq;
using AssettoServer.Shared.Model;

namespace AssettoServer.Server;

public static class RaceParticipantPolicy
{
    public static bool ShouldControlSlot(AiMode mode, bool hasClient) => mode switch
    {
        AiMode.Fixed => true,
        AiMode.Auto => !hasClient,
        _ => false
    };

    public static HashSet<byte> FreezeBotRoster(IEnumerable<(byte SessionId, AiMode Mode, bool HasClient)> slots)
        => slots.Where(slot => ShouldControlSlot(slot.Mode, slot.HasClient))
            .Select(slot => slot.SessionId)
            .ToHashSet();

    public static bool ShouldReplaceDisconnectedDriver(bool raceRosterFrozen) => !raceRosterFrozen;

    public static bool CanTakeOverBotSlot(bool enabled, bool raceIsActive, AiMode mode, bool aiControlled)
        => enabled && raceIsActive && mode == AiMode.Auto && aiControlled;

    /// <summary>
    /// A car reserved for named drivers keeps its race result for a grace period after its driver
    /// leaves, so a listed teammate (or the same driver) can rejoin and continue the stint.
    /// </summary>
    public static bool CanHoldForDriverChange(int graceSeconds, bool isRaceSession, bool raceHasEnded,
        IReadOnlyCollection<ulong> allowedGuids, EntryCarResult? result)
        => graceSeconds > 0
           && isRaceSession
           && !raceHasEnded
           && allowedGuids.Count > 0
           && result is { IsSpectator: false, IsDnf: false, HasCompletedLastLap: false };

    /// <summary>
    /// A joining driver continues the car's result only when both the previous holder and the joining
    /// driver are listed for that car and the result is still running.
    /// </summary>
    public static bool ShouldContinueResult(IReadOnlyCollection<ulong> allowedGuids, EntryCarResult previous,
        ulong joiningGuid)
        => allowedGuids.Contains(joiningGuid)
           && allowedGuids.Contains(previous.Guid)
           && previous is { IsSpectator: false, IsDnf: false, HasCompletedLastLap: false };

    /// <summary>
    /// Hands a running result to the next driver. Laps, times and position are kept; the shown driver
    /// changes and every driver who shared the car is recorded in <see cref="EntryCarResult.Drivers"/>.
    /// </summary>
    public static void HandOverResult(EntryCarResult result, ulong guid, string name, string team, string nationCode)
    {
        if (result.Drivers.Count == 0 && !string.IsNullOrWhiteSpace(result.Name))
            result.Drivers.Add(result.Name);
        if (!string.IsNullOrWhiteSpace(name) && !result.Drivers.Contains(name))
            result.Drivers.Add(name);

        result.Guid = guid;
        result.Name = name;
        if (!string.IsNullOrWhiteSpace(team))
            result.Team = team;
        if (!string.IsNullOrWhiteSpace(nationCode))
            result.NationCode = nationCode;
    }

    public static IEnumerable<byte> ExpiredDriverChangeHolds(IReadOnlyDictionary<byte, long> deadlines, long now)
        => deadlines.Where(hold => hold.Value <= now).Select(hold => hold.Key).ToArray();

    public static uint RefreshClassification(IDictionary<byte, EntryCarResult> results)
    {
        uint position = 0;
        uint leaderLapCount = 0;
        foreach (var spectator in results.Values.Where(result => result.IsSpectator))
            spectator.RacePos = byte.MaxValue;
        foreach (var participant in OrderClassification(results))
        {
            participant.Value.RacePos = position++;
            leaderLapCount = uint.Max(leaderLapCount, participant.Value.NumLaps);
        }

        return leaderLapCount;
    }

    public static uint ReplaceParticipant(IDictionary<byte, EntryCarResult> results, byte sessionId, EntryCarResult replacement)
    {
        results[sessionId] = replacement;
        return RefreshClassification(results);
    }

    public static IEnumerable<KeyValuePair<byte, EntryCarResult>> OrderClassification(
        IEnumerable<KeyValuePair<byte, EntryCarResult>> results)
        => results.Where(result => !result.Value.IsSpectator)
            .OrderBy(result => result.Value.IsDnf)
            .ThenByDescending(result => result.Value.NumLaps)
            .ThenBy(result => result.Value.TotalTime);

    public static bool HasUnfinishedActiveParticipant(
        IEnumerable<(bool Active, EntryCarResult Result)> participants)
        => participants.Any(participant => participant.Active
                                           && !participant.Result.IsSpectator
                                           && !participant.Result.IsDnf
                                           && !participant.Result.HasCompletedLastLap);
}
