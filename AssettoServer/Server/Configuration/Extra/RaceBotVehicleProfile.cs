using JetBrains.Annotations;
using YamlDotNet.Serialization;

namespace AssettoServer.Server.Configuration.Extra;

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.WithMembers)]
public class RaceBotVehicleProfile
{
    [YamlMember(Description = "Car model this locally-derived profile applies to")]
    public string Model { get; init; } = "";
    [YamlMember(Description = "Profile provenance, for example ui_car.json or manual")]
    public string Source { get; init; } = "manual";
    public float MassKg { get; init; } = 1200;
    public float PowerKw { get; init; } = 110;
    public float TopSpeedKph { get; init; } = 200;
    public float ZeroToHundredSeconds { get; init; } = 8;
    public float MaxBrakeDeceleration { get; init; } = 8.5f;
    public float LateralGripG { get; init; } = 1;
    public float TyreDiameterMeters { get; init; } = 0.65f;
    public int EngineIdleRpm { get; init; } = 900;
    public int EngineMaxRpm { get; init; } = 7000;
    public int GearCount { get; init; } = 6;

    [YamlIgnore] public float TopSpeedMs => TopSpeedKph / 3.6f;
}
