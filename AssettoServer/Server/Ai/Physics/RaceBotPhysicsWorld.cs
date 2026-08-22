using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using AssettoServer.Server.Configuration;
using AssettoServer.Server.Configuration.Extra;
using AssettoServer.Server.Runtime;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuPhysics.Trees;
using BepuUtilities;
using BepuUtilities.Memory;
using Serilog;

namespace AssettoServer.Server.Ai.Physics;

public readonly record struct RaceBotPhysicsState(Vector3 Position, Vector3 ProtocolPosition,
    Quaternion Orientation, Vector3 Velocity, float ForwardSpeed, float LongitudinalAcceleration,
    float SteeringAngleRadians, float SlipAngleDegrees, int RecoveryCount);
public readonly record struct RaceBotPhysicsTelemetry(float HeightErrorMeters,
    float SuspensionCompressionMeters, float UprightDot, float UpwardSpeedMetersPerSecond,
    float ExcessUpwardSpeedMetersPerSecond, int GroundedWheelCount,
    int SurfaceDiscontinuityCount, int TrackCorrectionCount);

public readonly record struct RaceBotPhysicsControl(bool Hold, bool Stop, Vector3 TargetPosition,
    Vector3 SteeringTargetPosition, Vector3 TargetForward, float TargetSpeed, float MaximumAcceleration,
    float MaximumBrakeDeceleration, float LateralGripG, float? ManualSteering = null,
    float? ManualAcceleration = null, bool ReverseRecovery = false);
public readonly record struct RacePhysicsDiagnostics(int BotCount, float MinimumY, float MaximumY, float MaximumSpeed,
    float MaximumSlipAngleDegrees, float MaximumSteeringAngleDegrees, float MaximumUpwardSpeed,
    float MaximumExcessUpwardSpeed,
    float MaximumSplineHeightError, float MaximumSuspensionCompression,
    float MinimumUprightDot, int OverturnedBots, int TotalRecoveries, int TotalTrackCorrections,
    int MinimumGroundedWheelCount, int TotalSurfaceDiscontinuities,
    int LaunchedBots, long LaunchStepSpread, long StaticPairTests, long StaticManifolds,
    long VehicleManifolds);

public sealed class RaceBotPhysicsWorld : IDisposable
{
    private const float OverturnedUprightDot = 0.25f;
    private const float RecoveryDelaySeconds = 1f;
    // Course drive authority reaches zero at 15 m. Recover before a car stranded against a
    // barrier can sit forever with a high requested speed but no usable engine authority.
    private const float MaximumRecoveryHorizontalError = 10f;
    private const float ImmediateRecoveryHorizontalError = 25f;
    private const float ImmobilizedRecoverySpeed = 2f;
    private const float MaximumRecoveryHeightError = 3f;
    private const float ImmediateRecoveryHeightError = 1.25f;
    private const float ImmediateRecoveryExcessUpwardSpeed = 3f;
    private const float MaximumAngularSpeed = 2.5f;
    private const float MaximumAngularAcceleration = 16f;
    private const float AttitudeGain = 4f;
    internal const float MaximumSteeringAngleRadians = 0.55850536f; // 32 degrees
    private const float MaximumSteeringRateRadiansPerSecond = 1.5f;
    private const float DownforceCoefficient = 0.0035f;
    private const float MaximumDownforceAcceleration = 6f;
    private const float SuspensionNaturalFrequencyHz = 5f;
    private const float SuspensionDampingRatio = 0.85f;
    private const float MaximumSuspensionAcceleration = 40f;
    private const float SuspensionRayExtraLength = 0.75f;
    private const float MaximumSurfaceResidualStep = 0.04f;
    private const float TrackRayHeight = 50f;
    private const float TrackRayLength = 100f;

    private readonly BufferPool _pool = new();
    private readonly Simulation _simulation;
    private readonly ThreadDispatcher? _dispatcher;
    private readonly RacePhysicsAsset _asset;
    private readonly Dictionary<string, ColliderShape> _colliders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<byte, BodyRecord> _bodies = [];
    private readonly object _sync = new();
    private readonly ContinuousDetection _botContinuousDetection;
    private readonly PhysicsContactMetrics _contactMetrics = new();
    private readonly PhysicsCollisionGroups _collisionGroups = new();
    private readonly TrackCollisionHandles _trackCollisionHandles = new();
    private readonly RacePhysicsFidelity _fidelity;
    private readonly StaticHandle _drivableTrackHandle;
    private long _stepIndex;
    private bool _disposed;

    private sealed class ColliderShape
    {
        public required TypedIndex ShapeIndex { get; init; }
        public required Vector3 Center { get; init; }
        public required BodyInertia UnitInertia { get; init; }
        public required float ProtocolReferenceHeight { get; init; }
        public required float WheelbaseMeters { get; init; }
        public required float HalfWidthMeters { get; init; }
        public required RaceWheelCollider[] Wheels { get; init; }
    }

    private sealed class RaycastWheelState
    {
        public required RaceWheelCollider Geometry { get; init; }
        public bool HasSurface { get; set; }
        public float SurfaceHeight { get; set; }
        public Vector3 SurfaceNormal { get; set; } = Vector3.UnitY;
        public float CompressionMeters { get; set; }
    }

    private sealed class BodyRecord
    {
        public required byte SessionId { get; init; }
        public required string Model { get; init; }
        public required BodyHandle Handle { get; init; }
        public required ColliderShape Collider { get; init; }
        public required BodyInertia DynamicInertia { get; init; }
        public required RaycastWheelState[] Wheels { get; init; }
        public required float MassKg { get; init; }
        public bool IsBot { get; set; }
        public bool IsHeld { get; set; }
        public RaceGridPose HeldPose { get; set; }
        public RaceBotPhysicsControl Control { get; set; }
        public float LastLongitudinalAcceleration { get; set; }
        public float LastSteeringAngleRadians { get; set; }
        public float LastSlipAngleDegrees { get; set; }
        public float MaximumSlipAngleDegrees { get; set; }
        public float MaximumSteeringAngleDegrees { get; set; }
        public float RecoveryNeededSeconds { get; set; }
        public float MaximumUpwardSpeed { get; set; }
        public float MaximumExcessUpwardSpeed { get; set; }
        public float MaximumSplineHeightError { get; set; }
        public int RecoveryCount { get; set; }
        public int TrackCorrectionCount { get; set; }
        public int SurfaceDiscontinuityCount { get; set; }
        public int GroundedWheelCount { get; set; }
        public float RoadSurfaceHeight { get; set; }
        public Vector3 RoadNormal { get; set; } = Vector3.UnitY;
        public float MaximumSuspensionCompressionObserved { get; set; }
        public long FirstMovingStep { get; set; }
    }

    public int GridCount => _asset.Grid.Count;

    public RaceBotPhysicsWorld(ACServerConfiguration configuration, ServerRuntimeOptions runtimeOptions)
    {
        var physics = configuration.Extra.AiParams.Race.Physics;
        string assetPath = Path.GetFullPath(Path.Combine(configuration.BaseFolder, physics.AssetFile));
        string presetRoot = Path.GetFullPath(configuration.BaseFolder) + Path.DirectorySeparatorChar;
        if (!assetPath.StartsWith(presetRoot, StringComparison.OrdinalIgnoreCase))
            throw new ConfigurationException("Race Physics AssetFile must stay inside the server preset directory");
        if (!File.Exists(assetPath))
            throw new ConfigurationException($"Prepared race physics asset is missing: {assetPath}");

        _asset = RacePhysicsAsset.Load(assetPath);
        var solver = physics.Fidelity switch
        {
            RacePhysicsFidelity.Efficient => new SolveDescription(4, 1),
            RacePhysicsFidelity.Balanced => new SolveDescription(8, 2),
            RacePhysicsFidelity.High => new SolveDescription(12, 4),
            _ => throw new ArgumentOutOfRangeException(nameof(physics.Fidelity))
        };
        _fidelity = physics.Fidelity;
        _botContinuousDetection = physics.Fidelity == RacePhysicsFidelity.High
            ? ContinuousDetection.Continuous(1e-3f, 1e-2f)
            : ContinuousDetection.Passive;
        _simulation = Simulation.Create(_pool,
            new NarrowPhaseCallbacks(physics.Friction, _contactMetrics, _collisionGroups,
                _trackCollisionHandles),
            new PoseIntegratorCallbacks(new Vector3(0, -9.81f, 0)), solver);
        // The parallel solver is useful for live servers but can change contact ordering between runs.
        if (!runtimeOptions.IsRaceSimulation
            && physics.Fidelity != RacePhysicsFidelity.Efficient && Environment.ProcessorCount > 2)
            _dispatcher = new ThreadDispatcher(Math.Min(8, Environment.ProcessorCount - 1));

        _drivableTrackHandle = AddTrackMesh(_asset.TrackTriangles)
                                ?? throw new ConfigurationException("Prepared race physics asset has no drivable track mesh");
        _trackCollisionHandles.Drivable = _drivableTrackHandle;
        AddTrackMesh(_asset.TrackBarrierTriangles);
        foreach (var (model, vertices) in _asset.CarColliderVertices)
        {
            var collider = CreateVehicleCollider(vertices, _asset.CarWheelColliders[model]);
            _colliders.Add(model, collider);
            Log.Debug("Race collider {Model}: AC protocol reference height {Height:F3} m, wheelbase {Wheelbase:F2} m, half width {HalfWidth:F2} m",
                model, collider.ProtocolReferenceHeight, collider.WheelbaseMeters,
                collider.HalfWidthMeters);
        }

        Log.Information("Rigid-body race world loaded: {DriveTriangles} drivable and {BarrierTriangles} barrier triangles, "
                        + "{GridSlots} grid slots, {Colliders} car colliders, fidelity {Fidelity}",
            _asset.TrackTriangles.Count, _asset.TrackBarrierTriangles.Count, _asset.Grid.Count,
            _colliders.Count, physics.Fidelity);
    }

