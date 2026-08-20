using System.IO;
using System.Windows;
using System.Windows.Threading;
using AssettoServer.RaceControl.Core.Infrastructure;
using AssettoServer.RaceControl.Core.Storage;
using AssettoServer.RaceControl.Theming;

namespace AssettoServer.RaceControl;

public partial class App : Application
{
    private readonly RaceControlPaths _paths = new();
    private ApplicationSettingsStore? _settingsStore;

    public ApplicationSettings Settings { get; private set; } = new();
    public string DataRoot => _paths.DataRoot;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        _settingsStore = new ApplicationSettingsStore(_paths);
        Settings = _settingsStore.Load();
        ThemeManager.Apply(Settings);
        base.OnStartup(e);
    }

    public void ApplySettings(ApplicationSettings settings)
    {
        Settings = settings.Copy();
        ThemeManager.Apply(Settings);
        _settingsStore?.Save(Settings);
    }

    public void SaveSettings() => _settingsStore?.Save(Settings);

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AssettoServer Race Control",
                "Logs");
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(
                Path.Combine(logDirectory, "race-control-crash.log"),
                $"[{DateTimeOffset.Now:O}] {e.Exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // The original exception is more useful than a secondary logging failure.
        }

        MessageBox.Show(
            $"Race Control encountered an unexpected error and must close.\n\n{e.Exception.Message}\n\nA diagnostic was written to the local Logs folder.",
            "AssettoServer Race Control",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(1);
    }
}
