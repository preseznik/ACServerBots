using AssettoServer.Server.Weather;
using AssettoServer.Utils;
using JetBrains.Annotations;

namespace AssettoServer.Server.Configuration.Kunos;

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.WithMembers)]
public class WeatherConfiguration
{
    [IniField("GRAPHICS")] public string Graphics
    {
        get => _graphics;
        set
        {
            _graphics = string.IsNullOrWhiteSpace(value) ? "3_clear" : value;
            WeatherFxParams = WeatherFxParams.FromString(_graphics);
        }
    }
    [IniField("BASE_TEMPERATURE_AMBIENT")] public float BaseTemperatureAmbient { get; init; }
    [IniField("BASE_TEMPERATURE_ROAD")] public float BaseTemperatureRoad { get; init; }
    [IniField("VARIATION_AMBIENT")] public float VariationAmbient { get; init; }
    [IniField("VARIATION_ROAD")] public float VariationRoad { get; init; }
    [IniField("WIND_BASE_SPEED_MIN")] public float WindBaseSpeedMin { get; init; }
    [IniField("WIND_BASE_SPEED_MAX")] public float WindBaseSpeedMax { get; init; }
    [IniField("WIND_BASE_DIRECTION")] public int WindBaseDirection { get; init; }
    [IniField("WIND_VARIATION_DIRECTION")] public int WindVariationDirection { get; init; }
    public WeatherFxParams WeatherFxParams { get; private set; } = WeatherFxParams.FromString("3_clear");

    private string _graphics = "3_clear";
}
