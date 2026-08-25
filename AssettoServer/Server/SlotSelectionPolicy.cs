using System;
using System.Collections.Generic;
using System.Linq;
using AssettoServer.Server.Configuration.Extra;

namespace AssettoServer.Server;

internal static class SlotSelectionPolicy
{
    internal static IReadOnlyList<T> OrderForConnection<T>(IEnumerable<T> candidates,
        RaceJoinSlotSelection selection, Func<T, bool> isSpectator, bool supportsSpectating,
        bool explicitSlotRequest, Func<T, int> reservationPriority,
        Func<T, int> slotIndex, Random? random = null)
    {
        var materialized = candidates.ToArray();
        if (explicitSlotRequest)
            return materialized.Where(candidate => !isSpectator(candidate) || supportsSpectating).ToArray();

        var ordered = Order(materialized.Where(candidate => !isSpectator(candidate)), selection,
            reservationPriority, slotIndex, random).ToList();
        if (supportsSpectating)
        {
            ordered.AddRange(Order(materialized.Where(isSpectator), RaceJoinSlotSelection.First,
                reservationPriority, slotIndex, random));
        }
        return ordered;
    }

    internal static IReadOnlyList<T> Order<T>(IEnumerable<T> candidates,
        RaceJoinSlotSelection selection, Func<T, int> reservationPriority,
        Func<T, int> slotIndex, Random? random = null)
    {
        var ordered = new List<T>();
        foreach (var priorityGroup in candidates
                     .GroupBy(reservationPriority)
                     .OrderByDescending(group => group.Key))
        {
            var group = priorityGroup.OrderBy(slotIndex).ToList();
            switch (selection)
            {
                case RaceJoinSlotSelection.Last:
                    group.Reverse();
                    break;
                case RaceJoinSlotSelection.Random:
                    Shuffle(group, random ?? Random.Shared);
                    break;
            }
            ordered.AddRange(group);
        }
        return ordered;
    }

    private static void Shuffle<T>(IList<T> values, Random random)
    {
        for (int index = values.Count - 1; index > 0; index--)
        {
            int swapIndex = random.Next(index + 1);
            (values[index], values[swapIndex]) = (values[swapIndex], values[index]);
        }
    }
}
