using AssettoServer.RaceControl.Core.Infrastructure;
using AssettoServer.RaceControl.Core.Models;
using AssettoServer.RaceControl.Core.Storage;
using NUnit.Framework;

namespace AssettoServer.RaceControl.Tests;

public sealed class SavedGridStoreTests
{
    [Test]
    public void SaveLoadListAndDelete_RoundTripsExactGridRows()
    {
        using var factory = new TestContentFactory();
        var paths = new RaceControlPaths(factory.DataRoot);
        var store = new SavedGridStore(paths);
        var expected = new SavedGridPreset
        {
            Name = "GT favorites",
            Slots =
            [
                new GridSlotPreset
                {
                    CarId = "gt3_a", SkinId = "red", DriverName = "Racer 01",
                    TeamName = "Works", NationCode = "ITA", BallastKg = 12,
                    RestrictorPercent = 5, Difficulty = 0.88, Aggression = 0.67,
                    Mode = SlotMode.Auto,
                },
                new GridSlotPreset
                {
                    CarId = "camera_car", SkinId = "black", DriverName = "TV",
                    Mode = SlotMode.Spectator,
                },
            ],
        };

        string path = store.Save(expected);
        var summary = store.List().Single();
        var actual = store.Load(path);

        Assert.Multiple(() =>
        {
            Assert.That(summary.Id, Is.EqualTo(expected.Id));
            Assert.That(summary.Name, Is.EqualTo("GT favorites"));
            Assert.That(actual.Slots, Has.Count.EqualTo(2));
            Assert.That(actual.Slots[0].BallastKg, Is.EqualTo(12));
            Assert.That(actual.Slots[0].RestrictorPercent, Is.EqualTo(5));
            Assert.That(actual.Slots[0].Difficulty, Is.EqualTo(0.88));
            Assert.That(actual.Slots[0].Aggression, Is.EqualTo(0.67));
            Assert.That(actual.Slots[1].Mode, Is.EqualTo(SlotMode.Spectator));
        });

        store.Delete(path);
        Assert.That(store.List(), Is.Empty);
    }

    [Test]
    public void Delete_RejectsPathOutsideSavedGridDirectory()
    {
        using var factory = new TestContentFactory();
        var store = new SavedGridStore(new RaceControlPaths(factory.DataRoot));

        Assert.Throws<InvalidOperationException>(() => store.Delete(
            Path.Combine(factory.DataRoot, "Presets", "event.json")));
    }

    [Test]
    public void List_IgnoresCorruptOrUnsupportedFavorites()
    {
        using var factory = new TestContentFactory();
        var paths = new RaceControlPaths(factory.DataRoot);
        paths.EnsureCreated();
        File.WriteAllText(Path.Combine(paths.GridsDirectory, "corrupt.json"), "not json");
        File.WriteAllText(Path.Combine(paths.GridsDirectory, "future.json"),
            "{ \"schemaVersion\": 99, \"name\": \"Future\", \"slots\": [] }");
        var store = new SavedGridStore(paths);

        Assert.That(store.List(), Is.Empty);
    }
}
