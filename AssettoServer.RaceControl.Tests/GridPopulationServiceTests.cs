using AssettoServer.RaceControl.Core.Content;
using AssettoServer.RaceControl.Core.Models;
using NUnit.Framework;

namespace AssettoServer.RaceControl.Tests;

public sealed class GridPopulationServiceTests
{
    [Test]
    public void Populate_ClassCreatesRequestedReplaceableSlotsAcrossMatchingCars()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation(carIds: ["race_a", "street", "race_b"]);
        WriteMetadata(factory, "race_a", "race", 1998, 300, 1000);
        WriteMetadata(factory, "street", "street", 2004, 150, 1200);
        WriteMetadata(factory, "race_b", "Race", 2001, 450, 1200);

        var result = new GridPopulationService().Populate(factory.Scan(),
            new GridPopulationRequest(3, GridPopulationCategory.Class, "race", NamePrefix: "Opponent"));

        Assert.Multiple(() =>
        {
            Assert.That(result.EligibleCarCount, Is.EqualTo(2));
            Assert.That(result.Slots, Has.Count.EqualTo(3));
            Assert.That(result.Slots.Select(slot => slot.CarId),
                Is.EqualTo(new[] { "race_a", "race_b", "race_a" }));
            Assert.That(result.Slots.All(slot => slot.Mode == SlotMode.Auto), Is.True);
            Assert.That(result.Slots.Select(slot => slot.DriverName),
                Is.EqualTo(new[] { "Opponent 01", "Opponent 02", "Opponent 03" }));
        });
    }

    [TestCase(GridPopulationCategory.MaximumHorsepower, 200, "street")]
    [TestCase(GridPopulationCategory.ModelYear, 2001, "race_b")]
    [TestCase(GridPopulationCategory.MaximumPowerToWeight, 310, "race_a")]
    public void Populate_NumericCriterionExcludesCarsWithoutMatchingMetadata(
        GridPopulationCategory category, double value, string expectedCar)
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation(carIds: ["race_a", "street", "race_b"]);
        WriteMetadata(factory, "race_a", "race", 1998, 300, 1000);
        WriteMetadata(factory, "street", "street", 2004, 150, 1200);
        WriteMetadata(factory, "race_b", "race", 2001, 450, 1000);
        var request = category switch
        {
            GridPopulationCategory.MaximumHorsepower =>
                new GridPopulationRequest(1, category, MaximumHorsepower: value),
            GridPopulationCategory.ModelYear =>
                new GridPopulationRequest(1, category, ModelYear: (int)value),
            GridPopulationCategory.MaximumPowerToWeight =>
                new GridPopulationRequest(1, category, MaximumPowerToWeightHpPerTonne: value),
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };

        var result = new GridPopulationService().Populate(factory.Scan(), request);

        Assert.That(result.Slots.Select(slot => slot.CarId), Does.Contain(expectedCar));
        if (category != GridPopulationCategory.MaximumPowerToWeight)
            Assert.That(result.EligibleCarCount, Is.EqualTo(1));
        else
            Assert.That(result.EligibleCarCount, Is.EqualTo(2));
    }

    private static void WriteMetadata(TestContentFactory factory, string carId, string className,
        int year, int horsepower, int massKg)
    {
        File.WriteAllText(Path.Combine(factory.AcRoot, "content", "cars", carId, "ui", "ui_car.json"), $$"""
        {
          "name": "{{carId}}",
          "brand": "Codex",
          "class": "{{className}}",
          "year": {{year}},
          "specs": { "bhp": "{{horsepower}} bhp", "weight": "{{massKg}} kg" }
        }
        """);
    }
}
