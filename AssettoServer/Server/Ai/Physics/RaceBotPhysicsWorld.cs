using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using AssettoServer.Server.Configuration;
using AssettoServer.Server.Configuration.Extra;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;
using BepuUtilities.Memory;
using Serilog;

namespace AssettoServer.Server.Ai.Physics;

public readonly record struct RaceBotPhysicsState(Vector3 Position, Vector3 ProtocolPosition,
    Quaternion Orientation, Vector3 Velocity, float ForwardSpeed, float LongitudinalAcceleration, int RecoveryCount);

public readonly record struct RaceBotPhysicsControl(bool Hold, Vector3 TargetPosition, Vector3 TargetForward,
    float TargetSpeed, float MaximumAcceleration, float MaximumBrakeDeceleration, float LateralGripG);
public readonly record struct RacePhysicsDiagnostics(int BotCount, float MinimumY, float MaximumY, float MaximumSpeed,
    float MaximumUpwardSpeed, float MaximumSplineHeightError, float MaximumSuspensionCompression,
    float MinimumUprightDot, int OverturnedBots, int TotalRecoveries, long StaticPairTests, long StaticManifolds);

public sealed class RaceBotPhysicsWorld : IDisposable
{
    private const float OverturnedUprightDot = 0.25f;
    private const float RecoveryDelaySeconds = 1f;
    private const float MaximumRecoveryHorizontalError = 25f;
    private const float MaximumRecoveryHeightError = 3f;
    private const float ImmediateRecoveryHeightError = 1.25f;
    private const float ImmediateRecoveryExcessUpwardSpeed = 3f;
    private const float MaximumAngularSpeed = 2.5f;
    private const float MaximumAngularAcceleration = 16f;
    private const float AttitudeGain = 4f;
    private const float DownforceCoefficient = 0.0035f;
    private const float MaximumDownforceAcceleration = 6f;

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
    private readonly SpringSettings _suspensionSettings;
    private bool _disposed;

    private sealed class WheelShape
    {
        public required RaceWheelCollider Geometry { get; init; }
        public required TypedIndex ShapeIndex { get; init; }
        public required BodyInertia UnitInertia { get; init; }
    }

    private sealed class ColliderShape
    {
        public required TypedIndex ShapeIndex { get; init; }
        public required Vector3 Center { get; init; }
        public required BodyInertia UnitInertia { get; init; }
        public required float ProtocolReferenceHeight { get; init; }
        public required float MaximumSuspensionCompression { get; init; }
        public required WheelShape[] Wheels { get; init; }
    }

    private sealed class WheelBody
    {
        public required RaceWheelCollider Geometry { get; init; }
        public required BodyHandle Handle { get; init; }
        public required BodyInertia DynamicInertia { get; init; }
    }

    private sealed class BodyRecord
    {
        public required byte SessionId { get; init; }
        public required string Model { get; init; }
        public required BodyHandle Handle { get; init; }
        public required ColliderShape Collider { get; init; }
        public required BodyInertia DynamicInertia { get; init; }
        public required WheelBody[] Wheels { get; init; }
        public bool IsBot { get; set; }
        public bool IsHeld { get; set; }
        public RaceGridPose HeldPose { get; set; }
        public RaceBotPhysicsControl Control { get; set; }
        public float LastLongitudinalAcceleration { get; set; }
        public float RecoveryNeededSeconds { get; set; }
        public float MaximumUpwardSpeed { get; set; }
        public float MaximumSplineHeightError { get; set; }
        public float TrackHeightOffset { get; set; }
        public bool HasTrackHeightOffset { get; set; }
        public int RecoveryCount { get; set; }
    }

    public int GridCount => _asset.Grid.Count;

