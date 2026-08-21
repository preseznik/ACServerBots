using System.Numerics;
using AssettoServer.Server;
using AssettoServer.Server.Ai;
using AssettoServer.Server.Ai.Physics;
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
    public void EveryRaceBotSharesTheSameLaunchGraceWindow()
    {
        const long start = 10_000;

        Assert.Multiple(() =>
        {
            Assert.That(RaceBotMath.IsInRaceLaunchWindow(SessionType.Race, start - 1, start), Is.False);
            Assert.That(RaceBotMath.IsInRaceLaunchWindow(SessionType.Race, start, start), Is.True);
            Assert.That(RaceBotMath.IsInRaceLaunchWindow(SessionType.Race,
                start + RaceBotMath.RaceLaunchGraceMilliseconds - 1, start), Is.True);
            Assert.That(RaceBotMath.IsInRaceLaunchWindow(SessionType.Race,
                start + RaceBotMath.RaceLaunchGraceMilliseconds, start), Is.False);
            Assert.That(RaceBotMath.IsInRaceLaunchWindow(SessionType.Practice, start, start), Is.False);
            Assert.That(RaceBotMath.CanTransitionLane(SessionType.Race,
                start + RaceBotMath.RaceLaneTransitionDelayMilliseconds - 1, start), Is.False);
            Assert.That(RaceBotMath.CanTransitionLane(SessionType.Race,
                start + RaceBotMath.RaceLaneTransitionDelayMilliseconds, start), Is.True);
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
            Assert.That(layout.SignedDistanceAhead(98, 0, 2, 0, points), Is.EqualTo(40));
            Assert.That(layout.SignedDistanceAhead(2, 0, 98, 0, points), Is.EqualTo(-40));
            Assert.That(layout.SignedDistanceAhead(0, 0.5f, 1, 0.5f, points), Is.EqualTo(10));
            Assert.That(layout.DistanceFromStart(2, 0.5f, points), Is.EqualTo(25));
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
    public void RaceGridPoseUsesExactAcStartTransform()
    {
        var orientation = Quaternion.CreateFromYawPitchRoll(0.4f, -0.1f, 0.05f);
        var transform = Matrix4x4.CreateFromQuaternion(orientation)
                        * Matrix4x4.CreateTranslation(12.5f, 3.25f, -44);
        var pose = RaceGridPose.FromMatrix(transform);

        Assert.Multiple(() =>
        {
            Assert.That(pose.Position, Is.EqualTo(new Vector3(12.5f, 3.25f, -44)));
            Assert.That(Math.Abs(Quaternion.Dot(pose.Orientation, orientation)), Is.EqualTo(1).Within(1e-5f));
        });
    }

    [Test]
    public void RacePhysicsAssetRoundTripsExactGeometryAndGrid()
    {
        string path = Path.Combine(Path.GetTempPath(), $"race-physics-{Guid.NewGuid():N}.bin");
        try
        {
            var asset = new RacePhysicsAsset
            {
                Grid = [new RaceGridPose(new Vector3(1, 2, 3), Quaternion.Identity)],
                TrackTriangles = [new Kn5Triangle(Vector3.Zero, Vector3.UnitX, Vector3.UnitZ)],
                TrackBarrierTriangles = [new Kn5Triangle(Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ)],
                CarColliderVertices = new Dictionary<string, Vector3[]>
                {
                    ["test_car"] = [Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ]
                },
                CarWheelColliders = new Dictionary<string, RaceWheelCollider[]>
                {
                    ["test_car"] =
                    [
                        new RaceWheelCollider(new Vector3(0.7f, 0.3f, 1.2f), 0.3f),
                        new RaceWheelCollider(new Vector3(-0.7f, 0.3f, 1.2f), 0.3f),
                        new RaceWheelCollider(new Vector3(0.7f, 0.3f, -1.2f), 0.3f),
                        new RaceWheelCollider(new Vector3(-0.7f, 0.3f, -1.2f), 0.3f)
                    ]
                }
            };

            asset.Save(path);
            var loaded = RacePhysicsAsset.Load(path);

            Assert.Multiple(() =>
            {
                Assert.That(loaded.Grid, Has.Count.EqualTo(1));
                Assert.That(loaded.Grid[0].Position, Is.EqualTo(new Vector3(1, 2, 3)));
                Assert.That(loaded.TrackTriangles, Has.Count.EqualTo(1));
                Assert.That(loaded.TrackTriangles[0].C, Is.EqualTo(Vector3.UnitZ));
                Assert.That(loaded.TrackBarrierTriangles, Has.Count.EqualTo(1));
                Assert.That(loaded.CarColliderVertices["TEST_CAR"], Has.Length.EqualTo(4));
                Assert.That(loaded.CarWheelColliders["TEST_CAR"], Has.Length.EqualTo(4));
                Assert.That(loaded.CarWheelColliders["TEST_CAR"][0].Radius, Is.EqualTo(0.3f));
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Kn5TrackTrianglesAreRewoundForBepuVisibleFaces()
    {
        var source = new Kn5Triangle(Vector3.Zero, Vector3.UnitX, Vector3.UnitZ);
        var converted = RaceBotPhysicsWorld.ToBepuTrackTriangle(source);

        Assert.Multiple(() =>
        {
            Assert.That(converted.A, Is.EqualTo(source.A));
            Assert.That(converted.B, Is.EqualTo(source.C));
            Assert.That(converted.C, Is.EqualTo(source.B));
        });
    }

    [Test]
    public void RacePhysicsFidelityDefaultsToBalancedAndKeepsExactAsset()
    {
        var physics = new RacePhysicsParams();

        Assert.Multiple(() =>
        {
            Assert.That(physics.Fidelity, Is.EqualTo(RacePhysicsFidelity.Balanced));
            Assert.That(physics.AssetFile, Is.EqualTo("race-physics.bin"));
            Assert.That(physics.Friction, Is.GreaterThan(0));
        });
    }

    [Test]
    public void PhysicalTrackMeshSelectionUsesAcSurfaceConventions()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RacePhysicsAssetBuilder.IsPhysicalTrackMesh("1ROAD"), Is.True);
            Assert.That(RacePhysicsAssetBuilder.IsPhysicalTrackMesh("ROAD_SUB0"), Is.False);
            Assert.That(RacePhysicsAssetBuilder.IsPhysicalTrackMesh("curb_graph"), Is.False);
            Assert.That(RacePhysicsAssetBuilder.IsPhysicalTrackMesh("crb-grph15"), Is.False);
            Assert.That(RacePhysicsAssetBuilder.IsPhysicalTrackMesh("WALL_OUTER"), Is.True);
            Assert.That(RacePhysicsAssetBuilder.IsPhysicalTrackMesh("grandstand"), Is.False);
            Assert.That(RacePhysicsAssetBuilder.IsBarrierTrackMesh("12WALL001"), Is.True);
            Assert.That(RacePhysicsAssetBuilder.IsBarrierTrackMesh("22TRM-NRM004"), Is.False);
        });
    }

    [Test]
    public void TrackTriangleDeduplicationIgnoresVertexOrderAndSubMillimetreNoise()
    {
        var first = new Kn5Triangle(Vector3.Zero, Vector3.UnitX, Vector3.UnitZ);
        var reversed = new Kn5Triangle(
            Vector3.UnitZ + new Vector3(0.00001f),
            Vector3.UnitX,
            Vector3.Zero);
        var distinct = new Kn5Triangle(Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ);

        var result = RacePhysicsAssetBuilder.DeduplicateTriangles([first, reversed, distinct]);

        Assert.That(result, Is.EqualTo(new[] { first, distinct }));
    }

    [Test]
    public void ProtocolRotationRoundTripsRigidBodyOrientation()
    {
        var protocol = new Vector3(0.6f, -0.2f, 0.1f);
        var roundTrip = RacePhysicsMath.ToProtocolRotation(RacePhysicsMath.FromProtocolRotation(protocol));

        Assert.Multiple(() =>
        {
            Assert.That(roundTrip.X, Is.EqualTo(protocol.X).Within(1e-5f));
            Assert.That(roundTrip.Y, Is.EqualTo(protocol.Y).Within(1e-5f));
            Assert.That(roundTrip.Z, Is.EqualTo(protocol.Z).Within(1e-5f));
        });
    }

    [Test]
    public void ProtocolRotationUsesAssettoCorsaForwardConvention()
    {
        var expectedForward = Vector3.Normalize(new Vector3(1, 0.2f, 0));
        var protocol = new Vector3(
            MathF.Atan2(expectedForward.Z, expectedForward.X) - MathF.PI / 2,
            -(MathF.Atan2(new Vector2(expectedForward.Z, expectedForward.X).Length(), expectedForward.Y) - MathF.PI / 2),
            0);

        var orientation = RacePhysicsMath.FromProtocolRotation(protocol);
        var actualForward = Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, orientation));

        Assert.That(Vector3.Dot(actualForward, expectedForward), Is.GreaterThan(0.999f));
    }

    [Test]
    public void AcStartPoseIsGroundedOnPhysicalTrackSurface()
    {
        var triangles = new[]
        {
            new Kn5Triangle(new Vector3(-10, 5, -10), new Vector3(-10, 5, 10), new Vector3(10, 5, -10))
        };
        var pose = new RaceGridPose(new Vector3(0, 6.125f, 0), Quaternion.Identity);

        var grounded = RacePhysicsAssetBuilder.GroundGridPose(pose, triangles);

        Assert.That(grounded.Position, Is.EqualTo(new Vector3(0, 5, 0)));
    }

    [Test]
    public void WheelCollidersUseStandardAcWheelNodesAndIgnoreOtherTransforms()
    {
        var transforms = new[]
        {
            new Kn5NamedTransform("BODY_OUTLIER", Matrix4x4.CreateTranslation(0, -3, 0)),
            new Kn5NamedTransform("WHEEL_LF", Matrix4x4.CreateTranslation(0.7f, 0.31f, 1.2f)),
            new Kn5NamedTransform("WHEEL_RF", Matrix4x4.CreateTranslation(-0.7f, 0.31f, 1.2f)),
            new Kn5NamedTransform("WHEEL_LR", Matrix4x4.CreateTranslation(0.72f, 0.33f, -1.2f)),
            new Kn5NamedTransform("WHEEL_RR", Matrix4x4.CreateTranslation(-0.72f, 0.33f, -1.2f))
        };

        var wheels = RacePhysicsAssetBuilder.ReadWheelColliders(transforms, "test_car");

        Assert.Multiple(() =>
        {
            Assert.That(wheels, Has.Length.EqualTo(4));
            Assert.That(wheels.Select(wheel => wheel.Radius), Is.EqualTo(new[] { 0.31f, 0.31f, 0.33f, 0.33f }));
            Assert.That(wheels.Min(wheel => wheel.Center.Y - wheel.Radius), Is.Zero.Within(1e-6f));
        });
    }

    [Test]
    public void ProtocolPositionUsesModelWheelHeightWithoutMovingPhysicalOrigin()
    {
        var wheels = new[]
        {
            new RaceWheelCollider(new Vector3(0.7f, 0.31f, 1.2f), 0.31f),
            new RaceWheelCollider(new Vector3(-0.7f, 0.31f, 1.2f), 0.31f),
            new RaceWheelCollider(new Vector3(0.72f, 0.33f, -1.2f), 0.33f),
            new RaceWheelCollider(new Vector3(-0.72f, 0.33f, -1.2f), 0.33f)
        };
        var physicalOrigin = new Vector3(12, 4, -8);
        var orientation = Quaternion.CreateFromYawPitchRoll(0.4f, -0.15f, 0.08f);
        float referenceHeight = RaceBotPhysicsWorld.GetProtocolReferenceHeight(wheels);

        var protocolPosition = RaceBotPhysicsWorld.ToProtocolPosition(physicalOrigin, orientation, referenceHeight);
        var roundTrip = RaceBotPhysicsWorld.FromProtocolPosition(protocolPosition, orientation, referenceHeight);

        Assert.Multiple(() =>
        {
            Assert.That(referenceHeight, Is.EqualTo(0.32f).Within(1e-6f));
            Assert.That(Vector3.Distance(protocolPosition, physicalOrigin), Is.EqualTo(0.32f).Within(1e-5f));
            Assert.That(roundTrip.X, Is.EqualTo(physicalOrigin.X).Within(1e-5f));
            Assert.That(roundTrip.Y, Is.EqualTo(physicalOrigin.Y).Within(1e-5f));
            Assert.That(roundTrip.Z, Is.EqualTo(physicalOrigin.Z).Within(1e-5f));
        });
    }

    [TestCase(0.2f, 0.12f)]
    [TestCase(0.32f, 0.16f)]
    [TestCase(0.6f, 0.22f)]
    public void SuspensionTravelScalesWithWheelRadiusWithinStableBounds(float radius, float expected)
    {
        Assert.That(RaceBotPhysicsWorld.GetSuspensionLength(radius), Is.EqualTo(expected).Within(1e-6f));
    }

    [Test]
    public void SuspensionLimitsPreventTheChassisCollapsingOntoTheRoad()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RaceBotPhysicsWorld.GetSuspensionCompressionLimit(0.2f), Is.EqualTo(0.06f));
            Assert.That(RaceBotPhysicsWorld.GetSuspensionCompressionLimit(0.32f), Is.EqualTo(0.08f));
            Assert.That(RaceBotPhysicsWorld.GetSuspensionCompressionLimit(0.6f), Is.EqualTo(0.10f));
            Assert.That(RaceBotPhysicsWorld.GetSuspensionExtensionLimit(0.32f), Is.EqualTo(0.04f));
        });
    }

    [Test]
    public void RenderOriginIsReconstructedFromTheSimulatedWheelContact()
    {
        var wheel = new RaceWheelCollider(new Vector3(0.8f, 0.32f, 1.2f), 0.32f);
        var orientation = Quaternion.CreateFromYawPitchRoll(0.3f, -0.08f, 0.04f);
        var expectedOrigin = new Vector3(12, 4, -8);
        var wheelPosition = expectedOrigin + Vector3.Transform(wheel.Center, orientation);

        var reconstructed = RaceBotPhysicsWorld.GetWheelOriginSample(wheel, wheelPosition, orientation);

        Assert.That(Vector3.Distance(reconstructed, expectedOrigin), Is.LessThan(1e-5f));
    }

    [Test]
    public void SuspensionSafetyClampOnlyCorrectsTravelBeyondTheBumpStop()
    {
        var chassisOrigin = Vector3.Zero;

        Assert.Multiple(() =>
        {
            Assert.That(RaceBotPhysicsWorld.GetSuspensionCompressionCorrection(chassisOrigin,
                new Vector3(0, 0.05f, 0), Quaternion.Identity, 0.08f), Is.Zero);
            Assert.That(RaceBotPhysicsWorld.GetSuspensionCompressionCorrection(chassisOrigin,
                new Vector3(0, 0.30f, 0), Quaternion.Identity, 0.08f), Is.EqualTo(0.22f).Within(1e-6f));
        });
    }

    [Test]
    public void TrackSupportOnlyCorrectsEmergencySubmersionOrFlight()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RaceBotPhysicsWorld.GetTrackSupportCorrection(0.89f), Is.Zero);
            Assert.That(RaceBotPhysicsWorld.GetTrackSupportCorrection(1.10f),
                Is.EqualTo(1.10f).Within(1e-6f));
            Assert.That(RaceBotPhysicsWorld.GetTrackSupportCorrection(-0.99f), Is.Zero);
            Assert.That(RaceBotPhysicsWorld.GetTrackSupportCorrection(-1.20f),
                Is.EqualTo(-1.20f).Within(1e-6f));
        });
    }

    [Test]
    public void TrackSupportLiftsSubmergedBotsWithoutAPositionSnap()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RaceBotPhysicsWorld.GetTrackSupportVerticalSpeed(0.10f, 0.5f),
                Is.EqualTo(0.9f).Within(1e-6f));
            Assert.That(RaceBotPhysicsWorld.GetTrackSupportVerticalSpeed(1f, 0.5f),
                Is.EqualTo(2.5f).Within(1e-6f));
            Assert.That(RaceBotPhysicsWorld.GetTrackSupportVerticalSpeed(-0.25f, 0.5f),
                Is.EqualTo(0.5f).Within(1e-6f));
        });
    }

    [Test]
    public void NetworkRenderHeightUsesTheAuthoritativeTrackSurfaceWithoutMovingThePhysicalCar()
    {
        var physicalOrigin = new Vector3(12, -3, 8);
        var trackTarget = new Vector3(15, 4.5f, 11);

        var renderOrigin = RaceBotPhysicsWorld.GetTrackRenderOrigin(physicalOrigin, trackTarget);

        Assert.That(renderOrigin, Is.EqualTo(new Vector3(12, 4.5f, 8)));
    }

    [Test]
    public void NetworkRideHeightAddsSmallModelScaledClearance()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RaceBotPhysicsWorld.GetNetworkRideHeightClearance(0.20f), Is.EqualTo(0.03f));
            Assert.That(RaceBotPhysicsWorld.GetNetworkRideHeightClearance(0.32f), Is.EqualTo(0.04f));
            Assert.That(RaceBotPhysicsWorld.GetNetworkRideHeightClearance(0.60f), Is.EqualTo(0.05f));
            Assert.That(RaceBotPhysicsWorld.GetTrackRenderOrigin(Vector3.Zero,
                new Vector3(0, 4, 0), 0.04f).Y, Is.EqualTo(4.04f));
        });
    }

    [Test]
    public void TrackSupportMatchesVerticalSpeedToTheAuthoredSlope()
    {
        var slope = Vector3.Normalize(new Vector3(0, 0.1f, 1));
        var velocity = new Vector3(0, 5, 20);

        Assert.Multiple(() =>
        {
            Assert.That(RaceBotPhysicsWorld.GetTargetVerticalSpeed(slope, velocity, Quaternion.Identity),
                Is.EqualTo(slope.Y * 20).Within(1e-5f));
            Assert.That(RaceBotPhysicsWorld.GetTargetVerticalSpeed(Vector3.UnitY, velocity, Quaternion.Identity),
                Is.EqualTo(3).Within(1e-5f));
        });
    }

    [Test]
    public void RacePhysicsStabilityCutsDriveAndRequestsRecoveryWhenOverturned()
    {
        var physicalOrigin = new Vector3(10, 2, 20);

        Assert.Multiple(() =>
        {
            Assert.That(RaceBotPhysicsWorld.GetDriveScale(1), Is.EqualTo(1));
            Assert.That(RaceBotPhysicsWorld.GetDriveScale(0.2f), Is.Zero);
            Assert.That(RaceBotPhysicsWorld.GetCourseDriveScale(2, 0.5f), Is.EqualTo(1));
            Assert.That(RaceBotPhysicsWorld.GetCourseDriveScale(10, 0.5f), Is.EqualTo(0.5f));
            Assert.That(RaceBotPhysicsWorld.GetCourseDriveScale(2, 4), Is.Zero);
            Assert.That(RaceBotPhysicsWorld.NeedsRecovery(-0.9f, physicalOrigin, physicalOrigin), Is.True);
            Assert.That(RaceBotPhysicsWorld.NeedsRecovery(1, physicalOrigin,
                physicalOrigin + new Vector3(26, 0, 0)), Is.True);
            Assert.That(RaceBotPhysicsWorld.NeedsRecovery(1, physicalOrigin,
                physicalOrigin + new Vector3(5, 0.3f, 0)), Is.False);
            Assert.That(RaceBotPhysicsWorld.NeedsImmediateRecovery(1, physicalOrigin,
                physicalOrigin + new Vector3(0, 1.5f, 0), Vector3.Zero, Vector3.UnitZ), Is.True);
            Assert.That(RaceBotPhysicsWorld.NeedsImmediateRecovery(1, physicalOrigin, physicalOrigin,
                new Vector3(0, 7, 20), Vector3.UnitZ), Is.True);
            Assert.That(RaceBotPhysicsWorld.NeedsImmediateRecovery(1, physicalOrigin, physicalOrigin,
                new Vector3(0, 4, 20), Vector3.Normalize(new Vector3(0, 0.2f, 1))), Is.False);
        });
    }

    [Test]
    public void RacePhysicsAttitudeControlCountersRollWithoutSuppressingTargetHeading()
    {
        var rolled = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.6f);
        var correctedVelocity = RaceBotPhysicsWorld.CalculateStabilizedAngularVelocity(
            rolled, Vector3.UnitZ, Vector3.Zero, 1f / 60);
        var alignedVelocity = RaceBotPhysicsWorld.CalculateStabilizedAngularVelocity(
            Quaternion.Identity, Vector3.UnitZ, Vector3.Zero, 1f / 60);
        var boundedImpactVelocity = RaceBotPhysicsWorld.CalculateStabilizedAngularVelocity(
            Quaternion.Identity, Vector3.UnitZ, new Vector3(20, 0, 0), 1f / 60);

        Assert.Multiple(() =>
        {
            Assert.That(correctedVelocity.Z, Is.LessThan(0), "positive roll must receive a negative restoring rate");
            Assert.That(correctedVelocity.Length(), Is.LessThanOrEqualTo(16f / 60 + 1e-5f));
            Assert.That(alignedVelocity.Length(), Is.Zero.Within(1e-6f));
            Assert.That(boundedImpactVelocity.Length(), Is.LessThanOrEqualTo(2.5f));
        });
    }

    [Test]
    public void RaceLaneTransitionRequiresForwardMotionAndIsSpeedBounded()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RaceBotMath.AdvanceLaneOffset(0, 2, 0, 1), Is.Zero,
                "a held car must not translate into its lane");
            Assert.That(RaceBotMath.AdvanceLaneOffset(0, 2, 0.9f, 1), Is.Zero,
                "near-stationary motion must not advance a lane change");
            Assert.That(RaceBotMath.AdvanceLaneOffset(0, 2, 10, 1), Is.EqualTo(0.6f).Within(1e-6f));
            Assert.That(RaceBotMath.AdvanceLaneOffset(0, 2, 30, 1), Is.EqualTo(0.9f).Within(1e-6f));
            Assert.That(RaceBotMath.AdvanceLaneOffset(0, 2, 10, 1, 2),
                Is.EqualTo(1.2f).Within(1e-6f), "committed passes may request a quicker path change");
            Assert.That(RaceBotMath.AdvanceLaneOffset(1.9f, 2, 30, 1), Is.EqualTo(2));
        });
    }

    [Test]
    public void RaceSteeringTurnsTowardLaneWithoutInjectingSidewaysDrive()
    {
        var steeringDirection = RaceBotPhysicsWorld.CalculateSteeringDirection(Vector3.Zero,
            new Vector3(2, 0, 0), Vector3.UnitZ, 10);
        float lookAhead = RaceBotPhysicsWorld.GetSteeringLookAheadMeters(10);
        float steeringAngle = RaceBotPhysicsWorld.CalculateSteeringAngle(Vector3.UnitZ,
            steeringDirection, lookAhead, 2.5f);
        float yawRate = RaceBotPhysicsWorld.CalculateTargetYawRate(10, 2.5f, steeringAngle, 1);
        var driveDelta = RaceBotPhysicsWorld.CalculateLongitudinalVelocityDelta(Vector3.UnitZ, 5, 0.1f);

        Assert.Multiple(() =>
        {
            Assert.That(steeringDirection.X, Is.GreaterThan(0));
            Assert.That(steeringAngle, Is.GreaterThan(0));
            Assert.That(yawRate, Is.GreaterThan(0));
            Assert.That(RaceBotPhysicsWorld.CalculateTargetYawRate(0, 2.5f, steeringAngle, 1), Is.Zero,
                "steering a stationary car must not rotate or translate it");
            Assert.That(RaceBotPhysicsWorld.MoveSteeringAngle(0, 1, 0.1f),
                Is.EqualTo(0.15f).Within(1e-6f), "steering input must not snap between locks");
            Assert.That(Vector3.Dot(driveDelta, Vector3.UnitX), Is.Zero.Within(1e-6f),
                "engine force must stay on the longitudinal axis");
            Assert.That(Vector3.Dot(driveDelta, Vector3.UnitZ), Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(RaceBotPhysicsWorld.CalculateTargetYawRate(20, 2.5f,
                RaceBotPhysicsWorld.MaximumSteeringAngleRadians, 1), Is.LessThan(9.81f / 20),
                "yaw control must leave grip available to remove lateral slip");
            Assert.That(RaceBotPhysicsWorld.CalculateSlipStabilizedYawRate(0.3f, -0.2f, 20, 1),
                Is.LessThan(0.3f), "stability control must counter yaw away from the velocity vector");
        });
    }

    [Test]
    public void RaceTyreGripRemovesSlipGraduallyAndSteeringTelemetryIsBounded()
    {
        var velocity = new Vector3(5, 0, 20);
        var corrected = RaceBotPhysicsWorld.ApplyLateralGrip(velocity, Vector3.UnitZ, 1, 1f / 60);
        float removedLateralSpeed = velocity.X - corrected.X;

        Assert.Multiple(() =>
        {
            Assert.That(removedLateralSpeed, Is.GreaterThan(0));
            Assert.That(removedLateralSpeed, Is.LessThanOrEqualTo(9.81f / 60 + 1e-6f),
                "tyre grip must not snap the velocity vector sideways");
            Assert.That(corrected.Z, Is.EqualTo(velocity.Z).Within(1e-6f));
            Assert.That(RaceBotPhysicsWorld.CalculateSlipAngleDegrees(Vector3.UnitZ * 20,
                Vector3.UnitZ), Is.Zero.Within(1e-6f));
            Assert.That(RaceBotPhysicsWorld.CalculateSlipAngleDegrees(velocity, Vector3.UnitZ),
                Is.EqualTo(MathF.Atan2(5, 20) * 180 / MathF.PI).Within(1e-5f));
            Assert.That(RaceBotPhysicsWorld.EncodeSteeringAngle(0), Is.EqualTo(127));
            Assert.That(RaceBotPhysicsWorld.EncodeSteeringAngle(
                RaceBotPhysicsWorld.MaximumSteeringAngleRadians), Is.EqualTo(254));
            Assert.That(RaceBotPhysicsWorld.EncodeSteeringAngle(
                -RaceBotPhysicsWorld.MaximumSteeringAngleRadians), Is.Zero);
        });
    }

    [Test]
    public void RaceWheelbaseComesFromPreparedFrontAndRearAxles()
    {
        RaceWheelCollider[] wheels =
        [
            new(new Vector3(-0.8f, 0.32f, 1.4f), 0.32f),
            new(new Vector3(0.8f, 0.32f, 1.4f), 0.32f),
            new(new Vector3(-0.8f, 0.32f, -1.2f), 0.32f),
            new(new Vector3(0.8f, 0.32f, -1.2f), 0.32f)
        ];

        Assert.That(RaceBotPhysicsWorld.GetWheelbaseMeters(wheels), Is.EqualTo(2.6f).Within(1e-6f));
    }

    [Test]
    public void RecoveryPosePlacesThePhysicalOriginOnTheTrackTarget()
    {
        var trackTarget = new Vector3(20, 4, -15);
        var targetForward = Vector3.Normalize(new Vector3(1, 0.12f, 2));

        var pose = RaceBotPhysicsWorld.CreateRecoveryPose(trackTarget, targetForward);
        var recoveredProtocolPosition = RaceBotPhysicsWorld.ToProtocolPosition(
            pose.Position, pose.Orientation, 0.32f);
        var recoveredForward = Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, pose.Orientation));

        Assert.Multiple(() =>
        {
            Assert.That(Vector3.Distance(pose.Position, trackTarget), Is.LessThan(1e-5f));
            Assert.That(Vector3.Distance(recoveredProtocolPosition, trackTarget),
                Is.EqualTo(0.32f).Within(1e-5f));
            Assert.That(Vector3.Dot(recoveredForward, targetForward), Is.GreaterThan(0.999f));
            Assert.That(RaceBotPhysicsWorld.GetUprightDot(pose.Orientation), Is.GreaterThan(0.99f));
        });
    }

    [Test]
    public void RecoveryTeleportDoesNotCountAsRaceProgress()
    {
        var previousPosition = Vector3.Zero;
        var recoveredPosition = new Vector3(0, 0, 40);

        Assert.Multiple(() =>
        {
            Assert.That(AiState.CalculatePhysicsForwardProgress(recoveredPosition, previousPosition,
                Vector3.UnitZ, recoveryCount: 1, previousRecoveryCount: 0), Is.Zero);
            Assert.That(AiState.CalculatePhysicsForwardProgress(recoveredPosition, previousPosition,
                Vector3.UnitZ, recoveryCount: 1, previousRecoveryCount: 1), Is.EqualTo(40));
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
            Assert.That(RaceBotMath.ChooseOvertakeOffset(5, 1, 0.8f, 0), Is.LessThan(0));
            Assert.That(RaceBotMath.ChooseOvertakeOffset(1, 1, 0.8f, 0), Is.Zero);
            Assert.That(RaceBotMath.ChooseOvertakeOffset(5, 1, 0.1f, 0), Is.LessThan(-1.4f));
            Assert.That(RaceBotMath.BaseLaneOffset(4, 4, 0), Is.EqualTo(-1.2f));
            Assert.That(RaceBotMath.BaseLaneOffset(4, 4, 1), Is.EqualTo(1.2f));
            Assert.That(RaceBotMath.BaseLaneOffset(1, 1, 1), Is.Zero);
            Assert.That(RaceBotMath.RacingLineOffset(4, 4, 0, 90),
                Is.Not.EqualTo(RaceBotMath.RacingLineOffset(4, 4, 0, 0)));
            Assert.That(Math.Abs(RaceBotMath.RacingLineOffset(4, 4, 3, 101)
                                 - RaceBotMath.RacingLineOffset(4, 4, 3, 100)),
                Is.LessThan(0.02f), "line variation must be continuous rather than a lane jump");
            Assert.That(RaceBotMath.RacingLineOffset(1, 1, 1, 500), Is.Zero);
            Assert.That(RaceBotMath.GridPaceFactor(0.15f, 0), Is.EqualTo(0.85f));
            Assert.That(RaceBotMath.GridPaceFactor(0.15f, 1), Is.EqualTo(1));
            Assert.That(RaceBotMath.GridPaceFactor(0.15f, 4), Is.EqualTo(0.8875f).Within(1e-6f));
            Assert.That(RaceBotMath.GridPaceFactor(0.02f, 2), Is.EqualTo(0.99f));
            Assert.That(RaceBotMath.ChoosePassTarget(0, 0, 5, 5, false, false, 0),
                Is.EqualTo(-3.75f));
            Assert.That(RaceBotMath.ChoosePassTarget(0, 0.65f, 5, 5, false, false, 0),
                Is.EqualTo(-3.15f).Within(1e-6f));
            Assert.That(RaceBotMath.ChoosePassTarget(0, 0, 5, 5, true, false, 0),
                Is.EqualTo(3.75f));
            Assert.That(RaceBotMath.CommittedPassTarget(1.2f, true, 6, 6),
                Is.EqualTo(-2.6f).Within(1e-6f));
            Assert.That(RaceBotMath.CommittedPassTarget(1.2f, false, 6, 6),
                Is.EqualTo(4.75f), "a committed lane must remain inside the car-safe track bound");
            Assert.That(RaceBotMath.IsPracticalPassTarget(0, 6.5f, 10), Is.True);
            Assert.That(RaceBotMath.IsPracticalPassTarget(0, 6.6f, 10), Is.False,
                "a moving pass should wait for a usable corridor instead of crossing the track");
            Assert.That(RaceBotMath.IsPracticalPassTarget(0, 5, 0), Is.True,
                "a stopped car must remain passable even when only a wide escape route exists");
            Assert.That(RaceBotMath.HasPassTargetClearance(-3.2f, 0.2f, 10), Is.False);
            Assert.That(RaceBotMath.HasPassTargetClearance(4.0f, 0.2f, 10), Is.True);
            Assert.That(RaceBotMath.HasPassTargetClearance(-3.2f, 0.2f, 0), Is.True,
                "stopped-car escape must accept the best available safe corridor");
            Assert.That(RaceBotMath.ChoosePassTarget(0, 0, 4, 4, false, false, 0), Is.Null,
                "a pass must be rejected when neither side has steering tolerance");
            Assert.That(RaceBotMath.ChoosePassTarget(0, 0, 1, 1, false, false, 0), Is.Null);
            Assert.That(RaceBotMath.PassingTargetSpeed(20, 30, 20, 0.5f), Is.EqualTo(25f));
            Assert.That(RaceBotMath.PassingTargetSpeed(20, 21, 20, 1), Is.EqualTo(23.52f).Within(1e-5f));
            Assert.That(RaceBotMath.YieldingTargetSpeed(30, 20, 12, 0),
                Is.EqualTo(9.84f).Within(1e-5f));
            Assert.That(RaceBotMath.YieldingTargetSpeed(30, 20, 12, 1),
                Is.EqualTo(10.56f).Within(1e-5f));
            Assert.That(RaceBotMath.YieldingTargetSpeed(9, 20, 20, 0), Is.EqualTo(9));
            Assert.That(RaceBotMath.YieldingTargetSpeed(30, 2, 2, 0), Is.EqualTo(6));
            Assert.That(RaceBotMath.PassingCornerSpeedLimit(20, 30, 0), Is.EqualTo(23f));
            Assert.That(RaceBotMath.PassingCornerSpeedLimit(4, 30, 0), Is.EqualTo(10));
            Assert.That(RaceBotMath.PassingCornerSpeedLimit(float.PositiveInfinity, 30, 0), Is.EqualTo(30f));
            Assert.That(RaceBotMath.ShouldAttemptPass(20, 19, 12, 0.1f), Is.True);
            Assert.That(RaceBotMath.ShouldAttemptPass(20, 19, 20, 0.1f), Is.True);
            Assert.That(RaceBotMath.ShouldAttemptPass(20, 19, 25, 0.1f), Is.False);
            Assert.That(RaceBotMath.ShouldAttemptPass(20, 25, 20, 0.1f), Is.False);
            Assert.That(RaceBotMath.ShouldAttemptPass(20, 0, 2, 0.1f), Is.False);
            Assert.That(RaceBotMath.ShouldAttemptPass(20, 0, 3.4f, 0.1f), Is.False);
            Assert.That(RaceBotMath.ShouldAttemptPass(20, 0, 3.6f, 0.1f), Is.True,
                "a stopped bot must remain passable after the follower has braked close");
            Assert.That(RaceBotMath.ShouldAttemptPass(20, 5, 7.9f, 0.1f), Is.False);
            Assert.That(RaceBotMath.ShouldAttemptPass(20, 0, 8.1f, 0.1f), Is.True);
            Assert.That(RaceBotMath.ShouldAttemptPass(0, 0, 4, 0.1f), Is.True,
                "a stopped queue must not deadlock the pass planner");
            Assert.That(RaceBotMath.CommittedPassApproachSpeed(0, 4), Is.EqualTo(1.5f));
            Assert.That(RaceBotMath.CommittedPassApproachSpeed(0, 23), Is.EqualTo(5));
            Assert.That(RaceBotMath.CommittedPassApproachSpeed(8, 7), Is.EqualTo(8));
            Assert.That(RaceBotMath.HasPassAccelerationClearance(3.99f), Is.False);
            Assert.That(RaceBotMath.HasPassAccelerationClearance(4.0f), Is.True);
            Assert.That(RaceBotMath.ShouldResetPassAccelerationClearance(3.79f), Is.True);
            Assert.That(RaceBotMath.ShouldResetPassAccelerationClearance(3.8f), Is.False);
            Assert.That(RaceBotMath.HasSustainedPassAccelerationClearance(4, 1_000, 2_999), Is.False);
            Assert.That(RaceBotMath.HasSustainedPassAccelerationClearance(4, 1_000, 3_000), Is.True);
            Assert.That(RaceBotMath.FollowingDecisionDistance(20, 15, 0.1f),
                Is.LessThan(RaceBotMath.FollowingDecisionDistance(20, 0, 0.1f)));
            Assert.That(RaceBotMath.PassAttemptDistance(20, 0, 0.1f),
                Is.GreaterThan(RaceBotMath.PassAttemptDistance(20, 19, 0.1f)));
            Assert.That(RaceBotMath.CanAttemptPass(SessionType.Race, 4_999, 1_000), Is.False);
            Assert.That(RaceBotMath.CanAttemptPass(SessionType.Race, 5_000, 1_000), Is.True);
            Assert.That(RaceBotMath.CanAttemptPassPair(2, 2, 89_999, 90_000), Is.False);
            Assert.That(RaceBotMath.CanAttemptPassPair(2, 2, 90_000, 90_000), Is.True);
            Assert.That(RaceBotMath.CanAttemptPassPair(2, 1, 1, 60_000), Is.True,
                "pair cooldown must not block a different opponent");
            Assert.That(RaceBotMath.OvertakeCommitMilliseconds(0), Is.GreaterThan(
                RaceBotMath.OvertakeCommitMilliseconds(1)));
            Assert.That(RaceBotMath.OvertakeCommitMilliseconds(0.5f, 30), Is.GreaterThan(
                RaceBotMath.OvertakeCommitMilliseconds(0.5f, 5)));
            Assert.That(RaceBotMath.ShouldExtendPass(5, 15, 10, 0.5f, 0), Is.True);
            Assert.That(RaceBotMath.ShouldExtendPass(5, 10, 15, 0.5f, 0), Is.True,
                "a safe pass must survive a temporary corner-speed disadvantage");
            Assert.That(RaceBotMath.ShouldExtendPass(50, 15, 10, 0.5f, 0), Is.True);
            Assert.That(RaceBotMath.ShouldExtendPass(65, 15, 10, 0.5f, 0), Is.False);
            Assert.That(RaceBotMath.ShouldExtendPass(5, 15, 10, 0.5f,
                RaceBotMath.MaximumPassExtensions), Is.False);
            Assert.That(RaceBotMath.HasCompletedPass(false, 18, -17, 3), Is.False,
                "contact or ordering alone must not count as a pass");
            Assert.That(RaceBotMath.HasCompletedPass(true, 18, -17, 3), Is.True);
            Assert.That(RaceBotMath.HasCompletedPass(true, 18, -17, 7), Is.False);
            Assert.That(RaceBotMath.HasCompletedPass(true, 25, -17, 3), Is.False);
            Assert.That(RaceBotMath.CollisionRecoveryMilliseconds(1000, 3000, 0.5f, 7), Is.InRange(1000, 3000));
            Assert.That(RaceBotMath.AuthoredSplineSpeedLimit(20, 1), Is.EqualTo(20).Within(1e-6f));
            Assert.That(RaceBotMath.AuthoredSplineSpeedLimit(20, 0), Is.EqualTo(13).Within(1e-6f));
            Assert.That(RaceBotMath.AuthoredSplineSpeedLimit(0, 1), Is.EqualTo(float.PositiveInfinity));
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
