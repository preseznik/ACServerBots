using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using AssettoServer.RaceControl.Core.Storage;
using AssettoServer.RaceControl.Theming;

namespace AssettoServer.RaceControl;

public partial class SettingsWindow : Window, INotifyPropertyChanged
{
    private string _assettoCorsaRoot;
    private string _serverPayloadPath;

    public SettingsWindow(ApplicationSettings settings, string dataRoot,
        string assettoCorsaRoot, string serverPayloadPath)
    {
        Settings = settings.Copy();
        DataRoot = dataRoot;
        _assettoCorsaRoot = assettoCorsaRoot;
        _serverPayloadPath = serverPayloadPath;
        Settings.AssettoCorsaRoot = assettoCorsaRoot;
        Settings.ServerPayloadPath = serverPayloadPath;
        InitializeComponent();
        DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ApplicationSettings Settings { get; }
    public string DataRoot { get; }
    public IReadOnlyList<AppThemeMode> ThemeModes { get; } = Enum.GetValues<AppThemeMode>();
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

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;
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

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(DataRoot);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{DataRoot}\"") { UseShellExecute = true });
    }
}