    public RaceGridPose GetGridPose(int gridIndex)
    {
        if ((uint)gridIndex >= _asset.Grid.Count)
            throw new ConfigurationException($"Track physics asset exposes {_asset.Grid.Count} grid slots but slot {gridIndex} was requested");
        return _asset.Grid[gridIndex];
    }

    public float GetVehicleHalfWidthMeters(string model) => GetCollider(model).HalfWidthMeters;

    public void RegisterBot(byte sessionId, string model, RaceGridPose pose, float massKg)
    {
        lock (_sync)
        {
            RemoveBodyUnsafe(sessionId);
            var collider = GetCollider(model);
            var inertia = ScaleInertia(collider.UnitInertia, Math.Max(1, massKg));
            var bodyPose = ToCenterOfMassPose(pose, collider.Center);
            var collidable = new CollidableDescription(collider.ShapeIndex, 0.25f, _botContinuousDetection);
            var handle = _simulation.Bodies.Add(BodyDescription.CreateDynamic(bodyPose, inertia, collidable,
                new BodyActivityDescription(0.01f)));
            _collisionGroups.Assign(handle, sessionId, isWheel: false);
            var wheels = CreateRaycastWheels(collider);
            _bodies.Add(sessionId, new BodyRecord
            {
                SessionId = sessionId,
                Model = model,
                Handle = handle,
                Collider = collider,
                DynamicInertia = inertia,
                Wheels = wheels,
                MassKg = Math.Max(1, massKg),
                IsBot = true,
                HeldPose = pose,
                RoadSurfaceHeight = pose.Position.Y,
                RoadNormal = Vector3.Transform(Vector3.UnitY, pose.Orientation)
            });
        }
    }

    public void SetBotControl(byte sessionId, RaceBotPhysicsControl control)
    {
        lock (_sync)
        {
            if (!_bodies.TryGetValue(sessionId, out var record) || !record.IsBot)
                return;
            record.Control = control;
        }
    }

    public void TeleportBot(byte sessionId, RaceGridPose pose)
    {
        lock (_sync)
        {
            if (!_bodies.TryGetValue(sessionId, out var record) || !record.IsBot)
                return;
            var body = _simulation.Bodies[record.Handle];
            body.Pose = ToCenterOfMassPose(pose, record.Collider.Center);
            body.Velocity.Linear = Vector3.Zero;
            body.Velocity.Angular = Vector3.Zero;
            ResetSuspension(record, pose);
            body.Awake = true;
            record.HeldPose = pose;
            record.LastLongitudinalAcceleration = 0;
            record.LastSteeringAngleRadians = 0;
            record.LastSlipAngleDegrees = 0;
            record.RecoveryNeededSeconds = 0;
        }
    }

    public void SynchronizeHuman(byte sessionId, string model, Vector3 position, Vector3 rotation, Vector3 velocity)
    {
        lock (_sync)
        {
            var orientation = RacePhysicsMath.FromProtocolRotation(rotation);
            if (!_bodies.TryGetValue(sessionId, out var record) || record.IsBot)
            {
                RemoveBodyUnsafe(sessionId);
                var collider = GetCollider(model);
                var origin = FromProtocolPosition(position, orientation, collider.ProtocolReferenceHeight);
                var pose = ToCenterOfMassPose(new RaceGridPose(origin, orientation), collider.Center);
                var handle = _simulation.Bodies.Add(BodyDescription.CreateKinematic(pose,
                    new CollidableDescription(collider.ShapeIndex, ContinuousDetection.Passive),
                    new BodyActivityDescription(0.01f)));
                record = new BodyRecord
                {
                    SessionId = sessionId,
                    Model = model,
                    Handle = handle,
                    Collider = collider,
                    DynamicInertia = default,
                    Wheels = [],
                    MassKg = 1,
                    IsBot = false
                };
                _collisionGroups.Assign(handle, sessionId, isWheel: false);
                _bodies.Add(sessionId, record);
            }

            var body = _simulation.Bodies[record.Handle];
            var physicalOrigin = FromProtocolPosition(position, orientation,
                record.Collider.ProtocolReferenceHeight);
            body.Pose = ToCenterOfMassPose(new RaceGridPose(physicalOrigin, orientation), record.Collider.Center);
            body.Velocity.Linear = velocity;
            body.Velocity.Angular = Vector3.Zero;
            body.Awake = true;
        }
    }

    public void RemoveBody(byte sessionId)
    {
        lock (_sync)
            RemoveBodyUnsafe(sessionId);
    }

    public void Step(float deltaSeconds)
    {
        lock (_sync)
        {
            _stepIndex++;
            _contactMetrics.BeginStep(_stepIndex);
            foreach (var record in _bodies.Values.Where(x => x.IsBot))
                ApplyControl(record, deltaSeconds);
            _simulation.Timestep(deltaSeconds, _dispatcher);
            foreach (var record in _bodies.Values.Where(x => x is { IsBot: true, IsHeld: true }))
                HoldAtGrid(record);
            foreach (var record in _bodies.Values.Where(x => x is { IsBot: true, Control.Stop: true }))
                StopBot(record);
            foreach (var record in _bodies.Values.Where(x => x is { IsBot: true, IsHeld: false }))
                RecoverTransientLaunch(record);
            foreach (var record in _bodies.Values.Where(x => x.IsBot))
                CaptureMaximums(record);
        }
    }

    public bool TryGetBotState(byte sessionId, out RaceBotPhysicsState state)
    {
        lock (_sync)
        {
            if (!_bodies.TryGetValue(sessionId, out var record) || !record.IsBot)
            {
                state = default;
                return false;
            }
            var body = _simulation.Bodies[record.Handle];
            var chassisOrigin = GetChassisOrigin(record, body);
            var renderOrigin = chassisOrigin + Vector3.Transform(Vector3.UnitY
                * GetNetworkRideHeightClearance(record.Collider.ProtocolReferenceHeight),
                body.Pose.Orientation);
            var protocolPosition = ToProtocolPosition(renderOrigin, body.Pose.Orientation,
                record.Collider.ProtocolReferenceHeight);
            var forward = Vector3.Transform(Vector3.UnitZ, body.Pose.Orientation);
            state = new RaceBotPhysicsState(chassisOrigin, protocolPosition, body.Pose.Orientation,
                body.Velocity.Linear,
                Vector3.Dot(body.Velocity.Linear, forward), record.LastLongitudinalAcceleration,
                record.LastSteeringAngleRadians, record.LastSlipAngleDegrees,
                record.RecoveryCount);
            return true;
        }
    }

    public bool TryGetBotTelemetry(byte sessionId, out RaceBotPhysicsTelemetry telemetry)
    {
        lock (_sync)
        {
            if (!_bodies.TryGetValue(sessionId, out var record) || !record.IsBot)
            {
                telemetry = default;
                return false;
            }

            var body = _simulation.Bodies[record.Handle];
            var chassisOrigin = GetChassisOrigin(record, body);
            float excessUpwardSpeed = GetExcessUpwardSpeed(record.Control.TargetForward,
                body.Velocity.Linear);
            telemetry = new RaceBotPhysicsTelemetry(
                Math.Abs(chassisOrigin.Y - record.RoadSurfaceHeight),
                record.Wheels.Length == 0 ? 0 : record.Wheels.Max(wheel => wheel.CompressionMeters),
                GetUprightDot(body.Pose.Orientation),
                body.Velocity.Linear.Y,
                excessUpwardSpeed,
                record.GroundedWheelCount,
                record.SurfaceDiscontinuityCount,
                record.TrackCorrectionCount);
            return true;
        }
    }

