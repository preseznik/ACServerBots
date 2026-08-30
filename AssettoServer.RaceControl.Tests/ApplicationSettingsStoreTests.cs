using AssettoServer.RaceControl.Core.Infrastructure;
using AssettoServer.RaceControl.Core.Storage;
using NUnit.Framework;

namespace AssettoServer.RaceControl.Tests;

public sealed class ApplicationSettingsStoreTests
{
    [Test]
    public void SaveAndLoad_RoundTripsThemeAndStartupPreferences()
    {
        using var factory = new TestContentFactory();
        var store = new ApplicationSettingsStore(new RaceControlPaths(factory.DataRoot));
        var settings = new ApplicationSettings
        {
            Theme = AppThemeMode.Light,
            LoadMostRecentPresetOnStartup = true,
            RememberLastPage = true,
            LastPageIndex = 4,
            ConfirmBeforeStoppingServerOnExit = false,
            CompactGridRows = true,
            AssettoCorsaRoot = @"C:\Games\Assetto Corsa",
            ServerPayloadPath = @"C:\Servers\AssettoServer",
            WebUiEnabled = true,
            WebUiBindAddress = "192.168.1.25",
            WebUiPort = 8872,
        };

        store.Save(settings);
        var loaded = store.Load();

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Theme, Is.EqualTo(AppThemeMode.Light));
            Assert.That(loaded.LoadMostRecentPresetOnStartup, Is.True);
            Assert.That(loaded.LastPageIndex, Is.EqualTo(4));
            Assert.That(loaded.ConfirmBeforeStoppingServerOnExit, Is.False);
            Assert.That(loaded.CompactGridRows, Is.True);
            Assert.That(loaded.AssettoCorsaRoot, Is.EqualTo(@"C:\Games\Assetto Corsa"));
            Assert.That(loaded.ServerPayloadPath, Is.EqualTo(@"C:\Servers\AssettoServer"));
            Assert.That(loaded.WebUiEnabled, Is.True);
            Assert.That(loaded.WebUiBindAddress, Is.EqualTo("192.168.1.25"));
            Assert.That(loaded.WebUiPort, Is.EqualTo(8872));
        });
    }

    [Test]
    public void Defaults_EnableLoopbackWebGui()
    {
        using var factory = new TestContentFactory();
        var store = new ApplicationSettingsStore(new RaceControlPaths(factory.DataRoot));

        var loaded = store.Load();

        Assert.Multiple(() =>
        {
            Assert.That(loaded.WebUiEnabled, Is.True);
            Assert.That(loaded.WebUiBindAddress, Is.EqualTo("127.0.0.1"));
            Assert.That(loaded.WebUiPort, Is.EqualTo(8772));
        });
    }
}