    public RaceBotPhysicsWorld(ACServerConfiguration configuration)
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
        // Fidelity controls numerical resolution, not the suspension tune. Changing spring frequency
        // with fidelity made light cars harsher and less stable at otherwise higher quality settings.
        _suspensionSettings = new SpringSettings(5, 0.8f);
        _botContinuousDetection = physics.Fidelity == RacePhysicsFidelity.High
            ? ContinuousDetection.Continuous(1e-3f, 1e-2f)
            : ContinuousDetection.Passive;
        _simulation = Simulation.Create(_pool,
            new NarrowPhaseCallbacks(physics.Friction, _contactMetrics, _collisionGroups),
            new PoseIntegratorCallbacks(new Vector3(0, -9.81f, 0)), solver);
        if (physics.Fidelity != RacePhysicsFidelity.Efficient && Environment.ProcessorCount > 2)
            _dispatcher = new ThreadDispatcher(Math.Min(8, Environment.ProcessorCount - 1));

        AddTrackMesh(_asset.TrackTriangles);
        AddTrackMesh(_asset.TrackBarrierTriangles);
        foreach (var (model, vertices) in _asset.CarColliderVertices)
        {
            var collider = CreateVehicleCollider(vertices, _asset.CarWheelColliders[model]);
            _colliders.Add(model, collider);
            Log.Debug("Race collider {Model}: AC protocol reference height {Height:F3} m", model,
                collider.ProtocolReferenceHeight);
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

    public void RegisterBot(byte sessionId, string model, RaceGridPose pose, float massKg)
    {
        lock (_sync)
        {
            RemoveBodyUnsafe(sessionId);
            var collider = GetCollider(model);
            float wheelMass = Math.Max(5, massKg * 0.015f);
            var inertia = ScaleInertia(collider.UnitInertia, Math.Max(1, massKg - wheelMass * collider.Wheels.Length));
            var bodyPose = ToCenterOfMassPose(pose, collider.Center);
            var collidable = new CollidableDescription(collider.ShapeIndex, 0.25f, _botContinuousDetection);
            var handle = _simulation.Bodies.Add(BodyDescription.CreateDynamic(bodyPose, inertia, collidable,
                new BodyActivityDescription(0.01f)));
            _collisionGroups.Assign(handle, sessionId, isWheel: false);
            var wheels = CreateSuspendedWheels(sessionId, pose, handle, collider, wheelMass);
            _bodies.Add(sessionId, new BodyRecord
            {
                SessionId = sessionId,
                Model = model,
                Handle = handle,
                Collider = collider,
                DynamicInertia = inertia,
                Wheels = wheels,
                IsBot = true,
                HeldPose = pose
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
            foreach (var record in _bodies.Values.Where(x => x.IsBot))
                ApplyControl(record, deltaSeconds);
            _simulation.Timestep(deltaSeconds, _dispatcher);
            foreach (var record in _bodies.Values.Where(x => x is { IsBot: true, IsHeld: true }))
                HoldAtGrid(record);
            foreach (var record in _bodies.Values.Where(x => x is { IsBot: true, IsHeld: false }))
                EnforceSuspensionTravel(record);
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
            var supportedOrigin = GetWheelSupportedOrigin(record, chassisOrigin, body.Pose.Orientation);
            var supportedVelocity = GetWheelSupportedVelocity(record, body.Velocity.Linear);
            var protocolPosition = ToProtocolPosition(supportedOrigin, body.Pose.Orientation,
                record.Collider.ProtocolReferenceHeight);
            var forward = Vector3.Transform(Vector3.UnitZ, body.Pose.Orientation);
            state = new RaceBotPhysicsState(supportedOrigin, protocolPosition, body.Pose.Orientation,
                supportedVelocity,
                Vector3.Dot(body.Velocity.Linear, forward), record.LastLongitudinalAcceleration,
                record.RecoveryCount);
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
            float maxUpwardSpeed = 0;
            float maxSplineHeightError = 0;
            float maxSuspensionCompression = 0;
            float minUprightDot = 1;
            int overturnedBots = 0;
            int totalRecoveries = 0;
            foreach (var record in _bodies.Values.Where(x => x.IsBot))
            {
                var body = _simulation.Bodies[record.Handle];
                var chassisOrigin = GetChassisOrigin(record, body);
                var origin = GetWheelSupportedOrigin(record, chassisOrigin, body.Pose.Orientation);
                float uprightDot = GetUprightDot(body.Pose.Orientation);
                minY = Math.Min(minY, origin.Y);
                maxY = Math.Max(maxY, origin.Y);
                maxSpeed = Math.Max(maxSpeed, body.Velocity.Linear.Length());
                maxUpwardSpeed = Math.Max(maxUpwardSpeed, record.MaximumUpwardSpeed);
                maxSplineHeightError = Math.Max(maxSplineHeightError, record.MaximumSplineHeightError);
                maxSuspensionCompression = Math.Max(maxSuspensionCompression,
                    GetSuspensionCompression(chassisOrigin, origin, body.Pose.Orientation));
                minUprightDot = Math.Min(minUprightDot, uprightDot);
                if (uprightDot < OverturnedUprightDot)
                    overturnedBots++;
                totalRecoveries += record.RecoveryCount;
                count++;
            }
            return new RacePhysicsDiagnostics(count, count == 0 ? 0 : minY, count == 0 ? 0 : maxY, maxSpeed,
                maxUpwardSpeed, maxSplineHeightError, maxSuspensionCompression,
                count == 0 ? 1 : minUprightDot, overturnedBots, totalRecoveries,
                Interlocked.Read(ref _contactMetrics.StaticPairTests),
                Interlocked.Read(ref _contactMetrics.StaticManifolds));
        }
    }

    private void CaptureMaximums(BodyRecord record)
    {
        var body = _simulation.Bodies[record.Handle];
        var chassisOrigin = GetChassisOrigin(record, body);
        var origin = GetWheelSupportedOrigin(record, chassisOrigin, body.Pose.Orientation);
        var supportedVelocity = GetWheelSupportedVelocity(record, body.Velocity.Linear);
        UpdateTrackHeightOffset(record, origin, supportedVelocity, body.Pose.Orientation);
        var physicalTarget = GetTrackContactTarget(record, record.Control, origin);
        record.MaximumUpwardSpeed = Math.Max(record.MaximumUpwardSpeed, supportedVelocity.Y);
        record.MaximumSplineHeightError = Math.Max(record.MaximumSplineHeightError,
            Math.Abs(origin.Y - physicalTarget.Y));
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
            return;
        }

        if (record.IsHeld)
        {
            record.IsHeld = false;
            body.SetLocalInertia(record.DynamicInertia);
            foreach (var wheel in record.Wheels)
                _simulation.Bodies[wheel.Handle].SetLocalInertia(wheel.DynamicInertia);
            body.Awake = true;
        }

        var orientation = body.Pose.Orientation;
        var chassisOrigin = GetChassisOrigin(record, body);
        var origin = GetWheelSupportedOrigin(record, chassisOrigin, orientation);
        var targetForward = Vector3.Normalize(control.TargetForward);
        var physicalTarget = GetTrackContactTarget(record, control, origin);
        float uprightDot = GetUprightDot(orientation);
        if (NeedsRecovery(uprightDot, origin, physicalTarget))
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
        var guidedForward = Vector3.Normalize(targetForward + Vector3.Clamp(lineError * 0.2f,
            new Vector3(-0.5f), new Vector3(0.5f)));

        float forwardSpeed = Vector3.Dot(body.Velocity.Linear, guidedForward);
        float uprightDriveScale = GetDriveScale(uprightDot);
        float courseDriveScale = GetCourseDriveScale(lineError.Length(),
            Math.Abs(origin.Y - physicalTarget.Y));
        float driveScale = uprightDriveScale * courseDriveScale;
        float speedError = control.TargetSpeed * driveScale - forwardSpeed;
        float acceleration = Math.Clamp(speedError / Math.Max(deltaSeconds, 1e-3f),
            -control.MaximumBrakeDeceleration, control.MaximumAcceleration);
        body.Velocity.Linear += guidedForward * (acceleration * deltaSeconds);
        record.LastLongitudinalAcceleration = acceleration;

        var verticalVelocity = Vector3.UnitY * Vector3.Dot(body.Velocity.Linear, Vector3.UnitY);
        var longitudinalVelocity = guidedForward * Vector3.Dot(body.Velocity.Linear, guidedForward);
        var lateralVelocity = body.Velocity.Linear - verticalVelocity - longitudinalVelocity;
        float maximumLateralCorrection = Math.Max(0.4f, control.LateralGripG) * 9.81f * deltaSeconds
                                         * uprightDriveScale;
        body.Velocity.Linear -= Vector3.Clamp(lateralVelocity, new Vector3(-maximumLateralCorrection),
            new Vector3(maximumLateralCorrection));

        float downforceAcceleration = Math.Min(MaximumDownforceAcceleration,
            Math.Max(0, forwardSpeed) * Math.Max(0, forwardSpeed) * DownforceCoefficient);
        body.Velocity.Linear.Y -= downforceAcceleration * deltaSeconds;
        body.Velocity.Angular = CalculateStabilizedAngularVelocity(orientation, targetForward,
            body.Velocity.Angular, deltaSeconds);
        body.Awake = true;
    }

    private void RecoverTransientLaunch(BodyRecord record)
    {
        var body = _simulation.Bodies[record.Handle];
        var control = record.Control;
        var chassisOrigin = GetChassisOrigin(record, body);
        var origin = GetWheelSupportedOrigin(record, chassisOrigin, body.Pose.Orientation);
        var supportedVelocity = GetWheelSupportedVelocity(record, body.Velocity.Linear);
        UpdateTrackHeightOffset(record, origin, supportedVelocity, body.Pose.Orientation);
        var physicalTarget = GetTrackContactTarget(record, control, origin);
        if (NeedsImmediateRecovery(GetUprightDot(body.Pose.Orientation), origin, physicalTarget,
                supportedVelocity, control.TargetForward))
            RecoverBot(record, control);
    }

    private void EnforceSuspensionTravel(BodyRecord record)
    {
        var body = _simulation.Bodies[record.Handle];
        var chassisOrigin = GetChassisOrigin(record, body);
        var supportedOrigin = GetWheelSupportedOrigin(record, chassisOrigin, body.Pose.Orientation);
        float correction = GetSuspensionCompressionCorrection(chassisOrigin, supportedOrigin,
            body.Pose.Orientation, record.Collider.MaximumSuspensionCompression);
        if (correction <= 0)
            return;
        var bodyUp = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, body.Pose.Orientation));
        body.Pose.Position += bodyUp * correction;
        var supportedVelocity = GetWheelSupportedVelocity(record, body.Velocity.Linear);
        float closingSpeed = Vector3.Dot(supportedVelocity - body.Velocity.Linear, bodyUp);
        if (closingSpeed > 0)
            body.Velocity.Linear += bodyUp * closingSpeed;
        body.Awake = true;
    }

