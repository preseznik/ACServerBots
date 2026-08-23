using AssettoServer.RaceControl.Core.Content;
using NUnit.Framework;

namespace AssettoServer.RaceControl.Tests;

public sealed class AcContentCatalogCacheTests
{
    [Test]
    public void SaveAndLoad_RoundTripsCatalogForInstallation()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation(8, true, "car_one", "car_two");
        var expected = factory.Scan();
        var cache = new AcContentCatalogCache(Path.Combine(factory.DataRoot, "Cache", "Content"));

        cache.Save(factory.AcRoot, expected);
        var actual = cache.TryLoad(factory.AcRoot);

        Assert.That(actual, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(actual!.Cars.Select(car => car.Id), Is.EqualTo(expected.Cars.Select(car => car.Id)));
            Assert.That(actual.Tracks.Select(track => track.Key), Is.EqualTo(expected.Tracks.Select(track => track.Key)));
            Assert.That(actual.Tracks.Single().PitBoxes, Is.EqualTo(8));
            Assert.That(actual.Weather.Select(weather => weather.Id), Is.EqualTo(expected.Weather.Select(weather => weather.Id)));
            Assert.That(actual.ScannedAt, Is.EqualTo(expected.ScannedAt));
        });
    }

    [Test]
    public void Cache_IsScopedToAssettoCorsaRoot()
    {
        using var first = new TestContentFactory();
        using var second = new TestContentFactory();
        first.CreateInstallation(carIds: ["first_car"]);
        second.CreateInstallation(carIds: ["second_car"]);
        var cacheDirectory = Path.Combine(first.DataRoot, "Cache", "Content");
        var cache = new AcContentCatalogCache(cacheDirectory);

        cache.Save(first.AcRoot, first.Scan());

        Assert.That(cache.TryLoad(second.AcRoot), Is.Null);
        Assert.That(cache.GetCachePath(first.AcRoot), Is.Not.EqualTo(cache.GetCachePath(second.AcRoot)));
    }

    [Test]
    public void TryLoad_CorruptCacheFallsBackToScan()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation();
        var cache = new AcContentCatalogCache(Path.Combine(factory.DataRoot, "Cache", "Content"));
        Directory.CreateDirectory(Path.GetDirectoryName(cache.GetCachePath(factory.AcRoot))!);
        File.WriteAllText(cache.GetCachePath(factory.AcRoot), "not json");

        Assert.That(cache.TryLoad(factory.AcRoot), Is.Null);
    }
}
