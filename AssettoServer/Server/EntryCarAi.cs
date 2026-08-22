using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;
using AssettoServer.Server.Ai;
using AssettoServer.Server.Ai.Physics;
using AssettoServer.Server.Ai.Splines;
using AssettoServer.Server.Configuration.Extra;
using AssettoServer.Shared.Model;
using AssettoServer.Shared.Network.Packets.Outgoing;

namespace AssettoServer.Server;

public enum AiMode
{
    None,
    Auto,
    Fixed
}

public enum RaceControlBotControlMode
{
    Automatic,
    Stopped,
    Manual,
}

public readonly record struct RaceControlBotInput(float Steering, float Throttle, float Brake,
    DateTimeOffset UpdatedAt);

public readonly record struct RaceAiDiagnostics(float MaximumAbsoluteLateralOffsetMeters,
    float MaximumPassSeparationMeters, int PassCommitCount, int SeparatedPassCount,
    int CompletedPassCount, int StoppedObstaclePassCommitCount,
    int StoppedObstaclePassCompletedCount);
public readonly record struct RaceAiStateSnapshot(int SplinePointId, Vector3 Position, Vector3 Velocity,
    float CurrentSpeed, float TargetSpeed, float LateralOffsetMeters,
    float MaximumLateralOffsetMeters, float ClosestObstacleMeters, float SteeringAngleRadians,
    bool IsStoppedForObstacle, bool IsOvertaking, byte? OvertakeTargetSessionId, bool PassingLeft,
    int PassCommitCount, int SeparatedPassCount, int CompletedPassCount,
    int StoppedObstaclePassCommitCount, int StoppedObstaclePassCompletedCount);

public partial class EntryCar
{
    public bool AiControlled { get; set; }
    public AiMode AiMode { get; set; }
    public int TargetAiStateCount { get; private set; } = 1;
    public byte[] LastSeenAiSpawn { get; }
    public byte[] AiPakSequenceIds { get; }
    public AiState?[] LastSeenAiState { get; }
    public string? AiName { get; private set; }
    public bool AiEnableColorChanges { get; set; } = false;
    public int AiIdleEngineRpm { get; set; } = 800;
    public int AiMaxEngineRpm { get; set; } = 3000;
    public float AiAcceleration { get; set; }
    public float AiDeceleration { get; set; }
    public float AiCorneringSpeedFactor { get; set; }
    public float AiCorneringBrakeDistanceFactor { get; set; }
    public float AiCorneringBrakeForceFactor { get; set; }
    public float AiSplineHeightOffsetMeters { get; set; }
    public int? AiMaxOverbooking { get; set; }
    public int AiMinSpawnProtectionTimeMilliseconds { get; set; }
    public int AiMaxSpawnProtectionTimeMilliseconds { get; set; }
    public int? MinLaneCount { get; set; }
    public int? MaxLaneCount { get; set; }
    public int AiMinCollisionStopTimeMilliseconds { get; set; }
    public int AiMaxCollisionStopTimeMilliseconds { get; set; }
    public float VehicleLengthPreMeters { get; set; }
    public float VehicleLengthPostMeters { get; set; }
    public int? MinAiSafetyDistanceMetersSquared { get; set; }
    public int? MaxAiSafetyDistanceMetersSquared { get; set; }
    public List<LaneSpawnBehavior>? AiAllowedLanes { get; set; }
    public float TyreDiameterMeters { get; set; }
    public RaceBotVehicleProfile? RaceVehicleProfile { get; private set; }
    
    // Theoretically, this list should never include null values. Since we access it as a Span later, we might catch a null anyway
    // when it is updated concurrently
    private readonly List<AiState?> _aiStates = [];
    private readonly object _aiControlLock = new();
    private RaceControlBotControlMode _raceControlMode;
    private RaceControlBotInput _raceControlInput;
    private Span<AiState?> AiStatesSpan => CollectionsMarshal.AsSpan(_aiStates);
    
    private readonly Func<EntryCar, AiState> _aiStateFactory;
    private readonly AiSpline? _spline;

    private void AiInit()
    {
        AiName = $"{_configuration.Extra.AiParams.NamePrefix} {SessionId}";
        SetAiOverbooking(0);

        _configuration.Extra.AiParams.PropertyChanged += OnConfigReload;
        OnConfigReload(_configuration, new PropertyChangedEventArgs(string.Empty));
    }

