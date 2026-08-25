using System;
using System.Collections.Generic;
using System.Numerics;
using AssettoServer.Server.Configuration.Extra;
using AssettoServer.Shared.Model;
using AssettoServer.Utils;

namespace AssettoServer.Server.Ai;

public static class RaceBotMath
{
    public const int RaceLaunchGraceMilliseconds = 500;
    public const int RaceLaneTransitionDelayMilliseconds = 2_000;
    public const float RaceGridForwardLaunchDistanceMeters = 25f;
    public const float RaceGridMergeRearClearanceMeters = 8f;
    public const float RaceGridMergeFrontClearanceMeters = 14f;
    public const float RaceGridMergeTransitionMultiplier = 0.5f;
    public const float RaceGridPathBlendDistanceMeters = 40f;
    public const int RacePassStartDelayMilliseconds = 4_000;
    public const float EmergencyObstacleDistanceMeters = 3f;
    public const float MinimumMovingPassCommitClearanceMeters = 8f;
    public const float MinimumStoppedPassCommitClearanceMeters = 1f;
    public const float StoppedObstacleSpeedMetersPerSecond = 1.5f;
    public const float StoppedObstaclePassPlanningDistanceMeters = 30f;
    public const float MaximumPredictivePassPlanningDistanceMeters = 180f;
    public const float PredictiveCorridorLongitudinalBufferMeters = 1.5f;
    public const float PredictiveCorridorRiskDistanceMeters = 12f;
    public const float PredictiveCorridorRiskLateralMeters = 2f;
    public const float PredictivePassSafetyBufferMeters = 0.45f;
    public const float PredictiveHumanUncertaintyMeters = 0.6f;
    public const float StoppedObstacleSafetyMarginMeters = 0.10f;
    public const float StoppedObstacleTargetBufferMeters = 0.5f;
    public const float PassSeparationEvaluationDistanceMeters = 15f;
    public const float MaximumPlausiblePassSeparationMeters = 6f;
    public const float MinimumPassingSeparationMeters = 2.5f;
    public const float MinimumPassAccelerationSeparationMeters = 2.75f;
    public const float PassAccelerationClearanceResetMeters = 2.55f;
    public const int PassAccelerationClearanceHoldMilliseconds = 2_000;
    public const float MinimumPassTargetSeparationMeters = MinimumPassingSeparationMeters;
    public const float MaximumMovingPassLaneChangeMeters = 6.5f;
    public const float MaximumRacePassCenterOffsetMeters = 5.5f;
    // Keep broad obstacle discovery even when a narrow but physically usable pass is legal.
    public const float ObstacleCorridorHalfWidthMeters = 3.1f;
    public const float PassCompletionClearanceMeters = 16f;
    public const float PassCompletionEvaluationDistanceMeters = 20f;
    public const int PassLaneReleaseMilliseconds = 8_000;
    public const int RecentlyPassedCooldownMilliseconds = 15_000;
    public const int FailedPassRetryMilliseconds = 3_000;
    public const int FailedPassLaneReleaseMilliseconds = 3_000;
    public const int SamePairPassCooldownMilliseconds = 90_000;
    public const int PassExtensionMilliseconds = 15_000;
    public const int MaximumPassExtensions = 4;
    public const float PassLaneRearReservationMeters = 15f;
    public const float StoppedQueueLinkDistanceMeters = 14f;
    public const float StoppedQueueLateralCorridorMeters = 5.5f;
    public const float StoppedQueueApproachSpeedMetersPerSecond = 8f;
    public const float StoppedPassMinimumPlanningSpeedMetersPerSecond = 2.5f;
    public const int BlockedPassContactDelayMilliseconds = 750;
    public const int BlockedPassReverseMilliseconds = 1_200;
    public const int MaximumBlockedPassRecoveryAttempts = 2;
    public const int StoppedPassRecoveryCooldownMilliseconds = 4_000;
    public const long RecentVehicleContactStepWindow = 12;
    public const int RaceContactPersistenceMilliseconds = 2_000;
    public const int StoppedPassCompletionHoldMilliseconds = 500;
    public const float CongestionCrawlSpeedMetersPerSecond = 2.5f;
    public const int FieldDeadlockMinimumBotCount = 4;
    public const float FieldDeadlockMinimumStoppedRatio = 0.75f;
    public const float FieldDeadlockSpeedMetersPerSecond = 0.75f;
    public const int FieldDeadlockDetectionMilliseconds = 6_000;
    public const int FieldDeadlockRecoveryCooldownMilliseconds = 4_000;
    public const float ImmobilizedRaceBotSpeedMetersPerSecond = 0.25f;
    public const int ImmobilizedRaceBotRecoveryMilliseconds = 8_000;
    public const int ImmobilizedRaceBotRecoverySpacingMilliseconds = 1_500;
    public const float FieldDeadlockRecoveryAdvanceMeters = 18f;
    public const float FieldDeadlockRecoveryClearanceMeters = 8f;
    public const float MaximumFieldDeadlockRecoveryAdvanceMeters = 60f;
    public const float RaceCornerBrakeDistanceFactor = 1.15f;
    public const float RaceCornerBrakeForceFactor = 0.85f;
    public const float RaceCornerStabilitySpeedFactor = 0.95f;
    public const float MinimumLaneCommandLeadMeters = 1.25f;
    public const float MaximumLaneCommandLeadMeters = 2.25f;
    public const float FullOffroadPenaltyMeters = 2f;
    public const float MinimumOffroadTargetSpeedScale = 0.45f;
    public const float MaximumOffroadDragDeceleration = 3f;
    public const float MaximumOffroadRecoveryEdgeReserveMeters = 1.5f;
    private const float CarHalfWidthWithMarginMeters = 1.25f;
    private const float PreferredPassingSeparationMeters = 3.8f;

    public static float PassTrackEdgeReserveMeters(float speedMetersPerSecond) =>
        Math.Clamp(0.25f + Math.Max(0, speedMetersPerSecond) * 0.015f, 0.35f, 0.8f);