    public RacePhysicsDiagnostics GetDiagnostics()
    {
        lock (_sync)
        {
            int count = 0;
            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;
            float maxSpeed = 0;
            float maxSlipAngle = 0;
            float maxSteeringAngle = 0;
            float maxUpwardSpeed = 0;
            float maxExcessUpwardSpeed = 0;
            float maxSplineHeightError = 0;
            float maxSuspensionCompression = 0;
            float minUprightDot = 1;
            int overturnedBots = 0;
            int totalRecoveries = 0;
            int totalTrackCorrections = 0;
            int minimumGroundedWheelCount = 4;
            int totalSurfaceDiscontinuities = 0;
            bool hasMovingBot = false;
            int launchedBots = 0;
            long firstMovingStep = long.MaxValue;
            long lastMovingStep = long.MinValue;
            foreach (var record in _bodies.Values.Where(x => x.IsBot))
            {
                var body = _simulation.Bodies[record.Handle];
                var chassisOrigin = GetChassisOrigin(record, body);
                float uprightDot = GetUprightDot(body.Pose.Orientation);
                minY = Math.Min(minY, chassisOrigin.Y);
                maxY = Math.Max(maxY, chassisOrigin.Y);
                maxSpeed = Math.Max(maxSpeed, body.Velocity.Linear.Length());
                maxSlipAngle = Math.Max(maxSlipAngle, record.MaximumSlipAngleDegrees);
                maxSteeringAngle = Math.Max(maxSteeringAngle, record.MaximumSteeringAngleDegrees);
                maxUpwardSpeed = Math.Max(maxUpwardSpeed, record.MaximumUpwardSpeed);
                maxExcessUpwardSpeed = Math.Max(maxExcessUpwardSpeed, record.MaximumExcessUpwardSpeed);
                maxSplineHeightError = Math.Max(maxSplineHeightError, record.MaximumSplineHeightError);
                maxSuspensionCompression = Math.Max(maxSuspensionCompression,
                    record.MaximumSuspensionCompressionObserved);
                minUprightDot = Math.Min(minUprightDot, uprightDot);
                if (uprightDot < OverturnedUprightDot)
                    overturnedBots++;
                totalRecoveries += record.RecoveryCount;
                totalTrackCorrections += record.TrackCorrectionCount;
                totalSurfaceDiscontinuities += record.SurfaceDiscontinuityCount;
                if (record.FirstMovingStep > 0)
                {
                    hasMovingBot = true;
                    minimumGroundedWheelCount = Math.Min(minimumGroundedWheelCount,
                        record.GroundedWheelCount);
                    launchedBots++;
                    firstMovingStep = Math.Min(firstMovingStep, record.FirstMovingStep);
                    lastMovingStep = Math.Max(lastMovingStep, record.FirstMovingStep);
                }
                count++;
            }
            long launchStepSpread = launchedBots > 1 ? lastMovingStep - firstMovingStep : 0;
            return new RacePhysicsDiagnostics(count, count == 0 ? 0 : minY, count == 0 ? 0 : maxY, maxSpeed,
                maxSlipAngle, maxSteeringAngle, maxUpwardSpeed, maxExcessUpwardSpeed,
                maxSplineHeightError, maxSuspensionCompression,
                count == 0 ? 1 : minUprightDot, overturnedBots, totalRecoveries, totalTrackCorrections,
                hasMovingBot ? minimumGroundedWheelCount : 4, totalSurfaceDiscontinuities,
                launchedBots, launchStepSpread,
                Interlocked.Read(ref _contactMetrics.StaticPairTests),
                Interlocked.Read(ref _contactMetrics.StaticManifolds),
                Interlocked.Read(ref _contactMetrics.VehicleManifolds));
        }
    }

    public (byte A, byte B, long Count) GetMostFrequentVehicleContactPair()
    {
        lock (_sync)
            return _contactMetrics.GetMostFrequentVehiclePair();
    }

    public long GetVehicleContactManifoldCount(byte firstSessionId, byte secondSessionId) =>
        _contactMetrics.GetVehiclePairCount(firstSessionId, secondSessionId);

    public bool WasVehicleContactRecent(byte firstSessionId, byte secondSessionId,
        long maximumStepAge = RaceBotMath.RecentVehicleContactStepWindow)
    {
        lock (_sync)
            return _contactMetrics.WasVehiclePairActiveRecently(firstSessionId,
                secondSessionId, _stepIndex, maximumStepAge);
    }

    private void CaptureMaximums(BodyRecord record)
    {
        var body = _simulation.Bodies[record.Handle];
        var chassisOrigin = GetChassisOrigin(record, body);
        record.MaximumUpwardSpeed = Math.Max(record.MaximumUpwardSpeed, body.Velocity.Linear.Y);
        record.MaximumExcessUpwardSpeed = Math.Max(record.MaximumExcessUpwardSpeed,
            GetExcessUpwardSpeed(record.Control.TargetForward, body.Velocity.Linear));
        record.MaximumSplineHeightError = Math.Max(record.MaximumSplineHeightError,
            Math.Abs(chassisOrigin.Y - record.RoadSurfaceHeight));
        if (record.FirstMovingStep == 0)
        {
            var forward = Vector3.Transform(Vector3.UnitZ, body.Pose.Orientation);
            if (Vector3.Dot(body.Velocity.Linear, forward) >= 0.5f)
                record.FirstMovingStep = _stepIndex;
        }
    }

