using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ACEditor.App.Settings;
using ACEditor.App.Themes;
using ACEditor.App.Controls;
using ACEditor.App.ViewModels;
using ACEditor.Core.Models;
using ACEditor.Core.Staging;
using ACEditor.Core.Tools;
using Microsoft.Win32;

namespace ACEditor.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private string? _lastStage;

    public MainWindow()
    {
        InitializeComponent();
        ThemeManager.ApplyWindow(this);
        DataContext = _viewModel;
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo, (_, _) => _viewModel.Undo()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo, (_, _) => _viewModel.Redo()));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Redo, Key.Y, ModifierKeys.Control));
    }

    private async void ImportTrack_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select an Assetto Corsa or DiRT 2 track folder" };
        if (dialog.ShowDialog(this) == true) await _viewModel.ImportAsync(dialog.FolderName);
    }

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "AC Editor projects (*.acedit)|*.acedit|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) == true) await _viewModel.OpenProjectAsync(dialog.FileName);
    }

    private async void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Project is null) return;
        if (string.IsNullOrWhiteSpace(_viewModel.Project.ProjectFile))
        {
            await SaveProjectAsAsync();
            return;
        }
        await _viewModel.SaveProjectAsync(_viewModel.Project.ProjectFile);
    }

    private async void SaveProjectAs_Click(object sender, RoutedEventArgs e) => await SaveProjectAsAsync();

    private async Task SaveProjectAsAsync()
    {
        if (_viewModel.Project is null) return;
        var dialog = new SaveFileDialog
        {
            Filter = "AC Editor projects (*.acedit)|*.acedit",
            FileName = _viewModel.Project.Name + ".acedit",
            AddExtension = true,
            DefaultExt = ".acedit"
        };
        if (dialog.ShowDialog(this) == true) await _viewModel.SaveProjectAsync(dialog.FileName);
    }

    private async void BuildStage_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Project is null) return;
        var dialog = new OpenFolderDialog { Title = "Select the parent folder for the staged copy" };
        if (dialog.ShowDialog(this) != true) return;
        string output = Path.Combine(dialog.FolderName, Sanitize(_viewModel.Project.Name) + "-stage");
        StageResult? result = await _viewModel.StageAsync(output);
        if (result?.Succeeded == true) _lastStage = result.OutputDirectory;
    }

    private void OpenStage_Click(object sender, RoutedEventArgs e)
    {
        string? stage = _lastStage;
        if (stage is null || !Directory.Exists(stage))
        {
            MessageBox.Show(this, "Build a staged copy first.", "AC Editor", MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        OpenPath(stage);
    }

    private async void Publish_Click(object sender, RoutedEventArgs e)
    {
        string? stage = _lastStage;
        if (stage is null || !Directory.Exists(stage))
        {
            MessageBox.Show(this, "Build and validate a staged copy before publishing.", "AC Editor",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new OpenFolderDialog { Title = "Select the exact installed track directory to replace" };
        if (dialog.ShowDialog(this) != true) return;
        if (MessageBox.Show(this,
                $"Publish the staged copy to:\n{dialog.FolderName}\n\nThe existing directory will be renamed to a timestamped backup.",
                "Publish with backup", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;
        try
        {
            PublishResult result = await new SafePublisher().PublishAsync(stage, dialog.FolderName);
            MessageBox.Show(this, result.BackupPath is null
                    ? $"Published to {result.InstalledPath}."
                    : $"Published to {result.InstalledPath}.\nBackup: {result.BackupPath}",
                "Publish complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Publish failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void KsEditor_Click(object sender, RoutedEventArgs e)
    {
        string? executable = new ToolchainDiscovery().Discover().KsEditorExecutable;
        if (executable is null)
        {
            MessageBox.Show(this, "ksEditor was not found.", "AC Editor", MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(executable)! });
    }

    private void SceneTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TrackNode node) _viewModel.SelectedNode = node;
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => _viewModel.Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => _viewModel.Redo();
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (new SettingsWindow { Owner = this }.ShowDialog() == true) _viewModel.RefreshSettings();
    }
    private void ViewportMode_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string value } &&
            Enum.TryParse(value, out ViewportRenderMode mode) && SceneViewport is not null)
            SceneViewport.RenderMode = mode;
    }

    private async void ExportPssgTexture_Click(object sender, RoutedEventArgs e)
    {
        TrackTexture? texture = _viewModel.SelectedTexture;
        if (texture?.EmbeddedData is not { Length: > 0 }) return;
        string sourceName = texture.Name.Split('#').LastOrDefault() ?? "texture";
        var dialog = new SaveFileDialog
        {
            Filter = "DirectDraw Surface (*.dds)|*.dds",
            FileName = Sanitize(Path.ChangeExtension(sourceName, ".dds")),
            AddExtension = true,
            DefaultExt = ".dds"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await _viewModel.ExportSelectedTextureAsync(dialog.FileName);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Texture export failed", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void ReplacePssgTexture_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a DDS replacement",
            Filter = "DirectDraw Surface (*.dds)|*.dds"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await _viewModel.ReplaceSelectedPssgTextureAsync(dialog.FileName);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Texture replacement failed", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private static void OpenPath(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    private static string Sanitize(string value) => string.Concat(value.Select(character =>
        Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
}