    private void OnConfigReload(object? sender, PropertyChangedEventArgs args)
    {
        AiSplineHeightOffsetMeters = _configuration.Extra.AiParams.SplineHeightOffsetMeters;
        AiAcceleration = _configuration.Extra.AiParams.DefaultAcceleration;
        AiDeceleration = _configuration.Extra.AiParams.DefaultDeceleration;
        AiCorneringSpeedFactor = _configuration.Extra.AiParams.CorneringSpeedFactor;
        AiCorneringBrakeDistanceFactor = _configuration.Extra.AiParams.CorneringBrakeDistanceFactor;
        AiCorneringBrakeForceFactor = _configuration.Extra.AiParams.CorneringBrakeForceFactor;
        TyreDiameterMeters = _configuration.Extra.AiParams.TyreDiameterMeters;
        AiMinSpawnProtectionTimeMilliseconds = _configuration.Extra.AiParams.MinSpawnProtectionTimeMilliseconds;
        AiMaxSpawnProtectionTimeMilliseconds = _configuration.Extra.AiParams.MaxSpawnProtectionTimeMilliseconds;
        AiMinCollisionStopTimeMilliseconds = _configuration.Extra.AiParams.MinCollisionStopTimeMilliseconds;
        AiMaxCollisionStopTimeMilliseconds = _configuration.Extra.AiParams.MaxCollisionStopTimeMilliseconds;

        RaceVehicleProfile = _configuration.Extra.AiParams.Behavior == AiBehaviorMode.Race
            ? _configuration.Extra.AiParams.Race.VehicleProfiles.Find(profile =>
                string.Equals(profile.Model, Model, StringComparison.OrdinalIgnoreCase))
            : null;
        if (RaceVehicleProfile != null)
        {
            AiIdleEngineRpm = RaceVehicleProfile.EngineIdleRpm;
            AiMaxEngineRpm = RaceVehicleProfile.EngineMaxRpm;
            AiAcceleration = 100 / 3.6f / RaceVehicleProfile.ZeroToHundredSeconds;
            AiDeceleration = RaceVehicleProfile.MaxBrakeDeceleration;
            AiCorneringSpeedFactor *= RaceVehicleProfile.LateralGripG;
            TyreDiameterMeters = RaceVehicleProfile.TyreDiameterMeters;
            AiSplineHeightOffsetMeters = RaceVehicleProfile.SplineHeightOffsetMeters;
            Logger.Debug("Using {Source} race vehicle profile: {MassKg} kg, {PowerKw} kW, {TopSpeedKph} km/h",
                RaceVehicleProfile.Source, RaceVehicleProfile.MassKg, RaceVehicleProfile.PowerKw,
                RaceVehicleProfile.TopSpeedKph);
        }
        else if (_configuration.Extra.AiParams.Behavior == AiBehaviorMode.Race)
        {
            Logger.Warning("No race vehicle profile found; using legacy global AI parameters");
        }

        foreach (var carOverrides in _configuration.Extra.AiParams.CarSpecificOverrides)
        {
            if (carOverrides.Model == Model)
            {
                if (carOverrides.SplineHeightOffsetMeters.HasValue)
                    AiSplineHeightOffsetMeters = carOverrides.SplineHeightOffsetMeters.Value;
                if (carOverrides.EngineIdleRpm.HasValue)
                    AiIdleEngineRpm = carOverrides.EngineIdleRpm.Value;
                if (carOverrides.EngineMaxRpm.HasValue)
                    AiMaxEngineRpm = carOverrides.EngineMaxRpm.Value;
                if (carOverrides.Acceleration.HasValue)
                    AiAcceleration = carOverrides.Acceleration.Value;
                if (carOverrides.Deceleration.HasValue)
                    AiDeceleration = carOverrides.Deceleration.Value;
                if (carOverrides.CorneringSpeedFactor.HasValue)
                    AiCorneringSpeedFactor = carOverrides.CorneringSpeedFactor.Value;
                if (carOverrides.CorneringBrakeDistanceFactor.HasValue)
                    AiCorneringBrakeDistanceFactor = carOverrides.CorneringBrakeDistanceFactor.Value;
                if (carOverrides.CorneringBrakeForceFactor.HasValue)
                    AiCorneringBrakeForceFactor = carOverrides.CorneringBrakeForceFactor.Value;
                if (carOverrides.TyreDiameterMeters.HasValue)
                    TyreDiameterMeters = carOverrides.TyreDiameterMeters.Value;
                if (carOverrides.MaxOverbooking.HasValue)
                    AiMaxOverbooking = carOverrides.MaxOverbooking.Value;
                if (carOverrides.MinSpawnProtectionTimeMilliseconds.HasValue)
                    AiMinSpawnProtectionTimeMilliseconds = carOverrides.MinSpawnProtectionTimeMilliseconds.Value;
                if (carOverrides.MaxSpawnProtectionTimeMilliseconds.HasValue)
                    AiMaxSpawnProtectionTimeMilliseconds = carOverrides.MaxSpawnProtectionTimeMilliseconds.Value;
                if (carOverrides.MinCollisionStopTimeMilliseconds.HasValue)
                    AiMinCollisionStopTimeMilliseconds = carOverrides.MinCollisionStopTimeMilliseconds.Value;
                if (carOverrides.MaxCollisionStopTimeMilliseconds.HasValue)
                    AiMaxCollisionStopTimeMilliseconds = carOverrides.MaxCollisionStopTimeMilliseconds.Value;
                if (carOverrides.VehicleLengthPreMeters.HasValue)
                    VehicleLengthPreMeters = carOverrides.VehicleLengthPreMeters.Value;
                if (carOverrides.VehicleLengthPostMeters.HasValue)
                    VehicleLengthPostMeters = carOverrides.VehicleLengthPostMeters.Value;
                
                AiAllowedLanes = carOverrides.AllowedLanes;
                MinAiSafetyDistanceMetersSquared = carOverrides.MinAiSafetyDistanceMetersSquared;
                MaxAiSafetyDistanceMetersSquared = carOverrides.MaxAiSafetyDistanceMetersSquared;
                MinLaneCount = carOverrides.MinLaneCount;
                MaxLaneCount = carOverrides.MaxLaneCount;
            }
        }
    }