    private void ApplyControl(BodyRecord record, float deltaSeconds)
    {
        var body = _simulation.Bodies[record.Handle];
        var control = record.Control;
        if (control.Hold)
        {
            if (!record.IsHeld)
            {
                record.IsHeld = true;
                body.SetLocalInertia(default);
            }
            HoldAtGrid(record);
            record.LastLongitudinalAcceleration = 0;
            record.LastSteeringAngleRadians = 0;
            record.LastSlipAngleDegrees = 0;
            return;
        }

        if (record.IsHeld)
        {
            record.IsHeld = false;
            body.SetLocalInertia(record.DynamicInertia);
            body.Awake = true;
        }

        if (control.Stop)
        {
            StopBot(record);
            return;
        }

        var orientation = body.Pose.Orientation;
        var chassisOrigin = GetChassisOrigin(record, body);
        var origin = chassisOrigin;
        var targetForward = Vector3.Normalize(control.TargetForward);
        var physicalTarget = GetTrackSupportTarget(control, origin);
        var recoveryTarget = GetRecoveryAssessmentTarget(control.TargetPosition, physicalTarget);
        float uprightDot = GetUprightDot(orientation);
        var horizontalVelocity = body.Velocity.Linear with { Y = 0 };
        if (NeedsRecovery(uprightDot, origin, recoveryTarget, horizontalVelocity.Length()))
            record.RecoveryNeededSeconds += deltaSeconds;
        else
            record.RecoveryNeededSeconds = 0;

        if (record.RecoveryNeededSeconds >= RecoveryDelaySeconds)
        {
            RecoverBot(record, control);
            return;
        }

        var lineError = control.TargetPosition - origin;
        lineError.Y = 0;
        var bodyForward = Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, orientation));
        float forwardSpeed = Vector3.Dot(body.Velocity.Linear, bodyForward);
        float lookAheadMeters = GetSteeringLookAheadMeters(forwardSpeed, lineError.Length());
        bool manual = control.ManualSteering.HasValue;
        var steeringDirection = manual
            ? bodyForward
            : CalculateSteeringDirectionToTarget(origin, control.SteeringTargetPosition,
                targetForward);
        float requestedSteeringAngle = manual
            ? Math.Clamp(control.ManualSteering!.Value, -1, 1) * MaximumSteeringAngleRadians
            : CalculateSteeringAngle(bodyForward, steeringDirection,
                lookAheadMeters, record.Collider.WheelbaseMeters);
        float steeringAngle = MoveSteeringAngle(record.LastSteeringAngleRadians,
            requestedSteeringAngle, deltaSeconds);
        float targetYawRate = CalculateTargetYawRate(forwardSpeed, record.Collider.WheelbaseMeters,
            steeringAngle, control.LateralGripG);
        float signedSlipAngle = CalculateSignedSlipAngleRadians(body.Velocity.Linear, bodyForward);
        targetYawRate = CalculateSlipStabilizedYawRate(targetYawRate, signedSlipAngle,
            forwardSpeed, control.LateralGripG);
        float uprightDriveScale = GetDriveScale(uprightDot);
        float courseDriveScale = GetCourseDriveScale(lineError.Length(),
            Math.Abs(origin.Y - physicalTarget.Y));
        float driveScale = uprightDriveScale * courseDriveScale;
        float speedError = control.TargetSpeed * driveScale - forwardSpeed;
        float acceleration = control.ManualAcceleration.HasValue
            ? Math.Clamp(control.ManualAcceleration.Value * uprightDriveScale,
                -control.MaximumBrakeDeceleration, control.MaximumAcceleration)
            : Math.Clamp(speedError / Math.Max(deltaSeconds, 1e-3f),
                -control.MaximumBrakeDeceleration, control.MaximumAcceleration);
        if (manual && acceleration < 0 && forwardSpeed <= 0 && !control.ReverseRecovery)
            acceleration = 0;
        if (control.ReverseRecovery && forwardSpeed <= -2.5f)
            acceleration = 0;
        // Engine and brake authority is strictly longitudinal. Lane changes are produced by yawing
        // the chassis and its velocity vector through bounded tyre grip, never by lateral thrust.
        body.Velocity.Linear += CalculateLongitudinalVelocityDelta(bodyForward, acceleration, deltaSeconds);
        record.LastLongitudinalAcceleration = acceleration;
        body.Velocity.Linear = ApplyLateralGrip(body.Velocity.Linear, bodyForward,
            control.LateralGripG * uprightDriveScale, deltaSeconds);
        record.LastSteeringAngleRadians = steeringAngle;
        record.LastSlipAngleDegrees = CalculateSlipAngleDegrees(body.Velocity.Linear, bodyForward);
        record.MaximumSlipAngleDegrees = Math.Max(record.MaximumSlipAngleDegrees,
            record.LastSlipAngleDegrees);
        record.MaximumSteeringAngleDegrees = Math.Max(record.MaximumSteeringAngleDegrees,
            Math.Abs(steeringAngle) * 180 / MathF.PI);

        float downforceAcceleration = Math.Min(MaximumDownforceAcceleration,
            Math.Max(0, forwardSpeed) * Math.Max(0, forwardSpeed) * DownforceCoefficient);
        body.Velocity.Linear.Y -= downforceAcceleration * deltaSeconds;
        body.Velocity.Angular = CalculateStabilizedAngularVelocity(orientation, steeringDirection,
            body.Velocity.Angular, deltaSeconds, targetYawRate, record.RoadNormal);
        ApplyRaycastSuspension(record, body, chassisOrigin, orientation, deltaSeconds);
        body.Awake = true;
    }

    private void RecoverTransientLaunch(BodyRecord record)
    {
        var body = _simulation.Bodies[record.Handle];
        var control = record.Control;
        var chassisOrigin = GetChassisOrigin(record, body);
        var physicalTarget = GetTrackSupportTarget(control, chassisOrigin);
        if (NeedsImmediateRecovery(GetUprightDot(body.Pose.Orientation), chassisOrigin, physicalTarget,
                body.Velocity.Linear, control.TargetForward))
            RecoverBot(record, control);
    }

    private void RecoverBot(BodyRecord record, RaceBotPhysicsControl control)
    {
        var body = _simulation.Bodies[record.Handle];
        var chassisOrigin = GetChassisOrigin(record, body);
        var origin = chassisOrigin;
        var recoveryPose = CreateRecoveryPose(GetTrackRecoveryTarget(control, origin),
            control.TargetForward);
        body.Pose = ToCenterOfMassPose(recoveryPose, record.Collider.Center);
        body.Velocity.Linear = Vector3.Normalize(control.TargetForward) * Math.Min(Math.Max(0, control.TargetSpeed), 5f);
        body.Velocity.Angular = Vector3.Zero;
        ResetSuspension(record, recoveryPose);
        body.Awake = true;
        record.LastLongitudinalAcceleration = 0;
        record.LastSteeringAngleRadians = 0;
        record.LastSlipAngleDegrees = 0;
        record.RecoveryNeededSeconds = 0;
        record.RecoveryCount++;
        Log.Warning("Recovered overturned/off-track race bot {SessionId} ({Model}) from "
                    + "({OriginX:F1}, {OriginY:F1}, {OriginZ:F1}) to ({TargetX:F1}, {TargetY:F1}, {TargetZ:F1}); recovery {RecoveryCount}",
            record.SessionId, record.Model, origin.X, origin.Y, origin.Z,
            recoveryPose.Position.X, recoveryPose.Position.Y, recoveryPose.Position.Z, record.RecoveryCount);
    }

    private void HoldAtGrid(BodyRecord record)
    {
        var body = _simulation.Bodies[record.Handle];
        body.Pose = ToCenterOfMassPose(record.HeldPose, record.Collider.Center);
        body.Velocity.Linear = Vector3.Zero;
        body.Velocity.Angular = Vector3.Zero;
        ResetSuspension(record, record.HeldPose);
        body.Awake = true;
    }

    private void StopBot(BodyRecord record)
    {
        var body = _simulation.Bodies[record.Handle];
        body.Velocity.Linear = Vector3.Zero;
        body.Velocity.Angular = Vector3.Zero;
        body.Awake = true;
        record.LastLongitudinalAcceleration = 0;
        record.LastSteeringAngleRadians = 0;
        record.LastSlipAngleDegrees = 0;
    }

    private ColliderShape GetCollider(string model) => _colliders.TryGetValue(model, out var collider)
        ? collider
        : throw new ConfigurationException($"Prepared race physics asset has no collider for car model: {model}");

    private static RigidPose ToCenterOfMassPose(RaceGridPose originPose, Vector3 localCenter) =>
        new(originPose.Position + Vector3.Transform(localCenter, originPose.Orientation), originPose.Orientation);

    private static Vector3 GetChassisOrigin(BodyRecord record, BodyReference body) =>
        body.Pose.Position - Vector3.Transform(record.Collider.Center, body.Pose.Orientation);

    private void ApplyRaycastSuspension(BodyRecord record, BodyReference body, Vector3 chassisOrigin,
        Quaternion orientation, float deltaSeconds)
    {
        if (record.Wheels.Length == 0)
            return;

        var bodyUp = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, orientation));
        var bodyRight = Vector3.Transform(Vector3.UnitX, orientation) with { Y = 0 };
        bodyRight = bodyRight.LengthSquared() > 1e-6f ? Vector3.Normalize(bodyRight) : Vector3.UnitX;
        float expectedSurfaceStep = GetTargetVerticalSpeed(record.Control.TargetForward,
            body.Velocity.Linear, orientation) * Math.Max(0, deltaSeconds);
        float cornerMass = record.MassKg / record.Wheels.Length;
        float angularFrequency = 2 * MathF.PI * SuspensionNaturalFrequencyHz;
        float springStiffness = cornerMass * angularFrequency * angularFrequency;
        float damping = 2 * SuspensionDampingRatio * cornerMass * angularFrequency;
        float maximumForce = cornerMass * MaximumSuspensionAcceleration;
        int grounded = 0;
        var roadNormalSum = Vector3.Zero;
        float roadHeightSum = 0;

        foreach (var wheel in record.Wheels)
        {
            float suspensionLength = GetSuspensionLength(wheel.Geometry.Radius);
            var anchor = chassisOrigin + Vector3.Transform(
                wheel.Geometry.Center + Vector3.UnitY * suspensionLength, orientation);
            if (!TryGetWheelSurface(record, wheel, anchor, bodyRight, expectedSurfaceStep,
                    out var surfacePoint, out var surfaceNormal))
            {
                wheel.CompressionMeters = 0;
                continue;
            }

            var wheelCenter = surfacePoint + surfaceNormal * wheel.Geometry.Radius;
            float currentLength = Vector3.Dot(anchor - wheelCenter, bodyUp);
            float compression = suspensionLength - currentLength;
            wheel.CompressionMeters = Math.Max(0, compression);
            record.MaximumSuspensionCompressionObserved = Math.Max(
                record.MaximumSuspensionCompressionObserved, wheel.CompressionMeters);

            float extensionLimit = GetSuspensionExtensionLimit(wheel.Geometry.Radius);
            if (compression < -extensionLimit)
                continue;

            float compressionLimit = GetSuspensionCompressionLimit(wheel.Geometry.Radius);
            float boundedCompression = Math.Clamp(compression, -extensionLimit, compressionLimit);
            var contactOffset = surfacePoint - body.Pose.Position;
            body.GetVelocityForOffset(contactOffset, out var contactVelocity);
            float normalVelocity = Vector3.Dot(contactVelocity, surfaceNormal);
            float staticLoad = cornerMass * 9.81f / Math.Max(0.35f, surfaceNormal.Y);
            float force = Math.Clamp(staticLoad + springStiffness * boundedCompression
                                                - damping * normalVelocity,
                0, maximumForce);
            body.ApplyImpulse(surfaceNormal * (force * Math.Max(0, deltaSeconds)), contactOffset);
            grounded++;
            roadHeightSum += surfacePoint.Y;
            roadNormalSum += surfaceNormal;
        }

        record.GroundedWheelCount = grounded;
        if (grounded == 0)
            return;
        record.RoadSurfaceHeight = roadHeightSum / grounded;
        if (roadNormalSum.LengthSquared() > 1e-6f)
            record.RoadNormal = SmoothSurfaceNormal(record.RoadNormal,
                Vector3.Normalize(roadNormalSum), 0.35f);
    }

    private bool TryGetWheelSurface(BodyRecord record, RaycastWheelState wheel, Vector3 anchor,
        Vector3 bodyRight, float expectedSurfaceStep, out Vector3 surfacePoint, out Vector3 surfaceNormal)
    {
        int sampleCount = _fidelity switch
        {
            RacePhysicsFidelity.Efficient => 1,
            RacePhysicsFidelity.Balanced => 3,
            RacePhysicsFidelity.High => 5,
            _ => 1
        };
        float suspensionLength = GetSuspensionLength(wheel.Geometry.Radius);
        float extensionLength = GetSuspensionExtensionLimit(wheel.Geometry.Radius);
        float rayLength = suspensionLength + extensionLength + wheel.Geometry.Radius
                          + SuspensionRayExtraLength;
        float referenceHeight = wheel.HasSurface
            ? wheel.SurfaceHeight + expectedSurfaceStep
            : record.Control.TargetPosition.Y;
        float heightSum = 0;
        var normalSum = Vector3.Zero;
        int hitCount = 0;
        float tyreHalfWidth = Math.Clamp(wheel.Geometry.Radius * 0.45f, 0.08f, 0.18f);

        for (int sample = 0; sample < sampleCount; sample++)
        {
            float normalizedOffset = sampleCount == 1 ? 0 : sample / (float)(sampleCount - 1) * 2 - 1;
            var rayOrigin = anchor + bodyRight * (normalizedOffset * tyreHalfWidth);
            var handler = new TrackRayHitHandler(_drivableTrackHandle, referenceHeight, rayOrigin.Y);
            _simulation.RayCast(rayOrigin, -Vector3.UnitY, rayLength, ref handler);
            if (!handler.Hit || handler.Normal.Y < 0.15f)
                continue;
            heightSum += handler.Height;
            normalSum += handler.Normal;
            hitCount++;
        }

        if (hitCount == 0)
        {
            wheel.HasSurface = false;
            surfacePoint = default;
            surfaceNormal = Vector3.UnitY;
            return false;
        }

        float rawHeight = heightSum / hitCount;
        float stabilizedHeight = StabilizeSurfaceHeight(wheel.SurfaceHeight, rawHeight,
            expectedSurfaceStep, wheel.HasSurface, MaximumSurfaceResidualStep,
            out bool discontinuityLimited);
        if (discontinuityLimited)
            record.SurfaceDiscontinuityCount++;
        var rawNormal = normalSum.LengthSquared() > 1e-6f
            ? Vector3.Normalize(normalSum)
            : Vector3.UnitY;
        if (rawNormal.Y < 0)
            rawNormal = -rawNormal;
        surfaceNormal = SmoothSurfaceNormal(wheel.SurfaceNormal, rawNormal,
            wheel.HasSurface ? 0.35f : 1f);
        wheel.SurfaceHeight = stabilizedHeight;
        wheel.SurfaceNormal = surfaceNormal;
        wheel.HasSurface = true;
        surfacePoint = new Vector3(anchor.X, stabilizedHeight, anchor.Z);
        return true;
    }

    internal static float StabilizeSurfaceHeight(float previousHeight, float rawHeight,
        float expectedSurfaceStep, bool hasPreviousSurface, float maximumResidualStep,
        out bool limited)
    {
        if (!hasPreviousSurface)
        {
            limited = false;
            return rawHeight;
        }
        float predictedHeight = previousHeight + expectedSurfaceStep;
        float residual = rawHeight - predictedHeight;
        float maximumStep = Math.Max(0, maximumResidualStep);
        limited = Math.Abs(residual) > maximumStep;
        return predictedHeight + Math.Clamp(residual, -maximumStep, maximumStep);
    }

    internal static Vector3 SmoothSurfaceNormal(Vector3 previous, Vector3 current, float blend)
    {
        if (previous.LengthSquared() < 1e-6f)
            previous = Vector3.UnitY;
        if (current.LengthSquared() < 1e-6f)
            current = Vector3.UnitY;
        previous = Vector3.Normalize(previous);
        current = Vector3.Normalize(current);
        if (Vector3.Dot(previous, current) < 0)
            current = -current;
        var result = Vector3.Lerp(previous, current, Math.Clamp(blend, 0, 1));
        return result.LengthSquared() > 1e-6f ? Vector3.Normalize(result) : current;
    }

    private Vector3 GetTrackSupportTarget(RaceBotPhysicsControl control, Vector3 physicalOrigin)
    {
        var target = physicalOrigin with { Y = control.TargetPosition.Y };
        target.Y = TryGetTrackSurfaceHeight(target, out float surfaceHeight)
            ? surfaceHeight
            : physicalOrigin.Y;
        return target;
    }

    private Vector3 GetTrackRecoveryTarget(RaceBotPhysicsControl control, Vector3 fallbackOrigin)
    {
        var target = control.TargetPosition;
        target.Y = TryGetTrackSurfaceHeight(control.TargetPosition, out float surfaceHeight)
            ? surfaceHeight
            : fallbackOrigin.Y;
        return target;
    }

    private bool TryGetTrackSurfaceHeight(Vector3 splineTarget, out float surfaceHeight)
    {
        var handler = new TrackRayHitHandler(_drivableTrackHandle, splineTarget.Y,
            splineTarget.Y + TrackRayHeight);
        var rayOrigin = new Vector3(splineTarget.X, splineTarget.Y + TrackRayHeight, splineTarget.Z);
        _simulation.RayCast(rayOrigin, -Vector3.UnitY, TrackRayLength, ref handler);
        surfaceHeight = handler.Height;
        return handler.Hit;
    }

    internal static float GetNetworkRideHeightClearance(float protocolReferenceHeight) =>
        Math.Clamp(protocolReferenceHeight * 0.20f, 0.05f, 0.08f);

    internal static float GetTargetVerticalSpeed(Vector3 targetForward, Vector3 velocity,
        Quaternion orientation)
    {
        if (targetForward.LengthSquared() < 1e-6f)
            return 0;
        var forward = Vector3.Transform(Vector3.UnitZ, orientation);
        float forwardSpeed = Math.Max(0, Vector3.Dot(velocity, forward));
        return Math.Clamp(Vector3.Normalize(targetForward).Y * forwardSpeed, -5f, 5f);
    }

    internal static float GetExcessUpwardSpeed(Vector3 targetForward, Vector3 velocity)
    {
        return Math.Max(0, velocity.Y - GetExpectedVerticalSpeedFromAuthoredSlope(targetForward, velocity));
    }

    internal static float GetExpectedVerticalSpeedFromAuthoredSlope(Vector3 targetForward, Vector3 velocity)
    {
        var horizontalForward = targetForward with { Y = 0 };
        float horizontalLength = horizontalForward.Length();
        if (horizontalLength < 1e-4f)
            return 0;
        horizontalForward /= horizontalLength;
        var horizontalVelocity = velocity with { Y = 0 };
        float horizontalSpeed = Math.Max(0, Vector3.Dot(horizontalVelocity, horizontalForward));
        return targetForward.Y / horizontalLength * horizontalSpeed;
    }

    internal static Vector3 ToProtocolPosition(Vector3 physicalOrigin, Quaternion orientation,
        float protocolReferenceHeight) =>
        physicalOrigin + Vector3.Transform(Vector3.UnitY * protocolReferenceHeight, orientation);

    internal static Vector3 FromProtocolPosition(Vector3 protocolPosition, Quaternion orientation,
        float protocolReferenceHeight) =>
        protocolPosition - Vector3.Transform(Vector3.UnitY * protocolReferenceHeight, orientation);

    internal static float GetProtocolReferenceHeight(IReadOnlyList<RaceWheelCollider> wheels) =>
        wheels.Average(wheel => wheel.Center.Y);

    internal static float GetUprightDot(Quaternion orientation) =>
        Vector3.Dot(Vector3.Transform(Vector3.UnitY, orientation), Vector3.UnitY);

    internal static float GetDriveScale(float uprightDot) =>
        Math.Clamp((uprightDot - 0.45f) / 0.5f, 0, 1);

    internal static float GetCourseDriveScale(float horizontalError, float heightError)
    {
        float horizontalScale = Math.Clamp((15f - Math.Max(0, horizontalError)) / 10f, 0, 1);
        float heightScale = Math.Clamp((4f - Math.Max(0, heightError)) / 3f, 0, 1);
        return Math.Min(horizontalScale, heightScale);
    }

    internal static bool NeedsRecovery(float uprightDot, Vector3 physicalOrigin,
        Vector3 targetPosition, float horizontalSpeedMetersPerSecond)
    {
        var horizontalError = physicalOrigin - targetPosition;
        horizontalError.Y = 0;
        float horizontalErrorSquared = horizontalError.LengthSquared();
        bool offCourse = horizontalErrorSquared
                         > MaximumRecoveryHorizontalError * MaximumRecoveryHorizontalError
                         && (horizontalSpeedMetersPerSecond < ImmobilizedRecoverySpeed
                             || horizontalErrorSquared > ImmediateRecoveryHorizontalError
                                 * ImmediateRecoveryHorizontalError);
        return uprightDot < OverturnedUprightDot
               || offCourse
               || Math.Abs(physicalOrigin.Y - targetPosition.Y) > MaximumRecoveryHeightError;
    }

    internal static Vector3 GetRecoveryAssessmentTarget(Vector3 courseTarget,
        Vector3 trackSupportTarget) => new(courseTarget.X, trackSupportTarget.Y, courseTarget.Z);

    internal static bool NeedsImmediateRecovery(float uprightDot, Vector3 physicalOrigin, Vector3 physicalTarget,
        Vector3 velocity, Vector3 targetForward)
    {
        if (uprightDot < 0 || Math.Abs(physicalOrigin.Y - physicalTarget.Y) > ImmediateRecoveryHeightError)
            return true;
        if (targetForward.LengthSquared() < 1e-6f)
            return false;
        float expectedVerticalSpeed = GetExpectedVerticalSpeedFromAuthoredSlope(targetForward, velocity);
        return velocity.Y - expectedVerticalSpeed > ImmediateRecoveryExcessUpwardSpeed;
    }

    internal static Vector3 CalculateStabilizedAngularVelocity(Quaternion orientation, Vector3 targetForward,
        Vector3 angularVelocity, float deltaSeconds, float? targetYawRate = null,
        Vector3? targetUpHint = null)
    {
        targetForward = Vector3.Normalize(targetForward);
        var targetUpReference = targetUpHint.HasValue && targetUpHint.Value.LengthSquared() > 1e-6f
            ? Vector3.Normalize(targetUpHint.Value)
            : Vector3.UnitY;
        var targetRight = Vector3.Cross(targetUpReference, targetForward);
        if (targetRight.LengthSquared() < 1e-6f)
            return MoveTowards(angularVelocity, Vector3.Zero, MaximumAngularAcceleration * deltaSeconds);
        targetRight = Vector3.Normalize(targetRight);
        var targetUp = Vector3.Normalize(Vector3.Cross(targetForward, targetRight));
        var actualForward = Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, orientation));
        var actualUp = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, orientation));
        var desired = (Vector3.Cross(actualForward, targetForward) + Vector3.Cross(actualUp, targetUp))
                      * AttitudeGain;
        if (targetYawRate.HasValue)
            desired += targetUp * (targetYawRate.Value - Vector3.Dot(desired, targetUp));
        desired = ClampMagnitude(desired, MaximumAngularSpeed);
        var boundedAngularVelocity = ClampMagnitude(angularVelocity, MaximumAngularSpeed);
        return MoveTowards(boundedAngularVelocity, desired,
            MaximumAngularAcceleration * Math.Max(0, deltaSeconds));
    }

    internal static RaceGridPose CreateRecoveryPose(Vector3 trackTargetPosition, Vector3 targetForward)
    {
        var orientation = RacePhysicsMath.FromForward(targetForward);
        return new RaceGridPose(trackTargetPosition, orientation);
    }

    private static Vector3 ClampMagnitude(Vector3 value, float maximum)
    {
        float lengthSquared = value.LengthSquared();
        return lengthSquared > maximum * maximum ? value * (maximum / MathF.Sqrt(lengthSquared)) : value;
    }

    private static Vector3 MoveTowards(Vector3 current, Vector3 target, float maximumDelta)
    {
        var delta = target - current;
        float length = delta.Length();
        return length <= maximumDelta || length <= 1e-6f ? target : current + delta * (maximumDelta / length);
    }

    private static float MoveTowards(float current, float target, float maximumDelta) =>
        current + Math.Clamp(target - current, -maximumDelta, maximumDelta);

    internal static float GetSteeringLookAheadMeters(float forwardSpeedMetersPerSecond,
        float courseErrorMeters = 0)
    {
        float normalLookAhead = Math.Clamp(6 + Math.Max(0, forwardSpeedMetersPerSecond) * 0.45f,
            6, 18);
        // A car already displaced from the course needs a gentler intercept. Pointing a short
        // pure-pursuit target across a five-metre error saturated steering, crossed the line,
        // and created a repeatable side-to-side departure at the same complex corners.
        return Math.Clamp(normalLookAhead + Math.Max(0, courseErrorMeters) * 1.5f,
            normalLookAhead, 30);
    }

    internal static float MoveSteeringAngle(float currentRadians, float targetRadians, float deltaSeconds) =>
        MoveTowards(currentRadians, targetRadians,
            MaximumSteeringRateRadiansPerSecond * Math.Max(0, deltaSeconds));

    internal static Vector3 CalculateSteeringDirection(Vector3 origin, Vector3 targetPosition,
        Vector3 pathForward, float forwardSpeedMetersPerSecond)
    {
        if (pathForward.LengthSquared() < 1e-6f)
            return Vector3.UnitZ;

        pathForward = Vector3.Normalize(pathForward);
        var horizontalPathForward = pathForward with { Y = 0 };
        if (horizontalPathForward.LengthSquared() < 1e-6f)
            horizontalPathForward = Vector3.UnitZ;
        else
            horizontalPathForward = Vector3.Normalize(horizontalPathForward);

        var steeringTarget = targetPosition
                             + horizontalPathForward * GetSteeringLookAheadMeters(forwardSpeedMetersPerSecond);
        return CalculateSteeringDirectionToTarget(origin, steeringTarget, pathForward);
    }

    internal static Vector3 CalculateSteeringDirectionToTarget(Vector3 origin,
        Vector3 steeringTarget, Vector3 pathForward)
    {
        if (pathForward.LengthSquared() < 1e-6f)
            return Vector3.UnitZ;
        pathForward = Vector3.Normalize(pathForward);
        var horizontalDirection = steeringTarget - origin;
        horizontalDirection.Y = 0;
        if (horizontalDirection.LengthSquared() < 1e-6f)
            horizontalDirection = pathForward with { Y = 0 };
        if (horizontalDirection.LengthSquared() < 1e-6f)
            horizontalDirection = Vector3.UnitZ;
        else
            horizontalDirection = Vector3.Normalize(horizontalDirection);

        float vertical = Math.Clamp(pathForward.Y, -0.5f, 0.5f);
        float horizontalScale = MathF.Sqrt(Math.Max(0, 1 - vertical * vertical));
        return Vector3.Normalize(horizontalDirection * horizontalScale + Vector3.UnitY * vertical);
    }

    internal static float CalculateSteeringAngle(Vector3 bodyForward, Vector3 steeringDirection,
        float lookAheadMeters, float wheelbaseMeters)
    {
        bodyForward.Y = 0;
        steeringDirection.Y = 0;
        if (bodyForward.LengthSquared() < 1e-6f || steeringDirection.LengthSquared() < 1e-6f)
            return 0;
        bodyForward = Vector3.Normalize(bodyForward);
        steeringDirection = Vector3.Normalize(steeringDirection);
        float signedSin = Vector3.Cross(bodyForward, steeringDirection).Y;
        float headingError = MathF.Atan2(signedSin, Vector3.Dot(bodyForward, steeringDirection));
        float curvature = 2 * MathF.Sin(headingError) / Math.Max(1, lookAheadMeters);
        return Math.Clamp(MathF.Atan(Math.Max(1, wheelbaseMeters) * curvature),
            -MaximumSteeringAngleRadians, MaximumSteeringAngleRadians);
    }

    internal static float CalculateTargetYawRate(float forwardSpeedMetersPerSecond, float wheelbaseMeters,
        float steeringAngleRadians, float lateralGripG)
    {
        float speed = Math.Max(0, forwardSpeedMetersPerSecond);
        if (speed < 0.5f)
            return 0;
        float requestedYawRate = speed / Math.Max(1, wheelbaseMeters) * MathF.Tan(steeringAngleRadians);
        // Keep part of the lateral tyre budget available to remove existing slip instead of
        // commanding the theoretical cornering limit continuously.
        float gripYawRate = Math.Max(0.4f, lateralGripG) * 9.81f / Math.Max(2, speed) * 0.7f;
        float maximumYawRate = Math.Min(MaximumAngularSpeed, gripYawRate);
        return Math.Clamp(requestedYawRate, -maximumYawRate, maximumYawRate);
    }

    internal static float CalculateSlipStabilizedYawRate(float requestedYawRate,
        float signedSlipAngleRadians, float forwardSpeedMetersPerSecond, float lateralGripG)
    {
        float speed = Math.Max(0, forwardSpeedMetersPerSecond);
        if (speed < 2)
            return requestedYawRate;
        float gripYawRate = Math.Max(0.4f, lateralGripG) * 9.81f / speed * 0.7f;
        float stabilityAuthority = Math.Min(1.2f, Math.Abs(signedSlipAngleRadians) * 2);
        float maximumYawRate = Math.Min(MaximumAngularSpeed, Math.Max(gripYawRate, stabilityAuthority));
        return Math.Clamp(requestedYawRate + signedSlipAngleRadians * 2,
            -maximumYawRate, maximumYawRate);
    }

    internal static Vector3 ApplyLateralGrip(Vector3 velocity, Vector3 bodyForward,
        float lateralGripG, float deltaSeconds)
    {
        var bodyRight = Vector3.Cross(Vector3.UnitY, bodyForward);
        if (bodyRight.LengthSquared() < 1e-6f || deltaSeconds <= 0)
            return velocity;
        bodyRight = Vector3.Normalize(bodyRight);
        float lateralSpeed = Vector3.Dot(velocity, bodyRight);
        float correction = Math.Clamp(lateralSpeed,
            -Math.Max(0, lateralGripG) * 9.81f * deltaSeconds,
            Math.Max(0, lateralGripG) * 9.81f * deltaSeconds);
        return velocity - bodyRight * correction;
    }

    internal static Vector3 CalculateLongitudinalVelocityDelta(Vector3 bodyForward,
        float accelerationMetersPerSecondSquared, float deltaSeconds)
    {
        if (bodyForward.LengthSquared() < 1e-6f || deltaSeconds <= 0)
            return Vector3.Zero;
        return Vector3.Normalize(bodyForward) * accelerationMetersPerSecondSquared * deltaSeconds;
    }

    internal static float CalculateSlipAngleDegrees(Vector3 velocity, Vector3 bodyForward) =>
        Math.Abs(CalculateSignedSlipAngleRadians(velocity, bodyForward)) * 180 / MathF.PI;

    internal static float CalculateSignedSlipAngleRadians(Vector3 velocity, Vector3 bodyForward)
    {
        bodyForward.Y = 0;
        if (bodyForward.LengthSquared() < 1e-6f)
            return 0;
        bodyForward = Vector3.Normalize(bodyForward);
        var horizontalVelocity = velocity with { Y = 0 };
        if (horizontalVelocity.LengthSquared() < 0.25f)
            return 0;
        horizontalVelocity = Vector3.Normalize(horizontalVelocity);
        return MathF.Atan2(Vector3.Cross(bodyForward, horizontalVelocity).Y,
            Vector3.Dot(bodyForward, horizontalVelocity));
    }

    internal static byte EncodeSteeringAngle(float steeringAngleRadians)
    {
        float normalized = Math.Clamp(steeringAngleRadians / MaximumSteeringAngleRadians, -1, 1);
        return (byte)Math.Clamp(MathF.Round(127 + normalized * 127), 0, 254);
    }

    internal static float GetWheelbaseMeters(IReadOnlyList<RaceWheelCollider> wheels)
    {
        if (wheels.Count < 4)
            return 2.5f;
        float frontAxle = (wheels[0].Center.Z + wheels[1].Center.Z) * 0.5f;
        float rearAxle = (wheels[2].Center.Z + wheels[3].Center.Z) * 0.5f;
        float wheelbase = Math.Abs(frontAxle - rearAxle);
        return float.IsFinite(wheelbase) && wheelbase is >= 1 and <= 5 ? wheelbase : 2.5f;
    }

    private ColliderShape CreateVehicleCollider(Vector3[] chassisVertices, RaceWheelCollider[] wheels)
    {
        var hull = new ConvexHull(chassisVertices.AsSpan(), _pool, out var center);
        var unitInertia = hull.ComputeInertia(1);

        return new ColliderShape
        {
            ShapeIndex = _simulation.Shapes.Add(hull),
            Center = center,
            UnitInertia = unitInertia,
            ProtocolReferenceHeight = GetProtocolReferenceHeight(wheels),
            WheelbaseMeters = GetWheelbaseMeters(wheels),
            HalfWidthMeters = GetVehicleHalfWidthMeters(chassisVertices, wheels),
            Wheels = wheels
        };
    }

    internal static float GetVehicleHalfWidthMeters(Vector3[] chassisVertices,
        RaceWheelCollider[] wheels)
    {
        float halfWidth = chassisVertices.Length == 0
            ? 0
            : chassisVertices.Max(vertex => Math.Abs(vertex.X));
        foreach (var wheel in wheels)
            halfWidth = Math.Max(halfWidth, Math.Abs(wheel.Center.X) + wheel.Radius);
        return Math.Clamp(halfWidth, 0.5f, 3f);
    }

    private static RaycastWheelState[] CreateRaycastWheels(ColliderShape collider) =>
        collider.Wheels.Select(wheel => new RaycastWheelState { Geometry = wheel }).ToArray();

    private static void ResetSuspension(BodyRecord record, RaceGridPose pose)
    {
        foreach (var wheel in record.Wheels)
        {
            wheel.HasSurface = false;
            wheel.SurfaceHeight = pose.Position.Y;
            wheel.SurfaceNormal = Vector3.Transform(Vector3.UnitY, pose.Orientation);
            wheel.CompressionMeters = 0;
        }
        record.GroundedWheelCount = 0;
        record.RoadSurfaceHeight = pose.Position.Y;
        record.RoadNormal = Vector3.Transform(Vector3.UnitY, pose.Orientation);
    }

    internal static float GetSuspensionLength(float wheelRadius) => Math.Clamp(wheelRadius * 0.5f, 0.12f, 0.22f);
    internal static float GetSuspensionCompressionLimit(float wheelRadius) =>
        Math.Clamp(wheelRadius * 0.25f, 0.06f, 0.10f);
    internal static float GetSuspensionExtensionLimit(float wheelRadius) =>
        GetSuspensionCompressionLimit(wheelRadius) * 0.5f;

    private static BodyInertia ScaleInertia(BodyInertia unitInertia, float massKg)
    {
        Symmetric3x3.Scale(unitInertia.InverseInertiaTensor, 1 / massKg, out var inverseInertia);
        return new BodyInertia { InverseMass = 1 / massKg, InverseInertiaTensor = inverseInertia };
    }

    private void RemoveBodyUnsafe(byte sessionId)
    {
        if (!_bodies.Remove(sessionId, out var record))
            return;
        _collisionGroups.Remove(record.Handle);
        if (_simulation.Bodies.BodyExists(record.Handle))
            _simulation.Bodies.Remove(record.Handle);
    }

    private StaticHandle? AddTrackMesh(IReadOnlyList<Kn5Triangle> sourceTriangles)
    {
        if (sourceTriangles.Count == 0)
            return null;
        _pool.Take<Triangle>(sourceTriangles.Count, out var triangles);
        for (int i = 0; i < triangles.Length; i++)
        {
            var source = ToBepuTrackTriangle(sourceTriangles[i]);
            triangles[i] = new Triangle(source.A, source.B, source.C);
        }
        var mesh = new Mesh(triangles, Vector3.One, _pool);
        return _simulation.Statics.Add(new StaticDescription(Vector3.Zero, _simulation.Shapes.Add(mesh)));
    }

    private struct TrackRayHitHandler : IRayHitHandler
    {
        private readonly int _trackHandle;
        private readonly float _referenceHeight;
        private readonly float _rayOriginY;
        private float _bestDistance;

        public bool Hit { get; private set; }
        public float Height { get; private set; }
        public Vector3 Normal { get; private set; }

        public TrackRayHitHandler(StaticHandle trackHandle, float referenceHeight, float rayOriginY)
        {
            _trackHandle = trackHandle.Value;
            _referenceHeight = referenceHeight;
            _rayOriginY = rayOriginY;
            _bestDistance = float.PositiveInfinity;
            Hit = false;
            Height = 0;
            Normal = Vector3.UnitY;
        }

        public bool AllowTest(CollidableReference collidable) =>
            collidable.Mobility == CollidableMobility.Static && collidable.StaticHandle.Value == _trackHandle;

        public bool AllowTest(CollidableReference collidable, int childIndex) => true;

        public void OnRayHit(in RayData ray, ref float maximumT, float t, in Vector3 normal,
            CollidableReference collidable, int childIndex)
        {
            float height = _rayOriginY - t;
            float distance = Math.Abs(height - _referenceHeight);
            if (distance >= _bestDistance)
                return;
            _bestDistance = distance;
            Height = height;
            Normal = normal.Y < 0 ? -normal : normal;
            Hit = true;
        }
    }

    internal static Kn5Triangle ToBepuTrackTriangle(Kn5Triangle source)
    {
        // BEPU's plane mesh convention faces the opposite way from KN5's visible winding.
        return new Kn5Triangle(source.A, source.C, source.B);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _simulation.Dispose();
        _dispatcher?.Dispose();
        _pool.Clear();
    }

    private sealed class PhysicsContactMetrics
    {
        public long StaticPairTests;
        public long StaticManifolds;
        public long VehicleManifolds;
        public readonly long[] VehiclePairManifolds = new long[1 << 16];
        private readonly long[] _vehiclePairLastActiveStep = new long[1 << 16];
        private long _currentStep;

        public void BeginStep(long step) => Volatile.Write(ref _currentStep, step);

        public void RecordVehiclePair(byte a, byte b)
        {
            int low = Math.Min(a, b);
            int high = Math.Max(a, b);
            int index = (low << 8) | high;
            Interlocked.Increment(ref VehiclePairManifolds[index]);
            Interlocked.Exchange(ref _vehiclePairLastActiveStep[index],
                Volatile.Read(ref _currentStep));
        }

        public (byte A, byte B, long Count) GetMostFrequentVehiclePair()
        {
            long maximum = 0;
            int maximumIndex = 0;
            for (int i = 0; i < VehiclePairManifolds.Length; i++)
            {
                long count = Interlocked.Read(ref VehiclePairManifolds[i]);
                if (count <= maximum)
                    continue;
                maximum = count;
                maximumIndex = i;
            }
            return ((byte)(maximumIndex >> 8), (byte)maximumIndex, maximum);
        }

        public long GetVehiclePairCount(byte a, byte b)
        {
            int low = Math.Min(a, b);
            int high = Math.Max(a, b);
            return Interlocked.Read(ref VehiclePairManifolds[(low << 8) | high]);
        }

        public bool WasVehiclePairActiveRecently(byte a, byte b, long currentStep,
            long maximumStepAge)
        {
            int low = Math.Min(a, b);
            int high = Math.Max(a, b);
            long activeStep = Interlocked.Read(ref _vehiclePairLastActiveStep[(low << 8) | high]);
            return activeStep > 0 && currentStep >= activeStep
                                  && currentStep - activeStep <= Math.Max(0, maximumStepAge);
        }
    }

    private sealed class PhysicsCollisionGroups
    {
        private readonly Dictionary<int, CollisionBody> _bodies = [];

        public void Assign(BodyHandle handle, byte group, bool isWheel) =>
            _bodies[handle.Value] = new CollisionBody(group, isWheel);
        public void Remove(BodyHandle handle) => _bodies.Remove(handle.Value);

        public bool AllowVehicleContact(BodyHandle a, BodyHandle b)
        {
            if (!_bodies.TryGetValue(a.Value, out var bodyA) || !_bodies.TryGetValue(b.Value, out var bodyB))
                return true;
            return bodyA.Group != bodyB.Group && !bodyA.IsWheel && !bodyB.IsWheel;
        }

        public bool TryGetVehiclePair(BodyHandle a, BodyHandle b, out byte groupA, out byte groupB)
        {
            if (_bodies.TryGetValue(a.Value, out var bodyA)
                && _bodies.TryGetValue(b.Value, out var bodyB)
                && bodyA.Group != bodyB.Group && !bodyA.IsWheel && !bodyB.IsWheel)
            {
                groupA = bodyA.Group;
                groupB = bodyB.Group;
                return true;
            }
            groupA = 0;
            groupB = 0;
            return false;
        }

        private readonly record struct CollisionBody(byte Group, bool IsWheel);
    }

    private sealed class TrackCollisionHandles
    {
        public StaticHandle? Drivable { get; set; }

        public bool IsDrivable(CollidableReference collidable) =>
            Drivable.HasValue && collidable.Mobility == CollidableMobility.Static
                               && collidable.StaticHandle.Value == Drivable.Value.Value;
    }

    private readonly struct NarrowPhaseCallbacks(float friction, PhysicsContactMetrics metrics,
        PhysicsCollisionGroups collisionGroups, TrackCollisionHandles trackCollisionHandles)
        : INarrowPhaseCallbacks
    {
        private readonly float _friction = friction;
        private readonly PhysicsContactMetrics _metrics = metrics;
        private readonly PhysicsCollisionGroups _collisionGroups = collisionGroups;
        private readonly TrackCollisionHandles _trackCollisionHandles = trackCollisionHandles;
        public void Initialize(Simulation simulation) { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b,
            ref float speculativeMargin)
        {
            if (a.Mobility == CollidableMobility.Static || b.Mobility == CollidableMobility.Static)
                Interlocked.Increment(ref _metrics.StaticPairTests);
            // Road support comes from the four raycast suspension contacts. Letting the convex
            // chassis collide with the raw road triangle soup reintroduces seam impulses and
            // competes with the suspension. Barrier meshes remain ordinary rigid contacts.
            if (_trackCollisionHandles.IsDrivable(a) || _trackCollisionHandles.IsDrivable(b))
                return false;
            if (a.Mobility != CollidableMobility.Static && b.Mobility != CollidableMobility.Static
                && !_collisionGroups.AllowVehicleContact(a.BodyHandle, b.BodyHandle))
                return false;
            return a.Mobility == CollidableMobility.Dynamic || b.Mobility == CollidableMobility.Dynamic;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB) => true;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair,
            ref TManifold manifold, out PairMaterialProperties pairMaterial)
            where TManifold : unmanaged, IContactManifold<TManifold>
        {
            if (pair.A.Mobility == CollidableMobility.Static || pair.B.Mobility == CollidableMobility.Static)
                Interlocked.Increment(ref _metrics.StaticManifolds);
            else
            {
                Interlocked.Increment(ref _metrics.VehicleManifolds);
                if (_collisionGroups.TryGetVehiclePair(pair.A.BodyHandle, pair.B.BodyHandle,
                        out var groupA, out var groupB))
                    _metrics.RecordVehiclePair(groupA, groupB);
            }
            // The convex hull is the chassis, not a tyre. Let the race controller supply longitudinal
            // and lateral tyre forces while retaining friction for car-to-car and car-to-human impacts.
            float contactFriction = pair.A.Mobility == CollidableMobility.Static
                                    || pair.B.Mobility == CollidableMobility.Static
                ? 0.02f
                : _friction;
            pairMaterial = new PairMaterialProperties(contactFriction, 2, new SpringSettings(30, 1));
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA,
            int childIndexB, ref ConvexContactManifold manifold) => true;

        public void Dispose() { }
    }

    private struct PoseIntegratorCallbacks(Vector3 gravity) : IPoseIntegratorCallbacks
    {
        private readonly Vector3 _gravity = gravity;
        private Vector3Wide _gravityDt;

        public readonly AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;
        public readonly bool AllowSubstepsForUnconstrainedBodies => false;
        public readonly bool IntegrateVelocityForKinematics => false;
        public void Initialize(Simulation simulation) { }
        public void PrepareForIntegration(float dt) => _gravityDt = Vector3Wide.Broadcast(_gravity * dt);

        public void IntegrateVelocity(Vector<int> bodyIndices, Vector3Wide position, QuaternionWide orientation,
            BodyInertiaWide localInertia, Vector<int> integrationMask, int workerIndex, Vector<float> dt,
            ref BodyVelocityWide velocity)
        {
            velocity.Linear += _gravityDt;
            velocity.Angular *= new Vector<float>(0.995f);
        }
    }
}

