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
