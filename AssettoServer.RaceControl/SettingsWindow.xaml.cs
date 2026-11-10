using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using AssettoServer.RaceControl.Core.Storage;
using AssettoServer.RaceControl.Core.Infrastructure;
using AssettoServer.RaceControl.Core.Staging;
using AssettoServer.RaceControl.Core.Network;
using AssettoServer.RaceControl.Core.Web;
using AssettoServer.RaceControl.Infrastructure;
using AssettoServer.RaceControl.Theming;

namespace AssettoServer.RaceControl;

public partial class SettingsWindow : Window, INotifyPropertyChanged
{
    private string _assettoCorsaRoot;
    private string _serverPayloadPath;
    private string _webUiPortText;
    private bool _installing;
    private readonly CancellationTokenSource _installationCancellation = new();

    public SettingsWindow(ApplicationSettings settings, string dataRoot,
        string assettoCorsaRoot, string serverPayloadPath, string webDashboardStatus)
    {
        Settings = settings.Copy();
        DataRoot = dataRoot;
        _assettoCorsaRoot = assettoCorsaRoot;
        _serverPayloadPath = serverPayloadPath;
        _webUiPortText = settings.WebUiPort.ToString();
        Settings.AssettoCorsaRoot = assettoCorsaRoot;
        Settings.ServerPayloadPath = serverPayloadPath;
        WebDashboardStatus = webDashboardStatus;
        WebBindAddresses = NetworkAddressService.GetPrivateIpv4Addresses()
            .Append(Settings.WebUiBindAddress)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        InitializeComponent();
        DataContext = this;
        Closing += (_, e) => { if (_installing) { _installationCancellation.Cancel(); e.Cancel = true; } };
        _legacyCatalog = new LegacyInstanceCatalog(new RaceControlPaths(dataRoot));
        Loaded += async (_, _) => await RefreshLegacyInstancesAsync();
    }

    private readonly LegacyInstanceCatalog _legacyCatalog;
    private string _legacyInstancesSummary = "Looking for old instance folders…";

