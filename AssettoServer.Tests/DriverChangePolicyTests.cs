using AssettoServer.Server;
using AssettoServer.Shared.Model;

namespace AssettoServer.Tests;

[TestFixture]
public class DriverChangePolicyTests
{
    private const ulong Alice = 76561198000000001;
    private const ulong Bob = 76561198000000002;
    private const ulong Stranger = 76561198000000099;

    [Test]
    public void RunningReservedRaceCarIsHeldForDriverChange()
    {
        var result = Running(Alice, "Alice", laps: 12);

        Assert.That(RaceParticipantPolicy.CanHoldForDriverChange(300, isRaceSession: true, raceHasEnded: false,
            [Alice, Bob], result), Is.True);
    }

    [Test]
    public void CarsAreNotHeldWithoutGraceReservationRaceOrRunningResult()
    {
        var running = Running(Alice, "Alice", laps: 12);
        Assert.Multiple(() =>
        {
            Assert.That(RaceParticipantPolicy.CanHoldForDriverChange(0, true, false, [Alice, Bob], running), Is.False,
                "grace 0 disables driver changes");
            Assert.That(RaceParticipantPolicy.CanHoldForDriverChange(300, true, false, [], running), Is.False,
                "open slots keep the old DNF behaviour");
            Assert.That(RaceParticipantPolicy.CanHoldForDriverChange(300, false, false, [Alice, Bob], running), Is.False,
                "practice and qualifying results are per driver");
            Assert.That(RaceParticipantPolicy.CanHoldForDriverChange(300, true, true, [Alice, Bob], running), Is.False,
                "nothing to hold after the race has ended");
            Assert.That(RaceParticipantPolicy.CanHoldForDriverChange(300, true, false, [Alice, Bob],
                Running(Alice, "Alice", 12, dnf: true)), Is.False);
            Assert.That(RaceParticipantPolicy.CanHoldForDriverChange(300, true, false, [Alice, Bob],
                Running(Alice, "Alice", 12, finished: true)), Is.False);
            Assert.That(RaceParticipantPolicy.CanHoldForDriverChange(300, true, false, [Alice, Bob], null), Is.False);
        });
    }

    [Test]
    public void EmptyTeamCarAtRaceStart_WaitsForALateDriverWhoStartsFresh()
    {
        // SessionManager gives a car with nobody in it a blank result when the race session begins.
        var empty = new EntryCarResult(null);

        Assert.Multiple(() =>
        {
            Assert.That(RaceParticipantPolicy.CanHoldForDriverChange(300, true, false, [Alice, Bob], empty), Is.True,
                "an empty team car waits for its first driver");
            Assert.That(RaceParticipantPolicy.ShouldContinueResult([Alice, Bob], empty, Bob), Is.False,
                "there are no laps to continue, so the late driver gets a fresh result");
        });
    }

    [Test]
    public void OnlyListedTeammatesContinueTheCarsResult()
    {
        var result = Running(Alice, "Alice", laps: 12);

        Assert.Multiple(() =>
        {
            Assert.That(RaceParticipantPolicy.ShouldContinueResult([Alice, Bob], result, Bob), Is.True);
            Assert.That(RaceParticipantPolicy.ShouldContinueResult([Alice, Bob], result, Stranger), Is.False);
            Assert.That(RaceParticipantPolicy.ShouldContinueResult([Bob], result, Bob), Is.False,
                "a result produced by someone not listed (e.g. a bot) is not inherited");
            Assert.That(RaceParticipantPolicy.ShouldContinueResult([Alice, Bob], Running(Alice, "Alice", 12, dnf: true), Bob),
                Is.False);
        });
    }

    [Test]
    public void HandOverKeepsLapsTimeAndRecordsEveryDriver()
    {
        var result = Running(Alice, "Alice", laps: 12);
        result.TotalTime = 1_234_567;
        result.BestLap = 98_000;
        result.RacePos = 3;

        RaceParticipantPolicy.HandOverResult(result, Bob, "Bob", "Team Endurance", "SVN");
        RaceParticipantPolicy.HandOverResult(result, Alice, "Alice", "", "");

        Assert.Multiple(() =>
        {
            Assert.That(result.NumLaps, Is.EqualTo(12));
            Assert.That(result.TotalTime, Is.EqualTo(1_234_567));
            Assert.That(result.BestLap, Is.EqualTo(98_000));
            Assert.That(result.RacePos, Is.EqualTo(3));
            Assert.That(result.Guid, Is.EqualTo(Alice));
            Assert.That(result.Name, Is.EqualTo("Alice"));
            Assert.That(result.Team, Is.EqualTo("Team Endurance"));
            Assert.That(result.NationCode, Is.EqualTo("SVN"));
            Assert.That(result.Drivers, Is.EqualTo(new[] { "Alice", "Bob" }));
        });
    }

    [Test]
    public void HandedOverResultStillCountsAsUnfinishedActiveParticipant()
    {
        var result = Running(Alice, "Alice", laps: 12);
        RaceParticipantPolicy.HandOverResult(result, Bob, "Bob", "", "");

        Assert.That(RaceParticipantPolicy.HasUnfinishedActiveParticipant([(true, result)]), Is.True);
    }

    [Test]
    public void ExpiredHoldsAreThoseAtOrPastTheirDeadline()
    {
        var deadlines = new Dictionary<byte, long> { [1] = 1_000, [2] = 2_000, [3] = 3_000 };

        Assert.That(RaceParticipantPolicy.ExpiredDriverChangeHolds(deadlines, 2_000), Is.EquivalentTo(new byte[] { 1, 2 }));
    }

    private static EntryCarResult Running(ulong guid, string name, uint laps, bool dnf = false, bool finished = false)
        => new(guid, name) { NumLaps = laps, IsDnf = dnf, HasCompletedLastLap = finished };
}