internal static class RacePhysicsMath
{
    public static Quaternion FromForward(Vector3 forward)
    {
        forward = Vector3.Normalize(forward);
        return FromProtocolRotation(new Vector3(
            MathF.Atan2(forward.Z, forward.X) - MathF.PI / 2,
            -(MathF.Atan2(new Vector2(forward.Z, forward.X).Length(), forward.Y) - MathF.PI / 2),
            0));
    }

    public static Quaternion FromProtocolRotation(Vector3 rotation) =>
        Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(-rotation.X, -rotation.Y, -rotation.Z));

    public static Vector3 ToProtocolRotation(Quaternion orientation)
    {
        orientation = Quaternion.Normalize(orientation);
        float sinPitch = 2 * (orientation.W * orientation.X - orientation.Y * orientation.Z);
        float pitch = MathF.Asin(Math.Clamp(sinPitch, -1, 1));
        float yaw = MathF.Atan2(2 * (orientation.W * orientation.Y + orientation.X * orientation.Z),
            1 - 2 * (orientation.X * orientation.X + orientation.Y * orientation.Y));
        float roll = MathF.Atan2(2 * (orientation.W * orientation.Z + orientation.X * orientation.Y),
            1 - 2 * (orientation.X * orientation.X + orientation.Z * orientation.Z));
        return new Vector3(-yaw, -pitch, -roll);
    }
}
