using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using AssettoServer.Server.Ai.Splines;
using AssettoServer.Server.Ai.Physics;
using AssettoServer.Server.Configuration;
using AssettoServer.Server.Configuration.Extra;
using AssettoServer.Server.Weather;
using AssettoServer.Shared.Model;
using AssettoServer.Shared.Network.Packets.Incoming;
using AssettoServer.Shared.Network.Packets.Outgoing;
using AssettoServer.Shared.Utils;
using AssettoServer.Utils;
using JPBotelho;
using Serilog;
using SunCalcNet.Model;

namespace AssettoServer.Server.Ai;

public class AiState : IDisposable
{
    public CarStatus Status { get; } = new();
    public bool Initialized { get; private set; }

    public int CurrentSplinePointId
    {
        get => _currentSplinePointId;
        private set
        {
            _spline.SlowestAiStates.Enter(value, this);
            _spline.SlowestAiStates.Leave(_currentSplinePointId, this);
            _currentSplinePointId = value;
        }
    }

    private int _currentSplinePointId;
    
    public long SpawnProtectionEnds { get; set; }
    public float SafetyDistanceSquared { get; set; } = 20 * 20;
    public float Acceleration { get; set; }
    public float CurrentSpeed { get; private set; }
    public float TargetSpeed { get; private set; }
    public float InitialMaxSpeed { get; private set; }
    public float MaxSpeed { get; private set; }
    public Color Color { get; private set; }
    public byte SpawnCounter { get; private set; }
    public float ClosestAiObstacleDistance { get; private set; }
    public float PhysicalLateralOffsetMeters { get; private set; }
    public float MaximumAbsoluteLateralOffsetMeters { get; private set; }
    public float MaximumPassSeparationMeters { get; private set; }
    public int PassCommitCount { get; private set; }
    public int SeparatedPassCount { get; private set; }
    public int CompletedPassCount { get; private set; }
    public EntryCar EntryCar { get; }

    private const float WalkingSpeed = 10 / 3.6f;

    private Vector3 _startTangent;
    private Vector3 _endTangent;

    private float _currentVecLength;
    private float _currentVecProgress;
    private long _lastTick;
    private bool _stoppedForObstacle;
    private long _stoppedForObstacleSince;
    private long _ignoreObstaclesUntil;
    private long _stoppedForCollisionUntil;
    private long _obstacleHonkStart;
    private long _obstacleHonkEnd;
    private CarStatusFlags _indicator = 0;
    private int _nextJunctionId;
    private bool _junctionPassed;
    private float _endIndicatorDistance;
    private float _minObstacleDistance;
    private double _randomTwilight;
    private RaceSplineLayout? _raceLayout;
    private RaceLapTracker? _raceLapTracker;
    private long _raceLapStartedAt;
    private bool _holdingForRaceStart;
    private bool _gridLineMergePending;
    private bool _gridLineMergeStarted;
    private float _gridLaunchDistanceMeters;
    private float _gridLaunchLateralOffsetMeters;
    private float _lateralOffsetMeters;
    private float _targetLateralOffsetMeters;
    private float _baseLateralOffsetMeters;
    private long _overtakeUntil;
    private byte? _overtakeTargetSessionId;
    private bool _currentPassSeparationRecorded;
    private bool _currentPassAccelerationClearanceRecorded;
    private long _passAccelerationClearanceSince;
    private bool _passingLeft;
    private bool _yieldingToPasser;
    private float _yieldSpeedReference;
    private long _returnToLineAt;
    private long _passCooldownUntil;
    private byte? _recentPassTargetSessionId;
    private long _recentPassPairUntil;
    private int _overtakeExtensionCount;
    private Vector3 _physicsLastPosition;
    private int _physicsRecoveryCount;
    private float _steeringAngleRadians;

    private readonly ACServerConfiguration _configuration;
    private readonly SessionManager _sessionManager;
    private readonly EntryCarManager _entryCarManager;
    private readonly WeatherManager _weatherManager;
    private readonly AiSpline _spline;
    private readonly JunctionEvaluator _junctionEvaluator;
    private readonly RaceBotPhysicsWorld? _racePhysicsWorld;

    private readonly record struct RaceParticipantSnapshot(EntryCar Car, AiState? AiState,
        CarStatus Status, float Speed);

    private readonly record struct RaceObstacle(RaceParticipantSnapshot Participant, float ClearanceMeters,
        float LongitudinalMeters, float LateralOffsetMeters, float RelativeLateralMeters);

    private static readonly List<Color> CarColors =
    [
        Color.FromArgb(13, 17, 22),
        Color.FromArgb(19, 24, 31),
        Color.FromArgb(28, 29, 33),
        Color.FromArgb(12, 13, 24),
        Color.FromArgb(11, 20, 33),
        Color.FromArgb(151, 154, 151),
        Color.FromArgb(153, 157, 160),
        Color.FromArgb(194, 196, 198),
        Color.FromArgb(234, 234, 234),
        Color.FromArgb(255, 255, 255),
        Color.FromArgb(182, 17, 27),
        Color.FromArgb(218, 25, 24),
        Color.FromArgb(73, 17, 29),
        Color.FromArgb(35, 49, 85),
        Color.FromArgb(28, 53, 81),
        Color.FromArgb(37, 58, 167),
        Color.FromArgb(21, 92, 45),
        Color.FromArgb(18, 46, 43)
    ];

    public AiState(EntryCar entryCar, SessionManager sessionManager, WeatherManager weatherManager,
        ACServerConfiguration configuration, EntryCarManager entryCarManager, AiSpline spline,
        RaceBotPhysicsWorld? racePhysicsWorld = null)
    {
        EntryCar = entryCar;
        _sessionManager = sessionManager;
        _weatherManager = weatherManager;
        _configuration = configuration;
        _entryCarManager = entryCarManager;
        _spline = spline;
        _junctionEvaluator = new JunctionEvaluator(spline);
        _racePhysicsWorld = racePhysicsWorld;

        _lastTick = _sessionManager.ServerTimeMilliseconds;
    }

    ~AiState()
    {
        Despawn();
    }
    
    public void Dispose()
    {
        Despawn();
        GC.SuppressFinalize(this);
    }

    public void Despawn()
    {
        Initialized = false;
        _spline.SlowestAiStates.Leave(CurrentSplinePointId, this);
        if (_configuration.Extra.AiParams.Behavior == AiBehaviorMode.Race)
            _racePhysicsWorld?.RemoveBody(EntryCar.SessionId);
    }

    private void SetRandomSpeed()
    {
        float configuredMaxSpeed = _configuration.Extra.AiParams.Behavior == AiBehaviorMode.Race
                                   && EntryCar.RaceVehicleProfile != null
            ? EntryCar.RaceVehicleProfile.TopSpeedMs
            : _configuration.Extra.AiParams.MaxSpeedMs;
        float variation = configuredMaxSpeed * _configuration.Extra.AiParams.MaxSpeedVariationPercent;

        float fastLaneOffset = 0;
        if (_configuration.Extra.AiParams.Behavior != AiBehaviorMode.Race
            && _spline.Points[CurrentSplinePointId].LeftId >= 0)
        {
            fastLaneOffset = _configuration.Extra.AiParams.RightLaneOffsetMs;
        }
        InitialMaxSpeed = _configuration.Extra.AiParams.Behavior == AiBehaviorMode.Race
            ? configuredMaxSpeed * RaceBotMath.GridPaceFactor(
                _configuration.Extra.AiParams.MaxSpeedVariationPercent, EntryCar.SessionId)
            : configuredMaxSpeed + fastLaneOffset - (variation / 2) + (float)Random.Shared.NextDouble() * variation;
        if (_configuration.Extra.AiParams.Behavior == AiBehaviorMode.Race)
        {
            InitialMaxSpeed *= RaceBotMath.PaceFactor(_configuration.Extra.AiParams.Race.Difficulty);
        }
        CurrentSpeed = InitialMaxSpeed;
        TargetSpeed = InitialMaxSpeed;
        MaxSpeed = InitialMaxSpeed;
    }

    private void SetRandomColor()
    {
        Color = CarColors[Random.Shared.Next(CarColors.Count)];
    }

