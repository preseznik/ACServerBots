using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ACEditor.App.Themes;
using ACEditor.Core.Tools;
using Microsoft.Win32;

namespace ACEditor.App.Settings;

public partial class SettingsWindow : Window
{
    private readonly ToolchainSettingsStore _store = new();
    private readonly ApplicationSettings _working;
    private readonly AppTheme _originalTheme;
    private bool _loading = true;
    private bool _saved;

    public SettingsWindow()
    {
        InitializeComponent();
        ThemeManager.ApplyWindow(this);

        try { _working = _store.LoadApplicationSettings(); }
        catch (InvalidDataException) { _working = new ApplicationSettings(); }
        _originalTheme = ThemeManager.CurrentSetting;

        SystemThemeRadio.IsChecked = _working.Theme == AppTheme.System;
        DarkThemeRadio.IsChecked = _working.Theme == AppTheme.Dark;
        LightThemeRadio.IsChecked = _working.Theme == AppTheme.Light;
        ToolchainPaths effective = new ToolchainDiscovery().Discover();
        AssettoCorsaPath.Text = effective.AssettoCorsaRoot ?? string.Empty;
        Dirt2Path.Text = effective.Dirt2Root ?? string.Empty;
        EgoPssgPath.Text = effective.EgoPssgEditorRoot ?? string.Empty;
        BlenderPath.Text = effective.BlenderExecutable ?? string.Empty;
        TexconvPath.Text = effective.TexconvExecutable ?? string.Empty;
        KsEditorPath.Text = effective.KsEditorExecutable ?? string.Empty;
        _loading = false;

        Closing += SettingsWindow_Closing;
    }

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string value } ||
            !Enum.TryParse(value, out AppTheme theme)) return;
        _working.Theme = theme;
        ThemeManager.Apply(theme);
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string target } || FindName(target) is not TextBox textBox) return;
        var dialog = new OpenFolderDialog { Title = "Select tool or game directory", InitialDirectory = textBox.Text };
        if (dialog.ShowDialog(this) == true) textBox.Text = dialog.FolderName;
    }

    private void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string target } || FindName(target) is not TextBox textBox) return;
        var dialog = new OpenFileDialog
        {
            Title = "Select executable",
            Filter = "Applications (*.exe)|*.exe|All files (*.*)|*.*",
            FileName = textBox.Text
        };
        if (dialog.ShowDialog(this) == true) textBox.Text = dialog.FileName;
    }

    private void ClearPaths_Click(object sender, RoutedEventArgs e)
    {
        AssettoCorsaPath.Clear();
        Dirt2Path.Clear();
        EgoPssgPath.Clear();
        BlenderPath.Clear();
        TexconvPath.Clear();
        KsEditorPath.Clear();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _working.Toolchain = new ToolchainPaths
        {
            AssettoCorsaRoot = ValueOrNull(AssettoCorsaPath.Text),
            Dirt2Root = ValueOrNull(Dirt2Path.Text),
            EgoPssgEditorRoot = ValueOrNull(EgoPssgPath.Text),
            BlenderExecutable = ValueOrNull(BlenderPath.Text),
            TexconvExecutable = ValueOrNull(TexconvPath.Text),
            KsEditorExecutable = ValueOrNull(KsEditorPath.Text)
        };
        try
        {
            _store.Save(_working);
            _saved = true;
            DialogResult = true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Settings could not be saved",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void SettingsWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_saved) ThemeManager.Apply(_originalTheme);
    }

    private static string? ValueOrNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
