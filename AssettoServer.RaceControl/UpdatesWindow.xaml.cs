using System.Diagnostics;
using System.IO;
using System.Windows;
using AssettoServer.RaceControl.Core.Infrastructure;
using AssettoServer.RaceControl.Core.Updates;
using AssettoServer.Release;

namespace AssettoServer.RaceControl;

public partial class UpdatesWindow : Window
{
    private readonly RaceControlPaths _paths = new();
    private readonly System.Net.Http.HttpClient _client = ReleaseUpdateService.CreateClient();
    private ReleaseUpdate? _release;
    private CancellationTokenSource? _operation;
    private bool _ready;
    private bool _closed;

    public UpdatesWindow(ReleaseUpdate? release = null)
    {
        InitializeComponent();
        VersionText.Text = "Installed version: " + ReleaseIdentity.Version;
        StartupCheck.IsChecked = RaceControlApp.Settings.CheckForUpdatesOnStartup;
        PreviewCheck.IsChecked = RaceControlApp.Settings.IncludePreviewUpdates;
        PortableNotice.Visibility = _paths.IsPortable ? Visibility.Visible : Visibility.Collapsed;
        _release = release;
        _ready = true;
        Closed += (_, _) => { _closed = true; _operation?.Cancel(); _client.Dispose(); };
    }

    private App RaceControlApp => (App)Application.Current;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_release is null) await CheckAsync();
        else ShowRelease();
    }

    private void Preferences_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        RaceControlApp.Settings.CheckForUpdatesOnStartup = StartupCheck.IsChecked == true;
        RaceControlApp.Settings.IncludePreviewUpdates = PreviewCheck.IsChecked == true;
        try { RaceControlApp.SaveSettings(); }
        catch (Exception exception) { StatusText.Text = "Could not save update preferences: " + exception.Message; return; }
        _release = null;
        InstallButton.IsEnabled = false;
        StatusText.Text = "Preferences saved. Check now for available updates.";
    }

    private void SetBusy(bool busy)
    {
        if (_closed) return;
        CheckButton.IsEnabled = StartupCheck.IsEnabled = PreviewCheck.IsEnabled = !busy;
        InstallButton.IsEnabled = !busy && _release?.Installer is not null;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowRelease()
    {
        StatusText.Text = _release is null ? "You have the latest available release."
            : $"Race Control {_release.Version} is available." + (_release.Installer is null
                ? " No verified installer is attached. Open GitHub releases for downloads." : " Ready to download.");
        SetBusy(false);
    }

    private async void Check_Click(object sender, RoutedEventArgs e) => await CheckAsync();

    private async Task CheckAsync()
    {
        if (_operation is not null) return;
        _operation = new();
        SetBusy(true);
        StatusText.Text = "Checking GitHub releases…";
        try
        {
            _release = await new ReleaseUpdateService(_client).CheckAsync(ReleaseIdentity.Version,
                PreviewCheck.IsChecked == true, _operation.Token);
            if (!_closed) ShowRelease();
        }
        catch (OperationCanceledException) { if (!_closed) StatusText.Text = "Update check cancelled or timed out."; }
        catch (Exception exception) { _release = null; if (!_closed) StatusText.Text = "Update check failed: " + exception.Message; }
        finally { _operation.Dispose(); _operation = null; SetBusy(false); }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_operation is not null || _release?.Installer is not { } installer) return;
        if (Owner is not MainWindow main || !main.CanInstallUpdate)
        {
            StatusText.Text = "Stop the server and wait for preparation to finish before installing an update.";
            return;
        }
        _operation = new();
        SetBusy(true);
        DownloadProgress.Visibility = Visibility.Visible;
        DownloadProgress.Value = 0;
        StatusText.Text = $"Downloading Race Control {_release.Version}…";
        try
        {
            var progress = new Progress<int>(value => { if (!_closed) DownloadProgress.Value = value; });
            string path = await new ReleaseUpdateService(_client).DownloadAsync(installer, _paths, progress, _operation.Token);
            var command = await Task.Run(() => ReleaseUpdateService.InstallerCommand(installer, path, _paths), _operation.Token);
            if (_closed || _operation.IsCancellationRequested) return;
            if (!main.CanInstallUpdate) throw new IOException("Download verified. Stop the server before installing.");
            using var process = Process.Start(command) ?? throw new IOException("The installer did not start.");
            Close();
            main.Close();
        }
        catch (OperationCanceledException) { if (!_closed) StatusText.Text = "Download cancelled."; }
        catch (Exception exception) { if (!_closed) StatusText.Text = "Update failed: " + exception.Message; }
        finally
        {
            _operation.Dispose();
            _operation = null;
            if (!_closed) DownloadProgress.Visibility = Visibility.Collapsed;
            SetBusy(false);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();
    private void Releases_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo(
        _release?.Page.AbsoluteUri ?? ReleaseUpdateService.Repository + "/releases") { UseShellExecute = true });
}
