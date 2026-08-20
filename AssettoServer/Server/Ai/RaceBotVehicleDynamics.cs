using System;
using AssettoServer.Server.Configuration.Extra;

namespace AssettoServer.Server.Ai;

public readonly record struct RaceBotVehicleStep(float SpeedMetersPerSecond, float AccelerationMetersPerSecondSquared);
public readonly record struct RaceBotVehicleTelemetry(byte ProtocolGear, ushort EngineRpm);

public static class RaceBotVehicleDynamics
{
    private const float HundredKphMs = 100 / 3.6f;
    private const float DrivetrainEfficiency = 0.82f;

    public static RaceBotVehicleStep Step(float currentSpeed, float targetSpeed, float deltaSeconds,
        RaceBotVehicleProfile profile)
    {
        currentSpeed = Math.Max(0, currentSpeed);
        targetSpeed = Math.Clamp(targetSpeed, 0, profile.TopSpeedMs);
        deltaSeconds = Math.Max(0, deltaSeconds);
        if (deltaSeconds == 0 || Math.Abs(targetSpeed - currentSpeed) < 0.001f)
            return new RaceBotVehicleStep(targetSpeed, 0);

        float acceleration;
        if (targetSpeed < currentSpeed)
        {
            acceleration = -profile.MaxBrakeDeceleration;
        }
        else
        {
            float referenceAcceleration = HundredKphMs / profile.ZeroToHundredSeconds;
            float wheelPowerWatts = profile.PowerKw * 1000 * DrivetrainEfficiency;
            float powerLimitedAcceleration = wheelPowerWatts
                                             / (profile.MassKg * Math.Max(4, currentSpeed));

            float topSpeed = Math.Max(1, profile.TopSpeedMs);
            float dragAcceleration = wheelPowerWatts * currentSpeed * currentSpeed
                                     / (profile.MassKg * topSpeed * topSpeed * topSpeed);
            float physicalAcceleration = Math.Max(0, powerLimitedAcceleration - dragAcceleration);
            float physicalBlend = Math.Clamp((currentSpeed - HundredKphMs) / 10, 0, 1);
            acceleration = referenceAcceleration
                           + (physicalAcceleration - referenceAcceleration) * physicalBlend;
        }

        float nextSpeed = Math.Max(0, currentSpeed + acceleration * deltaSeconds);
        if ((acceleration < 0 && nextSpeed < targetSpeed) || (acceleration > 0 && nextSpeed > targetSpeed))
        {
            nextSpeed = targetSpeed;
            acceleration = 0;
        }

        return new RaceBotVehicleStep(nextSpeed, acceleration);
    }

    public static RaceBotVehicleTelemetry GetTelemetry(float speedMetersPerSecond, RaceBotVehicleProfile profile)
    {
        float normalizedSpeed = Math.Clamp(speedMetersPerSecond / Math.Max(1, profile.TopSpeedMs), 0, 1);
        int gear = normalizedSpeed <= 0.001f
            ? 1
            : Math.Clamp((int)MathF.Ceiling(normalizedSpeed * profile.GearCount), 1, profile.GearCount);
        float gearStart = (gear - 1f) / profile.GearCount;
        float gearProgress = Math.Clamp((normalizedSpeed - gearStart) * profile.GearCount, 0, 1);
        float rpmFraction = normalizedSpeed <= 0.001f ? 0 : 0.52f + gearProgress * 0.48f;
        int rpm = (int)MathF.Round(profile.EngineIdleRpm
                                  + (profile.EngineMaxRpm - profile.EngineIdleRpm) * rpmFraction);

        // AC encodes reverse as 0, neutral as 1, first as 2, and so on.
        return new RaceBotVehicleTelemetry((byte)(gear + 1), (ushort)Math.Clamp(rpm, 0, ushort.MaxValue));
    }
}
