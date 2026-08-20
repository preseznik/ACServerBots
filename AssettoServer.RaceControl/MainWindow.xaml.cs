using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using AssettoServer.RaceControl.ViewModels;

namespace AssettoServer.RaceControl;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private bool _initialized;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await _viewModel.InitializeAsync();
    }

    private async void BrowseAcRoot_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the Assetto Corsa installation",
            InitialDirectory = Directory.Exists(_viewModel.Preset.AssettoCorsaRoot)
                ? _viewModel.Preset.AssettoCorsaRoot
                : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        };
        if (dialog.ShowDialog(this) == true)
        {
            await _viewModel.SetAssettoCorsaRootAsync(dialog.FolderName);
        }
    }

    private void BrowseServerPayload_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the published standalone AssettoServer folder",
            InitialDirectory = Directory.Exists(_viewModel.Preset.ServerPayloadPath)
                ? _viewModel.Preset.ServerPayloadPath
                : AppContext.BaseDirectory,
        };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.SetServerPayloadPath(dialog.FolderName);
        }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || !_viewModel.IsServerRunning)
        {
            _viewModel.Dispose();
            return;
        }

        e.Cancel = true;
        var answer = MessageBox.Show(
            this,
            "The LAN server is still running. Stop it and close Race Control?",
            "AssettoServer Race Control",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        await _viewModel.RequestStopAsync();
        _allowClose = true;
        Close();
    }

    private void LogTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        LogTextBox.ScrollToEnd();
    }
}
