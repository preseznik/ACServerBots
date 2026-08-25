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
        });
    }
}
