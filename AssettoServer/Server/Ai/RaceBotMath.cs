using System;
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
    public const int RacePassStartDelayMilliseconds = 4_000;
    public const float EmergencyObstacleDistanceMeters = 3f;
    public const float MinimumMovingPassCommitClearanceMeters = 8f;
    public const float MinimumStoppedPassCommitClearanceMeters = 3.5f;
    public const float PassSeparationEvaluationDistanceMeters = 15f;
    public const float MaximumPlausiblePassSeparationMeters = 6f;
    public const float MinimumPassingSeparationMeters = 3.1f;
    public const float MinimumPassAccelerationSeparationMeters = 4.0f;
    public const float PassAccelerationClearanceResetMeters = 3.8f;
    public const int PassAccelerationClearanceHoldMilliseconds = 2_000;
    public const float MinimumPassTargetSeparationMeters = MinimumPassingSeparationMeters;
    public const float MaximumMovingPassLaneChangeMeters = 6.5f;
    public const float ObstacleCorridorHalfWidthMeters = MinimumPassingSeparationMeters;
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
    private const float CarHalfWidthWithMarginMeters = 1.25f;
    private const float PreferredPassingSeparationMeters = 3.8f;

    public static bool ShouldHoldForCountdown(SessionType sessionType, long serverTimeMilliseconds, long startTimeMilliseconds)
        => sessionType == SessionType.Race && serverTimeMilliseconds < startTimeMilliseconds;

    public static bool IsInRaceLaunchWindow(SessionType sessionType, long serverTimeMilliseconds,
        long startTimeMilliseconds) => sessionType == SessionType.Race
                                       && serverTimeMilliseconds >= startTimeMilliseconds
                                       && serverTimeMilliseconds < startTimeMilliseconds + RaceLaunchGraceMilliseconds;

    public static bool CanAttemptPass(SessionType sessionType, long serverTimeMilliseconds,
        long startTimeMilliseconds) => sessionType != SessionType.Race
                                       || serverTimeMilliseconds >= startTimeMilliseconds
                                           + RacePassStartDelayMilliseconds;

    public static bool CanAttemptPassPair(byte targetSessionId, byte? recentTargetSessionId,
        long serverTimeMilliseconds, long pairCooldownUntil) => targetSessionId != recentTargetSessionId
                                                               || serverTimeMilliseconds
                                                               >= pairCooldownUntil;

    public static bool CanTransitionLane(SessionType sessionType, long serverTimeMilliseconds,
        long startTimeMilliseconds) => sessionType != SessionType.Race
                                       || serverTimeMilliseconds >= startTimeMilliseconds
                                           + RaceLaneTransitionDelayMilliseconds;

    public static bool CanBeginGridLineMerge(SessionType sessionType, long serverTimeMilliseconds,
        long startTimeMilliseconds, float forwardDistanceMeters, bool corridorOccupied) =>
        sessionType != SessionType.Race
        || (!corridorOccupied
            && CanTransitionLane(sessionType, serverTimeMilliseconds, startTimeMilliseconds)
            && forwardDistanceMeters >= RaceGridForwardLaunchDistanceMeters);

    public static bool OccupiesGridLineMergeCorridor(float longitudinalMeters,
        float participantOffsetMeters, float targetOffsetMeters) =>
        longitudinalMeters >= -RaceGridMergeRearClearanceMeters
        && longitudinalMeters <= RaceGridMergeFrontClearanceMeters
        && Math.Abs(participantOffsetMeters - targetOffsetMeters) < MinimumPassingSeparationMeters;

    public static float PaceFactor(float difficulty) => 0.65f + Math.Clamp(difficulty, 0, 1) * 0.35f;

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

    public static float CorneringSpeedSquared(float radiusMeters, float corneringFactor, float difficulty)
        => PhysicsUtils.CalculateMaxCorneringSpeedSquared(radiusMeters, corneringFactor)
           * PaceFactor(difficulty) * PaceFactor(difficulty);

    public static float AuthoredSplineSpeedLimit(float speedMetersPerSecond, float difficulty) =>
        speedMetersPerSecond > 0 ? speedMetersPerSecond * PaceFactor(difficulty) : float.PositiveInfinity;

    public static float FollowingGapMeters(float speedMetersPerSecond, float aggression)
        => 5 + Math.Max(0, speedMetersPerSecond) * (1.6f - Math.Clamp(aggression, 0, 1) * 0.8f);

    public static float FollowingTargetSpeed(float currentSpeed, float leadSpeed, float distanceMeters, float aggression)
    {
        var gap = FollowingGapMeters(currentSpeed, aggression);
        if (distanceMeters >= gap * 1.5f)
            return currentSpeed;
        if (distanceMeters <= gap * 0.5f)
            return 0;
        return Math.Min(currentSpeed, Math.Max(0, leadSpeed));
    }

    public static float OvertakeTriggerDistance(float speedMetersPerSecond, float aggression) =>
        Math.Clamp(8 + Math.Max(0, speedMetersPerSecond) * 0.75f, 12, 25)
        * (0.90f + Math.Clamp(aggression, 0, 1) * 0.10f);

    public static float FollowingDecisionDistance(float currentSpeed, float leadSpeed,
        float aggression) => leadSpeed < 8 || leadSpeed < currentSpeed * 0.6f
        ? FollowingGapMeters(currentSpeed, aggression) * 1.5f
        : OvertakeTriggerDistance(currentSpeed, aggression) * 1.25f;

    public static float PassAttemptDistance(float currentSpeed, float leadSpeed,
        float aggression) => leadSpeed < 8 || leadSpeed < currentSpeed * 0.6f
        ? Math.Min(35, FollowingGapMeters(currentSpeed, aggression))
        : OvertakeTriggerDistance(currentSpeed, aggression);

    public static bool ShouldAttemptPass(float currentSpeed, float leadSpeed, float distanceMeters,
        float aggression) => distanceMeters > (leadSpeed < 1.5f
                                 ? MinimumStoppedPassCommitClearanceMeters
                                 : MinimumMovingPassCommitClearanceMeters)
                             && distanceMeters <= PassAttemptDistance(currentSpeed, leadSpeed, aggression)
                             && leadSpeed <= currentSpeed + 1f;

    public static float CommittedPassApproachSpeed(float leadSpeed, float clearanceMeters)
    {
        if (leadSpeed >= 1.5f)
            return leadSpeed;
        return Math.Clamp((clearanceMeters - EmergencyObstacleDistanceMeters) * 0.35f,
            1.5f, 5f);
    }

    public static bool HasPassAccelerationClearance(float lateralSeparationMeters) =>
        lateralSeparationMeters >= MinimumPassAccelerationSeparationMeters;

    public static bool ShouldResetPassAccelerationClearance(float lateralSeparationMeters) =>
        lateralSeparationMeters < PassAccelerationClearanceResetMeters;

    public static bool HasSustainedPassAccelerationClearance(float lateralSeparationMeters,
        long clearanceSinceMilliseconds, long serverTimeMilliseconds) =>
        HasPassAccelerationClearance(lateralSeparationMeters)
        && clearanceSinceMilliseconds > 0
        && serverTimeMilliseconds - clearanceSinceMilliseconds
        >= PassAccelerationClearanceHoldMilliseconds;

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

    public static float CommittedPassTarget(float obstacleOffsetMeters, bool passingLeft,
        float sideLeftMeters, float sideRightMeters) => ClampLaneOffset(
        obstacleOffsetMeters + (passingLeft ? -PreferredPassingSeparationMeters
            : PreferredPassingSeparationMeters), sideLeftMeters, sideRightMeters);

    public static float? ChoosePassTarget(float currentOffsetMeters, float obstacleOffsetMeters,
        float sideLeftMeters, float sideRightMeters, bool leftBlocked, bool rightBlocked, int seed)
    {
        float leftTarget = CommittedPassTarget(obstacleOffsetMeters, passingLeft: true,
            sideLeftMeters, sideRightMeters);
        float rightTarget = CommittedPassTarget(obstacleOffsetMeters, passingLeft: false,
            sideLeftMeters, sideRightMeters);
        bool leftAvailable = !leftBlocked
                             && obstacleOffsetMeters - leftTarget >= MinimumPassTargetSeparationMeters;
        bool rightAvailable = !rightBlocked
                              && rightTarget - obstacleOffsetMeters >= MinimumPassTargetSeparationMeters;
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

    public static bool IsPracticalPassTarget(float currentOffsetMeters, float targetOffsetMeters,
        float leadSpeedMetersPerSecond) => leadSpeedMetersPerSecond < 1.5f
                                          || Math.Abs(targetOffsetMeters - currentOffsetMeters)
                                          <= MaximumMovingPassLaneChangeMeters;

    public static bool HasPassTargetClearance(float targetOffsetMeters, float obstacleOffsetMeters,
        float leadSpeedMetersPerSecond) => leadSpeedMetersPerSecond < 1.5f
                                          || Math.Abs(targetOffsetMeters - obstacleOffsetMeters)
                                          >= PreferredPassingSeparationMeters - 0.01f;

    public static float PassingTargetSpeed(float initialMaxSpeed, float absoluteMaxSpeed,
        float leadSpeed, float aggression) => Math.Min(Math.Max(0, absoluteMaxSpeed) * 1.12f,
        Math.Max(Math.Max(0, initialMaxSpeed), Math.Max(0, leadSpeed) + 4f
                                                   + Math.Clamp(aggression, 0, 1) * 2f));

    public static float YieldingTargetSpeed(float normalTargetSpeed, float speedAtYieldStart,
        float passerSpeed, float aggression)
    {
        float retainedSpeed = Math.Max(8, Math.Max(0, speedAtYieldStart)
                                          * (0.90f + Math.Clamp(aggression, 0, 1) * 0.06f));
        float passConversionSpeed = Math.Max(6, Math.Max(0, passerSpeed)
                                                * (0.82f + Math.Clamp(aggression, 0, 1) * 0.06f));
        return Math.Min(Math.Max(0, normalTargetSpeed), Math.Min(retainedSpeed, passConversionSpeed));
    }

    public static float PassingCornerSpeedLimit(float normalLimit, float absoluteMaxSpeed,
        float aggression)
    {
        if (float.IsPositiveInfinity(normalLimit))
            return Math.Max(0, absoluteMaxSpeed);
        float passingLimit = Math.Max(Math.Max(0, normalLimit)
                                          * (1.15f + Math.Clamp(aggression, 0, 1) * 0.05f),
            Math.Min(10, Math.Max(0, absoluteMaxSpeed)));
        return Math.Min(Math.Max(0, absoluteMaxSpeed) * 1.12f, passingLimit);
    }

    public static float ChooseOvertakeOffset(float sideLeftMeters, float sideRightMeters, float aggression, int seed)
    {
        var target = ChoosePassTarget(0, 0, sideLeftMeters, sideRightMeters,
            leftBlocked: false, rightBlocked: false, seed);
        return target ?? 0;
    }

    public static int CollisionRecoveryMilliseconds(int minimum, int maximum, float aggression, int seed)
    {
        if (maximum <= minimum)
            return Math.Max(0, minimum);
        var normalized = Math.Clamp(1 - aggression, 0, 1);
        var deterministicNoise = (uint)seed % 101 / 100f;
        return minimum + (int)((maximum - minimum) * Math.Clamp(normalized * 0.75f + deterministicNoise * 0.25f, 0, 1));
    }
}
