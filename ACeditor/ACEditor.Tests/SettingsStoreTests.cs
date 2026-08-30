using System.Text.Json;
using ACEditor.Core.Tools;

namespace ACEditor.Tests;

public sealed class SettingsStoreTests
{
    [Test]
    public void RoundTrip_PreservesThemeAndToolOverrides()
    {
        using var temporary = new TemporaryDirectory();
        string path = Path.Combine(temporary.Create(), "settings.json");
        var store = new ToolchainSettingsStore(path);

        store.Save(new ApplicationSettings
        {
            Theme = AppTheme.Light,
            Toolchain = new ToolchainPaths
            {
                AssettoCorsaRoot = @"D:\Games\Assetto Corsa",
                BlenderExecutable = @"D:\Tools\Blender\blender.exe"
            }
        });

        ApplicationSettings loaded = store.LoadApplicationSettings();
        Assert.Multiple(() =>
        {
            Assert.That(loaded.Theme, Is.EqualTo(AppTheme.Light));
            Assert.That(loaded.Toolchain.AssettoCorsaRoot, Is.EqualTo(@"D:\Games\Assetto Corsa"));
            Assert.That(loaded.Toolchain.BlenderExecutable, Is.EqualTo(@"D:\Tools\Blender\blender.exe"));
        });
    }

    [Test]
    public void LoadApplicationSettings_MigratesLegacyFlatToolPaths()
    {
        using var temporary = new TemporaryDirectory();
        string path = Path.Combine(temporary.Create(), "settings.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new ToolchainPaths
        {
            Dirt2Root = @"E:\Games\DiRT 2",
            EgoPssgEditorRoot = @"E:\Tools\Ego PSSG Editor"
        }));

        ApplicationSettings loaded = new ToolchainSettingsStore(path).LoadApplicationSettings();
        Assert.Multiple(() =>
        {
            Assert.That(loaded.Theme, Is.EqualTo(AppTheme.System));
            Assert.That(loaded.Toolchain.Dirt2Root, Is.EqualTo(@"E:\Games\DiRT 2"));
            Assert.That(loaded.Toolchain.EgoPssgEditorRoot, Is.EqualTo(@"E:\Tools\Ego PSSG Editor"));
        });
    }

    [Test]
    public void SaveToolchain_PreservesExistingTheme()
    {
        using var temporary = new TemporaryDirectory();
        string path = Path.Combine(temporary.Create(), "settings.json");
        var store = new ToolchainSettingsStore(path);
        store.Save(new ApplicationSettings { Theme = AppTheme.Dark });

        store.Save(new ToolchainPaths { TexconvExecutable = @"D:\Tools\texconv.exe" });

        ApplicationSettings loaded = store.LoadApplicationSettings();
        Assert.Multiple(() =>
        {
            Assert.That(loaded.Theme, Is.EqualTo(AppTheme.Dark));
            Assert.That(loaded.Toolchain.TexconvExecutable, Is.EqualTo(@"D:\Tools\texconv.exe"));
        });
    }
}