    public void RemoveUnsafeStates()
    {
        foreach (var aiState in AiStatesSpan)
        {
            if (aiState is not { Initialized: true }) continue;

            foreach (var targetAiState in AiStatesSpan)
            {
                if (aiState != targetAiState
                    && targetAiState is { Initialized: true }
                    && Vector3.DistanceSquared(aiState.Status.Position, targetAiState.Status.Position) < _configuration.Extra.AiParams.MinStateDistanceSquared
                    && (_configuration.Extra.AiParams.TwoWayTraffic || Vector3.Dot(aiState.Status.Velocity, targetAiState.Status.Velocity) > 0))
                {
                    aiState.Despawn();
                    Logger.Verbose("Removed close state from AI {SessionId}", SessionId);
                }
            }
        }
    }

    public void AiUpdate(float? fixedDeltaSeconds = null)
    {
        lock (_aiControlLock)
        {
            if (!AiControlled)
                return;

            foreach (var aiState in AiStatesSpan)
            {
                aiState?.Update(fixedDeltaSeconds);
            }
        }
    }

    public void AiPrepareRacePhysics(float fixedDeltaSeconds)
    {
        lock (_aiControlLock)
        {
            if (!AiControlled) return;
            foreach (var aiState in AiStatesSpan)
                aiState?.PrepareRacePhysics(fixedDeltaSeconds);
        }
    }

    public void AiCompleteRacePhysics(float fixedDeltaSeconds)
    {
        lock (_aiControlLock)
        {
            if (!AiControlled) return;
            foreach (var aiState in AiStatesSpan)
                aiState?.CompleteRacePhysics(fixedDeltaSeconds);
        }
    }

    public void AiObstacleDetection()
    {
        lock (_aiControlLock)
        {
            if (!AiControlled)
                return;

            foreach (var aiState in AiStatesSpan)
            {
                aiState?.DetectObstacles();
            }
        }
    }

    public AiState? GetBestStateForPlayer(CarStatus playerStatus)
    {
        AiState? bestState = null;
        float minDistance = float.MaxValue;

        foreach (var aiState in AiStatesSpan)
        {
            if (aiState is not { Initialized: true }) continue;

            float distance = Vector3.DistanceSquared(aiState.Status.Position, playerStatus.Position);

            if (_configuration.Extra.AiParams.TwoWayTraffic)
            {
                if (distance < minDistance)
                {
                    bestState = aiState;
                    minDistance = distance;
                }
            }
            else
            {
                bool isBestSameDirection = bestState != null && Vector3.Dot(bestState.Status.Velocity, playerStatus.Velocity) > 0;
                bool isCandidateSameDirection = Vector3.Dot(aiState.Status.Velocity, playerStatus.Velocity) > 0;
                bool isPlayerFastEnough = playerStatus.Velocity.LengthSquared() > 1;
                bool isTieBreaker = minDistance < _configuration.Extra.AiParams.MinStateDistanceSquared &&
                                    distance < _configuration.Extra.AiParams.MinStateDistanceSquared &&
                                    isPlayerFastEnough;

                // Tie breaker: Multiple close states, so take the one with min distance and same direction
                if ((isTieBreaker && isCandidateSameDirection && (distance < minDistance || !isBestSameDirection))
                    || (!isTieBreaker && distance < minDistance))
                {
                    bestState = aiState;
                    minDistance = distance;
                }
            }
        }

        return bestState;
    }

