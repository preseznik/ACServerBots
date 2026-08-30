using System.IO;
using System.Windows;
using ACEditor.App.Themes;
using ACEditor.Core.Tools;

namespace ACEditor.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ApplicationSettings settings;
        try { settings = new ToolchainSettingsStore().LoadApplicationSettings(); }
        catch (InvalidDataException) { settings = new ApplicationSettings(); }

        AppTheme theme = settings.Theme;
        string? overrideTheme = Environment.GetEnvironmentVariable("ACEDITOR_THEME");
        if (Enum.TryParse(overrideTheme, ignoreCase: true, out AppTheme parsed)) theme = parsed;

        ThemeManager.Apply(theme);
        base.OnStartup(e);
    }
}
