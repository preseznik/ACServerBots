using AssettoServer.Server.Configuration.Kunos;
using NUnit.Framework;

namespace AssettoServer.Tests;

public sealed class WeatherConfigurationTests
{
    [Test]
    public void Defaults_AreSafeWhenGraphicsFieldIsMissing()
    {
        var weather = new WeatherConfiguration();

        Assert.Multiple(() =>
        {
            Assert.That(weather.Graphics, Is.EqualTo("3_clear"));
            Assert.That(weather.WeatherFxParams, Is.Not.Null);
        });
    }

    [Test]
    public void Graphics_BlankValueFallsBackWithoutNullWeatherParameters()
    {
        var weather = new WeatherConfiguration { Graphics = string.Empty };

        Assert.Multiple(() =>
        {
            Assert.That(weather.Graphics, Is.EqualTo("3_clear"));
            Assert.That(weather.WeatherFxParams, Is.Not.Null);
        });
    }
}
