using NUnit.Framework;

namespace AssettoServer.RaceControl.Tests;

public sealed class AcContentScannerTests
{
    [Test]
    public void Scan_DiscoversCarSkinTrackAndPhysicsInputs()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation(6, true, "car_one", "car_two");
        File.AppendAllText(
            Path.Combine(factory.AcRoot, "content", "weather", "3_clear", "weather.ini"),
            "\n[__LAUNCHER_CM]\nWEATHER_TYPE=15\n");

        var catalog = factory.Scan();

        Assert.That(catalog.Cars, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(catalog.Cars[0].Brand, Is.EqualTo("Codex"));
            Assert.That(catalog.Cars[0].Skins.Single().Name, Is.EqualTo("Red"));
            Assert.That(catalog.Cars[0].MassKg, Is.EqualTo(1000));
            Assert.That(catalog.Cars[0].HasCollider, Is.True);
            Assert.That(catalog.Tracks.Single().PitBoxes, Is.EqualTo(6));
            Assert.That(catalog.Tracks.Single().HasFastLane, Is.True);
            Assert.That(catalog.Weather.Single().Id, Is.EqualTo("3_clear"));
            Assert.That(catalog.Weather.Single().Name, Is.EqualTo("Clear"));
            Assert.That(catalog.Weather.Single().WeatherFxType, Is.EqualTo(15));
        });
    }

    [Test]
    public void Scan_MalformedModMetadataDoesNotHideInstalledCar()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation();
        var uiPath = Path.Combine(factory.AcRoot, "content", "cars", "car_one", "ui", "ui_car.json");
        File.WriteAllText(uiPath, "{ \"name\": \"Broken\nDescription\" }");

        var car = factory.Scan().Cars.Single();

        Assert.That(car.Id, Is.EqualTo("car_one"));
        Assert.That(car.Name, Is.EqualTo("Car One"));
    }

    [Test]
    public void Scan_StockMultilineDescriptionStillProvidesVehicleSpecs()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation();
        var uiPath = Path.Combine(factory.AcRoot, "content", "cars", "car_one", "ui", "ui_car.json");
        File.WriteAllText(uiPath, """
        {
          "name": "GT3 Test",
          "description": "First line
        second line",
          "specs": {
            "bhp": "530bhp",
            "weight": "1265kg",
            "topspeed": "280+km/h"
          }
        }
        """);

        var car = factory.Scan().Cars.Single();

        Assert.Multiple(() =>
        {
            Assert.That(car.Name, Is.EqualTo("GT3 Test"));
            Assert.That(car.MassKg, Is.EqualTo(1265));
            Assert.That(car.PowerHp, Is.EqualTo(530));
            Assert.That(car.TopSpeedKmh, Is.EqualTo(280));
        });
    }
}
