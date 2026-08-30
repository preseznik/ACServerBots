using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows;
using System.Windows.Threading;
using AssettoServer.RaceControl.Core.Infrastructure;
using AssettoServer.RaceControl.Core.Storage;
using AssettoServer.RaceControl.Core.Web;
using AssettoServer.RaceControl.Theming;

namespace AssettoServer.RaceControl;

public partial class App : Application
{
    private readonly RaceControlPaths _paths = new();
    private ApplicationSettingsStore? _settingsStore;
    private readonly SemaphoreSlim _webServerLock = new(1, 1);
    private IRaceControlWebControl? _webControl;
    private RaceControlWebServer? _webServer;

    public ApplicationSettings Settings { get; private set; } = new();
    public string DataRoot => _paths.DataRoot;
    public string WebDashboardStatus { get; private set; } = "Not started";
    public string WebDashboardUrl => new RaceControlWebServerOptions(
        Settings.WebUiEnabled, Settings.WebUiBindAddress, Settings.WebUiPort).BrowserUrl;

    static App()
    {
        AssemblyLoadContext.Default.Resolving += ResolveLocalizedAssembly;
    }

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

    public void AttachWebControl(IRaceControlWebControl control) => _webControl = control;

    public async Task RestartWebDashboardAsync(CancellationToken cancellationToken = default)
    {
        await _webServerLock.WaitAsync(cancellationToken);
        try
        {
            if (_webServer is not null)
            {
                await _webServer.DisposeAsync();
                _webServer = null;
            }

            if (!Settings.WebUiEnabled)
            {
                WebDashboardStatus = "Disabled";
                LogWebDashboard("Web GUI disabled in application settings.");
                return;
            }
            if (_webControl is null)
            {
                WebDashboardStatus = "Waiting for launcher";
                return;
            }

            var options = new RaceControlWebServerOptions(true,
                Settings.WebUiBindAddress, Settings.WebUiPort);
            var server = new RaceControlWebServer(options, _paths, _webControl, LogWebDashboard);
            try
            {
                await server.StartAsync(cancellationToken);
                _webServer = server;
                WebDashboardStatus = $"Listening at {server.BrowserUrl}";
            }
            catch (Exception exception)
            {
                await server.DisposeAsync();
                WebDashboardStatus = $"Could not start: {exception.Message}";
                LogWebDashboard($"ERROR: {WebDashboardStatus}");
            }
        }
        finally
        {
            _webServerLock.Release();
        }
    }

    private void LogWebDashboard(string message)
    {
        try
        {
            Directory.CreateDirectory(_paths.LogsDirectory);
            File.AppendAllText(Path.Combine(_paths.LogsDirectory, "web-gui.log"),
                $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Web diagnostics must never take down the desktop launcher.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _webServer?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        finally
        {
            _webServerLock.Dispose();
            base.OnExit(e);
        }
    }

    private static Assembly? ResolveLocalizedAssembly(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        var culture = assemblyName.CultureName;
        if (assemblyName.Name is null
            || !assemblyName.Name.EndsWith(".resources", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(culture)
            || culture.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || culture.Contains(Path.DirectorySeparatorChar)
            || culture.Contains(Path.AltDirectorySeparatorChar))
        {
            return null;
        }

        var candidate = Path.Combine(AppContext.BaseDirectory, "lang", culture, $"{assemblyName.Name}.dll");
        return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
    }

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