    private void RecoverBot(BodyRecord record, RaceBotPhysicsControl control)
    {
        var body = _simulation.Bodies[record.Handle];
        var chassisOrigin = GetChassisOrigin(record, body);
        var origin = GetWheelSupportedOrigin(record, chassisOrigin, body.Pose.Orientation);
        var recoveryPose = CreateRecoveryPose(GetTrackContactTarget(record, control, origin),
            control.TargetForward);
        body.Pose = ToCenterOfMassPose(recoveryPose, record.Collider.Center);
        body.Velocity.Linear = Vector3.Normalize(control.TargetForward) * Math.Min(Math.Max(0, control.TargetSpeed), 5f);
        body.Velocity.Angular = Vector3.Zero;
        PositionWheels(record, recoveryPose, body.Velocity.Linear, dynamic: true);
        body.Awake = true;
        record.LastLongitudinalAcceleration = 0;
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
        PositionWheels(record, record.HeldPose, Vector3.Zero, dynamic: false);
        body.Awake = true;
    }

    private ColliderShape GetCollider(string model) => _colliders.TryGetValue(model, out var collider)
        ? collider
        : throw new ConfigurationException($"Prepared race physics asset has no collider for car model: {model}");

    private static RigidPose ToCenterOfMassPose(RaceGridPose originPose, Vector3 localCenter) =>
        new(originPose.Position + Vector3.Transform(localCenter, originPose.Orientation), originPose.Orientation);

