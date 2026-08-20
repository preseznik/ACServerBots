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
    float MaximumSplineHeightError, float MinimumUprightDot, int OverturnedBots, int TotalRecoveries,
    long StaticPairTests, long StaticManifolds);

public sealed class RaceBotPhysicsWorld : IDisposable
{
    private const float OverturnedUprightDot = 0.25f;
    private const float RecoveryDelaySeconds = 1f;
    private const float MaximumRecoveryHorizontalError = 25f;
    private const float MaximumRecoveryHeightError = 3f;
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
    private bool _disposed;

    private sealed class ColliderShape
    {
        public required TypedIndex ShapeIndex { get; init; }
        public required Vector3 Center { get; init; }
        public required BodyInertia UnitInertia { get; init; }
        public required float ProtocolReferenceHeight { get; init; }
    }

    private sealed class BodyRecord
    {
        public required byte SessionId { get; init; }
        public required string Model { get; init; }
        public required BodyHandle Handle { get; init; }
        public required ColliderShape Collider { get; init; }
        public required BodyInertia DynamicInertia { get; init; }
        public bool IsBot { get; set; }
        public bool IsHeld { get; set; }
        public RaceGridPose HeldPose { get; set; }
        public RaceBotPhysicsControl Control { get; set; }
        public float LastLongitudinalAcceleration { get; set; }
        public float RecoveryNeededSeconds { get; set; }
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
        _botContinuousDetection = physics.Fidelity == RacePhysicsFidelity.High
            ? ContinuousDetection.Continuous(1e-3f, 1e-2f)
            : ContinuousDetection.Passive;
        _simulation = Simulation.Create(_pool, new NarrowPhaseCallbacks(physics.Friction, _contactMetrics),
            new PoseIntegratorCallbacks(new Vector3(0, -9.81f, 0)), solver);
        if (physics.Fidelity != RacePhysicsFidelity.Efficient && Environment.ProcessorCount > 2)
            _dispatcher = new ThreadDispatcher(Math.Min(8, Environment.ProcessorCount - 1));

        AddTrackMesh();
        foreach (var (model, vertices) in _asset.CarColliderVertices)
        {
            var collider = CreateVehicleCollider(vertices, _asset.CarWheelColliders[model]);
            _colliders.Add(model, collider);
            Log.Debug("Race collider {Model}: AC protocol reference height {Height:F3} m", model,
                collider.ProtocolReferenceHeight);
        }

        Log.Information("Rigid-body race world loaded: {Triangles} track triangles, {GridSlots} grid slots, "
                        + "{Colliders} car colliders, fidelity {Fidelity}", _asset.TrackTriangles.Count,
            _asset.Grid.Count, _colliders.Count, physics.Fidelity);
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
            var inertia = ScaleInertia(collider.UnitInertia, massKg);
            var bodyPose = ToCenterOfMassPose(pose, collider.Center);
            var collidable = new CollidableDescription(collider.ShapeIndex, 0.25f, _botContinuousDetection);
            var handle = _simulation.Bodies.Add(BodyDescription.CreateDynamic(bodyPose, inertia, collidable,
                new BodyActivityDescription(0.01f)));
            _bodies.Add(sessionId, new BodyRecord
            {
                SessionId = sessionId,
                Model = model,
                Handle = handle,
                Collider = collider,
                DynamicInertia = inertia,
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
                    IsBot = false
                };
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
            var origin = body.Pose.Position - Vector3.Transform(record.Collider.Center, body.Pose.Orientation);
            var protocolPosition = ToProtocolPosition(origin, body.Pose.Orientation,
                record.Collider.ProtocolReferenceHeight);
            var forward = Vector3.Transform(Vector3.UnitZ, body.Pose.Orientation);
            state = new RaceBotPhysicsState(origin, protocolPosition, body.Pose.Orientation, body.Velocity.Linear,
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
            float maxSplineHeightError = 0;
            float minUprightDot = 1;
            int overturnedBots = 0;
            int totalRecoveries = 0;
            foreach (var record in _bodies.Values.Where(x => x.IsBot))
            {
                var body = _simulation.Bodies[record.Handle];
                var origin = body.Pose.Position - Vector3.Transform(record.Collider.Center, body.Pose.Orientation);
                float uprightDot = GetUprightDot(body.Pose.Orientation);
                minY = Math.Min(minY, origin.Y);
                maxY = Math.Max(maxY, origin.Y);
                maxSpeed = Math.Max(maxSpeed, body.Velocity.Linear.Length());
                maxSplineHeightError = Math.Max(maxSplineHeightError,
                    Math.Abs(origin.Y - record.Control.TargetPosition.Y));
                minUprightDot = Math.Min(minUprightDot, uprightDot);
                if (uprightDot < OverturnedUprightDot)
                    overturnedBots++;
                totalRecoveries += record.RecoveryCount;
                count++;
            }
            return new RacePhysicsDiagnostics(count, count == 0 ? 0 : minY, count == 0 ? 0 : maxY, maxSpeed,
                maxSplineHeightError, count == 0 ? 1 : minUprightDot, overturnedBots, totalRecoveries,
                Interlocked.Read(ref _contactMetrics.StaticPairTests),
                Interlocked.Read(ref _contactMetrics.StaticManifolds));
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
            return;
        }

        if (record.IsHeld)
        {
            record.IsHeld = false;
            body.SetLocalInertia(record.DynamicInertia);
            body.Awake = true;
        }

        var orientation = body.Pose.Orientation;
        var origin = body.Pose.Position - Vector3.Transform(record.Collider.Center, orientation);
        var targetForward = Vector3.Normalize(control.TargetForward);
        float uprightDot = GetUprightDot(orientation);
        if (NeedsRecovery(uprightDot, origin, control.TargetPosition))
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
            Math.Abs(origin.Y - control.TargetPosition.Y));
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

