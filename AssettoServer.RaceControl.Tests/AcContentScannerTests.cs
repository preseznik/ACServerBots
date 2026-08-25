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
            Assert.That(catalog.Cars[0].Year, Is.EqualTo(2000));
            Assert.That(catalog.Cars[0].PowerToWeightHpPerTonne, Is.EqualTo(200));
            Assert.That(catalog.Cars[0].HasCollider, Is.True);
            Assert.That(catalog.Tracks.Single().PitBoxes, Is.EqualTo(6));
            Assert.That(catalog.Tracks.Single().HasFastLane, Is.True);
            Assert.That(catalog.Tracks.Single().RaceBotPreflight!.CanPrepare, Is.True);
            Assert.That(catalog.Weather.Single().Id, Is.EqualTo("3_clear"));
            Assert.That(catalog.Weather.Single().Name, Is.EqualTo("Clear"));
            Assert.That(catalog.Weather.Single().WeatherFxType, Is.EqualTo(15));
        });
    }

    [Test]
    public void Scan_PreflightRejectsOpenSplineAndCleansModelComments()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation();
        string trackRoot = Path.Combine(factory.AcRoot, "content", "tracks", "test_track");
        File.WriteAllText(Path.Combine(trackRoot, "models.ini"),
            "[MODEL_0]\nFILE=track.kn5 ; physical road\n[MODEL_1]\nFILE=; optional missing model\n");
        string fastLane = Path.Combine(trackRoot, "ai", "fast_lane.ai");
        using (var stream = File.Create(fastLane))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(-1);
            writer.Write(32);
            for (int index = 0; index < 32; index++)
            {
                writer.Write(index * 10f);
                writer.Write(0f);
                writer.Write(0f);
                writer.Write(100f);
                writer.Write(0f);
            }
        }

        var preflight = factory.Scan().Tracks.Single().RaceBotPreflight!;

        Assert.Multiple(() =>
        {
            Assert.That(preflight.HasReadableClosedSpline, Is.False);
            Assert.That(preflight.Failure, Does.Contain("open"));
            Assert.That(preflight.MissingModelFiles, Is.Empty);
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

    [Test]
    public void Scan_NonFiniteVehicleSpecIsIgnored()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation();
        var uiPath = Path.Combine(factory.AcRoot, "content", "cars", "car_one", "ui", "ui_car.json");
        File.WriteAllText(uiPath, """
        {
          "name": "Invalid Spec Test",
          "specs": { "bhp": 1e999, "weight": "1000 kg" }
        }
        """);

        var car = factory.Scan().Cars.Single();

        Assert.Multiple(() =>
        {
            Assert.That(car.PowerHp, Is.Null);
            Assert.That(car.MassKg, Is.EqualTo(1000));
        });
    }
}