    public static float MaximumPredictivePassCenterOffsetMeters(float speedMetersPerSecond) =>
        Math.Clamp(4.5f - Math.Max(0, speedMetersPerSecond) * 0.02f,
            3.8f, 4.5f);

    public static bool IsStoppedObstacle(float speedMetersPerSecond) =>
        Math.Max(0, speedMetersPerSecond) < StoppedObstacleSpeedMetersPerSecond;

    public static bool ShouldUsePredictiveCorridorPlanner(float currentSpeed,
        float leadSpeed)
    {
        _ = currentSpeed;
        return IsStoppedObstacle(leadSpeed);
    }

    public static bool ShouldStopAfterReportedCollision(AiBehaviorMode behavior) =>
        behavior != AiBehaviorMode.Race;

    public static float CongestionCrawlTargetSpeed(float leadSpeedMetersPerSecond,
        float clearanceMeters, int nearbyParticipantCount = 1)
    {
        float leadSpeed = Math.Max(0, leadSpeedMetersPerSecond);
        float clearanceAllowance = Math.Clamp(Math.Max(0, clearanceMeters) * 0.25f, 0, 1.5f);
        // A dense race pack should keep rolling and create lateral opportunities instead of
        // settling into a traffic-style stop-go queue. Keep the pressure bounded so collision
        // avoidance and the physical speed controller remain authoritative.
        float packPressure = Math.Clamp(Math.Max(0, nearbyParticipantCount - 1) * 0.30f,
            0, 1.2f);
        return Math.Clamp(Math.Max(CongestionCrawlSpeedMetersPerSecond, leadSpeed)
                          + clearanceAllowance + packPressure,
            CongestionCrawlSpeedMetersPerSecond, 5f);
    }

    public static bool ShouldAbandonBlockedPass(int recoveryAttemptCount) =>
        recoveryAttemptCount >= MaximumBlockedPassRecoveryAttempts;

    public static bool CanUseFieldDeadlockRecovery(bool aiControlled, AiBehaviorMode behavior,
        RaceControlBotControlMode controlMode) => aiControlled
                                                  && behavior == AiBehaviorMode.Race
                                                  && controlMode
                                                  == RaceControlBotControlMode.Automatic;

    public static bool IsFieldStalled(int activeBotCount, int stoppedBotCount,
        float maximumSpeedMetersPerSecond) => activeBotCount >= FieldDeadlockMinimumBotCount
                                             && stoppedBotCount
                                             >= Math.Ceiling(activeBotCount
                                                 * FieldDeadlockMinimumStoppedRatio)
                                             && maximumSpeedMetersPerSecond
                                             <= FieldDeadlockSpeedMetersPerSecond;

    public static bool IsFieldDeadlocked(int activeBotCount, int stoppedBotCount,
        float maximumSpeedMetersPerSecond, long stalledMilliseconds) =>
        IsFieldStalled(activeBotCount, stoppedBotCount, maximumSpeedMetersPerSecond)
        && stalledMilliseconds >= FieldDeadlockDetectionMilliseconds;

    public static bool IsRaceBotImmobilized(float speedMetersPerSecond,
        long stalledMilliseconds) => Math.Max(0, speedMetersPerSecond)
                                     <= ImmobilizedRaceBotSpeedMetersPerSecond
                                     && stalledMilliseconds
                                     >= ImmobilizedRaceBotRecoveryMilliseconds;

    public static bool StoppedPassReservationsConflict(byte firstTargetSessionId,
        IReadOnlyList<byte> firstClusterSessionIds, bool firstPassingLeft,
        float firstTargetOffsetMeters, byte secondTargetSessionId,
        IReadOnlyList<byte> secondClusterSessionIds, bool secondPassingLeft,
        float secondTargetOffsetMeters, float minimumLaneSeparationMeters)
    {
        bool sameCluster = firstTargetSessionId == secondTargetSessionId
                           || Contains(firstClusterSessionIds, secondTargetSessionId)
                           || Contains(secondClusterSessionIds, firstTargetSessionId)
                           || Overlaps(firstClusterSessionIds, secondClusterSessionIds);
        if (!sameCluster)
            return false;

        return firstPassingLeft == secondPassingLeft
               || Math.Abs(firstTargetOffsetMeters - secondTargetOffsetMeters)
               < Math.Max(0, minimumLaneSeparationMeters);
    }

