using AssettoServer.Server;
using AssettoServer.Server.Ai;
using AssettoServer.Server.Ai.Splines;
using AssettoServer.Server.Configuration;
using AssettoServer.Server.Configuration.Extra;
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
    public void FirstHumanRestartWaitsForBotOnlyTransitionAndRearmsWhenEmpty()
    {
        var gate = new FirstHumanSessionRestartGate();

        Assert.Multiple(() =>
        {
            Assert.That(gate.TrySchedule(false, 1, true), Is.False, "disabled configurations must not restart");
            Assert.That(gate.TrySchedule(true, 1, false), Is.False, "human-only rosters must not restart");
            Assert.That(gate.TrySchedule(true, 2, true), Is.False, "additional humans must not restart");
            Assert.That(gate.TrySchedule(true, 1, true), Is.True, "the first human in a bot-only roster should restart");
            Assert.That(gate.TrySchedule(true, 1, true), Is.False, "the same occupied period must restart only once");
        });

        gate.UpdateConnectedHumanCount(0);

        Assert.That(gate.TrySchedule(true, 1, true), Is.True, "an empty server should re-arm the next first-human restart");
    }

    [Test]
    public void RaceVehicleProfileRaisesModelOriginAboveSplineSurface()
    {
        var profile = new RaceBotVehicleProfile();

        Assert.Multiple(() =>
        {
            Assert.That(profile.TyreDiameterMeters, Is.EqualTo(0.65f));
            Assert.That(profile.SplineHeightOffsetMeters, Is.EqualTo(profile.TyreDiameterMeters / 2));
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
    public void VehicleProfilesProduceDifferentAccelerationAndBoundedTopSpeeds()
    {
        var cityCar = Profile("city", 715, 33.6f, 135, 19.5f, 6300);
        var sportsCar = Profile("sports", 1200, 177.5f, 248, 7.4f, 7250);

        float citySpeed = SimulateAcceleration(cityCar, 10);
        float sportsSpeed = SimulateAcceleration(sportsCar, 10);
        float cityLongRunSpeed = SimulateAcceleration(cityCar, 120);

        Assert.Multiple(() =>
        {
            Assert.That(sportsSpeed, Is.GreaterThan(citySpeed + 8), "per-model profiles must change bot pace");
            Assert.That(cityLongRunSpeed, Is.LessThanOrEqualTo(cityCar.TopSpeedMs + 0.01f));
            Assert.That(cityLongRunSpeed, Is.GreaterThan(cityCar.TopSpeedMs * 0.9f));
        });
    }

    [Test]
    public void VehicleDynamicsBrakesWithoutOvershootAndReportsChangingGears()
    {
        var profile = Profile("test", 1200, 150, 240, 7, 7000);
        var step = RaceBotVehicleDynamics.Step(30, 10, 5, profile);
        var lowSpeed = RaceBotVehicleDynamics.GetTelemetry(5, profile);
        var highSpeed = RaceBotVehicleDynamics.GetTelemetry(60, profile);

        Assert.Multiple(() =>
        {
            Assert.That(step.SpeedMetersPerSecond, Is.EqualTo(10));
            Assert.That(step.AccelerationMetersPerSecondSquared, Is.Zero);
            Assert.That(highSpeed.ProtocolGear, Is.GreaterThan(lowSpeed.ProtocolGear));
            Assert.That(lowSpeed.EngineRpm, Is.InRange((ushort)profile.EngineIdleRpm, (ushort)profile.EngineMaxRpm));
            Assert.That(highSpeed.EngineRpm, Is.InRange((ushort)profile.EngineIdleRpm, (ushort)profile.EngineMaxRpm));
        });
    }

    [Test]
    public void VehicleDynamicsUsesPowerToWeightAboveLaunchSpeeds()
    {
        var lowPower = Profile("low-power", 1400, 80, 250, 7, 7000);
        var highPower = Profile("high-power", 1000, 300, 250, 7, 7000);

        var lowPowerStep = RaceBotVehicleDynamics.Step(40, lowPower.TopSpeedMs, 1, lowPower);
        var highPowerStep = RaceBotVehicleDynamics.Step(40, highPower.TopSpeedMs, 1, highPower);

        Assert.That(highPowerStep.SpeedMetersPerSecond,
            Is.GreaterThan(lowPowerStep.SpeedMetersPerSecond + 1));
    }

    [Test]
    public void VehicleDynamicsTracksReportedZeroToHundredTime()
    {
        var profile = Profile("reported-acceleration", 715, 33.6f, 135, 19.5f, 6300);
        float measuredSeconds = SimulateTimeToSpeed(profile, 100 / 3.6f);

        Assert.That(measuredSeconds, Is.EqualTo(profile.ZeroToHundredSeconds).Within(0.1f));
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

    private static RaceBotVehicleProfile Profile(string model, float massKg, float powerKw, float topSpeedKph,
        float zeroToHundredSeconds, int engineMaxRpm)
        => new()
        {
            Model = model,
            Source = "test",
            MassKg = massKg,
            PowerKw = powerKw,
            TopSpeedKph = topSpeedKph,
            ZeroToHundredSeconds = zeroToHundredSeconds,
            EngineMaxRpm = engineMaxRpm
        };

    private static float SimulateAcceleration(RaceBotVehicleProfile profile, float seconds)
    {
        float speed = 0;
        const float deltaSeconds = 1f / 60;
        for (int i = 0; i < seconds / deltaSeconds; i++)
        {
            speed = RaceBotVehicleDynamics.Step(speed, profile.TopSpeedMs, deltaSeconds, profile)
                .SpeedMetersPerSecond;
        }
        return speed;
    }

    private static float SimulateTimeToSpeed(RaceBotVehicleProfile profile, float targetSpeed)
    {
        float speed = 0;
        float elapsed = 0;
        const float deltaSeconds = 1f / 60;
        while (speed < targetSpeed && elapsed < 120)
        {
            speed = RaceBotVehicleDynamics.Step(speed, profile.TopSpeedMs, deltaSeconds, profile)
                .SpeedMetersPerSecond;
            elapsed += deltaSeconds;
        }
        return elapsed;
    }

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