    public void Teleport(int pointId, RaceGridPose? gridPose = null)
    {
        _junctionEvaluator.Clear();
        CurrentSplinePointId = pointId;
        if (!_junctionEvaluator.TryNext(CurrentSplinePointId, out var nextPointId))
            throw new InvalidOperationException($"Cannot get next spline point for {CurrentSplinePointId}");
        _currentVecLength = (_spline.Points[nextPointId].Position - _spline.Points[CurrentSplinePointId].Position).Length();
        _currentVecProgress = 0;
            
        CalculateTangents();
        
        SetRandomSpeed();
        SetRandomColor();

        var minDist = _configuration.Extra.AiParams.MinAiSafetyDistanceSquared;
        var maxDist = _configuration.Extra.AiParams.MaxAiSafetyDistanceSquared;
        if (_configuration.Extra.AiParams.LaneCountSpecificOverrides.TryGetValue(_spline.GetLanes(CurrentSplinePointId).Length, out var overrides))
        {
            minDist = overrides.MinAiSafetyDistanceSquared;
            maxDist = overrides.MaxAiSafetyDistanceSquared;
        }
        
        if (EntryCar.MinAiSafetyDistanceMetersSquared.HasValue)
            minDist = EntryCar.MinAiSafetyDistanceMetersSquared.Value;
        if (EntryCar.MaxAiSafetyDistanceMetersSquared.HasValue)
            maxDist = EntryCar.MaxAiSafetyDistanceMetersSquared.Value;

        SpawnProtectionEnds = _sessionManager.ServerTimeMilliseconds + Random.Shared.Next(EntryCar.AiMinSpawnProtectionTimeMilliseconds, EntryCar.AiMaxSpawnProtectionTimeMilliseconds);
        SafetyDistanceSquared = Random.Shared.Next((int)Math.Round(minDist * (1.0f / _configuration.Extra.AiParams.TrafficDensity)),
            (int)Math.Round(maxDist * (1.0f / _configuration.Extra.AiParams.TrafficDensity)));
        _stoppedForCollisionUntil = 0;
        _ignoreObstaclesUntil = 0;
        _obstacleHonkEnd = 0;
        _obstacleHonkStart = 0;
        _indicator = 0;
        _randomTwilight = Random.Shared.NextSingle(0, 12) * Math.PI / 180.0;
        _nextJunctionId = -1;
        _junctionPassed = false;
        _endIndicatorDistance = 0;
        _lastTick = _sessionManager.ServerTimeMilliseconds;
        _minObstacleDistance = Random.Shared.Next(8, 13);
        _raceLapTracker = _raceLayout == null
            ? null
            : new RaceLapTracker(_configuration.Extra.AiParams.Race.StartSplinePointId, _raceLayout.LengthMeters);
        _raceLapStartedAt = _sessionManager.ServerTimeMilliseconds;
        _holdingForRaceStart = false;
        _gridLineMergePending = false;
        _gridLineMergeStarted = false;
        _gridLaunchDistanceMeters = 0;
        ref readonly var spawnPoint = ref _spline.Points[CurrentSplinePointId];
        _baseLateralOffsetMeters = _configuration.Extra.AiParams.Behavior == AiBehaviorMode.Race
            ? GetRaceBaseLaneOffset(spawnPoint)
            : 0;
        _lateralOffsetMeters = _baseLateralOffsetMeters;
        _targetLateralOffsetMeters = _baseLateralOffsetMeters;
        _overtakeUntil = 0;
        _overtakeTargetSessionId = null;
        _currentPassSeparationRecorded = false;
        _currentPassAccelerationClearanceRecorded = false;
        _passAccelerationClearanceSince = 0;
        _yieldingToPasser = false;
        _yieldSpeedReference = 0;
        _returnToLineAt = 0;
        _passCooldownUntil = 0;
        _recentPassTargetSessionId = null;
        _recentPassPairUntil = 0;
        _overtakeExtensionCount = 0;
        _steeringAngleRadians = 0;
        PhysicalLateralOffsetMeters = 0;
        MaximumAbsoluteLateralOffsetMeters = 0;
        MaximumPassSeparationMeters = 0;
        PassCommitCount = 0;
        SeparatedPassCount = 0;
        CompletedPassCount = 0;
        SpawnCounter++;
        Initialized = true;
        if (_configuration.Extra.AiParams.Behavior == AiBehaviorMode.Race)
        {
            if (_racePhysicsWorld == null || EntryCar.RaceVehicleProfile == null)
                throw new InvalidOperationException("Race bot rigid-body world or vehicle profile is unavailable");
            var pose = gridPose ?? CreateSplinePose(pointId);
            var startSample = GetCurrentSplineSample(out _);
            var startTangent = startSample.Tangent with { Y = 0 };
            if (startTangent.LengthSquared() > 1e-6f)
            {
                var startLateral = Vector3.Normalize(Vector3.Cross(Vector3.UnitY,
                    Vector3.Normalize(startTangent)));
                _lateralOffsetMeters = RaceBotMath.ClampLaneOffset(
                    Vector3.Dot(pose.Position - startSample.Position, startLateral),
                    spawnPoint.SideLeft, spawnPoint.SideRight);
                PhysicalLateralOffsetMeters = _lateralOffsetMeters;
            }
            _gridLaunchLateralOffsetMeters = _lateralOffsetMeters;
            _gridLineMergePending = gridPose.HasValue
                                    && _sessionManager.CurrentSession.Configuration.Type == SessionType.Race;
            _targetLateralOffsetMeters = _gridLineMergePending
                ? _gridLaunchLateralOffsetMeters
                : _baseLateralOffsetMeters;
            _racePhysicsWorld.RegisterBot(EntryCar.SessionId, EntryCar.Model, pose,
                EntryCar.RaceVehicleProfile.MassKg);
            if (!_racePhysicsWorld.TryGetBotState(EntryCar.SessionId, out var physicsState))
                throw new InvalidOperationException("Race bot rigid body was not registered");
            _physicsLastPosition = physicsState.Position;
            _physicsRecoveryCount = physicsState.RecoveryCount;
            _steeringAngleRadians = physicsState.SteeringAngleRadians;
            Status.Timestamp = _sessionManager.ServerTimeMilliseconds;
            Status.Position = physicsState.ProtocolPosition;
            Status.Rotation = RacePhysicsMath.ToProtocolRotation(pose.Orientation);
            Status.Velocity = Vector3.Zero;
            CurrentSpeed = 0;
            Acceleration = 0;
            ApplyStatusTelemetry();
        }
        else
        {
            Update();
        }
    }

    public void ConfigureRace(RaceSplineLayout layout)
    {
        _raceLayout = layout;
    }

    private void CalculateTangents()
    {
        if (!_junctionEvaluator.TryNext(CurrentSplinePointId, out var nextPointId))
            throw new InvalidOperationException("Cannot get next spline point");

        var points = _spline.Points;
        
        if (_junctionEvaluator.TryPrevious(CurrentSplinePointId, out var previousPointId))
        {
            _startTangent = (points[nextPointId].Position - points[previousPointId].Position) * 0.5f;
        }
        else
        {
            _startTangent = (points[nextPointId].Position - points[CurrentSplinePointId].Position) * 0.5f;
        }

        if (_junctionEvaluator.TryNext(CurrentSplinePointId, out var nextNextPointId, 2))
        {
            _endTangent = (points[nextNextPointId].Position - points[CurrentSplinePointId].Position) * 0.5f;
        }
        else
        {
            _endTangent = (points[nextPointId].Position - points[CurrentSplinePointId].Position) * 0.5f;
        }
    }

    private bool Move(float progress)
    {
        var points = _spline.Points;
        var junctions = _spline.Junctions;
        
        bool recalculateTangents = false;
        while (progress > _currentVecLength)
        {
            progress -= _currentVecLength;
                
            if (!_junctionEvaluator.TryNext(CurrentSplinePointId, out var nextPointId)
                || !_junctionEvaluator.TryNext(nextPointId, out var nextNextPointId))
            {
                return false;
            }

            var previousPointId = CurrentSplinePointId;
            var segmentLength = _currentVecLength;
            CurrentSplinePointId = nextPointId;
            _currentVecLength = (points[nextNextPointId].Position - points[CurrentSplinePointId].Position).Length();
            recalculateTangents = true;

            if (_raceLapTracker != null)
            {
                var crossedStart = nextPointId == _configuration.Extra.AiParams.Race.StartSplinePointId;
                if (_raceLapTracker.ObservePointTransition(previousPointId, nextPointId, segmentLength, movingForward: true))
                {
                    var now = _sessionManager.ServerTimeMilliseconds;
                    var lapTime = (uint)Math.Max(1, now - _raceLapStartedAt);
                    _raceLapStartedAt = now;
                    _sessionManager.OnLapCompleted(EntryCar, EntryCar.AiName ?? $"Bot {EntryCar.SessionId}", new LapCompletedIncoming
                    {
                        Timestamp = (uint)now,
                        LapTime = lapTime,
                        Splits = [],
                        Cuts = 0,
                        NumLap = (byte)_raceLapTracker.CompletedLaps
                    });
                }
                else if (crossedStart)
                {
                    // The first crossing is the grid launch, not a completed lap.
                    _raceLapStartedAt = _sessionManager.ServerTimeMilliseconds;
                }
            }

            if (_junctionPassed)
            {
                _endIndicatorDistance -= _currentVecLength;

                if (_endIndicatorDistance < 0)
                {
                    _indicator = 0;
                    _junctionPassed = false;
                    _endIndicatorDistance = 0;
                }
            }
                
            if (_nextJunctionId >= 0 && points[CurrentSplinePointId].JunctionEndId == _nextJunctionId)
            {
                _junctionPassed = true;
                _endIndicatorDistance = junctions[_nextJunctionId].IndicateDistancePost;
                _nextJunctionId = -1;
            }
        }

        if (recalculateTangents)
        {
            CalculateTangents();
        }

        _currentVecProgress = progress;

        return true;
    }

    public bool CanSpawn(int spawnPointId, AiState? previousAi, AiState? nextAi)
    {
        var ops = _spline.Operations;
        ref readonly var spawnPoint = ref ops.Points[spawnPointId];

        if (!IsAllowedLaneCount(spawnPointId))
            return false;
        if (!IsAllowedLane(in spawnPoint))
            return false;
        if (!IsKeepingSafetyDistances(in spawnPoint, previousAi, nextAi))
            return false;

        return EntryCar.CanSpawnAiState(spawnPoint.Position, this);
    }

    private bool IsKeepingSafetyDistances(in SplinePoint spawnPoint, AiState? previousAi, AiState? nextAi)
    {
        if (previousAi != null)
        {
            var distance = MathF.Max(0, Vector3.Distance(spawnPoint.Position, previousAi.Status.Position)
                           - previousAi.EntryCar.VehicleLengthPreMeters
                           - EntryCar.VehicleLengthPostMeters);

            var distanceSquared = distance * distance;
            if (distanceSquared < previousAi.SafetyDistanceSquared || distanceSquared < SafetyDistanceSquared)
                return false;
        }
        
        if (nextAi != null)
        {
            var distance = MathF.Max(0, Vector3.Distance(spawnPoint.Position, nextAi.Status.Position)
                                        - nextAi.EntryCar.VehicleLengthPostMeters
                                        - EntryCar.VehicleLengthPreMeters);

            var distanceSquared = distance * distance;
            if (distanceSquared < nextAi.SafetyDistanceSquared || distanceSquared < SafetyDistanceSquared)
                return false;
        }

        return true;
    }