    private void RecoverBot(BodyRecord record, RaceBotPhysicsControl control)
    {
        var body = _simulation.Bodies[record.Handle];
        var recoveryPose = CreateRecoveryPose(control.TargetPosition, control.TargetForward,
            record.Collider.ProtocolReferenceHeight);
        body.Pose = ToCenterOfMassPose(recoveryPose, record.Collider.Center);
        body.Velocity.Linear = Vector3.Normalize(control.TargetForward) * Math.Min(Math.Max(0, control.TargetSpeed), 5f);
        body.Velocity.Angular = Vector3.Zero;
        body.Awake = true;
        record.LastLongitudinalAcceleration = 0;
        record.RecoveryNeededSeconds = 0;
        record.RecoveryCount++;
        Log.Warning("Recovered overturned/off-track race bot {SessionId} ({Model}) at spline target; recovery {RecoveryCount}",
            record.SessionId, record.Model, record.RecoveryCount);
    }

    private void HoldAtGrid(BodyRecord record)
    {
        var body = _simulation.Bodies[record.Handle];
        body.Pose = ToCenterOfMassPose(record.HeldPose, record.Collider.Center);
        body.Velocity.Linear = Vector3.Zero;
        body.Velocity.Angular = Vector3.Zero;
        body.Awake = true;
    }

    private ColliderShape GetCollider(string model) => _colliders.TryGetValue(model, out var collider)
        ? collider
        : throw new ConfigurationException($"Prepared race physics asset has no collider for car model: {model}");

    private static RigidPose ToCenterOfMassPose(RaceGridPose originPose, Vector3 localCenter) =>
        new(originPose.Position + Vector3.Transform(localCenter, originPose.Orientation), originPose.Orientation);

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

    internal static RaceGridPose CreateRecoveryPose(Vector3 protocolTargetPosition, Vector3 targetForward,
        float protocolReferenceHeight)
    {
        var orientation = RacePhysicsMath.FromForward(targetForward);
        var physicalOrigin = FromProtocolPosition(protocolTargetPosition, orientation, protocolReferenceHeight);
        return new RaceGridPose(physicalOrigin, orientation);
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
        var chassisShapeIndex = _simulation.Shapes.Add(hull);

        _pool.Take<CompoundChild>(wheels.Length + 1, out var children);
        children[0] = new CompoundChild
        {
            ShapeIndex = chassisShapeIndex,
            LocalPose = new RigidPose(Vector3.Zero, Quaternion.Identity)
        };
        for (int i = 0; i < wheels.Length; i++)
        {
            var wheel = wheels[i];
            children[i + 1] = new CompoundChild
            {
                ShapeIndex = _simulation.Shapes.Add(new BepuPhysics.Collidables.Sphere(wheel.Radius)),
                LocalPose = new RigidPose(wheel.Center - center, Quaternion.Identity)
            };
        }

        return new ColliderShape
        {
            ShapeIndex = _simulation.Shapes.Add(new Compound(children)),
            Center = center,
            UnitInertia = unitInertia,
            ProtocolReferenceHeight = GetProtocolReferenceHeight(wheels)
        };
    }

    private static BodyInertia ScaleInertia(BodyInertia unitInertia, float massKg)
    {
        Symmetric3x3.Scale(unitInertia.InverseInertiaTensor, 1 / massKg, out var inverseInertia);
        return new BodyInertia { InverseMass = 1 / massKg, InverseInertiaTensor = inverseInertia };
    }

    private void RemoveBodyUnsafe(byte sessionId)
    {
        if (_bodies.Remove(sessionId, out var record) && _simulation.Bodies.BodyExists(record.Handle))
            _simulation.Bodies.Remove(record.Handle);
    }

    private void AddTrackMesh()
    {
        _pool.Take<Triangle>(_asset.TrackTriangles.Count, out var triangles);
        for (int i = 0; i < triangles.Length; i++)
        {
            var source = ToBepuTrackTriangle(_asset.TrackTriangles[i]);
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

    private readonly struct NarrowPhaseCallbacks(float friction, PhysicsContactMetrics metrics) : INarrowPhaseCallbacks
    {
        private readonly float _friction = friction;
        private readonly PhysicsContactMetrics _metrics = metrics;
        public void Initialize(Simulation simulation) { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b,
            ref float speculativeMargin)
        {
            if (a.Mobility == CollidableMobility.Static || b.Mobility == CollidableMobility.Static)
                Interlocked.Increment(ref _metrics.StaticPairTests);
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
            pairMaterial = new PairMaterialProperties(contactFriction, 3, new SpringSettings(30, 1));
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
