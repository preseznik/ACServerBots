using System;
using AssettoServer.Shared.Model;
using AssettoServer.Utils;

namespace AssettoServer.Server.Ai;

public static class RaceBotMath
{
    public const int RaceLaunchGraceMilliseconds = 500;
    public const int RacePassStartDelayMilliseconds = 4_000;
    public const float EmergencyObstacleDistanceMeters = 3f;
    public const float ObstacleCorridorHalfWidthMeters = 2f;
    public const float MinimumPassingSeparationMeters = 2.1f;
    public const float PassCompletionClearanceMeters = 7f;
    public const int PassLaneReleaseMilliseconds = 4_000;
    public const int RecentlyPassedCooldownMilliseconds = 15_000;
    public const int PassExtensionMilliseconds = 10_000;
    public const float PassLaneRearReservationMeters = 15f;
    private const float CarHalfWidthWithMarginMeters = 1.25f;
    private const float PreferredPassingSeparationMeters = 2.2f;

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

    public static float PaceFactor(float difficulty) => 0.65f + Math.Clamp(difficulty, 0, 1) * 0.35f;

    public static float GridPaceFactor(float configuredVariationPercent, int seed)
    {
        float variation = Math.Clamp(configuredVariationPercent, 0, 0.15f);
        float gridStep = Math.Abs(seed % 5) / 4f;
        return 1 - variation + variation * gridStep;
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
        float aggression) => distanceMeters > EmergencyObstacleDistanceMeters
                             && distanceMeters <= PassAttemptDistance(currentSpeed, leadSpeed, aggression)
                             && leadSpeed <= currentSpeed + 1f;

    public static int OvertakeCommitMilliseconds(float aggression, float clearanceMeters = 0) =>
        20_000 - (int)(Math.Clamp(aggression, 0, 1) * 5_000)
        + (int)(Math.Clamp(clearanceMeters, 0, 40) * 200);

    public static bool ShouldExtendPass(float longitudinalGapMeters, float passerSpeed,
        float targetSpeed, float aggression, bool alreadyExtended) => !alreadyExtended
        && longitudinalGapMeters > -PassCompletionClearanceMeters
        && longitudinalGapMeters <= OvertakeTriggerDistance(passerSpeed, aggression) * 1.5f
        && passerSpeed > targetSpeed + 0.5f;

    public static float BaseLaneOffset(float sideLeftMeters, float sideRightMeters, int seed)
    {
        float requested = Math.Abs(seed % 5) switch
        {
            0 => -0.65f,
            1 => 0.65f,
            2 => -0.30f,
            3 => 0.30f,
            _ => 0
        };
        return ClampLaneOffset(requested, sideLeftMeters, sideRightMeters);
    }

    public static float ClampLaneOffset(float offsetMeters, float sideLeftMeters, float sideRightMeters) =>
        Math.Clamp(offsetMeters,
            -Math.Max(0, sideLeftMeters - CarHalfWidthWithMarginMeters),
            Math.Max(0, sideRightMeters - CarHalfWidthWithMarginMeters));

    public static float? ChoosePassTarget(float currentOffsetMeters, float obstacleOffsetMeters,
        float sideLeftMeters, float sideRightMeters, bool leftBlocked, bool rightBlocked, int seed)
    {
        float leftLimit = Math.Max(0, sideLeftMeters - CarHalfWidthWithMarginMeters);
        float rightLimit = Math.Max(0, sideRightMeters - CarHalfWidthWithMarginMeters);
        float leftTarget = Math.Max(-leftLimit, obstacleOffsetMeters - PreferredPassingSeparationMeters);
        float rightTarget = Math.Min(rightLimit, obstacleOffsetMeters + PreferredPassingSeparationMeters);
        bool leftAvailable = !leftBlocked
                             && obstacleOffsetMeters - leftTarget >= MinimumPassingSeparationMeters;
        bool rightAvailable = !rightBlocked
                              && rightTarget - obstacleOffsetMeters >= MinimumPassingSeparationMeters;
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

    public static float PassingTargetSpeed(float initialMaxSpeed, float absoluteMaxSpeed,
        float leadSpeed, float aggression) => Math.Min(Math.Max(0, absoluteMaxSpeed) * 1.12f,
        Math.Max(Math.Max(0, initialMaxSpeed), Math.Max(0, leadSpeed) + 4f
                                                   + Math.Clamp(aggression, 0, 1) * 2f));

    public static float YieldingTargetSpeed(float normalTargetSpeed, float speedAtYieldStart,
        float aggression) => Math.Min(Math.Max(0, normalTargetSpeed),
        Math.Max(8, Math.Max(0, speedAtYieldStart)
                    * (0.90f + Math.Clamp(aggression, 0, 1) * 0.06f)));

    public static float PassingCornerSpeedLimit(float normalLimit, float absoluteMaxSpeed,
        float aggression)
    {
        if (float.IsPositiveInfinity(normalLimit))
            return Math.Max(0, absoluteMaxSpeed);
        return Math.Min(Math.Max(0, absoluteMaxSpeed) * 1.12f, Math.Max(0, normalLimit)
            * (1.15f + Math.Clamp(aggression, 0, 1) * 0.05f));
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