    private static Vector3 GetChassisOrigin(BodyRecord record, BodyReference body) =>
        body.Pose.Position - Vector3.Transform(record.Collider.Center, body.Pose.Orientation);

    private Vector3 GetWheelSupportedOrigin(BodyRecord record, Vector3 chassisOrigin, Quaternion orientation)
    {
        if (record.Wheels.Length == 0)
            return chassisOrigin;
        float supportedHeight = 0;
        foreach (var wheel in record.Wheels)
            supportedHeight += GetWheelOriginSample(wheel.Geometry,
                _simulation.Bodies[wheel.Handle].Pose.Position, orientation).Y;
        chassisOrigin.Y = supportedHeight / record.Wheels.Length;
        return chassisOrigin;
    }

    private Vector3 GetWheelSupportedVelocity(BodyRecord record, Vector3 chassisVelocity)
    {
        if (record.Wheels.Length == 0)
            return chassisVelocity;
        float supportedVerticalVelocity = 0;
        foreach (var wheel in record.Wheels)
            supportedVerticalVelocity += _simulation.Bodies[wheel.Handle].Velocity.Linear.Y;
        chassisVelocity.Y = supportedVerticalVelocity / record.Wheels.Length;
        return chassisVelocity;
    }

    private static Vector3 GetTrackContactTarget(BodyRecord record, RaceBotPhysicsControl control,
        Vector3 fallbackOrigin)
    {
        var target = control.TargetPosition;
        target.Y = record.HasTrackHeightOffset
            ? control.TargetPosition.Y + record.TrackHeightOffset
            : fallbackOrigin.Y;
        return target;
    }