    private bool IsAllowedLaneCount(int spawnPointId)
    {
        var laneCount = _spline.GetLanes(spawnPointId).Length;
        if (EntryCar.MinLaneCount.HasValue && laneCount < EntryCar.MinLaneCount.Value)
            return false;
        if (EntryCar.MaxLaneCount.HasValue && laneCount > EntryCar.MaxLaneCount.Value)
            return false;
        
        return true;
    }

    private bool IsAllowedLane(in SplinePoint spawnPoint)
    {
        var isAllowedLane = true;
        if (EntryCar.AiAllowedLanes != null)
        {
            isAllowedLane = (EntryCar.AiAllowedLanes.Contains(LaneSpawnBehavior.Middle) && spawnPoint.LeftId >= 0 && spawnPoint.RightId >= 0)
                            || (EntryCar.AiAllowedLanes.Contains(LaneSpawnBehavior.Left) && spawnPoint.LeftId < 0)
                            || (EntryCar.AiAllowedLanes.Contains(LaneSpawnBehavior.Right) && spawnPoint.RightId < 0);
        }

        return isAllowedLane;
    }

    private (AiState? ClosestAiState, float ClosestAiStateDistance, float MaxSpeed) SplineLookahead()
    {
        var points = _spline.Points;
        var junctions = _spline.Junctions;
        
        float maxBrakingDistance = PhysicsUtils.CalculateBrakingDistance(CurrentSpeed, EntryCar.AiDeceleration) * 2 + 20;
        AiState? closestAiState = null;
        float closestAiStateDistance = float.MaxValue;
        bool junctionFound = false;
        float distanceTravelled = 0;
        var pointId = CurrentSplinePointId;
        ref readonly var point = ref points[pointId]; 
        float maxSpeed = float.MaxValue;
        float currentSpeedSquared = CurrentSpeed * CurrentSpeed;
        while (distanceTravelled < maxBrakingDistance)
        {
            distanceTravelled += point.Length;
            pointId = _junctionEvaluator.Next(pointId);
            if (pointId < 0)
                break;

            point = ref points[pointId];

            if (!junctionFound && point.JunctionStartId >= 0 && distanceTravelled < junctions[point.JunctionStartId].IndicateDistancePre)
            {
                ref readonly var jct = ref junctions[point.JunctionStartId];
                
                var indicator = _junctionEvaluator.WillTakeJunction(point.JunctionStartId) ? jct.IndicateWhenTaken : jct.IndicateWhenNotTaken;
                if (indicator != 0)
                {
                    _indicator = indicator;
                    _nextJunctionId = point.JunctionStartId;
                    junctionFound = true;
                }
            }

            if (closestAiState == null)
            {
                var slowest = _spline.SlowestAiStates[pointId];

                if (slowest != null)
                {
                    closestAiState = slowest;
                    closestAiStateDistance = MathF.Max(0, Vector3.Distance(Status.Position, closestAiState.Status.Position)
                                                          - EntryCar.VehicleLengthPreMeters
                                                          - closestAiState.EntryCar.VehicleLengthPostMeters);
                }
            }

            float maxCorneringSpeedSquared = _configuration.Extra.AiParams.Behavior == AiBehaviorMode.Race
                ? RaceBotMath.CorneringSpeedSquared(point.Radius, EntryCar.AiCorneringSpeedFactor,
                    _configuration.Extra.AiParams.Race.Difficulty)
                : PhysicsUtils.CalculateMaxCorneringSpeedSquared(point.Radius, EntryCar.AiCorneringSpeedFactor);
            float pointSpeedLimit = MathF.Sqrt(maxCorneringSpeedSquared);
            if (_configuration.Extra.AiParams.Behavior == AiBehaviorMode.Race)
                pointSpeedLimit = Math.Min(pointSpeedLimit,
                    RaceBotMath.AuthoredSplineSpeedLimit(point.Speed,
                        _configuration.Extra.AiParams.Race.Difficulty));
            if (pointSpeedLimit * pointSpeedLimit < currentSpeedSquared)
            {
                float brakingDistance = PhysicsUtils.CalculateBrakingDistance(CurrentSpeed - pointSpeedLimit,
                                            EntryCar.AiDeceleration * EntryCar.AiCorneringBrakeForceFactor)
                                        * EntryCar.AiCorneringBrakeDistanceFactor;

                if (brakingDistance > distanceTravelled)
                {
                    maxSpeed = Math.Min(pointSpeedLimit, maxSpeed);
                }
            }
        }

        return (closestAiState, closestAiStateDistance, maxSpeed);
    }

    private bool ShouldIgnorePlayerObstacles()
    {
        if (_configuration.Extra.AiParams.IgnorePlayerObstacleSpheres != null)
        {
            foreach (var sphere in _configuration.Extra.AiParams.IgnorePlayerObstacleSpheres)
            {
                if (Vector3.DistanceSquared(Status.Position, sphere.Center) < sphere.RadiusMeters * sphere.RadiusMeters)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private (EntryCar? entryCar, float distance) FindClosestPlayerObstacle()
    {
        if (!ShouldIgnorePlayerObstacles())
        {
            EntryCar? closestCar = null;
            float minDistance = float.MaxValue;
            for (var i = 0; i < _entryCarManager.EntryCars.Length; i++)
            {
                var playerCar = _entryCarManager.EntryCars[i];
                if (playerCar.Client?.HasSentFirstUpdate == true)
                {
                    float distance = Vector3.DistanceSquared(playerCar.Status.Position, Status.Position);

                    if (distance < minDistance
                        && Math.Abs(playerCar.Status.Position.Y - Status.Position.Y) < 1.5
                        && GetAngleToCar(playerCar.Status) is > 166 and < 194)
                    {
                        minDistance = distance;
                        closestCar = playerCar;
                    }
                }
            }

            if (closestCar != null)
            {
                return (closestCar, MathF.Sqrt(minDistance));
            }
        }

        return (null, float.MaxValue);
    }

    private bool TryGetRaceParticipant(EntryCar car, out RaceParticipantSnapshot snapshot)
    {
        if (car.SessionId == EntryCar.SessionId)
        {
            snapshot = default;
            return false;
        }

        if (car.AiControlled)
        {
            var (state, _) = car.GetClosestAiState(Status.Position);
            if (state is not { Initialized: true })
            {
                snapshot = default;
                return false;
            }
            snapshot = new RaceParticipantSnapshot(car, state, state.Status, state.CurrentSpeed);
            return true;
        }

        if (car.Client?.HasSentFirstUpdate == true)
        {
            snapshot = new RaceParticipantSnapshot(car, null, car.Status, car.Status.Velocity.Length());
            return true;
        }

        snapshot = default;
        return false;
    }

    private RaceObstacle? FindClosestRaceObstacle()
    {
        var sample = GetCurrentSplineSample(out _);
        var forward = sample.Tangent with { Y = 0 };
        if (forward.LengthSquared() < 1e-6f)
            return null;
        forward = Vector3.Normalize(forward);
        var lateral = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        float maximumLookahead = Math.Max(60,
            RaceBotMath.OvertakeTriggerDistance(CurrentSpeed,
                _configuration.Extra.AiParams.Race.Aggression) * 1.5f);
        RaceObstacle? closest = null;

        foreach (var car in _entryCarManager.EntryCars)
        {
            if (!TryGetRaceParticipant(car, out var participant)
                || Math.Abs(participant.Status.Position.Y - Status.Position.Y) >= 3)
                continue;

            float longitudinal = GetRaceLongitudinalDistance(participant, forward);
            if (longitudinal <= 0 || longitudinal > maximumLookahead)
                continue;
            var relative = participant.Status.Position - Status.Position;
            relative.Y = 0;
            float relativeLateral = GetParticipantRelativeLateral(participant, relative, lateral);
            if (Math.Abs(relativeLateral) > RaceBotMath.ObstacleCorridorHalfWidthMeters)
                continue;

            float clearance = Math.Max(0, longitudinal - EntryCar.VehicleLengthPreMeters
                                                   - car.VehicleLengthPostMeters);
            if (closest.HasValue && clearance >= closest.Value.ClearanceMeters)
                continue;
            float obstacleOffset = PhysicalLateralOffsetMeters + relativeLateral;
            closest = new RaceObstacle(participant, clearance, longitudinal, obstacleOffset, relativeLateral);
        }

        return closest;
    }

    private bool IsPassTargetOccupied(float targetOffsetMeters, byte passTargetSessionId,
        float obstacleLongitudinalMeters)
    {
        var sample = GetCurrentSplineSample(out _);
        var forward = sample.Tangent with { Y = 0 };
        if (forward.LengthSquared() < 1e-6f)
            return true;
        forward = Vector3.Normalize(forward);
        var lateral = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));

        foreach (var car in _entryCarManager.EntryCars)
        {
            if (car.SessionId == passTargetSessionId
                || !TryGetRaceParticipant(car, out var participant)
                || Math.Abs(participant.Status.Position.Y - Status.Position.Y) >= 3)
                continue;
            float longitudinal = GetRaceLongitudinalDistance(participant, forward);
            if (longitudinal < -RaceBotMath.PassLaneRearReservationMeters
                || longitudinal > obstacleLongitudinalMeters + 10)
                continue;
            var relative = participant.Status.Position - Status.Position;
            relative.Y = 0;
            float currentOffset = PhysicalLateralOffsetMeters
                                  + GetParticipantRelativeLateral(participant, relative, lateral);
            bool occupiesTarget = Math.Abs(currentOffset - targetOffsetMeters)
                                  < RaceBotMath.MinimumPassingSeparationMeters;
            if (!occupiesTarget && participant.AiState?._overtakeTargetSessionId.HasValue == true)
                occupiesTarget = Math.Abs(participant.AiState._targetLateralOffsetMeters
                                         - targetOffsetMeters)
                                 < RaceBotMath.MinimumPassingSeparationMeters;
            if (occupiesTarget)
                return true;
        }

        return false;
    }

    private float GetRaceLongitudinalDistance(RaceParticipantSnapshot participant, Vector3 fallbackForward)
    {
        if (_raceLayout != null && participant.AiState?._raceLayout != null)
        {
            float currentProgress = _currentVecLength > 1e-5f
                ? _currentVecProgress / _currentVecLength
                : 0;
            float participantProgress = participant.AiState._currentVecLength > 1e-5f
                ? participant.AiState._currentVecProgress / participant.AiState._currentVecLength
                : 0;
            return _raceLayout.SignedDistanceAhead(CurrentSplinePointId, currentProgress,
                participant.AiState.CurrentSplinePointId, participantProgress, _spline.Points);
        }

        var relative = participant.Status.Position - Status.Position;
        relative.Y = 0;
        return Vector3.Dot(relative, fallbackForward);
    }

    private float GetParticipantRelativeLateral(RaceParticipantSnapshot participant,
        Vector3 relative, Vector3 localLateral)
    {
        if (participant.AiState is { Initialized: true } aiState)
            return aiState.PhysicalLateralOffsetMeters - PhysicalLateralOffsetMeters;
        return Vector3.Dot(relative, localLateral);
    }

    private void UpdateBaseLaneOffset()
    {
        ref readonly var point = ref _spline.Points[CurrentSplinePointId];
        _baseLateralOffsetMeters = GetRaceBaseLaneOffset(point);
        if (_overtakeTargetSessionId.HasValue
            && TryGetOvertakeTargetWorldRelative(out _, out _, out var targetLateral))
        {
            float obstacleOffset = PhysicalLateralOffsetMeters + targetLateral;
            _targetLateralOffsetMeters = RaceBotMath.CommittedPassTarget(obstacleOffset,
                _passingLeft, point.SideLeft, point.SideRight);
            return;
        }

        if (_gridLineMergePending)
        {
            bool corridorOccupied = IsGridLineMergeCorridorOccupied(_baseLateralOffsetMeters);
            if (!_gridLineMergeStarted)
            {
                bool canBeginMerge = RaceBotMath.CanBeginGridLineMerge(
                    _sessionManager.CurrentSession.Configuration.Type,
                    _sessionManager.ServerTimeMilliseconds,
                    _sessionManager.CurrentSession.StartTimeMilliseconds,
                    _gridLaunchDistanceMeters, corridorOccupied);
                if (canBeginMerge)
                {
                    _gridLineMergeStarted = true;
                    Log.Debug("Race bot {SessionId} joining racing line after {LaunchDistance:F1} m: grid {GridOffset:F2} -> line {LineOffset:F2} m",
                        EntryCar.SessionId, _gridLaunchDistanceMeters,
                        _gridLaunchLateralOffsetMeters, _baseLateralOffsetMeters);
                }
            }

            if (!_gridLineMergeStarted || corridorOccupied)
            {
                _targetLateralOffsetMeters = _lateralOffsetMeters;
                return;
            }

            _targetLateralOffsetMeters = _baseLateralOffsetMeters;
            if (Math.Abs(_lateralOffsetMeters - _targetLateralOffsetMeters) < 0.05f)
                _gridLineMergePending = false;
            return;
        }

        if (_sessionManager.ServerTimeMilliseconds >= _returnToLineAt)
            _targetLateralOffsetMeters = _baseLateralOffsetMeters;
        else
            _targetLateralOffsetMeters = RaceBotMath.ClampLaneOffset(_targetLateralOffsetMeters,
                point.SideLeft, point.SideRight);
    }

    private bool IsGridLineMergeCorridorOccupied(float targetOffsetMeters)
    {
        var sample = GetCurrentSplineSample(out _);
        var forward = sample.Tangent with { Y = 0 };
        if (forward.LengthSquared() < 1e-6f)
            return true;
        forward = Vector3.Normalize(forward);
        var lateral = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));