    public bool IsPositionSafe(int pointId)
    {
        ArgumentNullException.ThrowIfNull(_spline);

        var ops = _spline.Operations;
            
        foreach (var aiState in AiStatesSpan)
        {
            if (aiState is { Initialized: true }
                && Vector3.DistanceSquared(aiState.Status.Position, ops.Points[pointId].Position) < aiState.SafetyDistanceSquared
                && ops.IsSameDirection(aiState.CurrentSplinePointId, pointId))
            {
                return false;
            }
        }

        return true;
    }

    public (AiState? AiState, float DistanceSquared) GetClosestAiState(Vector3 position)
    {
        AiState? closestState = null;
        float minDistanceSquared = float.MaxValue;
        
        foreach (var aiState in AiStatesSpan)
        {
            if (aiState == null) continue;
            
            float distanceSquared = Vector3.DistanceSquared(position, aiState.Status.Position);
            if (distanceSquared < minDistanceSquared)
            {
                closestState = aiState;
                minDistanceSquared = distanceSquared;
            }
        }

        return (closestState, minDistanceSquared);
    }

    public void GetInitializedStates(List<AiState> initializedStates, List<AiState>? uninitializedStates = null)
    {
        foreach (var aiState in AiStatesSpan)
        {
            if (aiState == null) continue;
            
            if (aiState.Initialized)
            {
                initializedStates.Add(aiState);
            }
            else
            {
                uninitializedStates?.Add(aiState);
            }
        }
    }
    
    public bool CanSpawnAiState(Vector3 spawnPoint, AiState aiState)
    {
        // Remove state if AI slot overbooking was reduced
        if (_aiStates.IndexOf(aiState) >= TargetAiStateCount)
        {
            aiState.Dispose();
            _aiStates.Remove(aiState);

            Logger.Verbose("Removed state of Traffic {SessionId} due to overbooking reduction", SessionId);

            if (_aiStates.Count == 0)
            {
                Logger.Verbose("Traffic {SessionId} has no states left, disconnecting", SessionId);
                _entryCarManager.BroadcastPacket(new CarDisconnected { SessionId = SessionId });
            }

            return false;
        }

        foreach (var state in AiStatesSpan)
        {
            if (state == aiState || state is not { Initialized: true }) continue;

            if (Vector3.DistanceSquared(spawnPoint, state.Status.Position) < _configuration.Extra.AiParams.StateSpawnDistanceSquared)
            {
                return false;
            }
        }

        return true;
    }

    public void SetAiControl(bool aiControlled)
    {
        lock (_aiControlLock)
        {
            if (AiControlled != aiControlled)
            {
                AiControlled = aiControlled;

                if (AiControlled)
                {
                    Logger.Debug("Slot {SessionId} is now controlled by AI", SessionId);

                    AiReset();
                    _entryCarManager.BroadcastPacket(new CarConnected
                    {
                        SessionId = SessionId,
                        Name = AiName
                    });
                    if (_configuration.Extra.AiParams.HideAiCars)
                    {
                        _entryCarManager.BroadcastPacket(new CSPCarVisibilityUpdate
                        {
                            SessionId = SessionId,
                            Visible = CSPCarVisibility.Invisible
                        });
                    }
                }
                else
                {
                    Logger.Debug("Slot {SessionId} is no longer controlled by AI", SessionId);
                    if (_aiStates.Count > 0)
                    {
                        _entryCarManager.BroadcastPacket(new CarDisconnected { SessionId = SessionId });
                    }

                    if (_configuration.Extra.AiParams.HideAiCars)
                    {
                        _entryCarManager.BroadcastPacket(new CSPCarVisibilityUpdate
                        {
                            SessionId = SessionId,
                            Visible = CSPCarVisibility.Visible
                        });
                    }

                    AiReset();
                }
            }
        }
    }

    public RaceAiDiagnostics GetRaceAiDiagnostics()
    {
        lock (_aiControlLock)
        {
            float maximumOffset = 0;
            float maximumSeparation = 0;
            int commits = 0;
            int separated = 0;
            int completed = 0;
            int stoppedCommits = 0;
            int stoppedCompleted = 0;
            foreach (var state in AiStatesSpan)
            {
                if (state is not { Initialized: true })
                    continue;
                maximumOffset = Math.Max(maximumOffset, state.MaximumAbsoluteLateralOffsetMeters);
                maximumSeparation = Math.Max(maximumSeparation, state.MaximumPassSeparationMeters);
                commits += state.PassCommitCount;
                separated += state.SeparatedPassCount;
                completed += state.CompletedPassCount;
                stoppedCommits += state.StoppedObstaclePassCommitCount;
                stoppedCompleted += state.StoppedObstaclePassCompletedCount;
            }
            return new RaceAiDiagnostics(maximumOffset, maximumSeparation, commits, separated, completed,
                stoppedCommits, stoppedCompleted);
        }
    }

