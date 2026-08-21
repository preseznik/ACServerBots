using System;
using AssettoServer.Shared.Model;
using AssettoServer.Utils;

namespace AssettoServer.Server.Ai;

public static class RaceBotMath
{
    public const int RaceLaunchGraceMilliseconds = 2_000;
    public const float EmergencyObstacleDistanceMeters = 3f;

    public static bool ShouldHoldForCountdown(SessionType sessionType, long serverTimeMilliseconds, long startTimeMilliseconds)
        => sessionType == SessionType.Race && serverTimeMilliseconds < startTimeMilliseconds;

    public static bool IsInRaceLaunchWindow(SessionType sessionType, long serverTimeMilliseconds,
        long startTimeMilliseconds) => sessionType == SessionType.Race
                                       && serverTimeMilliseconds >= startTimeMilliseconds
                                       && serverTimeMilliseconds < startTimeMilliseconds + RaceLaunchGraceMilliseconds;

    public static float PaceFactor(float difficulty) => 0.65f + Math.Clamp(difficulty, 0, 1) * 0.35f;

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
        FollowingGapMeters(speedMetersPerSecond, aggression) * 1.25f;

    public static bool ShouldAttemptPass(float currentSpeed, float leadSpeed, float distanceMeters,
        float aggression) => distanceMeters > EmergencyObstacleDistanceMeters
                             && distanceMeters <= OvertakeTriggerDistance(currentSpeed, aggression)
                             && leadSpeed <= currentSpeed + 1f;

    public static int OvertakeCommitMilliseconds(float aggression) =>
        6_000 - (int)(Math.Clamp(aggression, 0, 1) * 1_500);

    public static float ChooseOvertakeOffset(float sideLeftMeters, float sideRightMeters, float aggression, int seed)
    {
        const float carHalfWidthWithMargin = 1.25f;
        const float minimumPassingOffset = 1.65f;
        var leftRoom = sideLeftMeters - carHalfWidthWithMargin;
        var rightRoom = sideRightMeters - carHalfWidthWithMargin;
        if (leftRoom < minimumPassingOffset && rightRoom < minimumPassingOffset)
            return 0;

        bool useLeft = rightRoom < minimumPassingOffset
                       || (leftRoom >= minimumPassingOffset
                           && (leftRoom > rightRoom
                               || (Math.Abs(leftRoom - rightRoom) < 0.1f && (seed & 1) == 0)));
        float room = useLeft ? leftRoom : rightRoom;
        float scaledOffset = Math.Min(2.2f, room) * (0.9f + 0.1f * Math.Clamp(aggression, 0, 1));
        float offset = Math.Min(room, Math.Max(minimumPassingOffset, scaledOffset));
        return useLeft ? -offset : offset;
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
