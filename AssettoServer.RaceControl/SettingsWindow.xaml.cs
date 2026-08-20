using System.Diagnostics;
using System.IO;
using System.Windows;
using AssettoServer.RaceControl.Core.Storage;
using AssettoServer.RaceControl.Theming;

namespace AssettoServer.RaceControl;

public partial class SettingsWindow : Window
{
    public SettingsWindow(ApplicationSettings settings, string dataRoot)
    {
        Settings = settings.Copy();
        DataRoot = dataRoot;
        InitializeComponent();
        DataContext = this;
    }

    public ApplicationSettings Settings { get; }
    public string DataRoot { get; }
    public IReadOnlyList<AppThemeMode> ThemeModes { get; } = Enum.GetValues<AppThemeMode>();

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Window_SourceInitialized(object? sender, EventArgs e) => ThemeManager.ApplyWindowChrome(this);

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(DataRoot);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{DataRoot}\"") { UseShellExecute = true });
    }
}
