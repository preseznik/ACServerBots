using AssettoServer.Server;
using AssettoServer.Server.Ai;
using AssettoServer.Server.Ai.Splines;
using AssettoServer.Server.Configuration;
using AssettoServer.Shared.Model;

namespace AssettoServer.Tests;

[TestFixture]
public class RaceBotsTests
{
    [Test]
    public void CountdownHoldingOnlyAppliesBeforeRaceStart()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RaceBotMath.ShouldHoldForCountdown(SessionType.Race, 999, 1000), Is.True);
            Assert.That(RaceBotMath.ShouldHoldForCountdown(SessionType.Race, 1000, 1000), Is.False);
            Assert.That(RaceBotMath.ShouldHoldForCountdown(SessionType.Practice, 999, 1000), Is.False);
        });
    }

    [Test]
    public void RaceListenerAcceptsPrivateIpv4AndRejectsPublicOrWildcardAddresses()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PrivateNetworkAddress.IsPrivateIpv4("192.168.1.10"), Is.True);
            Assert.That(PrivateNetworkAddress.IsPrivateIpv4("10.20.30.40"), Is.True);
            Assert.That(PrivateNetworkAddress.IsPrivateIpv4("172.20.1.2"), Is.True);
            Assert.That(PrivateNetworkAddress.IsPrivateIpv4("0.0.0.0"), Is.False);
            Assert.That(PrivateNetworkAddress.IsPrivateIpv4("8.8.8.8"), Is.False);
        });
    }

    [Test]
    public void ClosedSplineSupportsGridPlacementBehindStart()
    {
        var points = CreateClosedSpline(100, 10);
        var layout = RaceSplineLayout.Create(points, 0);

        Assert.Multiple(() =>
        {
            Assert.That(layout.LengthMeters, Is.EqualTo(1000));
            Assert.That(RaceSplineLayout.GetPointBehind(points, 0, 25), Is.EqualTo(97));
        });
    }

    [Test]
    public void OpenSplineIsRejected()
    {
        var points = CreateClosedSpline(30, 10);
        points[^1].NextId = -1;
        Assert.That(() => RaceSplineLayout.Create(points, 0), Throws.TypeOf<ConfigurationException>());
    }

    [Test]
    public void LapTrackerRequiresForwardProgressAndRejectsDoubleCrossing()
    {
        var tracker = new RaceLapTracker(0, 100);

        Assert.Multiple(() =>
        {
            Assert.That(tracker.ObservePointTransition(99, 0, 5, true), Is.False, "grid launch is not a lap");
            Assert.That(tracker.ObservePointTransition(10, 11, 90, false), Is.False, "wrong-way movement is ignored");
            Assert.That(tracker.ObservePointTransition(10, 11, 90, true), Is.False);
            Assert.That(tracker.ObservePointTransition(99, 0, 5, true), Is.True);
            Assert.That(tracker.ObservePointTransition(99, 0, 1, true), Is.False, "double crossing is rejected");
            Assert.That(tracker.CompletedLaps, Is.EqualTo(1));
        });
    }

    [Test]
    public void FreezeRosterHonorsNoneAutoAndFixedSlots()
    {
        var frozen = RaceParticipantPolicy.FreezeBotRoster([
            ((byte)0, AiMode.None, false),
            ((byte)1, AiMode.Auto, true),
            ((byte)2, AiMode.Auto, false),
            ((byte)3, AiMode.Fixed, false)
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(frozen, Is.EquivalentTo(new byte[] { 2, 3 }));
            Assert.That(RaceParticipantPolicy.ShouldReplaceDisconnectedDriver(raceRosterFrozen: true), Is.False);
            Assert.That(RaceParticipantPolicy.ShouldReplaceDisconnectedDriver(raceRosterFrozen: false), Is.True);
        });
    }

    [Test]
    public void MidRaceTakeoverOnlyClaimsActiveAutoBot()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RaceParticipantPolicy.CanTakeOverBotSlot(true, true, AiMode.Auto, true), Is.True);
            Assert.That(RaceParticipantPolicy.CanTakeOverBotSlot(false, true, AiMode.Auto, true), Is.False);
            Assert.That(RaceParticipantPolicy.CanTakeOverBotSlot(true, false, AiMode.Auto, true), Is.False);
            Assert.That(RaceParticipantPolicy.CanTakeOverBotSlot(true, true, AiMode.Auto, false), Is.False);
            Assert.That(RaceParticipantPolicy.CanTakeOverBotSlot(true, true, AiMode.Fixed, true), Is.False);
            Assert.That(RaceParticipantPolicy.CanTakeOverBotSlot(true, true, AiMode.None, false), Is.False);
        });
    }

    [Test]
    public void MidRaceTakeoverReplacesBotWithFreshHumanResult()
    {
        var results = new Dictionary<byte, EntryCarResult>
        {
            [0] = Result("Leader", laps: 1, total: 100_000),
            [2] = Result("Bot 2", laps: 2, total: 190_000)
        };
        var human = Result("Late driver", laps: 0, total: 0);

        var leaderLaps = RaceParticipantPolicy.ReplaceParticipant(results, 2, human);
        var packetRows = SessionManager.BuildClassificationLaps(results, SessionType.Race);

        Assert.Multiple(() =>
        {
            Assert.That(results[2], Is.SameAs(human));
            Assert.That(results[2].NumLaps, Is.Zero);
            Assert.That(leaderLaps, Is.EqualTo(1));
            Assert.That(results[0].RacePos, Is.Zero);
            Assert.That(results[2].RacePos, Is.EqualTo(1));
            Assert.That(packetRows.Single(row => row.SessionId == 2).NumLaps, Is.Zero);
            Assert.That(RaceParticipantPolicy.HasUnfinishedActiveParticipant([(true, results[2])]), Is.True);
        });
    }

    [Test]
    public void ClassificationOrdersFinishersBeforeDnfThenByProgress()
    {
        var results = new Dictionary<byte, EntryCarResult>
        {
            [0] = Result("Human", laps: 3, total: 300_000),
            [1] = Result("Bot 1", laps: 3, total: 295_000),
            [2] = Result("DNF", laps: 2, total: 200_000, dnf: true)
        };

        Assert.That(RaceParticipantPolicy.OrderClassification(results).Select(x => x.Key), Is.EqualTo(new byte[] { 1, 0, 2 }));
    }

    [Test]
    public void RaceCompletionWaitsForActiveHumanAndBotButNotDnf()
    {
        var finished = Result("Finished", 3, 300_000);
        finished.HasCompletedLastLap = true;
        var racing = Result("Bot", 2, 200_000);
        var dnf = Result("Disconnected", 1, 100_000, dnf: true);

        Assert.Multiple(() =>
        {
            Assert.That(RaceParticipantPolicy.HasUnfinishedActiveParticipant([(true, finished), (true, racing)]), Is.True);
            Assert.That(RaceParticipantPolicy.HasUnfinishedActiveParticipant([(true, finished), (true, dnf)]), Is.False);
            Assert.That(RaceParticipantPolicy.HasUnfinishedActiveParticipant([(true, finished), (false, racing)]), Is.False);
        });
    }

    [Test]
    public void RaceDrivingMathBrakesFollowsOvertakesAndBoundsRecovery()
    {
        var slowCorner = RaceBotMath.CorneringSpeedSquared(20, 0.65f, 0.5f);
        var fastCorner = RaceBotMath.CorneringSpeedSquared(200, 0.65f, 0.5f);

        Assert.Multiple(() =>
        {
            Assert.That(slowCorner, Is.LessThan(fastCorner));
            Assert.That(RaceBotMath.FollowingTargetSpeed(30, 10, 3, 0.5f), Is.Zero);
            Assert.That(RaceBotMath.ChooseOvertakeOffset(4, 1, 0.8f, 0), Is.LessThan(0));
            Assert.That(RaceBotMath.ChooseOvertakeOffset(1, 1, 0.8f, 0), Is.Zero);
            Assert.That(RaceBotMath.CollisionRecoveryMilliseconds(1000, 3000, 0.5f, 7), Is.InRange(1000, 3000));
        });
    }

    [Test]
    public void ClassificationPacketIncludesBotLapAndPositionsForEveryClient()
    {
        var results = new Dictionary<byte, EntryCarResult>
        {
            [0] = Result("Human", 1, 101_000),
            [1] = Result("Bot 1", 2, 199_000)
        };
        results[0].RacePos = 1;
        results[1].RacePos = 0;

        var packetRows = SessionManager.BuildClassificationLaps(results, SessionType.Race);

        Assert.Multiple(() =>
        {
            Assert.That(packetRows.Select(row => row.SessionId), Is.EqualTo(new byte[] { 1, 0 }));
            Assert.That(packetRows[0].NumLaps, Is.EqualTo(2));
            Assert.That(packetRows[0].LapTime, Is.EqualTo(199_000));
            Assert.That(packetRows[0].RacePos, Is.Zero);
        });
    }

    private static EntryCarResult Result(string name, uint laps, uint total, bool dnf = false)
        => new(1, name) { NumLaps = laps, TotalTime = total, IsDnf = dnf };

    private static SplinePoint[] CreateClosedSpline(int count, float segmentLength)
    {
        var points = new SplinePoint[count];
        for (int i = 0; i < count; i++)
        {
            points[i] = new SplinePoint
            {
                Id = i,
                Length = segmentLength,
                PreviousId = i == 0 ? count - 1 : i - 1,
                NextId = i == count - 1 ? 0 : i + 1
            };
        }
        return points;
    }
}