    private static bool Contains(IReadOnlyList<byte> values, byte value)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] == value)
                return true;
        }
        return false;
    }

    private static bool Overlaps(IReadOnlyList<byte> first, IReadOnlyList<byte> second)
    {
        for (int i = 0; i < first.Count; i++)
        {
            if (Contains(second, first[i]))
                return true;
        }
        return false;
    }

    public static float RequiredPassSeparation(float ownHalfWidthMeters,
        float obstacleHalfWidthMeters, bool stoppedObstacle)
    {
        float colliderClearance = Math.Max(0.5f, ownHalfWidthMeters)
                                  + Math.Max(0.5f, obstacleHalfWidthMeters)
                                  + (stoppedObstacle ? StoppedObstacleSafetyMarginMeters : 0.35f);
        return stoppedObstacle
            ? Math.Max(2.1f, colliderClearance)
            : Math.Max(MinimumPassingSeparationMeters, colliderClearance);
    }

    public static float PassAccelerationSeparation(float requiredSeparationMeters,
        bool stoppedObstacle) => stoppedObstacle
        ? requiredSeparationMeters + 0.15f
        : Math.Max(MinimumPassAccelerationSeparationMeters, requiredSeparationMeters + 0.2f);

    public static bool ShouldHoldForCountdown(SessionType sessionType, long serverTimeMilliseconds, long startTimeMilliseconds)
        => sessionType == SessionType.Race && serverTimeMilliseconds < startTimeMilliseconds;

    public static bool IsInRaceLaunchWindow(SessionType sessionType, long serverTimeMilliseconds,
        long startTimeMilliseconds) => sessionType == SessionType.Race
                                       && serverTimeMilliseconds >= startTimeMilliseconds
                                       && serverTimeMilliseconds < startTimeMilliseconds + RaceLaunchGraceMilliseconds;

    public static bool CanAttemptPass(SessionType sessionType, long serverTimeMilliseconds,
        long startTimeMilliseconds) => serverTimeMilliseconds >= startTimeMilliseconds
                                                           + RacePassStartDelayMilliseconds;

    public static bool CanAttemptPassPair(byte targetSessionId, byte? recentTargetSessionId,
        long serverTimeMilliseconds, long pairCooldownUntil) => targetSessionId != recentTargetSessionId
                                                               || serverTimeMilliseconds
                                                               >= pairCooldownUntil;

    public static bool IsReciprocalPass(byte ownSessionId, byte? targetPassSessionId) =>
        targetPassSessionId == ownSessionId;

    public static bool CanTransitionLane(SessionType sessionType, long serverTimeMilliseconds,
        long startTimeMilliseconds) => sessionType != SessionType.Race
                                       || serverTimeMilliseconds >= startTimeMilliseconds
                                           + RaceLaneTransitionDelayMilliseconds;

    public static bool CanBeginGridLineMerge(SessionType sessionType, long serverTimeMilliseconds,
        long startTimeMilliseconds, float forwardDistanceMeters, bool corridorOccupied) =>
        !corridorOccupied && CanBeginGridPathBlend(sessionType, serverTimeMilliseconds,
            startTimeMilliseconds, forwardDistanceMeters);

    public static bool CanBeginGridPathBlend(SessionType sessionType, long serverTimeMilliseconds,
        long startTimeMilliseconds, float forwardDistanceMeters) =>
        sessionType != SessionType.Race
        || (CanTransitionLane(sessionType, serverTimeMilliseconds, startTimeMilliseconds)
            && forwardDistanceMeters >= RaceGridForwardLaunchDistanceMeters);

    public static bool OccupiesGridLineMergeCorridor(float longitudinalMeters,
        float participantOffsetMeters, float targetOffsetMeters) =>
        longitudinalMeters >= -RaceGridMergeRearClearanceMeters
        && longitudinalMeters <= RaceGridMergeFrontClearanceMeters
        && Math.Abs(participantOffsetMeters - targetOffsetMeters) < MinimumPassingSeparationMeters;

    public static float GridLaunchDistance(Vector3 origin, Vector3 gridForward, Vector3 position)
    {
        gridForward.Y = 0;
        if (gridForward.LengthSquared() < 1e-6f)
            return 0;
        return Math.Max(0, Vector3.Dot(position - origin, Vector3.Normalize(gridForward)));
    }

    public static Vector3 GridLaunchLineTarget(Vector3 origin, Vector3 gridForward,
        Vector3 position, float referenceHeight)
    {
        gridForward.Y = 0;
        if (gridForward.LengthSquared() < 1e-6f)
            return position with { Y = referenceHeight };
        gridForward = Vector3.Normalize(gridForward);
        float distance = GridLaunchDistance(origin, gridForward, position);
        return (origin + gridForward * distance) with { Y = referenceHeight };
    }

    public static float AdvanceGridPathBlend(float currentBlend, float forwardSpeedMetersPerSecond,
        float deltaSeconds) => Math.Clamp(currentBlend
                                          + Math.Max(0, forwardSpeedMetersPerSecond)
                                          * Math.Max(0, deltaSeconds)
                                          / RaceGridPathBlendDistanceMeters, 0, 1);

    public static Vector3 BlendGridLaunchForward(Vector3 gridForward, Vector3 splineForward,
        float blend)
    {
        var horizontalGridForward = gridForward with { Y = 0 };
        if (horizontalGridForward.LengthSquared() < 1e-6f)
            horizontalGridForward = splineForward with { Y = 0 };
        if (horizontalGridForward.LengthSquared() < 1e-6f)
            horizontalGridForward = Vector3.UnitZ;
        horizontalGridForward = Vector3.Normalize(horizontalGridForward);

        if (splineForward.LengthSquared() < 1e-6f)
            splineForward = horizontalGridForward;
        else
            splineForward = Vector3.Normalize(splineForward);
        var launchForward = Vector3.Normalize(new Vector3(horizontalGridForward.X,
            splineForward.Y, horizontalGridForward.Z));
        var blended = Vector3.Lerp(launchForward, splineForward, Math.Clamp(blend, 0, 1));
        return blended.LengthSquared() < 1e-6f ? splineForward : Vector3.Normalize(blended);
    }

    public static float PlanarHeadingDifferenceDegrees(Vector3 first, Vector3 second)
    {
        first.Y = 0;
        second.Y = 0;
        if (first.LengthSquared() < 1e-6f || second.LengthSquared() < 1e-6f)
            return 0;
        float dot = Math.Clamp(Vector3.Dot(Vector3.Normalize(first), Vector3.Normalize(second)), -1, 1);
        return MathF.Acos(dot) * 180 / MathF.PI;
    }

    public static float PaceFactor(float difficulty) => 0.65f + Math.Clamp(difficulty, 0, 1) * 0.35f;

    public static float RacecraftValue(float baseline, float variancePercent, int slot,
        int salt)
    {
        float variation = Math.Clamp(variancePercent, 0, 100) / 100f;
        if (variation <= 0)
            return Math.Clamp(baseline, 0, 1);

        // Stable integer mixing gives every slot two independent personalities while
        // preserving the same grid across session restarts and accelerated simulations.
        uint hash = unchecked((uint)(slot + 1) * 0x9E3779B9u + (uint)salt);
        hash ^= hash >> 16;
        hash *= 0x7FEB352Du;
        hash ^= hash >> 15;
        hash *= 0x846CA68Bu;
        hash ^= hash >> 16;
        float signed = (hash & 0xFFFF) / 32767.5f - 1f;
        return Math.Clamp(baseline * (1 + signed * variation), 0, 1);
    }

    public static float GridPaceFactor(float configuredVariationPercent, int seed)
    {
        float variation = Math.Clamp(configuredVariationPercent, 0, 0.15f);
        float gridStep = Math.Abs(seed % 5) switch
        {
            0 => 0,
            1 => 1,
            2 => 0.5f,
            3 => 0.75f,
            _ => 0.25f
        };
        return 1 - variation + variation * gridStep;
    }

    public static float AdvanceLaneOffset(float currentOffsetMeters, float targetOffsetMeters,
        float forwardSpeedMetersPerSecond, float deltaSeconds, float transitionMultiplier = 1)
    {
        if (forwardSpeedMetersPerSecond < 1 || deltaSeconds <= 0)
            return currentOffsetMeters;

        float transitionRate = Math.Clamp(forwardSpeedMetersPerSecond * 0.06f, 0.20f, 0.90f)
                               * Math.Clamp(transitionMultiplier, 0.25f, 4);
        float maximumStep = transitionRate * deltaSeconds;
        return Math.Abs(targetOffsetMeters - currentOffsetMeters) <= maximumStep
            ? targetOffsetMeters
            : currentOffsetMeters + Math.Sign(targetOffsetMeters - currentOffsetMeters) * maximumStep;
    }

    public static float AdvanceStoppedPassLaneOffset(float currentOffsetMeters,
        float targetOffsetMeters, float forwardSpeedMetersPerSecond, float deltaSeconds,
        float transitionMultiplier = 1) => AdvanceLaneOffset(currentOffsetMeters,
        targetOffsetMeters,
        Math.Max(StoppedPassMinimumPlanningSpeedMetersPerSecond, forwardSpeedMetersPerSecond),
        deltaSeconds, transitionMultiplier);

    public static float AdvanceReachableLaneOffset(float currentOffsetMeters,
        float targetOffsetMeters, float physicalOffsetMeters, float forwardSpeedMetersPerSecond,
        float deltaSeconds, float transitionMultiplier = 1, bool stoppedPass = false)
    {
        float advanced = stoppedPass
            ? AdvanceStoppedPassLaneOffset(currentOffsetMeters, targetOffsetMeters,
                forwardSpeedMetersPerSecond, deltaSeconds, transitionMultiplier)
            : AdvanceLaneOffset(currentOffsetMeters, targetOffsetMeters,
                forwardSpeedMetersPerSecond, deltaSeconds, transitionMultiplier);
        float maximumLead = Math.Clamp(MinimumLaneCommandLeadMeters
                                       + Math.Max(0, forwardSpeedMetersPerSecond) * 0.03f,
            MinimumLaneCommandLeadMeters, MaximumLaneCommandLeadMeters);
        return Math.Clamp(advanced, physicalOffsetMeters - maximumLead,
            physicalOffsetMeters + maximumLead);
    }

    public static float CorneringSpeedSquared(float radiusMeters, float corneringFactor, float difficulty)
    {
        float speedFactor = PaceFactor(difficulty) * RaceCornerStabilitySpeedFactor;
        return PhysicsUtils.CalculateMaxCorneringSpeedSquared(radiusMeters, corneringFactor)
               * speedFactor * speedFactor;
    }

    public static float CornerApproachSpeedLimit(float cornerSpeedMetersPerSecond,
        float distanceMeters, float maximumBrakeDeceleration)
    {
        float cornerSpeed = Math.Max(0, cornerSpeedMetersPerSecond);
        float deceleration = Math.Max(0.1f, maximumBrakeDeceleration * RaceCornerBrakeForceFactor);
        float usableDistance = Math.Max(0, distanceMeters) / RaceCornerBrakeDistanceFactor;
        return MathF.Sqrt(cornerSpeed * cornerSpeed + 2 * deceleration * usableDistance);
    }

    public static float CornerBrakingDistance(float currentSpeedMetersPerSecond,
        float cornerSpeedMetersPerSecond, float maximumBrakeDeceleration)
    {
        float currentSpeed = Math.Max(0, currentSpeedMetersPerSecond);
        float cornerSpeed = Math.Clamp(cornerSpeedMetersPerSecond, 0, currentSpeed);
        float deceleration = Math.Max(0.1f, maximumBrakeDeceleration * RaceCornerBrakeForceFactor);
        return Math.Max(0, (currentSpeed * currentSpeed - cornerSpeed * cornerSpeed)
                           / (2 * deceleration)) * RaceCornerBrakeDistanceFactor;
    }

    public static float FollowingGapMeters(float speedMetersPerSecond, float aggression)
        => 5 + Math.Max(0, speedMetersPerSecond) * (1.6f - Math.Clamp(aggression, 0, 1) * 0.8f);

    public static float FollowingTargetSpeed(float currentSpeed, float leadSpeed, float distanceMeters, float aggression)
    {
        var gap = FollowingGapMeters(currentSpeed, aggression);
        if (distanceMeters >= gap * 1.5f)
            return currentSpeed;
        float normalizedGap = Math.Clamp((distanceMeters - EmergencyObstacleDistanceMeters)
                                         / Math.Max(1, gap * 1.5f - EmergencyObstacleDistanceMeters), 0, 1);
        float closingAllowance = normalizedGap * (1f + Math.Clamp(aggression, 0, 1) * 2f);
        return Math.Min(Math.Max(0, currentSpeed), Math.Max(0, leadSpeed) + closingAllowance);
    }

    public static float OvertakeTriggerDistance(float speedMetersPerSecond, float aggression) =>
        Math.Clamp(8 + Math.Max(0, speedMetersPerSecond) * 0.75f, 12, 25)
        * (0.90f + Math.Clamp(aggression, 0, 1) * 0.10f);

    public static float PredictivePassHorizonSeconds(float currentSpeed, float leadSpeed,
        float aggression)
    {
        float speedContribution = Math.Clamp(Math.Max(0, currentSpeed) / 50f, 0, 1) * 0.5f;
        float stoppedContribution = IsStoppedObstacle(leadSpeed) ? 0.5f : 0;
        return Math.Clamp(2.5f + Math.Clamp(aggression, 0, 1) * 0.5f
                               + speedContribution + stoppedContribution,
            2.5f, 4f);
    }

    public static float FollowingDecisionDistance(float currentSpeed, float leadSpeed,
        float aggression) => leadSpeed < 8 || leadSpeed < currentSpeed * 0.6f
        ? FollowingGapMeters(currentSpeed, aggression) * 1.5f
        : OvertakeTriggerDistance(currentSpeed, aggression) * 1.25f;

    public static float PassAttemptDistance(float currentSpeed, float leadSpeed,
        float aggression)
    {
        float speed = Math.Max(0, currentSpeed);
        float targetSpeed = Math.Max(0, leadSpeed);
        if (!IsStoppedObstacle(targetSpeed))
        {
            return targetSpeed < 8 || targetSpeed < speed * 0.6f
                ? Math.Min(35, FollowingGapMeters(speed, aggression))
                : OvertakeTriggerDistance(speed, aggression);
        }

        float closingSpeed = Math.Max(0, speed - targetSpeed);
        float predictiveDistance = closingSpeed
                                   * PredictivePassHorizonSeconds(speed, targetSpeed, aggression)
                                   + speed * 0.25f;
        return Math.Min(MaximumPredictivePassPlanningDistanceMeters,
            Math.Max(Math.Max(StoppedObstaclePassPlanningDistanceMeters,
                    FollowingDecisionDistance(speed, targetSpeed, aggression)),
                predictiveDistance));
    }

    public static bool ShouldAttemptPass(float currentSpeed, float leadSpeed, float distanceMeters,
        float aggression) => distanceMeters > (leadSpeed < 1.5f
                                 ? MinimumStoppedPassCommitClearanceMeters
                                 : MinimumMovingPassCommitClearanceMeters)
                             && distanceMeters <= PassAttemptDistance(currentSpeed, leadSpeed, aggression)
                             && leadSpeed <= currentSpeed + 1f;

    public static float[] BuildPredictivePassCorridors(float currentOffsetMeters,
        float sideLeftMeters, float sideRightMeters, float vehicleHalfWidthMeters,
        float trackEdgeReserveMeters, float maximumLeftPassCenterMeters,
        float minimumRightPassCenterMeters, int seed)
    {
        float edgeMargin = Math.Max(CarHalfWidthWithMarginMeters,
            Math.Max(0.5f, vehicleHalfWidthMeters)
            + Math.Max(0.15f, trackEdgeReserveMeters));
        float minimumCenter = -Math.Max(0, sideLeftMeters - edgeMargin);
        float maximumCenter = Math.Max(0, sideRightMeters - edgeMargin);
        var candidates = new List<float>(7);

        static void AddUnique(List<float> values, float value)
        {
            foreach (float existing in values)
            {
                if (Math.Abs(existing - value) < 0.05f)
                    return;
            }
            values.Add(value);
        }

        bool currentIsLegal = currentOffsetMeters <= maximumLeftPassCenterMeters
                              || currentOffsetMeters >= minimumRightPassCenterMeters;
        if (currentIsLegal && currentOffsetMeters >= minimumCenter
                           && currentOffsetMeters <= maximumCenter)
            AddUnique(candidates, currentOffsetMeters);

        if (maximumLeftPassCenterMeters >= minimumCenter)
        {
            float nearest = Math.Min(maximumLeftPassCenterMeters, maximumCenter);
            AddUnique(candidates, nearest);
            AddUnique(candidates, (minimumCenter + nearest) * 0.5f);
            AddUnique(candidates, minimumCenter);
        }
        if (minimumRightPassCenterMeters <= maximumCenter)
        {
            float nearest = Math.Max(minimumRightPassCenterMeters, minimumCenter);
            AddUnique(candidates, nearest);
            AddUnique(candidates, (maximumCenter + nearest) * 0.5f);
            AddUnique(candidates, maximumCenter);
        }

        candidates.Sort((first, second) =>
        {
            float firstCost = Math.Abs(first - currentOffsetMeters)
                              + (((seed & 1) == 0) == (first > 0) ? 0.05f : 0);
            float secondCost = Math.Abs(second - currentOffsetMeters)
                               + (((seed & 1) == 0) == (second > 0) ? 0.05f : 0);
            return firstCost.CompareTo(secondCost);
        });
        return candidates.ToArray();
    }

    public static float PredictLateralOffset(float currentOffsetMeters, float targetOffsetMeters,
        float elapsedSeconds, float horizonSeconds)
    {
        if (horizonSeconds <= 0)
            return targetOffsetMeters;
        float progress = Math.Clamp(elapsedSeconds / horizonSeconds, 0, 1);
        float smoothProgress = progress * progress * (3 - 2 * progress);
        return currentOffsetMeters
               + (targetOffsetMeters - currentOffsetMeters) * smoothProgress;
    }

    public static float PredictiveCorridorInteractionCost(float initialLongitudinalMeters,
        float ownSpeedMetersPerSecond, float participantSpeedMetersPerSecond,
        float ownCurrentOffsetMeters, float ownTargetOffsetMeters,
        float participantCurrentOffsetMeters, float participantTargetOffsetMeters,
        float ownLengthMeters, float participantLengthMeters,
        float requiredLateralSeparationMeters, float horizonSeconds)
    {
        float horizon = Math.Clamp(horizonSeconds, 0.25f, 6f);
        float longitudinalLimit = (Math.Max(0, ownLengthMeters)
                                   + Math.Max(0, participantLengthMeters)) * 0.5f
                                  + PredictiveCorridorLongitudinalBufferMeters;
        float totalRisk = 0;
        const int sampleCount = 17;
        for (int i = 0; i < sampleCount; i++)
        {
            float elapsed = horizon * i / (sampleCount - 1f);
            float longitudinal = initialLongitudinalMeters
                                 + (Math.Max(0, participantSpeedMetersPerSecond)
                                    - Math.Max(0, ownSpeedMetersPerSecond)) * elapsed;
            float ownOffset = PredictLateralOffset(ownCurrentOffsetMeters,
                ownTargetOffsetMeters, elapsed, horizon);
            float participantOffset = PredictLateralOffset(participantCurrentOffsetMeters,
                participantTargetOffsetMeters, elapsed, horizon);
            float longitudinalDistance = Math.Abs(longitudinal);
            float lateralDistance = Math.Abs(ownOffset - participantOffset);
            if (longitudinalDistance < longitudinalLimit
                && lateralDistance < requiredLateralSeparationMeters)
                return float.PositiveInfinity;

            float longitudinalRisk = 1 - Math.Clamp(
                (longitudinalDistance - longitudinalLimit)
                / PredictiveCorridorRiskDistanceMeters, 0, 1);
            float lateralRisk = 1 - Math.Clamp(
                (lateralDistance - requiredLateralSeparationMeters)
                / PredictiveCorridorRiskLateralMeters, 0, 1);
            totalRisk += longitudinalRisk * lateralRisk;
        }
        return totalRisk;
    }

    public static float CommittedPassApproachSpeed(float leadSpeed, float clearanceMeters)
    {
        if (leadSpeed >= 1.5f)
            return leadSpeed;
        return Math.Clamp(4f + Math.Max(0,
                clearanceMeters - EmergencyObstacleDistanceMeters) * 0.55f,
            4f, 14f);
    }

    public static bool HasPassAccelerationClearance(float lateralSeparationMeters) =>
        lateralSeparationMeters >= MinimumPassAccelerationSeparationMeters;

    public static bool HasPassAccelerationClearance(float lateralSeparationMeters,
        float requiredSeparationMeters) => lateralSeparationMeters >= requiredSeparationMeters;

    public static bool ShouldResetPassAccelerationClearance(float lateralSeparationMeters) =>
        lateralSeparationMeters < PassAccelerationClearanceResetMeters;

    public static bool ShouldResetPassAccelerationClearance(float lateralSeparationMeters,
        float requiredSeparationMeters) => lateralSeparationMeters
                                           < requiredSeparationMeters - 0.2f;

    public static bool HasSustainedPassAccelerationClearance(float lateralSeparationMeters,
        long clearanceSinceMilliseconds, long serverTimeMilliseconds) =>
        HasPassAccelerationClearance(lateralSeparationMeters)
        && clearanceSinceMilliseconds > 0
        && serverTimeMilliseconds - clearanceSinceMilliseconds
        >= PassAccelerationClearanceHoldMilliseconds;

    public static bool HasSustainedPassAccelerationClearance(float lateralSeparationMeters,
        float requiredSeparationMeters, long clearanceSinceMilliseconds,
        long serverTimeMilliseconds) => HasPassAccelerationClearance(lateralSeparationMeters,
                                              requiredSeparationMeters)
                                          && clearanceSinceMilliseconds > 0
                                          && serverTimeMilliseconds - clearanceSinceMilliseconds
                                          >= PassAccelerationClearanceHoldMilliseconds;

    public static float PassLaneRearReservationDistance(float ownSpeedMetersPerSecond,
        float trailingSpeedMetersPerSecond) => Math.Clamp(4
            + Math.Max(0, trailingSpeedMetersPerSecond - ownSpeedMetersPerSecond) * 1.5f,
        4, PassLaneRearReservationMeters);

    public static bool IsInsidePassLaneReservation(float longitudinalMeters,
        float rearReservationMeters, float obstacleLongitudinalMeters, bool stoppedObstacle) =>
        longitudinalMeters >= (stoppedObstacle ? 0 : -Math.Max(0, rearReservationMeters))
        && longitudinalMeters <= obstacleLongitudinalMeters + 10;

    public static int OvertakeCommitMilliseconds(float aggression, float clearanceMeters = 0) =>
        20_000 - (int)(Math.Clamp(aggression, 0, 1) * 5_000)
        + (int)(Math.Clamp(clearanceMeters, 0, 40) * 200);

    public static bool ShouldExtendPass(float longitudinalGapMeters, float passerSpeed,
        float targetSpeed, float aggression, int extensionCount) => extensionCount < MaximumPassExtensions
        && longitudinalGapMeters > -PassCompletionClearanceMeters
        && longitudinalGapMeters <= Math.Max(60,
            OvertakeTriggerDistance(passerSpeed, aggression) * 2f)
        && float.IsFinite(passerSpeed)
        && float.IsFinite(targetSpeed);

    public static bool HasCompletedPass(bool separationRecorded, float distanceMeters,
        float longitudinalMeters, float lateralMeters) => separationRecorded
                                                        && distanceMeters
                                                        <= PassCompletionEvaluationDistanceMeters
                                                        && Math.Abs(lateralMeters)
                                                        <= MaximumPlausiblePassSeparationMeters
                                                        && longitudinalMeters
                                                        < -PassCompletionClearanceMeters;

    public static float BaseLaneOffset(float sideLeftMeters, float sideRightMeters, int seed)
    {
        float requested = Math.Abs(seed % 5) switch
        {
            0 => -1.20f,
            1 => 1.20f,
            2 => -0.60f,
            3 => 0.60f,
            _ => 0
        };
        return ClampLaneOffset(requested, sideLeftMeters, sideRightMeters);
    }

    public static float RacingLineOffset(float sideLeftMeters, float sideRightMeters,
        int seed, float distanceMeters)
    {
        float phase = seed * 2.3999632f;
        float slowVariation = MathF.Sin(distanceMeters / 90f + phase) * 0.35f;
        float longVariation = MathF.Sin(distanceMeters / 210f - phase * 0.6f) * 0.15f;
        return ClampLaneOffset(BaseLaneOffset(sideLeftMeters, sideRightMeters, seed)
                               + slowVariation + longVariation,
            sideLeftMeters, sideRightMeters);
    }

    public static float ClampLaneOffset(float offsetMeters, float sideLeftMeters, float sideRightMeters) =>
        Math.Clamp(offsetMeters,
            -Math.Max(0, sideLeftMeters - CarHalfWidthWithMarginMeters),
            Math.Max(0, sideRightMeters - CarHalfWidthWithMarginMeters));

    public static float ClampLaneOffset(float offsetMeters, float sideLeftMeters,
        float sideRightMeters, float vehicleHalfWidthMeters)
        => ClampLaneOffset(offsetMeters, sideLeftMeters, sideRightMeters,
            vehicleHalfWidthMeters, 0.15f);

    public static float ClampLaneOffset(float offsetMeters, float sideLeftMeters,
        float sideRightMeters, float vehicleHalfWidthMeters, float trackEdgeReserveMeters)
    {
        float edgeMargin = Math.Max(CarHalfWidthWithMarginMeters,
            Math.Max(0.5f, vehicleHalfWidthMeters)
            + Math.Max(0.15f, trackEdgeReserveMeters));
        return Math.Clamp(offsetMeters,
            -Math.Max(0, sideLeftMeters - edgeMargin),
            Math.Max(0, sideRightMeters - edgeMargin));
    }

    public static float CourseBoundaryErrorMeters(float physicalOffsetMeters,
        float sideLeftMeters, float sideRightMeters, float vehicleHalfWidthMeters,
        float trackEdgeReserveMeters = 0.15f)
    {
        float edgeMargin = Math.Max(CarHalfWidthWithMarginMeters,
            Math.Max(0.5f, vehicleHalfWidthMeters)
            + Math.Max(0.15f, trackEdgeReserveMeters));
        float minimumCenter = -Math.Max(0, sideLeftMeters - edgeMargin);
        float maximumCenter = Math.Max(0, sideRightMeters - edgeMargin);
        if (physicalOffsetMeters < minimumCenter)
            return minimumCenter - physicalOffsetMeters;
        if (physicalOffsetMeters > maximumCenter)
            return physicalOffsetMeters - maximumCenter;
        return 0;
    }

    public static float OffroadSeverity(float boundaryErrorMeters) =>
        Math.Clamp(Math.Max(0, boundaryErrorMeters) / FullOffroadPenaltyMeters, 0, 1);

    public static float OffroadTargetSpeedScale(float boundaryErrorMeters) =>
        1 - OffroadSeverity(boundaryErrorMeters) * (1 - MinimumOffroadTargetSpeedScale);

    public static float OffroadDragDeceleration(float boundaryErrorMeters) =>
        OffroadSeverity(boundaryErrorMeters) * MaximumOffroadDragDeceleration;

    public static float OffroadRecoveryEdgeReserveMeters(float boundaryErrorMeters) =>
        0.15f + OffroadSeverity(boundaryErrorMeters)
        * (MaximumOffroadRecoveryEdgeReserveMeters - 0.15f);

    public static float LimitPassCorridorWidth(float sideWidthMeters,
        float vehicleHalfWidthMeters)
    {
        float edgeMargin = Math.Max(CarHalfWidthWithMarginMeters,
            Math.Max(0.5f, vehicleHalfWidthMeters) + 0.15f);
        return Math.Min(Math.Max(0, sideWidthMeters),
            MaximumRacePassCenterOffsetMeters + edgeMargin);
    }

    public static float CommittedPassTarget(float obstacleOffsetMeters, bool passingLeft,
        float sideLeftMeters, float sideRightMeters) => ClampLaneOffset(
        obstacleOffsetMeters + (passingLeft ? -PreferredPassingSeparationMeters
            : PreferredPassingSeparationMeters), sideLeftMeters, sideRightMeters);

    public static float CommittedPassTarget(float obstacleOffsetMeters, bool passingLeft,
        float sideLeftMeters, float sideRightMeters, float vehicleHalfWidthMeters,
        float trackEdgeReserveMeters = 0.15f) => ClampLaneOffset(
        obstacleOffsetMeters + (passingLeft ? -PreferredPassingSeparationMeters
            : PreferredPassingSeparationMeters), sideLeftMeters, sideRightMeters,
        vehicleHalfWidthMeters, trackEdgeReserveMeters);

    public static float? ChoosePassTarget(float currentOffsetMeters, float obstacleOffsetMeters,
        float sideLeftMeters, float sideRightMeters, bool leftBlocked, bool rightBlocked, int seed)
        => ChoosePassTarget(currentOffsetMeters, obstacleOffsetMeters, sideLeftMeters,
            sideRightMeters, leftBlocked, rightBlocked, seed,
            MinimumPassTargetSeparationMeters);

    public static float? ChoosePassTarget(float currentOffsetMeters, float obstacleOffsetMeters,
        float sideLeftMeters, float sideRightMeters, bool leftBlocked, bool rightBlocked, int seed,
        float requiredSeparationMeters)
        => ChoosePassTarget(currentOffsetMeters, obstacleOffsetMeters, sideLeftMeters,
            sideRightMeters, leftBlocked, rightBlocked, seed, requiredSeparationMeters,
            vehicleHalfWidthMeters: 1f);

    public static float? ChoosePassTarget(float currentOffsetMeters, float obstacleOffsetMeters,
        float sideLeftMeters, float sideRightMeters, bool leftBlocked, bool rightBlocked, int seed,
        float requiredSeparationMeters, float vehicleHalfWidthMeters,
        float trackEdgeReserveMeters = 0.15f)
    {
        float leftTarget = CommittedPassTarget(obstacleOffsetMeters, passingLeft: true,
            sideLeftMeters, sideRightMeters, vehicleHalfWidthMeters, trackEdgeReserveMeters);
        float rightTarget = CommittedPassTarget(obstacleOffsetMeters, passingLeft: false,
            sideLeftMeters, sideRightMeters, vehicleHalfWidthMeters, trackEdgeReserveMeters);
        bool leftAvailable = !leftBlocked
                             && obstacleOffsetMeters - leftTarget >= requiredSeparationMeters;
        bool rightAvailable = !rightBlocked
                              && rightTarget - obstacleOffsetMeters >= requiredSeparationMeters;
        if (!leftAvailable && !rightAvailable)
            return null;
        if (!rightAvailable)
            return leftTarget;
        if (!leftAvailable)
            return rightTarget;

        float leftMove = Math.Abs(currentOffsetMeters - leftTarget);
        float rightMove = Math.Abs(currentOffsetMeters - rightTarget);
        if (Math.Abs(leftMove - rightMove) > 0.1f)
            return leftMove < rightMove ? leftTarget : rightTarget;
        return (seed & 1) == 0 ? leftTarget : rightTarget;
    }

    public static (float? Primary, float? Alternate) ChooseStoppedObstaclePassTargets(
        float currentOffsetMeters, float envelopeMinimumEdgeMeters,
        float envelopeMaximumEdgeMeters, float sideLeftMeters, float sideRightMeters,
        float vehicleHalfWidthMeters, bool leftBlocked, bool rightBlocked, int seed)
    {
        float ownHalfWidth = Math.Max(0.5f, vehicleHalfWidthMeters);
        float edgeMargin = ownHalfWidth + StoppedObstacleSafetyMarginMeters;
        float minimumCenter = -Math.Max(0, sideLeftMeters - ownHalfWidth - 0.15f);
        float maximumCenter = Math.Max(0, sideRightMeters - ownHalfWidth - 0.15f);
        float minimumLeftTarget = envelopeMinimumEdgeMeters - edgeMargin;
        float minimumRightTarget = envelopeMaximumEdgeMeters + edgeMargin;
        bool leftAvailable = !leftBlocked
                             && minimumLeftTarget >= minimumCenter
                             && minimumLeftTarget <= maximumCenter;
        bool rightAvailable = !rightBlocked
                              && minimumRightTarget >= minimumCenter
                              && minimumRightTarget <= maximumCenter;
        float leftTarget = Math.Max(minimumCenter,
            minimumLeftTarget - StoppedObstacleTargetBufferMeters);
        float rightTarget = Math.Min(maximumCenter,
            minimumRightTarget + StoppedObstacleTargetBufferMeters);
        if (!leftAvailable && !rightAvailable)
            return (null, null);
        if (!rightAvailable)
            return (leftTarget, null);
        if (!leftAvailable)
            return (rightTarget, null);

        float leftMove = Math.Abs(currentOffsetMeters - leftTarget);
        float rightMove = Math.Abs(currentOffsetMeters - rightTarget);
        bool preferLeft = Math.Abs(leftMove - rightMove) > 0.1f
            ? leftMove < rightMove
            : (seed & 1) == 0;
        return preferLeft ? (leftTarget, rightTarget) : (rightTarget, leftTarget);
    }

    public static bool HasCompletedStoppedPass(bool separationRecorded,
        float longitudinalMeters, float lateralMeters, float requiredSeparationMeters) =>
        longitudinalMeters < -PassCompletionClearanceMeters
        && (separationRecorded
            || Math.Abs(lateralMeters) >= Math.Max(0, requiredSeparationMeters - 0.2f));

    public static bool IsPracticalPassTarget(float currentOffsetMeters, float targetOffsetMeters,
        float leadSpeedMetersPerSecond) => leadSpeedMetersPerSecond < 1.5f
                                          || Math.Abs(targetOffsetMeters - currentOffsetMeters)
                                          <= MaximumMovingPassLaneChangeMeters;

    public static bool HasPassTargetClearance(float targetOffsetMeters, float obstacleOffsetMeters,
        float leadSpeedMetersPerSecond) => leadSpeedMetersPerSecond < 1.5f
                                          || Math.Abs(targetOffsetMeters - obstacleOffsetMeters)
                                          >= PreferredPassingSeparationMeters - 0.01f;

    public static bool HasRequiredPassTargetClearance(float targetOffsetMeters, float obstacleOffsetMeters,
        float requiredSeparationMeters) => Math.Abs(targetOffsetMeters - obstacleOffsetMeters)
                                           >= requiredSeparationMeters;

    public static float PassingTargetSpeed(float initialMaxSpeed, float absoluteMaxSpeed,
        float leadSpeed, float aggression) => Math.Min(Math.Max(0, absoluteMaxSpeed) * 1.12f,
        Math.Max(Math.Max(0, initialMaxSpeed), Math.Max(0, leadSpeed) + 4f
                                                   + Math.Clamp(aggression, 0, 1) * 2f));

    public static float YieldingTargetSpeed(float normalTargetSpeed, float speedAtYieldStart,
        float passerSpeed, float aggression)
    {
        _ = passerSpeed;
        // Race yielding is a small lift, not traffic-style stopping. If a pass begins while
        // both cars are already rubbing or queued, retaining the captured near-zero speed
        // deadlocks the passer and target indefinitely.
        float retainedSpeed = Math.Max(CongestionCrawlSpeedMetersPerSecond,
                                  Math.Max(0, speedAtYieldStart))
                              * (0.98f + Math.Clamp(aggression, 0, 1) * 0.02f);
        return Math.Min(Math.Max(0, normalTargetSpeed), retainedSpeed);
    }

    public static float PassingCornerSpeedLimit(float normalLimit, float absoluteMaxSpeed,
        float aggression)
    {
        if (float.IsPositiveInfinity(normalLimit))
            return Math.Max(0, absoluteMaxSpeed);
        float passingLimit = Math.Max(Math.Max(0, normalLimit)
                                          * (1f + Math.Clamp(aggression, 0, 1) * 0.02f),
            Math.Min(10, Math.Max(0, absoluteMaxSpeed)));
        return Math.Min(Math.Max(0, absoluteMaxSpeed) * 1.12f, passingLimit);
    }

    public static float ChooseOvertakeOffset(float sideLeftMeters, float sideRightMeters, float aggression, int seed)
    {
        var target = ChoosePassTarget(0, 0, sideLeftMeters, sideRightMeters,
            leftBlocked: false, rightBlocked: false, seed);
        return target ?? 0;
    }

}