        foreach (var car in _entryCarManager.EntryCars)
        {
            if (!TryGetRaceParticipant(car, out var participant)
                || Math.Abs(participant.Status.Position.Y - Status.Position.Y) >= 3)
                continue;
            float longitudinal = GetRaceLongitudinalDistance(participant, forward);
            var relative = participant.Status.Position - Status.Position;
            relative.Y = 0;
            float participantOffset = PhysicalLateralOffsetMeters
                                      + GetParticipantRelativeLateral(participant, relative, lateral);
            if (RaceBotMath.OccupiesGridLineMergeCorridor(longitudinal,
                    participantOffset, targetOffsetMeters))
                return true;
        }

        return false;
    }

    private float GetRaceBaseLaneOffset(in SplinePoint point)
    {
        float progress = _currentVecLength > 1e-5f ? _currentVecProgress / _currentVecLength : 0;
        float distance = _raceLayout?.DistanceFromStart(CurrentSplinePointId, progress, _spline.Points) ?? 0;
        return RaceBotMath.RacingLineOffset(point.SideLeft, point.SideRight,
            EntryCar.SessionId, distance);
    }

    private bool TryBeginRaceOvertake(RaceObstacle obstacle)
    {
        if (_configuration.Extra.AiParams.Behavior != AiBehaviorMode.Race
            || obstacle.ClearanceMeters <= RaceBotMath.EmergencyObstacleDistanceMeters
            || _sessionManager.ServerTimeMilliseconds < _returnToLineAt
            || _sessionManager.ServerTimeMilliseconds < _passCooldownUntil)
            return false;
        if (!RaceBotMath.CanAttemptPassPair(obstacle.Participant.Car.SessionId,
                _recentPassTargetSessionId, _sessionManager.ServerTimeMilliseconds,
                _recentPassPairUntil))
            return false;
        if (obstacle.Participant.AiState?._overtakeTargetSessionId == EntryCar.SessionId)
            return false;

        ref readonly var point = ref _spline.Points[CurrentSplinePointId];
        float? target = RaceBotMath.ChoosePassTarget(_lateralOffsetMeters,
            obstacle.LateralOffsetMeters, point.SideLeft, point.SideRight,
            leftBlocked: false, rightBlocked: false, EntryCar.SessionId);
        if (!target.HasValue)
            return false;

        bool chosenLeft = target.Value < obstacle.LateralOffsetMeters;
        if (!RaceBotMath.HasPassTargetClearance(target.Value, obstacle.LateralOffsetMeters,
                obstacle.Participant.Speed)
            || IsPassTargetOccupied(target.Value, obstacle.Participant.Car.SessionId,
                obstacle.LongitudinalMeters))
        {
            target = RaceBotMath.ChoosePassTarget(_lateralOffsetMeters,
                obstacle.LateralOffsetMeters, point.SideLeft, point.SideRight,
                leftBlocked: chosenLeft, rightBlocked: !chosenLeft, EntryCar.SessionId);
            if (!target.HasValue
                || !RaceBotMath.HasPassTargetClearance(target.Value,
                    obstacle.LateralOffsetMeters, obstacle.Participant.Speed)
                || IsPassTargetOccupied(target.Value, obstacle.Participant.Car.SessionId,
                    obstacle.LongitudinalMeters))
                return false;
        }

        if (!RaceBotMath.IsPracticalPassTarget(_lateralOffsetMeters, target.Value,
                obstacle.Participant.Speed))
            return false;

        _targetLateralOffsetMeters = target.Value;
        _passingLeft = target.Value < obstacle.LateralOffsetMeters;
        _overtakeTargetSessionId = obstacle.Participant.Car.SessionId;
        _currentPassSeparationRecorded = false;
        _currentPassAccelerationClearanceRecorded = false;
        _passAccelerationClearanceSince = 0;
        _overtakeExtensionCount = 0;
        PassCommitCount++;
        _overtakeUntil = _sessionManager.ServerTimeMilliseconds
                         + RaceBotMath.OvertakeCommitMilliseconds(
                             _configuration.Extra.AiParams.Race.Aggression,
                             obstacle.ClearanceMeters);
        Log.Debug("Race bot {SessionId} passing {TargetSessionId} at {Distance:F1} m: lane {Current:F2} -> {Target:F2} m, obstacle {Obstacle:F2} m, track L/R {Left:F2}/{Right:F2} m",
            EntryCar.SessionId, obstacle.Participant.Car.SessionId, obstacle.ClearanceMeters,
            _lateralOffsetMeters, target.Value, obstacle.LateralOffsetMeters,
            point.SideLeft, point.SideRight);
        return true;
    }

    private bool IsBeingPassed(bool requireSeparation = false)
    {
        foreach (var car in _entryCarManager.EntryCars)
        {
            if (!TryGetRaceParticipant(car, out var participant))
                continue;
            if (participant.AiState?._overtakeTargetSessionId == EntryCar.SessionId
                && (!requireSeparation
                    || participant.AiState._currentPassAccelerationClearanceRecorded))
                return true;
        }
        return false;
    }

    private float GetSeparatedPasserSpeed()
    {
        float passerSpeed = float.PositiveInfinity;
        foreach (var car in _entryCarManager.EntryCars)
        {
            if (!TryGetRaceParticipant(car, out var participant)
                || participant.AiState?._overtakeTargetSessionId != EntryCar.SessionId
                || !participant.AiState._currentPassSeparationRecorded)
                continue;
            passerSpeed = Math.Min(passerSpeed, participant.Speed);
        }
        return float.IsPositiveInfinity(passerSpeed) ? CurrentSpeed : passerSpeed;
    }

    private void EndRaceOvertake(bool completed)
    {
        if (completed)
        {
            long pairCooldownUntil = _sessionManager.ServerTimeMilliseconds
                                     + RaceBotMath.SamePairPassCooldownMilliseconds;
            _recentPassTargetSessionId = _overtakeTargetSessionId;
            _recentPassPairUntil = pairCooldownUntil;
            CompletedPassCount++;
            _returnToLineAt = _sessionManager.ServerTimeMilliseconds
                              + RaceBotMath.PassLaneReleaseMilliseconds;
            var targetCar = _entryCarManager.EntryCars.FirstOrDefault(car =>
                car.SessionId == _overtakeTargetSessionId);
            if (targetCar != null && TryGetRaceParticipant(targetCar, out var participant)
                                  && participant.AiState != null)
            {
                participant.AiState._passCooldownUntil = Math.Max(
                    participant.AiState._passCooldownUntil,
                    _sessionManager.ServerTimeMilliseconds
                    + RaceBotMath.RecentlyPassedCooldownMilliseconds);
                participant.AiState._recentPassTargetSessionId = EntryCar.SessionId;
                participant.AiState._recentPassPairUntil = pairCooldownUntil;
            }
        }
        else
        {
            _returnToLineAt = _sessionManager.ServerTimeMilliseconds
                              + RaceBotMath.FailedPassLaneReleaseMilliseconds;
            _passCooldownUntil = Math.Max(_passCooldownUntil,
                _returnToLineAt + RaceBotMath.FailedPassRetryMilliseconds);
        }
        _overtakeTargetSessionId = null;
        _overtakeUntil = 0;
        _currentPassSeparationRecorded = false;
        _currentPassAccelerationClearanceRecorded = false;
        _passAccelerationClearanceSince = 0;
        _overtakeExtensionCount = 0;
    }

    private bool HasCompletedRaceOvertake()
    {
        return TryGetOvertakeTargetWorldRelative(out var relative, out var longitudinal, out var lateral)
               && RaceBotMath.HasCompletedPass(_currentPassSeparationRecorded,
                   relative.Length(), longitudinal, lateral);
    }

    private float? GetOvertakeTargetLongitudinalDistance()
    {
        if (!_overtakeTargetSessionId.HasValue)
            return null;
        var targetCar = _entryCarManager.EntryCars.FirstOrDefault(car =>
            car.SessionId == _overtakeTargetSessionId.Value);
        if (targetCar == null || !TryGetRaceParticipant(targetCar, out var participant))
            return null;
        if (TryGetOvertakeTargetWorldRelative(out var relative, out var worldLongitudinal, out _)
            && relative.LengthSquared() <= 20 * 20)
            return worldLongitudinal;
        var sample = GetCurrentSplineSample(out _);
        var forward = sample.Tangent with { Y = 0 };
        if (forward.LengthSquared() < 1e-6f)
            return null;
        forward = Vector3.Normalize(forward);
        return GetRaceLongitudinalDistance(participant, forward);
    }

    private bool TryGetOvertakeTargetWorldRelative(out Vector3 relative,
        out float longitudinal, out float lateral)
    {
        relative = Vector3.Zero;
        longitudinal = 0;
        lateral = 0;
        if (!_overtakeTargetSessionId.HasValue)
            return false;
        var targetCar = _entryCarManager.EntryCars.FirstOrDefault(car =>
            car.SessionId == _overtakeTargetSessionId.Value);
        if (targetCar == null || !TryGetRaceParticipant(targetCar, out var participant))
            return false;

        var sample = GetCurrentSplineSample(out _);
        var forward = sample.Tangent with { Y = 0 };
        if (forward.LengthSquared() < 1e-6f)
            return false;
        forward = Vector3.Normalize(forward);
        var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        relative = participant.Status.Position - Status.Position;
        relative.Y = 0;
        longitudinal = Vector3.Dot(relative, forward);
        lateral = GetParticipantRelativeLateral(participant, relative, right);
        return true;
    }

    private bool IsObstacle(EntryCar playerCar)
    {
        float aiRectWidth = 4; // Lane width
        float halfAiRectWidth = aiRectWidth / 2;
        float aiRectLength = 10; // length of rectangle infront of ai traffic
        float aiRectOffset = 1; // offset of the rectangle from ai position

        float obstacleRectWidth = 1; // width of obstacle car 
        float obstacleRectLength = 1; // length of obstacle car
        float halfObstacleRectWidth = obstacleRectWidth / 2;
        float halfObstanceRectLength = obstacleRectLength / 2;

        Vector3 forward = Vector3.Transform(-Vector3.UnitX, Matrix4x4.CreateRotationY(Status.Rotation.X));
        Matrix4x4 aiViewMatrix = Matrix4x4.CreateLookAt(Status.Position, Status.Position + forward, Vector3.UnitY);

        Matrix4x4 targetWorldViewMatrix = Matrix4x4.CreateRotationY(playerCar.Status.Rotation.X) * Matrix4x4.CreateTranslation(playerCar.Status.Position) * aiViewMatrix;

        Vector3 targetFrontLeft = Vector3.Transform(new Vector3(-halfObstanceRectLength, 0, halfObstacleRectWidth), targetWorldViewMatrix);
        Vector3 targetFrontRight = Vector3.Transform(new Vector3(-halfObstanceRectLength, 0, -halfObstacleRectWidth), targetWorldViewMatrix);
        Vector3 targetRearLeft = Vector3.Transform(new Vector3(halfObstanceRectLength, 0, halfObstacleRectWidth), targetWorldViewMatrix);
        Vector3 targetRearRight = Vector3.Transform(new Vector3(halfObstanceRectLength, 0, -halfObstacleRectWidth), targetWorldViewMatrix);

        static bool IsPointInside(Vector3 point, float width, float length, float offset)
            => MathF.Abs(point.X) >= width || (-point.Z >= offset && -point.Z <= offset + length);

        bool isObstacle = IsPointInside(targetFrontLeft, halfAiRectWidth, aiRectLength, aiRectOffset)
                          || IsPointInside(targetFrontRight, halfAiRectWidth, aiRectLength, aiRectOffset)
                          || IsPointInside(targetRearLeft, halfAiRectWidth, aiRectLength, aiRectOffset)
                          || IsPointInside(targetRearRight, halfAiRectWidth, aiRectLength, aiRectOffset);

        return isObstacle;
    }

    public void DetectObstacles()
    {
        if (!Initialized) return;
        if (_configuration.Extra.AiParams.Behavior == AiBehaviorMode.Race
            && RaceBotMath.ShouldHoldForCountdown(_sessionManager.CurrentSession.Configuration.Type,
                _sessionManager.ServerTimeMilliseconds, _sessionManager.CurrentSession.StartTimeMilliseconds))
        {
            SetTargetSpeed(0);
            return;
        }

        if (_configuration.Extra.AiParams.Behavior == AiBehaviorMode.Race
            && RaceBotMath.IsInRaceLaunchWindow(_sessionManager.CurrentSession.Configuration.Type,
                _sessionManager.ServerTimeMilliseconds, _sessionManager.CurrentSession.StartTimeMilliseconds))
        {
            // Grid rows are intentionally close. Applying normal stopped-car following logic here
            // releases the field one row at a time as each preceding row clears its random gap.
            // Give every participant the same launch window, then resume normal avoidance.
            _stoppedForObstacle = false;
            SetTargetSpeed(InitialMaxSpeed);
            return;
        }
            
        if (_configuration.Extra.AiParams.Behavior != AiBehaviorMode.Race
            && _sessionManager.ServerTimeMilliseconds < _ignoreObstaclesUntil)
        {
            SetTargetSpeed(MaxSpeed);
            return;
        }

        if (_sessionManager.ServerTimeMilliseconds < _stoppedForCollisionUntil)
        {
            SetTargetSpeed(0);
            return;
        }
            
        float targetSpeed = InitialMaxSpeed;
        float maxSpeed = InitialMaxSpeed;
        bool hasObstacle = false;
        bool passingWithSeparation = false;
        bool yieldingToPasser = false;

        var splineLookahead = SplineLookahead();
        var playerObstacle = FindClosestPlayerObstacle();

        if (_configuration.Extra.AiParams.Behavior == AiBehaviorMode.Race)
        {
            UpdateBaseLaneOffset();
            if (_overtakeTargetSessionId.HasValue && HasCompletedRaceOvertake())
            {
                Log.Debug("Race bot {SessionId} completed pass of {TargetSessionId}",
                    EntryCar.SessionId, _overtakeTargetSessionId.Value);
                EndRaceOvertake(completed: true);
            }
            else if (_overtakeUntil != 0
                     && _sessionManager.ServerTimeMilliseconds >= _overtakeUntil)
            {
                float overtakeTargetSpeed = float.NaN;
                var targetCar = _entryCarManager.EntryCars.FirstOrDefault(car =>
                    car.SessionId == _overtakeTargetSessionId);
                if (targetCar != null && TryGetRaceParticipant(targetCar, out var targetParticipant))
                    overtakeTargetSpeed = targetParticipant.Speed;
                float longitudinalGap = GetOvertakeTargetLongitudinalDistance() ?? float.NaN;
                if (_currentPassSeparationRecorded
                    && RaceBotMath.ShouldExtendPass(longitudinalGap, CurrentSpeed,
                        overtakeTargetSpeed, _configuration.Extra.AiParams.Race.Aggression,
                        _overtakeExtensionCount))
                {
                    _overtakeExtensionCount++;
                    _overtakeUntil = _sessionManager.ServerTimeMilliseconds
                                     + RaceBotMath.PassExtensionMilliseconds;
                    Log.Debug("Race bot {SessionId} extending pass of {TargetSessionId} ({Extension}/{MaximumExtensions}) at longitudinal gap {Gap:F1} m, speeds {PasserSpeed:F1}/{TargetSpeed:F1} m/s",
                        EntryCar.SessionId, _overtakeTargetSessionId,
                        _overtakeExtensionCount, RaceBotMath.MaximumPassExtensions, longitudinalGap,
                        CurrentSpeed, overtakeTargetSpeed);
                }
                else
                {
                    Log.Debug("Race bot {SessionId} timed out pass of {TargetSessionId} at longitudinal gap {Gap:F1} m, speeds {PasserSpeed:F1}/{TargetSpeed:F1} m/s",
                        EntryCar.SessionId, _overtakeTargetSessionId, longitudinalGap,
                        CurrentSpeed, overtakeTargetSpeed);
                    EndRaceOvertake(completed: false);
                }
            }

            bool committedOvertake = _overtakeTargetSessionId.HasValue;
            bool beingPassed = IsBeingPassed();
            var raceObstacle = FindClosestRaceObstacle();
            float aggression = _configuration.Extra.AiParams.Race.Aggression;
            ClosestAiObstacleDistance = raceObstacle.HasValue
                                        && raceObstacle.Value.Participant.AiState != null
                ? raceObstacle.Value.ClearanceMeters
                : -1;

            if (!committedOvertake && raceObstacle.HasValue
                && RaceBotMath.CanAttemptPass(_sessionManager.CurrentSession.Configuration.Type,
                    _sessionManager.ServerTimeMilliseconds,
                    _sessionManager.CurrentSession.StartTimeMilliseconds)
                && RaceBotMath.ShouldAttemptPass(CurrentSpeed, raceObstacle.Value.Participant.Speed,
                    raceObstacle.Value.ClearanceMeters, aggression))
                committedOvertake = TryBeginRaceOvertake(raceObstacle.Value);

            if (raceObstacle.HasValue
                && raceObstacle.Value.ClearanceMeters <= RaceBotMath.EmergencyObstacleDistanceMeters
                && Math.Abs(raceObstacle.Value.RelativeLateralMeters)
                < RaceBotMath.MinimumPassingSeparationMeters)
            {
                targetSpeed = 0;
                hasObstacle = true;
            }
            else if (committedOvertake && raceObstacle.HasValue)
            {
                float separation = Math.Abs(raceObstacle.Value.RelativeLateralMeters);
                if (!_currentPassAccelerationClearanceRecorded)
                {
                    targetSpeed = RaceBotMath.CommittedPassApproachSpeed(
                        raceObstacle.Value.Participant.Speed, raceObstacle.Value.ClearanceMeters);
                    hasObstacle = true;
                }
                else
                {
                    passingWithSeparation = true;
                    targetSpeed = RaceBotMath.PassingTargetSpeed(InitialMaxSpeed,
                        EntryCar.RaceVehicleProfile?.TopSpeedMs ?? InitialMaxSpeed,
                        raceObstacle.Value.Participant.Speed, aggression);
                }
            }
            else if (committedOvertake)
            {
                float leadSpeed = 0;
                var targetCar = _entryCarManager.EntryCars.FirstOrDefault(car =>
                    car.SessionId == _overtakeTargetSessionId!.Value);
                if (targetCar != null && TryGetRaceParticipant(targetCar, out var participant))
                    leadSpeed = participant.Speed;
                if (_currentPassAccelerationClearanceRecorded)
                {
                    passingWithSeparation = true;
                    targetSpeed = RaceBotMath.PassingTargetSpeed(InitialMaxSpeed,
                        EntryCar.RaceVehicleProfile?.TopSpeedMs ?? InitialMaxSpeed, leadSpeed, aggression);
                }
                else
                {
                    targetSpeed = RaceBotMath.CommittedPassApproachSpeed(leadSpeed, 0);
                    hasObstacle = true;
                }
            }
            else if (raceObstacle.HasValue
                     && raceObstacle.Value.ClearanceMeters
                     < RaceBotMath.FollowingDecisionDistance(CurrentSpeed,
                         raceObstacle.Value.Participant.Speed, aggression))
            {
                targetSpeed = RaceBotMath.FollowingTargetSpeed(CurrentSpeed,
                    raceObstacle.Value.Participant.Speed, raceObstacle.Value.ClearanceMeters, aggression);
                hasObstacle = true;
            }

            yieldingToPasser = !committedOvertake && beingPassed
                               && IsBeingPassed(requireSeparation: true);
        }
        else if (playerObstacle.distance < _minObstacleDistance
                 || splineLookahead.ClosestAiStateDistance < _minObstacleDistance)
        {
            targetSpeed = 0;
            hasObstacle = true;
        }
        else if (playerObstacle.distance < splineLookahead.ClosestAiStateDistance && playerObstacle.entryCar != null)
        {
            float playerSpeed = playerObstacle.entryCar.Status.Velocity.Length();

            if (playerSpeed < 0.1f)
            {
                playerSpeed = 0;
            }

            if ((playerSpeed < CurrentSpeed || playerSpeed == 0)
                && playerObstacle.distance < PhysicsUtils.CalculateBrakingDistance(CurrentSpeed - playerSpeed, EntryCar.AiDeceleration) * 2 + 20)
            {
                targetSpeed = Math.Max(WalkingSpeed, playerSpeed);
                hasObstacle = true;
            }
        }
        else if (splineLookahead.ClosestAiState != null)
        {
            float closestTargetSpeed = Math.Min(splineLookahead.ClosestAiState.CurrentSpeed, splineLookahead.ClosestAiState.TargetSpeed);
            float followingGap = PhysicsUtils.CalculateBrakingDistance(CurrentSpeed - closestTargetSpeed,
                EntryCar.AiDeceleration) * 2 + 20;
            if ((closestTargetSpeed < CurrentSpeed || splineLookahead.ClosestAiState.CurrentSpeed == 0)
                && splineLookahead.ClosestAiStateDistance < followingGap)
            {
                targetSpeed = Math.Max(WalkingSpeed, closestTargetSpeed);
                hasObstacle = true;
            }
        }

        float normalRouteSpeedLimit = splineLookahead.MaxSpeed;
        if (_configuration.Extra.AiParams.Behavior == AiBehaviorMode.Race)
            normalRouteSpeedLimit *= RaceBotMath.GridPaceFactor(
                _configuration.Extra.AiParams.MaxSpeedVariationPercent, EntryCar.SessionId);
        float routeSpeedLimit = passingWithSeparation
            ? RaceBotMath.PassingCornerSpeedLimit(normalRouteSpeedLimit,
                EntryCar.RaceVehicleProfile?.TopSpeedMs ?? InitialMaxSpeed,
                _configuration.Extra.AiParams.Race.Aggression)
            : normalRouteSpeedLimit;
        targetSpeed = Math.Min(routeSpeedLimit, targetSpeed);
        if (yieldingToPasser && !_yieldingToPasser)
            _yieldSpeedReference = CurrentSpeed;
        if (yieldingToPasser)
            targetSpeed = RaceBotMath.YieldingTargetSpeed(targetSpeed, _yieldSpeedReference,
                GetSeparatedPasserSpeed(), _configuration.Extra.AiParams.Race.Aggression);
        if (yieldingToPasser != _yieldingToPasser)
        {
            Log.Debug("Race bot {SessionId} {YieldState} for an active pass at {TargetSpeed:F1} m/s",
                EntryCar.SessionId, yieldingToPasser ? "yielding" : "stopped yielding",
                targetSpeed);
            _yieldingToPasser = yieldingToPasser;
        }
        if (!yieldingToPasser)
            _yieldSpeedReference = 0;

        if (CurrentSpeed == 0 && !_stoppedForObstacle)
        {
            _stoppedForObstacle = true;
            _stoppedForObstacleSince = _sessionManager.ServerTimeMilliseconds;
            _obstacleHonkStart = _stoppedForObstacleSince + Random.Shared.Next(3000, 7000);
            _obstacleHonkEnd = _obstacleHonkStart + Random.Shared.Next(500, 1500);
            Log.Verbose("AI {SessionId} stopped for obstacle", EntryCar.SessionId);
        }
        else if (CurrentSpeed > 0 && _stoppedForObstacle)
        {
            _stoppedForObstacle = false;
            Log.Verbose("AI {SessionId} no longer stopped for obstacle", EntryCar.SessionId);
        }
        else if (_configuration.Extra.AiParams.Behavior != AiBehaviorMode.Race
                 && _stoppedForObstacle
                 && _sessionManager.ServerTimeMilliseconds - _stoppedForObstacleSince
                 > _configuration.Extra.AiParams.IgnoreObstaclesAfterMilliseconds)
        {
            _ignoreObstaclesUntil = _sessionManager.ServerTimeMilliseconds + 10_000;
            Log.Verbose("AI {SessionId} ignoring obstacles until {IgnoreObstaclesUntil}", EntryCar.SessionId, _ignoreObstaclesUntil);
        }

        float deceleration = EntryCar.AiDeceleration;
        if (!hasObstacle)
        {
            deceleration *= EntryCar.AiCorneringBrakeForceFactor;
        }
        
        MaxSpeed = maxSpeed;
        SetTargetSpeed(targetSpeed, deceleration, EntryCar.AiAcceleration);
    }

    public void StopForCollision()
    {
        if (!ShouldIgnorePlayerObstacles())
        {
            var duration = _configuration.Extra.AiParams.Behavior == AiBehaviorMode.Race
                ? RaceBotMath.CollisionRecoveryMilliseconds(EntryCar.AiMinCollisionStopTimeMilliseconds,
                    EntryCar.AiMaxCollisionStopTimeMilliseconds, _configuration.Extra.AiParams.Race.Aggression,
                    EntryCar.SessionId + (int)_sessionManager.ServerTimeMilliseconds)
                : Random.Shared.Next(EntryCar.AiMinCollisionStopTimeMilliseconds, EntryCar.AiMaxCollisionStopTimeMilliseconds);
            _stoppedForCollisionUntil = _sessionManager.ServerTimeMilliseconds + duration;
        }
    }

    /// <returns>0 is the rear <br/> Angle is counterclockwise</returns>
    public float GetAngleToCar(CarStatus car)
    {
        float challengedAngle = (float) (Math.Atan2(Status.Position.X - car.Position.X, Status.Position.Z - car.Position.Z) * 180 / Math.PI);
        if (challengedAngle < 0)
            challengedAngle += 360;
        float challengedRot = Status.GetRotationAngle();

        challengedAngle += challengedRot;
        challengedAngle %= 360;

        return challengedAngle;
    }

    private void SetTargetSpeed(float speed, float deceleration, float acceleration)
    {
        TargetSpeed = speed;
        if (speed < CurrentSpeed)
        {
            Acceleration = -deceleration;
        }
        else if (speed > CurrentSpeed)
        {
            Acceleration = acceleration;
        }
        else
        {
            Acceleration = 0;
        }
    }

    private void SetTargetSpeed(float speed)
    {
        SetTargetSpeed(speed, EntryCar.AiDeceleration, EntryCar.AiAcceleration);
    }

    private RaceGridPose CreateSplinePose(int pointId)
    {
        if (!_junctionEvaluator.TryNext(pointId, out var nextPointId))
            throw new InvalidOperationException($"Cannot orient race bot at spline point {pointId}");
        var tangent = Vector3.Normalize(_spline.Points[nextPointId].Position - _spline.Points[pointId].Position);
        var rotation = new Vector3
        {
            X = MathF.Atan2(tangent.Z, tangent.X) - MathF.PI / 2,
            Y = (MathF.Atan2(new Vector2(tangent.Z, tangent.X).Length(), tangent.Y) - MathF.PI / 2) * -1f,
            Z = _spline.Operations.GetCamber(pointId, 0)
        };
        return new RaceGridPose(_spline.Points[pointId].Position,
            RacePhysicsMath.FromProtocolRotation(rotation));
    }

    private CatmullRom.CatmullRomPoint GetCurrentSplineSample(out int nextPoint)
    {
        if (!_junctionEvaluator.TryNext(CurrentSplinePointId, out nextPoint))
            throw new InvalidOperationException($"Cannot get next spline point for {CurrentSplinePointId}");
        return CatmullRom.Evaluate(_spline.Points[CurrentSplinePointId].Position,
            _spline.Points[nextPoint].Position, _startTangent, _endTangent,
            _currentVecLength > 1e-5f ? _currentVecProgress / _currentVecLength : 0);
    }

    public void PrepareRacePhysics(float fixedDeltaSeconds)
    {
        if (!Initialized || _racePhysicsWorld == null || EntryCar.RaceVehicleProfile == null)
            return;

        long currentTime = _sessionManager.ServerTimeMilliseconds;
        bool raceCountdown = RaceBotMath.ShouldHoldForCountdown(_sessionManager.CurrentSession.Configuration.Type,
            currentTime, _sessionManager.CurrentSession.StartTimeMilliseconds);
        if (raceCountdown)
        {
            _holdingForRaceStart = true;
            CurrentSpeed = 0;
            TargetSpeed = 0;
            Acceleration = 0;
        }
        else if (_holdingForRaceStart)
        {
            _holdingForRaceStart = false;
            SetTargetSpeed(InitialMaxSpeed);
        }

        if (RaceBotMath.CanTransitionLane(_sessionManager.CurrentSession.Configuration.Type,
                currentTime, _sessionManager.CurrentSession.StartTimeMilliseconds))
            _lateralOffsetMeters = RaceBotMath.AdvanceLaneOffset(_lateralOffsetMeters,
                _targetLateralOffsetMeters, CurrentSpeed, fixedDeltaSeconds,
                _overtakeTargetSessionId.HasValue ? 4
                : _gridLineMergePending ? RaceBotMath.RaceGridMergeTransitionMultiplier : 1);

        var sample = GetCurrentSplineSample(out _);
        var tangent = Vector3.Normalize(sample.Tangent);
        var lateral = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, tangent));
        var targetPosition = sample.Position + lateral * _lateralOffsetMeters;
        var vehicleStep = RaceBotVehicleDynamics.Step(CurrentSpeed, TargetSpeed, fixedDeltaSeconds,
            EntryCar.RaceVehicleProfile);
        float maximumAcceleration = vehicleStep.AccelerationMetersPerSecondSquared > 0
            ? vehicleStep.AccelerationMetersPerSecondSquared
            : EntryCar.AiAcceleration;
        _racePhysicsWorld.SetBotControl(EntryCar.SessionId, new RaceBotPhysicsControl(raceCountdown,
            targetPosition, tangent, TargetSpeed, Math.Max(0.1f, maximumAcceleration),
            EntryCar.RaceVehicleProfile.MaxBrakeDeceleration, EntryCar.RaceVehicleProfile.LateralGripG));
        _lastTick = currentTime;
    }

    public void CompleteRacePhysics(float fixedDeltaSeconds)
    {
        if (!Initialized || _racePhysicsWorld == null
            || !_racePhysicsWorld.TryGetBotState(EntryCar.SessionId, out var physicsState))
            return;

        var sampleBeforeMove = GetCurrentSplineSample(out _);
        var forward = Vector3.Normalize(sampleBeforeMove.Tangent);
        float forwardProgress = CalculatePhysicsForwardProgress(physicsState.Position, _physicsLastPosition,
            forward, physicsState.RecoveryCount, _physicsRecoveryCount);
        if (_gridLineMergePending)
            _gridLaunchDistanceMeters += Math.Max(0, forwardProgress);
        if (!Move(_currentVecProgress + forwardProgress))
        {
            Despawn();
            return;
        }

        _physicsLastPosition = physicsState.Position;
        _physicsRecoveryCount = physicsState.RecoveryCount;
        var currentSample = GetCurrentSplineSample(out _);
        var currentForward = currentSample.Tangent with { Y = 0 };
        if (currentForward.LengthSquared() > 1e-6f)
        {
            currentForward = Vector3.Normalize(currentForward);
            var lateral = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, currentForward));
            PhysicalLateralOffsetMeters = Vector3.Dot(physicsState.Position - currentSample.Position, lateral);
            bool afterLaunchWindow = !RaceBotMath.ShouldHoldForCountdown(
                                         _sessionManager.CurrentSession.Configuration.Type,
                                         _sessionManager.ServerTimeMilliseconds,
                                         _sessionManager.CurrentSession.StartTimeMilliseconds)
                                     && !RaceBotMath.IsInRaceLaunchWindow(
                                         _sessionManager.CurrentSession.Configuration.Type,
                                         _sessionManager.ServerTimeMilliseconds,
                                         _sessionManager.CurrentSession.StartTimeMilliseconds);
            if (afterLaunchWindow)
                MaximumAbsoluteLateralOffsetMeters = Math.Max(MaximumAbsoluteLateralOffsetMeters,
                    Math.Abs(PhysicalLateralOffsetMeters));
            if (_overtakeTargetSessionId.HasValue)
            {
                bool hasWorldRelative = TryGetOvertakeTargetWorldRelative(out var relative,
                    out var longitudinalGap, out var relativeLateral);
                float separation = Math.Abs(relativeLateral);
                bool closeEnoughToEvaluate = hasWorldRelative
                                             && relative.LengthSquared()
                                             <= RaceBotMath.PassSeparationEvaluationDistanceMeters
                                             * RaceBotMath.PassSeparationEvaluationDistanceMeters
                                             && longitudinalGap
                                             <= RaceBotMath.PassSeparationEvaluationDistanceMeters
                                             && separation
                                             <= RaceBotMath.MaximumPlausiblePassSeparationMeters;
                if (closeEnoughToEvaluate)
                    MaximumPassSeparationMeters = Math.Max(MaximumPassSeparationMeters, separation);
                if (!_currentPassSeparationRecorded
                    && closeEnoughToEvaluate
                    && separation >= RaceBotMath.MinimumPassingSeparationMeters)
                {
                    _currentPassSeparationRecorded = true;
                    SeparatedPassCount++;
                    Log.Debug("Race bot {SessionId} achieved passing separation from {TargetSessionId}: {Separation:F2} m",
                        EntryCar.SessionId, _overtakeTargetSessionId.Value, separation);
                }
                if (!_currentPassAccelerationClearanceRecorded && closeEnoughToEvaluate)
                {
                    if (RaceBotMath.ShouldResetPassAccelerationClearance(separation))
                        _passAccelerationClearanceSince = 0;
                    else if (_passAccelerationClearanceSince == 0
                             && RaceBotMath.HasPassAccelerationClearance(separation))
                        _passAccelerationClearanceSince = _sessionManager.ServerTimeMilliseconds;
                    else if (RaceBotMath.HasSustainedPassAccelerationClearance(separation,
                                 _passAccelerationClearanceSince,
                                 _sessionManager.ServerTimeMilliseconds))
                    {
                        _currentPassAccelerationClearanceRecorded = true;
                        Log.Debug("Race bot {SessionId} sustained pass acceleration clearance from {TargetSessionId}: {Separation:F2} m",
                            EntryCar.SessionId, _overtakeTargetSessionId.Value, separation);
                    }
                }
            }
        }
        CurrentSpeed = Math.Max(0, physicsState.ForwardSpeed);
        Acceleration = physicsState.LongitudinalAcceleration;
        _steeringAngleRadians = physicsState.SteeringAngleRadians;
        Status.Timestamp = _sessionManager.ServerTimeMilliseconds;
        Status.Position = physicsState.ProtocolPosition;
        Status.Rotation = RacePhysicsMath.ToProtocolRotation(physicsState.Orientation);
        Status.Velocity = physicsState.Velocity;
        ApplyStatusTelemetry();
    }

    internal static float CalculatePhysicsForwardProgress(Vector3 position, Vector3 previousPosition,
        Vector3 forward, int recoveryCount, int previousRecoveryCount) =>
        recoveryCount == previousRecoveryCount
            ? Math.Max(0, Vector3.Dot(position - previousPosition, forward))
            : 0;

    private void ApplyStatusTelemetry()
    {
        float tyreAngularSpeed = GetTyreAngularSpeed(CurrentSpeed, EntryCar.TyreDiameterMeters);
        byte encodedTyreAngularSpeed = (byte)(Math.Clamp(
            MathF.Round(MathF.Log10(tyreAngularSpeed + 1f) * 20f) * Math.Sign(tyreAngularSpeed), -100f, 154f) + 100f);
        byte encodedSteering = _configuration.Extra.AiParams.Behavior == AiBehaviorMode.Race
            ? RaceBotPhysicsWorld.EncodeSteeringAngle(_steeringAngleRadians)
            : (byte)127;
        Status.SteerAngle = encodedSteering;
        Status.WheelAngle = encodedSteering;
        Status.TyreAngularSpeed[0] = encodedTyreAngularSpeed;
        Status.TyreAngularSpeed[1] = encodedTyreAngularSpeed;
        Status.TyreAngularSpeed[2] = encodedTyreAngularSpeed;
        Status.TyreAngularSpeed[3] = encodedTyreAngularSpeed;
        if (EntryCar.RaceVehicleProfile != null)
        {
            var telemetry = RaceBotVehicleDynamics.GetTelemetry(CurrentSpeed, EntryCar.RaceVehicleProfile);
            Status.EngineRpm = telemetry.EngineRpm;
            Status.Gear = telemetry.ProtocolGear;
        }
        Status.StatusFlag = GetLights(_configuration.Extra.AiParams.EnableDaytimeLights,
                                _weatherManager.CurrentSunPosition, _randomTwilight)
                            | (_sessionManager.ServerTimeMilliseconds < _stoppedForCollisionUntil
                               || CurrentSpeed < 20 / 3.6f ? CarStatusFlags.HazardsOn : 0)
                            | (CurrentSpeed == 0 || Acceleration < 0 ? CarStatusFlags.BrakeLightsOn : 0)
                            | (_stoppedForObstacle && _sessionManager.ServerTimeMilliseconds > _obstacleHonkStart
                               && _sessionManager.ServerTimeMilliseconds < _obstacleHonkEnd ? CarStatusFlags.Horn : 0)
                            | GetWiperSpeed(_weatherManager.CurrentWeather.RainIntensity)
                            | _indicator;
    }

    public void Update(float? fixedDeltaSeconds = null)
    {
        if (!Initialized)
            return;

        var ops = _spline.Operations;

        long currentTime = _sessionManager.ServerTimeMilliseconds;
        long dt = fixedDeltaSeconds.HasValue
            ? (long)Math.Round(fixedDeltaSeconds.Value * 1000)
            : currentTime - _lastTick;
        _lastTick = currentTime;

        bool raceCountdown = _configuration.Extra.AiParams.Behavior == AiBehaviorMode.Race
                             && RaceBotMath.ShouldHoldForCountdown(_sessionManager.CurrentSession.Configuration.Type,
                                 currentTime, _sessionManager.CurrentSession.StartTimeMilliseconds);
        if (raceCountdown)
        {
            _holdingForRaceStart = true;
            CurrentSpeed = 0;
            TargetSpeed = 0;
            Acceleration = 0;
            dt = 0;
        }
        else if (_holdingForRaceStart)
        {
            _holdingForRaceStart = false;
            SetTargetSpeed(InitialMaxSpeed);
        }

        if (_configuration.Extra.AiParams.Behavior == AiBehaviorMode.Race
            && EntryCar.RaceVehicleProfile != null)
        {
            var step = RaceBotVehicleDynamics.Step(CurrentSpeed, TargetSpeed, dt / 1000f,
                EntryCar.RaceVehicleProfile);
            CurrentSpeed = step.SpeedMetersPerSecond;
            Acceleration = step.AccelerationMetersPerSecondSquared;
        }
        else if (Acceleration != 0)
        {
            CurrentSpeed += Acceleration * (dt / 1000.0f);
                
            if ((Acceleration < 0 && CurrentSpeed < TargetSpeed) || (Acceleration > 0 && CurrentSpeed > TargetSpeed))
            {
                CurrentSpeed = TargetSpeed;
                Acceleration = 0;
            }
        }

        float moveMeters = (dt / 1000.0f) * CurrentSpeed;
        if (!Move(_currentVecProgress + moveMeters) || !_junctionEvaluator.TryNext(CurrentSplinePointId, out var nextPoint))
        {
            Log.Debug("Car {SessionId} reached spline end, despawning", EntryCar.SessionId);
            Despawn();
            return;
        }

        CatmullRom.CatmullRomPoint smoothPos = CatmullRom.Evaluate(ops.Points[CurrentSplinePointId].Position, 
            ops.Points[nextPoint].Position, 
            _startTangent, 
            _endTangent, 
            _currentVecProgress / _currentVecLength);
            
        Vector3 rotation = new Vector3
        {
            X = MathF.Atan2(smoothPos.Tangent.Z, smoothPos.Tangent.X) - MathF.PI / 2,
            Y = (MathF.Atan2(new Vector2(smoothPos.Tangent.Z, smoothPos.Tangent.X).Length(), smoothPos.Tangent.Y) - MathF.PI / 2) * -1f,
            Z = ops.GetCamber(CurrentSplinePointId, _currentVecProgress / _currentVecLength)
        };

        float tyreAngularSpeed = GetTyreAngularSpeed(CurrentSpeed, EntryCar.TyreDiameterMeters);
        byte encodedTyreAngularSpeed =  (byte) (Math.Clamp(MathF.Round(MathF.Log10(tyreAngularSpeed + 1.0f) * 20.0f) * Math.Sign(tyreAngularSpeed), -100.0f, 154.0f) + 100.0f);

        float lateralStep = Math.Max(0, dt / 1000f) * 1.5f;
        _lateralOffsetMeters = Math.Abs(_targetLateralOffsetMeters - _lateralOffsetMeters) <= lateralStep
            ? _targetLateralOffsetMeters
            : _lateralOffsetMeters + Math.Sign(_targetLateralOffsetMeters - _lateralOffsetMeters) * lateralStep;
        var lateral = Vector3.Cross(Vector3.UnitY, Vector3.Normalize(smoothPos.Tangent));

        Status.Timestamp = _sessionManager.ServerTimeMilliseconds;
        Status.Position = (smoothPos.Position + lateral * _lateralOffsetMeters) with { Y = smoothPos.Position.Y + EntryCar.AiSplineHeightOffsetMeters };
        Status.Rotation = rotation;
        Status.Velocity = smoothPos.Tangent * CurrentSpeed;
        Status.SteerAngle = 127;
        Status.WheelAngle = 127;
        Status.TyreAngularSpeed[0] = encodedTyreAngularSpeed;
        Status.TyreAngularSpeed[1] = encodedTyreAngularSpeed;
        Status.TyreAngularSpeed[2] = encodedTyreAngularSpeed;
        Status.TyreAngularSpeed[3] = encodedTyreAngularSpeed;
        if (EntryCar.RaceVehicleProfile != null)
        {
            var telemetry = RaceBotVehicleDynamics.GetTelemetry(CurrentSpeed, EntryCar.RaceVehicleProfile);
            Status.EngineRpm = telemetry.EngineRpm;
            Status.Gear = telemetry.ProtocolGear;
        }
        else
        {
            Status.EngineRpm = (ushort)MathUtils.Lerp(EntryCar.AiIdleEngineRpm, EntryCar.AiMaxEngineRpm,
                CurrentSpeed / _configuration.Extra.AiParams.MaxSpeedMs);
            Status.Gear = 2;
        }
        Status.StatusFlag = GetLights(_configuration.Extra.AiParams.EnableDaytimeLights, _weatherManager.CurrentSunPosition, _randomTwilight)
                            | (_sessionManager.ServerTimeMilliseconds < _stoppedForCollisionUntil || CurrentSpeed < 20 / 3.6f ? CarStatusFlags.HazardsOn : 0)
                            | (CurrentSpeed == 0 || Acceleration < 0 ? CarStatusFlags.BrakeLightsOn : 0)
                            | (_stoppedForObstacle && _sessionManager.ServerTimeMilliseconds > _obstacleHonkStart && _sessionManager.ServerTimeMilliseconds < _obstacleHonkEnd ? CarStatusFlags.Horn : 0)
                            | GetWiperSpeed(_weatherManager.CurrentWeather.RainIntensity)
                            | _indicator;
    }
        
    private static float GetTyreAngularSpeed(float speed, float wheelDiameter)
    {
        return speed / (MathF.PI * wheelDiameter) * 6;
    }

    private static CarStatusFlags GetWiperSpeed(float rainIntensity)
    {
        return rainIntensity switch
        {
            < 0.05f => 0,
            < 0.25f => CarStatusFlags.WiperLevel1,
            < 0.5f => CarStatusFlags.WiperLevel2,
            _ => CarStatusFlags.WiperLevel3
        };
    }
    
    private static CarStatusFlags GetLights(bool daytimeLights, SunPosition? sunPosition, double twilight)
    {
        const CarStatusFlags lightFlags = CarStatusFlags.LightsOn | CarStatusFlags.HighBeamsOff;
        if (daytimeLights || sunPosition == null) return lightFlags;

        return sunPosition.Value.Altitude < twilight ? lightFlags : 0;
    }
}