    public RaceAiStateSnapshot? GetRaceAiStateSnapshot()
    {
        lock (_aiControlLock)
        {
            AiState? state = null;
            foreach (var candidate in AiStatesSpan)
            {
                if (candidate is not { Initialized: true })
                    continue;
                state = candidate;
                break;
            }
            if (state == null)
                return null;
            return new RaceAiStateSnapshot(state.CurrentSplinePointId, state.Status.Position,
                state.Status.Velocity, state.CurrentSpeed, state.TargetSpeed,
                state.PhysicalLateralOffsetMeters, state.MaximumAbsoluteLateralOffsetMeters,
                state.ClosestAiObstacleDistance, state.SteeringAngleRadians,
                state.IsStoppedForObstacle, state.IsOvertaking, state.OvertakeTargetSessionId,
                state.PassingLeft, state.PassCommitCount, state.SeparatedPassCount,
                state.CompletedPassCount, state.StoppedObstaclePassCommitCount,
                state.StoppedObstaclePassCompletedCount);
        }
    }

    public RaceControlBotControlMode GetRaceControlMode()
    {
        lock (_aiControlLock)
            return _raceControlMode;
    }

    public RaceControlBotInput GetRaceControlInput()
    {
        lock (_aiControlLock)
            return _raceControlInput;
    }

    public bool TrySetRaceControlMode(RaceControlBotControlMode mode)
    {
        lock (_aiControlLock)
        {
            if (!AiControlled || _configuration.Extra.AiParams.Behavior != AiBehaviorMode.Race
                              || !HasInitializedRaceControlState())
                return false;
            _raceControlMode = mode;
            _raceControlInput = mode == RaceControlBotControlMode.Manual
                ? new RaceControlBotInput(0, 0, 1, DateTimeOffset.UtcNow)
                : default;
            return true;
        }
    }

    public bool TrySetRaceControlInput(float steering, float throttle, float brake,
        DateTimeOffset updatedAt)
    {
        lock (_aiControlLock)
        {
            if (!AiControlled || _raceControlMode != RaceControlBotControlMode.Manual)
                return false;
            _raceControlInput = new RaceControlBotInput(
                Math.Clamp(steering, -1, 1), Math.Clamp(throttle, 0, 1),
                Math.Clamp(brake, 0, 1), updatedAt);
            return true;
        }
    }

    public bool TryTeleportRaceControlBot(int pointId)
    {
        lock (_aiControlLock)
        {
            if (!AiControlled || _configuration.Extra.AiParams.Behavior != AiBehaviorMode.Race)
                return false;
            foreach (var state in AiStatesSpan)
            {
                if (state is not { Initialized: true })
                    continue;
                state.TeleportForRaceControl(pointId);
                return true;
            }
            return false;
        }
    }

    private bool HasInitializedRaceControlState()
    {
        foreach (var state in AiStatesSpan)
        {
            if (state is { Initialized: true })
                return true;
        }
        return false;
    }

    public void SetAiOverbooking(int count)
    {
        if (AiMaxOverbooking.HasValue)
        {
            count = Math.Min(count, AiMaxOverbooking.Value);
        }

        if (count > _aiStates.Count)
        {
            int newAis = count - _aiStates.Count;
            for (int i = 0; i < newAis; i++)
            {
                _aiStates.Add(_aiStateFactory(this));
            }
        }

        TargetAiStateCount = count;
    }

    public AiState PrepareSingleAiState(int pointId, RaceSplineLayout raceLayout, RaceGridPose? gridPose = null)
    {
        AiReset();
        TargetAiStateCount = 1;
        var state = _aiStates[0] ?? throw new InvalidOperationException("Failed to create AI state");
        state.ConfigureRace(raceLayout);
        state.Teleport(pointId, gridPose);
        return state;
    }

    public void DespawnAiStates()
    {
        foreach (var state in AiStatesSpan)
        {
            state?.Despawn();
        }
    }

    private void AiReset()
    {
        _raceControlMode = RaceControlBotControlMode.Automatic;
        _raceControlInput = default;
        foreach (var state in AiStatesSpan)
        {
            state?.Despawn();
        }
        _aiStates.Clear();
        _aiStates.Add(_aiStateFactory(this));
    }
}
