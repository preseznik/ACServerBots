using AssettoServer.RaceControl.Core.Configuration;
using AssettoServer.RaceControl.Core.Models;
using NUnit.Framework;

namespace AssettoServer.RaceControl.Tests;

public sealed class ServerConfigurationRendererTests
{
    [Test]
    public void Render_TrimsLastEntriesToPitCapacityAndKeepsMixedModels()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation(2, true, "car_one", "car_two");
        var preset = factory.CreatePreset(3);

        var rendered = new ServerConfigurationRenderer().Render(preset, factory.Scan());

        Assert.Multiple(() =>
        {
            Assert.That(rendered.EffectiveGrid, Has.Count.EqualTo(2));
            Assert.That(rendered.Cars.Select(car => car.Id), Is.EquivalentTo(new[] { "car_one", "car_two" }));
            Assert.That(rendered.ServerConfiguration.Get("SERVER", "MAX_CLIENTS"), Is.EqualTo("2"));
            Assert.That(rendered.EntryList.Get("CAR_0", "AI"), Is.EqualTo("auto"));
        });
    }

    [Test]
    public void Render_DisablingBotsMakesEveryEntryHumanOnlyWithoutChangingPresetModes()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation();
        var preset = factory.CreatePreset(2, false);
        preset.Grid[0].Mode = SlotMode.Fixed;

        var rendered = new ServerConfigurationRenderer().Render(preset, factory.Scan());

        Assert.Multiple(() =>
        {
            Assert.That(rendered.EntryList.Get("CAR_0", "AI"), Is.EqualTo("none"));
            Assert.That(rendered.EntryList.Get("CAR_1", "AI"), Is.EqualTo("none"));
            Assert.That(preset.Grid[0].Mode, Is.EqualTo(SlotMode.Fixed));
            Assert.That(rendered.ExtraConfiguration, Does.Contain("EnableAi: false"));
        });
    }
}
