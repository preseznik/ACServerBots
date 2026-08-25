using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AssettoServer.RaceControl.Infrastructure;
using AssettoServer.RaceControl.Core.Storage;
using AssettoServer.RaceControl.Theming;
using AssettoServer.RaceControl.ViewModels;

namespace AssettoServer.RaceControl;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private bool _initialized;
    private bool _allowClose;
    private readonly DispatcherTimer _takeoverInputTimer;
    private bool _leftPressed;
    private bool _rightPressed;
    private bool _throttlePressed;
    private bool _brakePressed;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _takeoverInputTimer = new DispatcherTimer(TimeSpan.FromSeconds(1d / 60d),
            DispatcherPriority.Input, TakeoverInputTimer_Tick, Dispatcher);
        _takeoverInputTimer.Start();
        Closed += (_, _) => _takeoverInputTimer.Stop();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        UpdateThemeMenuChecks();
        await _viewModel.InitializeAsync(RaceControlApp.Settings);
    }

    private void Window_SourceInitialized(object? sender, EventArgs e) => ThemeManager.ApplyWindowChrome(this);

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        PersistWindowSettings();
        if (_allowClose || !_viewModel.IsServerRunning)
        {
            _viewModel.Dispose();
            return;
        }

        e.Cancel = true;
        if (RaceControlApp.Settings.ConfirmBeforeStoppingServerOnExit)
        {
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
        }

        await _viewModel.RequestStopAsync();
        _allowClose = true;
        Close();
    }

    private void LogTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        LogTextBox.ScrollToEnd();
    }

    private App RaceControlApp => (App)Application.Current;

    private async void NewRace_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            this,
            "Create a new event? Unsaved changes to the current event will be discarded.",
            "New LAN event",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
        {
            await _viewModel.CreateNewEventAsync();
        }
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        string previousAcRoot = _viewModel.Preset.AssettoCorsaRoot;
        string previousServerPayload = _viewModel.Preset.ServerPayloadPath;
        var dialog = new SettingsWindow(RaceControlApp.Settings, RaceControlApp.DataRoot,
            previousAcRoot, previousServerPayload) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            RaceControlApp.ApplySettings(dialog.Settings);
            UpdateThemeMenuChecks();
            if (!string.Equals(previousServerPayload, dialog.ServerPayloadPath,
                    StringComparison.OrdinalIgnoreCase))
                _viewModel.SetServerPayloadPath(dialog.ServerPayloadPath);
            if (!string.Equals(previousAcRoot, dialog.AssettoCorsaRoot,
                    StringComparison.OrdinalIgnoreCase))
                await _viewModel.SetAssettoCorsaRootAsync(dialog.AssettoCorsaRoot);
        }
    }

    private void ThemeMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag } || !Enum.TryParse<AppThemeMode>(tag, out var theme))
        {
            return;
        }

        var settings = RaceControlApp.Settings.Copy();
        settings.Theme = theme;
        RaceControlApp.ApplySettings(settings);
        UpdateThemeMenuChecks();
    }

    private void UpdateThemeMenuChecks()
    {
        ThemeSystemMenuItem.IsChecked = RaceControlApp.Settings.Theme == AppThemeMode.System;
        ThemeLightMenuItem.IsChecked = RaceControlApp.Settings.Theme == AppThemeMode.Light;
        ThemeDarkMenuItem.IsChecked = RaceControlApp.Settings.Theme == AppThemeMode.Dark;
    }

    private void Documentation_Click(object sender, RoutedEventArgs e)
    {
        var documentation = FindDocumentation();
        if (documentation is null)
        {
            MessageBox.Show(this, "race-control.md was not found beside the app or in the source tree.",
                "Documentation", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo(documentation) { UseShellExecute = true });
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "AssettoServer Race Control\n\nNative LAN race and server-authoritative bot event editor.\n\nAGPL-3.0 — modified source and build instructions are included with distributed builds.",
            "About Race Control",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel.IsBotTakeoverActive)
        {
            if (e.Key == Key.Escape)
            {
                ClearTakeoverKeys();
                _viewModel.ReleaseTakeoverFromInput();
                e.Handled = true;
                return;
            }
            if (SetTakeoverKey(e.Key, true))
            {
                e.Handled = true;
                return;
            }
        }
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        if (e.Key == Key.N)
        {
            NewRace_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.S && _viewModel.SavePresetCommand.CanExecute(null))
        {
            _viewModel.SavePresetCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.OemComma)
        {
            Settings_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (_viewModel.IsBotTakeoverActive && SetTakeoverKey(e.Key, false))
            e.Handled = true;
    }

    private void Window_Deactivated(object? sender, EventArgs e) => ClearTakeoverKeys();

    private async void TakeoverInputTimer_Tick(object? sender, EventArgs e)
    {
        if (!_viewModel.IsBotTakeoverActive || !IsActive)
        {
            ClearTakeoverKeys();
            return;
        }
        XInputController.TryRead(out var controller);
        float keyboardSteering = (_rightPressed ? 1 : 0) - (_leftPressed ? 1 : 0);
        float rawSteering = Math.Abs(keyboardSteering) > Math.Abs(controller.Steering)
            ? keyboardSteering
            : controller.Steering;
        // The manual rigid-body input convention is opposite the keyboard/XInput axis.
        // Keep Right and a positive thumb-stick X intuitive at the UI boundary.
        float steering = -rawSteering;
        float throttle = Math.Max(_throttlePressed ? 1 : 0, controller.Throttle);
        float brake = Math.Max(_brakePressed ? 1 : 0, controller.Brake);
        await _viewModel.UpdateTakeoverInputAsync(steering, throttle, brake);
    }

    private bool SetTakeoverKey(Key key, bool pressed)
    {
        switch (key)
        {
            case Key.Left:
                _leftPressed = pressed;
                return true;
            case Key.Right:
                _rightPressed = pressed;
                return true;
            case Key.Up:
                _throttlePressed = pressed;
                return true;
            case Key.Down:
            case Key.Space:
                _brakePressed = pressed;
                return true;
            default:
                return false;
        }
    }

    private void ClearTakeoverKeys()
    {
        _leftPressed = false;
        _rightPressed = false;
        _throttlePressed = false;
        _brakePressed = false;
    }

    private void PersistWindowSettings()
    {
        if (RaceControlApp.Settings.RememberLastPage)
        {
            RaceControlApp.Settings.LastPageIndex = _viewModel.SelectedPageIndex;
        }
        RaceControlApp.SaveSettings();
    }

    private static string? FindDocumentation()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "race-control.md");
        if (File.Exists(bundled))
        {
            return bundled;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "docs", "race-control.md");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            directory = directory.Parent;
        }
        return null;
    }
}