    public ObservableCollection<LegacyInstance> LegacyInstances { get; } = [];
    public bool HasLegacyInstances => LegacyInstances.Count > 0;
    public string LegacyInstancesSummary
    {
        get => _legacyInstancesSummary;
        private set
        {
            _legacyInstancesSummary = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LegacyInstancesSummary)));
        }
    }

    private async Task RefreshLegacyInstancesAsync()
    {
        IReadOnlyList<LegacyInstance> found;
        try
        {
            // Measuring folder sizes touches every file, so keep it off the UI thread.
            found = await Task.Run(_legacyCatalog.List);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LegacyInstancesSummary = $"Could not read the Instances folder: {exception.Message}";
            return;
        }

        LegacyInstances.Clear();
        foreach (var instance in found)
            LegacyInstances.Add(instance);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasLegacyInstances)));
        long total = found.Sum(instance => instance.SizeBytes);
        LegacyInstancesSummary = found.Count == 0
            ? "No old instance folders. Race Control now reuses one working server and keeps compact copies in History."
            : $"{found.Count} folder(s) from older Race Control builds use {LegacyInstanceCatalog.FormatSize(total)}. "
              + "Race Control no longer uses them. Select the ones you don't need and move them to the Recycle Bin.";
    }

    private void LegacyInstancesList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        => RemoveLegacyInstancesButton.IsEnabled = LegacyInstancesList.SelectedItems.Count > 0;

    private void SelectAllLegacyInstances_Click(object sender, RoutedEventArgs e) => LegacyInstancesList.SelectAll();

    private async void RemoveLegacyInstances_Click(object sender, RoutedEventArgs e)
    {
        var selected = LegacyInstancesList.SelectedItems.Cast<LegacyInstance>().ToArray();
        if (selected.Length == 0)
            return;
        string size = LegacyInstanceCatalog.FormatSize(selected.Sum(instance => instance.SizeBytes));
        if (MessageBox.Show(this,
                $"Move {selected.Length} old instance folder(s) ({size}) to the Recycle Bin?\n\nYou can restore them from the Recycle Bin.",
                "Old instance folders", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;

        var owner = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var failed = new List<string>();
        foreach (var instance in selected)
        {
            // Re-check right before removing: never Current, History, or anything outside Instances.
            if (!_legacyCatalog.CanRemove(instance.Path) || !RecycleBin.MoveDirectory(instance.Path, owner))
                failed.Add(instance.Name);
        }

        await RefreshLegacyInstancesAsync();
        if (failed.Count > 0)
            MessageBox.Show(this,
                $"These folders were not moved (in use, or a file is locked):\n\n{string.Join("\n", failed)}",
                "Old instance folders", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ApplicationSettings Settings { get; }
    public string DataRoot { get; }
    public bool MapsChanged { get; private set; }
    public IReadOnlyList<AppThemeMode> ThemeModes { get; } = Enum.GetValues<AppThemeMode>();
    public IReadOnlyList<string> WebBindAddresses { get; }
    public string WebDashboardStatus { get; }
    public string WebUiPortText
    {
        get => _webUiPortText;
        set
        {
            if (string.Equals(_webUiPortText, value, StringComparison.Ordinal))
                return;
            _webUiPortText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WebUiPortText)));
        }
    }
    public string AssettoCorsaRoot
    {
        get => _assettoCorsaRoot;
        private set
        {
            if (string.Equals(_assettoCorsaRoot, value, StringComparison.OrdinalIgnoreCase))
                return;
            _assettoCorsaRoot = value;
            Settings.AssettoCorsaRoot = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AssettoCorsaRoot)));
        }
    }

    public string ServerPayloadPath
    {
        get => _serverPayloadPath;
        private set
        {
            if (string.Equals(_serverPayloadPath, value, StringComparison.OrdinalIgnoreCase))
                return;
            _serverPayloadPath = value;
            Settings.ServerPayloadPath = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ServerPayloadPath)));
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_installing) return;
        if (!int.TryParse(WebUiPortText, out int webPort))
        {
            MessageBox.Show(this, "Web GUI port must be a whole number between 1 and 65535.",
                "Invalid Web GUI settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Settings.WebUiPort = webPort;
        var options = new RaceControlWebServerOptions(Settings.WebUiEnabled,
            Settings.WebUiBindAddress, Settings.WebUiPort);
        if (!options.TryValidate(out string error))
        {
            MessageBox.Show(this, error, "Invalid Web GUI settings",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Window_SourceInitialized(object? sender, EventArgs e) => ThemeManager.ApplyWindowChrome(this);

    private void BrowseAcRoot_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the Assetto Corsa installation",
            InitialDirectory = Directory.Exists(AssettoCorsaRoot)
                ? AssettoCorsaRoot
                : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        };
        if (dialog.ShowDialog(this) == true)
            AssettoCorsaRoot = dialog.FolderName;
    }

    private void BrowseServerPayload_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the published standalone AssettoServer folder",
            InitialDirectory = Directory.Exists(ServerPayloadPath)
                ? ServerPayloadPath
                : AppContext.BaseDirectory,
        };
        if (dialog.ShowDialog(this) == true)
            ServerPayloadPath = dialog.FolderName;
    }

    private async void InstallComponent_Click(object sender, RoutedEventArgs e)
    {
        if (_installing) return;
        string action = (string)((System.Windows.Controls.Button)sender).Tag;
        string component = action.Split('-')[1];
        string? archive = null;
        if (action.StartsWith("import", StringComparison.Ordinal))
        {
            var dialog = new OpenFileDialog { Title = "Import " + component + " release ZIP",
                Filter = "Release archive (*.zip)|*.zip", AddToRecent = false };
            if (dialog.ShowDialog(this) != true) return;
            archive = dialog.FileName;
        }
        _installing = true;
        ComponentButtons.IsEnabled = false;
        _installationCancellation.TryReset();
        try
        {
            var installer = new ComponentInstaller(new RaceControlPaths(DataRoot));
            var progress = new Progress<string>(message => ComponentStatus.Text = message);
            string installed = archive is null
                ? await installer.DownloadAsync(component, progress, _installationCancellation.Token)
                : await installer.ImportAsync(archive, component, progress, _installationCancellation.Token);
            if (component == "server") ServerPayloadPath = installed;
            if (component == "maps") MapsChanged = true;
            ComponentStatus.Text = component == "server" ? "Server installed. Save settings to use it."
                : component == "maps" ? "FPS maps installed. Save settings to refresh the map list."
                : "FPS hosting assets installed. Players install the client ZIP into Assetto Corsa.";
        }
        catch (OperationCanceledException) { ComponentStatus.Text = "Installation cancelled."; }
        catch (Exception exception) { ComponentStatus.Text = exception.Message; }
        finally { _installing = false; ComponentButtons.IsEnabled = true; }
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(DataRoot);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{DataRoot}\"") { UseShellExecute = true });
    }
}