    private static void UpdateTrackHeightOffset(BodyRecord record, Vector3 supportedOrigin,
        Vector3 supportedVelocity, Quaternion orientation)
    {
        if (record.Control.TargetForward.LengthSquared() < 1e-6f || GetUprightDot(orientation) < 0.9f)
            return;
        if (Math.Abs(supportedVelocity.Y) > 1.5f)
            return;
        record.TrackHeightOffset = supportedOrigin.Y - record.Control.TargetPosition.Y;
        record.HasTrackHeightOffset = true;
    }

    internal static Vector3 GetWheelOriginSample(RaceWheelCollider wheel, Vector3 wheelPosition,
        Quaternion orientation) => wheelPosition - Vector3.Transform(wheel.Center, orientation);

    internal static float GetSuspensionCompressionCorrection(Vector3 chassisOrigin, Vector3 supportedOrigin,
        Quaternion orientation, float maximumCompression)
        => Math.Max(0, GetSuspensionCompression(chassisOrigin, supportedOrigin, orientation) - maximumCompression);

    internal static float GetSuspensionCompression(Vector3 chassisOrigin, Vector3 supportedOrigin,
        Quaternion orientation)
    {
        var bodyUp = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, orientation));
        return Math.Max(0, Vector3.Dot(supportedOrigin - chassisOrigin, bodyUp));
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

    internal static bool NeedsRecovery(float uprightDot, Vector3 physicalOrigin, Vector3 targetPosition)
    {
        var horizontalError = physicalOrigin - targetPosition;
        horizontalError.Y = 0;
        return uprightDot < OverturnedUprightDot
               || horizontalError.LengthSquared() > MaximumRecoveryHorizontalError * MaximumRecoveryHorizontalError
               || Math.Abs(physicalOrigin.Y - targetPosition.Y) > MaximumRecoveryHeightError;
    }

    internal static bool NeedsImmediateRecovery(float uprightDot, Vector3 physicalOrigin, Vector3 physicalTarget,
        Vector3 velocity, Vector3 targetForward)
    {
        if (uprightDot < 0 || Math.Abs(physicalOrigin.Y - physicalTarget.Y) > ImmediateRecoveryHeightError)
            return true;
        if (targetForward.LengthSquared() < 1e-6f)
            return false;
        targetForward = Vector3.Normalize(targetForward);
        float expectedVerticalSpeed = targetForward.Y * Math.Max(0, Vector3.Dot(velocity, targetForward));
        return velocity.Y - expectedVerticalSpeed > ImmediateRecoveryExcessUpwardSpeed;
    }

    internal static Vector3 CalculateStabilizedAngularVelocity(Quaternion orientation, Vector3 targetForward,
        Vector3 angularVelocity, float deltaSeconds)
    {
        targetForward = Vector3.Normalize(targetForward);
        var targetRight = Vector3.Cross(Vector3.UnitY, targetForward);
        if (targetRight.LengthSquared() < 1e-6f)
            return MoveTowards(angularVelocity, Vector3.Zero, MaximumAngularAcceleration * deltaSeconds);
        targetRight = Vector3.Normalize(targetRight);
        var targetUp = Vector3.Normalize(Vector3.Cross(targetForward, targetRight));
        var actualForward = Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, orientation));
        var actualUp = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, orientation));
        var desired = (Vector3.Cross(actualForward, targetForward) + Vector3.Cross(actualUp, targetUp))
                      * AttitudeGain;
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

    private ColliderShape CreateVehicleCollider(Vector3[] chassisVertices, RaceWheelCollider[] wheels)
    {
        var hull = new ConvexHull(chassisVertices.AsSpan(), _pool, out var center);
        var unitInertia = hull.ComputeInertia(1);
        var wheelShapes = wheels.Select(wheel =>
        {
            var sphere = new BepuPhysics.Collidables.Sphere(wheel.Radius);
            return new WheelShape
            {
                Geometry = wheel,
                ShapeIndex = _simulation.Shapes.Add(sphere),
                UnitInertia = sphere.ComputeInertia(1)
            };
        }).ToArray();

        return new ColliderShape
        {
            ShapeIndex = _simulation.Shapes.Add(hull),
            Center = center,
            UnitInertia = unitInertia,
            ProtocolReferenceHeight = GetProtocolReferenceHeight(wheels),
            MaximumSuspensionCompression = wheels.Average(wheel =>
                GetSuspensionCompressionLimit(wheel.Radius)),
            Wheels = wheelShapes
        };
    }

    private WheelBody[] CreateSuspendedWheels(byte sessionId, RaceGridPose pose, BodyHandle chassisHandle,
        ColliderShape collider, float wheelMass)
    {
        var wheels = new WheelBody[collider.Wheels.Length];
        for (int i = 0; i < collider.Wheels.Length; i++)
        {
            var wheel = collider.Wheels[i];
            float suspensionLength = GetSuspensionLength(wheel.Geometry.Radius);
            var worldCenter = pose.Position + Vector3.Transform(wheel.Geometry.Center, pose.Orientation);
            var wheelInertia = ScaleInertia(wheel.UnitInertia, wheelMass);
            var wheelHandle = _simulation.Bodies.Add(BodyDescription.CreateDynamic(
                new RigidPose(worldCenter, pose.Orientation), wheelInertia,
                // Wheels are small enough to cross more than their radius in one 60 Hz step.
                // Always sweep them against the one-sided track mesh; chassis CCD still scales with fidelity.
                new CollidableDescription(wheel.ShapeIndex, 0.15f,
                    ContinuousDetection.Continuous(1e-3f, 1e-2f)),
                new BodyActivityDescription(0.01f)));
            _collisionGroups.Assign(wheelHandle, sessionId, isWheel: true);

            var suspensionDirection = -Vector3.UnitY;
            var suspensionAnchor = wheel.Geometry.Center + Vector3.UnitY * suspensionLength - collider.Center;
            _simulation.Solver.Add(chassisHandle, wheelHandle, new LinearAxisServo
            {
                LocalPlaneNormal = suspensionDirection,
                TargetOffset = suspensionLength,
                LocalOffsetA = suspensionAnchor,
                LocalOffsetB = default,
                ServoSettings = ServoSettings.Default,
                SpringSettings = _suspensionSettings
            });
            float compressionLimit = GetSuspensionCompressionLimit(wheel.Geometry.Radius);
            float extensionLimit = GetSuspensionExtensionLimit(wheel.Geometry.Radius);
            _simulation.Solver.Add(chassisHandle, wheelHandle, new LinearAxisLimit
            {
                LocalAxis = suspensionDirection,
                LocalOffsetA = suspensionAnchor,
                LocalOffsetB = default,
                MinimumOffset = suspensionLength - compressionLimit,
                MaximumOffset = suspensionLength + extensionLimit,
                SpringSettings = new SpringSettings(30, 1)
            });
            _simulation.Solver.Add(chassisHandle, wheelHandle, new PointOnLineServo
            {
                LocalDirection = suspensionDirection,
                LocalOffsetA = suspensionAnchor,
                LocalOffsetB = default,
                ServoSettings = ServoSettings.Default,
                SpringSettings = new SpringSettings(30, 1)
            });
            wheels[i] = new WheelBody
            {
                Geometry = wheel.Geometry,
                Handle = wheelHandle,
                DynamicInertia = wheelInertia
            };
        }
        return wheels;
    }

    private void PositionWheels(BodyRecord record, RaceGridPose pose, Vector3 velocity, bool dynamic)
    {
        foreach (var wheel in record.Wheels)
        {
            var body = _simulation.Bodies[wheel.Handle];
            body.SetLocalInertia(dynamic ? wheel.DynamicInertia : default);
            body.Pose = new RigidPose(
                pose.Position + Vector3.Transform(wheel.Geometry.Center, pose.Orientation), pose.Orientation);
            body.Velocity.Linear = velocity;
            body.Velocity.Angular = Vector3.Zero;
            body.Awake = true;
        }
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
        foreach (var wheel in record.Wheels)
        {
            _collisionGroups.Remove(wheel.Handle);
            if (_simulation.Bodies.BodyExists(wheel.Handle))
                _simulation.Bodies.Remove(wheel.Handle);
        }
    }

    private void AddTrackMesh(IReadOnlyList<Kn5Triangle> sourceTriangles)
    {
        if (sourceTriangles.Count == 0)
            return;
        _pool.Take<Triangle>(sourceTriangles.Count, out var triangles);
        for (int i = 0; i < triangles.Length; i++)
        {
            var source = ToBepuTrackTriangle(sourceTriangles[i]);
            triangles[i] = new Triangle(source.A, source.B, source.C);
        }
        var mesh = new Mesh(triangles, Vector3.One, _pool);
        _simulation.Statics.Add(new StaticDescription(Vector3.Zero, _simulation.Shapes.Add(mesh)));
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

        private readonly record struct CollisionBody(byte Group, bool IsWheel);
    }

    private readonly struct NarrowPhaseCallbacks(float friction, PhysicsContactMetrics metrics,
        PhysicsCollisionGroups collisionGroups) : INarrowPhaseCallbacks
    {
        private readonly float _friction = friction;
        private readonly PhysicsContactMetrics _metrics = metrics;
        private readonly PhysicsCollisionGroups _collisionGroups = collisionGroups;
        public void Initialize(Simulation simulation) { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b,
            ref float speculativeMargin)
        {
            if (a.Mobility == CollidableMobility.Static || b.Mobility == CollidableMobility.Static)
                Interlocked.Increment(ref _metrics.StaticPairTests);
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
