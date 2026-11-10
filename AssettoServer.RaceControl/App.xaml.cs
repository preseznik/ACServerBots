using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows;
using System.Windows.Threading;
using AssettoServer.RaceControl.Core.Infrastructure;
using AssettoServer.RaceControl.Core.Players;
using AssettoServer.RaceControl.Core.Storage;
using AssettoServer.RaceControl.Core.Web;
using AssettoServer.RaceControl.Infrastructure;
using AssettoServer.RaceControl.Theming;

namespace AssettoServer.RaceControl;

public partial class App : Application
{
    private readonly RaceControlPaths _paths = new();
    private ApplicationSettingsStore? _settingsStore;
    private readonly SemaphoreSlim _webServerLock = new(1, 1);
    private IRaceControlWebControl? _webControl;
    private RaceControlWebServer? _webServer;
    private Action<Exception>? _errorLog;
    private bool _showingError;

    public ApplicationSettings Settings { get; private set; } = new();
    /// <summary>Event registrations, shared by the Web GUI's Players page and the Grid's Registrations panel.</summary>
    public PlayerRegistrationStore Registrations { get; private set; } = null!;
    public string DataRoot => _paths.DataRoot;
    public string WebDashboardStatus { get; private set; } = "Not started";
    public string WebDashboardUrl => WebOptions.BrowserUrl;

    private RaceControlWebServerOptions WebOptions => new(
        Settings.WebUiEnabled, Settings.WebUiBindAddress, Settings.WebUiPort, Settings.WebAdminSteamIds);

    static App()
    {
        AssemblyLoadContext.Default.Resolving += ResolveLocalizedAssembly;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AsyncRelayCommand.ErrorHandler = ReportRecoverableError;
        try { _paths.ConfigureProcessStorage(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Race Control cannot write to its data folder:\n{_paths.DataRoot}\n\n{exception.Message}\n\nMove the portable folder to a writable location or correct its permissions.",
                "Race Control storage unavailable", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        if (File.Exists(Path.Combine(_paths.FpsAssetsDirectory, "asrc-fps-client.json")))
            AppContext.SetData("ASRC.FpsAssetRoot", _paths.FpsAssetsDirectory);
        if (File.Exists(Path.Combine(_paths.FpsMapsDirectory, "asrc-fps-maps.json")))
            AppContext.SetData("ASRC.FpsMapsRoot", _paths.FpsMapsDirectory);
        _settingsStore = new ApplicationSettingsStore(_paths);
        Registrations = new PlayerRegistrationStore(_paths);
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

    /// <summary>Shows unexpected but recoverable errors in the main window's log.</summary>
    public void AttachErrorLog(Action<Exception> log) => _errorLog = log;

    public async Task RestartWebDashboardAsync(CancellationToken cancellationToken = default)
    {
        await _webServerLock.WaitAsync(cancellationToken);
        try
        {
            if (_webServer is not null)
            {
                await StopWebDashboardCoreAsync();
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

            var options = WebOptions with { Enabled = true };
            var server = new RaceControlWebServer(options, _paths, _webControl, LogWebDashboard, Registrations);
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

    public async Task StopWebDashboardAsync(CancellationToken cancellationToken = default)
    {
        await _webServerLock.WaitAsync(cancellationToken);
        try
        {
            await StopWebDashboardCoreAsync();
        }
        finally
        {
            _webServerLock.Release();
        }
    }

    private async Task StopWebDashboardCoreAsync()
    {
        var server = Interlocked.Exchange(ref _webServer, null);
        if (server is null)
            return;
        try
        {
            await server.DisposeAsync();
            WebDashboardStatus = "Stopped";
        }
        catch (Exception exception)
        {
            WebDashboardStatus = $"Could not stop cleanly: {exception.Message}";
            LogWebDashboard($"ERROR: {WebDashboardStatus}");
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Web diagnostics must never take down the desktop launcher.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Normal window close awaits StopWebDashboardAsync before reaching OnExit. Keep this
        // fallback non-blocking: synchronous disposal here can deadlock with an HTTP request
        // waiting on the WPF dispatcher which is itself executing this method.
        var server = Interlocked.Exchange(ref _webServer, null);
        if (server is not null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await server.DisposeAsync();
                }
                catch
                {
                    // Process shutdown is already committed; diagnostics cannot delay it.
                }
            });
        }
        base.OnExit(e);
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
        e.Handled = true;
        // Before the main window is up, or after a truly fatal error, the app can't be trusted to keep
        // running. Anything else (a failed button, a locked file) only affects that one action, and
        // closing mid-race would also take the server controls down with it.
        if (!UnhandledErrorPolicy.IsFatal(e.Exception) && MainWindow is { IsLoaded: true })
        {
            ReportRecoverableError(e.Exception);
            return;
        }

        WriteCrashLog(e.Exception);
        MessageBox.Show(
            $"Race Control encountered an unexpected error and must close.\n\n{e.Exception.Message}\n\nA diagnostic was written to the local Logs folder.",
            "AssettoServer Race Control",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        Shutdown(1);
    }

    private void ReportRecoverableError(Exception exception)
    {
        if (UnhandledErrorPolicy.IsCancellation(exception))
            return;

        WriteCrashLog(exception);
        try
        {
            _errorLog?.Invoke(exception);
        }
        catch (Exception logException) when (!UnhandledErrorPolicy.IsFatal(logException))
        {
            WriteCrashLog(logException);
        }

        // MessageBox pumps messages, so a repeating error (a timer, say) could otherwise stack dialogs.
        if (_showingError)
            return;
        _showingError = true;
        try
        {
            MessageBox.Show(
                $"Something went wrong, but Race Control is still running.\n\n{exception.Message}\n\nDetails were written to Logs\\race-control-crash.log. If something looks wrong, save your event and restart Race Control when convenient.",
                "AssettoServer Race Control",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _showingError = false;
        }
    }

    private void WriteCrashLog(Exception exception)
    {
        try
        {
            var logDirectory = _paths.LogsDirectory;
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(
                Path.Combine(logDirectory, "race-control-crash.log"),
                $"[{DateTimeOffset.Now:O}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception logException) when (logException is IOException or UnauthorizedAccessException)
        {
            // The original exception is more useful than a secondary logging failure.
        }
    }
}
