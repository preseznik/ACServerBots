using AssettoServer.Server;
using AssettoServer.Server.Configuration.Extra;
using AssettoServer.Server.Configuration.Kunos;
using NUnit.Framework;

namespace AssettoServer.Tests;

public sealed class SlotSelectionPolicyTests
{
    private static readonly Candidate[] Candidates =
    [
        new(0, 0),
        new(1, 1),
        new(2, 0),
        new(3, 0),
        new(4, 1),
    ];

    [Test]
    public void FirstAndLastReverseConfiguredOrderWithinReservationPriority()
    {
        var first = Order(RaceJoinSlotSelection.First);
        var last = Order(RaceJoinSlotSelection.Last);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(new[] { 1, 4, 0, 2, 3 }));
            Assert.That(last, Is.EqualTo(new[] { 4, 1, 3, 2, 0 }));
        });
    }

    [Test]
    public void RandomVariesOrderWithoutBypassingReservedSlots()
    {
        var orders = Enumerable.Range(1, 20)
            .Select(seed => Order(RaceJoinSlotSelection.Random, new Random(seed)))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(orders.Select(order => order[0]).Distinct().ToArray(), Has.Length.GreaterThan(1));
            Assert.That(orders.All(order =>
                order.Take(2).ToHashSet().SetEquals(new[] { 1, 4 })
                && order.Skip(2).ToHashSet().SetEquals(new[] { 0, 2, 3 })), Is.True);
        });
    }

    [Test]
    public void SpectatorsAreFallbackOnlyUnlessExactSlotWasRequested()
    {
        var candidates = new[]
        {
            new SpectatorCandidate(0, false),
            new SpectatorCandidate(1, true),
            new SpectatorCandidate(2, false),
            new SpectatorCandidate(3, true),
        };

        var ordinary = SlotSelectionPolicy.OrderForConnection(candidates,
            RaceJoinSlotSelection.Last, candidate => candidate.IsSpectator,
            supportsSpectating: true, explicitSlotRequest: false,
            _ => 0, candidate => candidate.Slot).Select(candidate => candidate.Slot).ToArray();
        var unsupported = SlotSelectionPolicy.OrderForConnection(candidates,
            RaceJoinSlotSelection.First, candidate => candidate.IsSpectator,
            supportsSpectating: false, explicitSlotRequest: false,
            _ => 0, candidate => candidate.Slot).Select(candidate => candidate.Slot).ToArray();
        var exactSpectator = SlotSelectionPolicy.OrderForConnection([candidates[1]],
            RaceJoinSlotSelection.First, candidate => candidate.IsSpectator,
            supportsSpectating: true, explicitSlotRequest: true,
            _ => 0, candidate => candidate.Slot).Select(candidate => candidate.Slot).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(ordinary, Is.EqualTo(new[] { 2, 0, 1, 3 }));
            Assert.That(unsupported, Is.EqualTo(new[] { 0, 2 }));
            Assert.That(exactSpectator, Is.EqualTo(new[] { 1 }));
        });
    }

    [Test]
    public void ConnectionPriorityPrefersHumanFpsSlotsButDoesNotOverrideExactRequests()
    {
        var candidates = new[]
        {
            new FpsCandidate(0, FpsSlotRole.Auto),
            new FpsCandidate(1, FpsSlotRole.Auto),
            new FpsCandidate(2, FpsSlotRole.Bot),
            new FpsCandidate(3, FpsSlotRole.Human),
        };
        static int Priority(FpsCandidate candidate) => candidate.Role switch
        {
            FpsSlotRole.Human => 0,
            FpsSlotRole.Auto => 1,
            FpsSlotRole.Bot => 2,
            _ => 3,
        };

        var ordinary = SlotSelectionPolicy.OrderForConnection(candidates,
            RaceJoinSlotSelection.First, _ => false, supportsSpectating: false,
            explicitSlotRequest: false, _ => 0, candidate => candidate.Slot,
            connectionPriority: Priority).Select(candidate => candidate.Slot).ToArray();
        var exact = SlotSelectionPolicy.OrderForConnection([candidates[1]],
            RaceJoinSlotSelection.First, _ => false, supportsSpectating: false,
            explicitSlotRequest: true, _ => 0, candidate => candidate.Slot,
            connectionPriority: Priority).Select(candidate => candidate.Slot).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(ordinary, Is.EqualTo(new[] { 3, 0, 1, 2 }));
            Assert.That(exact, Is.EqualTo(new[] { 1 }));
        });
    }

    private static int[] Order(RaceJoinSlotSelection selection, Random? random = null) =>
        SlotSelectionPolicy.Order(Candidates, selection,
                candidate => candidate.ReservationPriority, candidate => candidate.Slot, random)
            .Select(candidate => candidate.Slot)
            .ToArray();

    private sealed record Candidate(int Slot, int ReservationPriority);
    private sealed record SpectatorCandidate(int Slot, bool IsSpectator);
    private sealed record FpsCandidate(int Slot, FpsSlotRole Role);
}
