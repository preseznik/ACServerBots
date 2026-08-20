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

    public static uint RefreshClassification(IDictionary<byte, EntryCarResult> results)
    {
        uint position = 0;
        uint leaderLapCount = 0;
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
        => results.OrderBy(result => result.Value.IsDnf)
            .ThenByDescending(result => result.Value.NumLaps)
            .ThenBy(result => result.Value.TotalTime);

    public static bool HasUnfinishedActiveParticipant(
        IEnumerable<(bool Active, EntryCarResult Result)> participants)
        => participants.Any(participant => participant.Active
                                           && !participant.Result.IsDnf
                                           && !participant.Result.HasCompletedLastLap);
}
