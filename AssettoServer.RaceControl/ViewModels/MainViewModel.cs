using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using AssettoServer.RaceControl.Core.Configuration;
using AssettoServer.RaceControl.Core.Content;
using AssettoServer.RaceControl.Core.Infrastructure;
using AssettoServer.RaceControl.Core.Models;
using AssettoServer.RaceControl.Core.Network;
using AssettoServer.RaceControl.Core.Runtime;
using AssettoServer.RaceControl.Core.Staging;
using AssettoServer.RaceControl.Core.Storage;
using AssettoServer.RaceControl.Core.Validation;
using AssettoServer.RaceControl.Infrastructure;

namespace AssettoServer.RaceControl.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly RaceControlPaths _paths = new();
    private readonly AcContentScanner _scanner = new();
    private readonly RaceControlValidator _validator = new();
    private readonly ServerConfigurationRenderer _renderer = new();
    private readonly CmPresetService _cmPresetService = new();
    private readonly ServerProcessController _processController = new();
    private PresetStore? _presetStore;
    private RaceControlPreset _preset = new();
    private AcContentCatalog? _catalog;
    private AcTrackLayout? _selectedTrack;
    private AcWeather? _selectedWeather;
    private GridSlotViewModel? _selectedGridSlot;
    private PresetSummary? _selectedSavedPreset;
    private InstanceSummary? _selectedRecentInstance;
    private StagedInstance? _lastInstance;
    private bool _isBusy;
    private int _selectedPageIndex;
    private string _statusText = "Ready";
    private string _progressText = string.Empty;
    private double _progressValue;
    private string _logText = string.Empty;

    public MainViewModel()
    {
        RefreshContentCommand = new AsyncRelayCommand(RefreshContentAsync, () => !IsBusy);
        SavePresetCommand = new RelayCommand(SavePreset, () => !IsBusy);
        LoadPresetCommand = new AsyncRelayCommand(LoadSelectedPresetAsync, () => SelectedSavedPreset is not null && !IsBusy);
        ImportCmPresetCommand = new AsyncRelayCommand(ImportLatestCmPresetAsync, () => !IsBusy);
        ExportCmPresetCommand = new RelayCommand(ExportCmPreset, () => _catalog is not null && !IsBusy);
        AddSlotCommand = new RelayCommand(AddSlot, () => Cars.Count > 0 && Grid.Count < 254 && !IsBusy);
        RemoveSlotCommand = new RelayCommand(RemoveSelectedSlot, () => SelectedGridSlot is not null && Grid.Count > 2 && !IsBusy);
        MoveSlotUpCommand = new RelayCommand(() => MoveSelectedSlot(-1), () => CanMoveSelectedSlot(-1));
        MoveSlotDownCommand = new RelayCommand(() => MoveSelectedSlot(1), () => CanMoveSelectedSlot(1));
        FillGridCommand = new RelayCommand(FillGridToPitCapacity, () => SelectedTrack is { PitBoxes: > 1 } && Cars.Count > 0 && !IsBusy);
        RandomizeSkinsCommand = new RelayCommand(RandomizeSkins, () => Grid.Count > 0 && !IsBusy);
        MakeAllReplaceableCommand = new RelayCommand(MakeAllReplaceable, () => Grid.Count > 0 && !IsBusy);
        ValidateCommand = new RelayCommand(Validate, () => _catalog is not null && !IsBusy);
        StageCommand = new AsyncRelayCommand(StageOnlyAsync, () => _catalog is not null && !IsBusy);
        LaunchCommand = new AsyncRelayCommand(StageAndLaunchAsync, () => _catalog is not null && !IsBusy && _processController.State == ServerProcessState.Stopped);
        StopCommand = new AsyncRelayCommand(StopServerAsync, () => _processController.State != ServerProcessState.Stopped);
        RestartCommand = new AsyncRelayCommand(RestartServerAsync, () => _lastInstance is not null && _processController.State == ServerProcessState.Running);
        OpenInstanceCommand = new RelayCommand(OpenInstance, () => _lastInstance is not null || SelectedRecentInstance is not null);
        OpenContentManagerCommand = new RelayCommand(OpenContentManager);
        ClearLogCommand = new RelayCommand(() => LogText = string.Empty);

        _processController.LogReceived += (_, line) => RunOnUi(() => AppendLog(line));
        _processController.StateChanged += (_, _) => RunOnUi(() =>
        {
            OnPropertyChanged(nameof(ServerStateText));
            OnPropertyChanged(nameof(IsServerRunning));
            RaiseCommandStates();
        });
    }

    public RaceControlPreset Preset
    {
        get => _preset;
        private set
        {
            if (SetProperty(ref _preset, value))
            {
                OnPropertyChanged(nameof(EventTitle));
            }
        }
    }

    public ObservableCollection<AcCar> Cars { get; } = [];
    public ObservableCollection<AcTrackLayout> Tracks { get; } = [];
    public ObservableCollection<AcWeather> Weather { get; } = [];
    public ObservableCollection<GridSlotViewModel> Grid { get; } = [];
    public ObservableCollection<ValidationMessageViewModel> ValidationMessages { get; } = [];
    public ObservableCollection<PresetSummary> SavedPresets { get; } = [];
    public ObservableCollection<InstanceSummary> RecentInstances { get; } = [];
    public ObservableCollection<string> NetworkAddresses { get; } = [];
    public IReadOnlyList<SlotMode> SlotModes { get; } = Enum.GetValues<SlotMode>();
    public IReadOnlyList<PhysicsFidelity> PhysicsFidelities { get; } = Enum.GetValues<PhysicsFidelity>();

    public AcTrackLayout? SelectedTrack
    {
        get => _selectedTrack;
        set
        {
            if (SetProperty(ref _selectedTrack, value) && value is not null)
            {
                Preset.TrackId = value.TrackId;
                Preset.TrackLayoutId = value.LayoutId;
                OnPropertyChanged(nameof(SelectedTrackDetails));
                RaiseCommandStates();
            }
        }
    }

    public AcWeather? SelectedWeather
    {
        get => _selectedWeather;
        set
        {
            // ItemsSource refreshes briefly clear Selector values. Do not let that erase
            // the persisted weather and produce an empty server GRAPHICS field.
            if (value is not null && SetProperty(ref _selectedWeather, value))
            {
                Preset.Conditions.WeatherId = value.Id;
            }
        }
    }

    public GridSlotViewModel? SelectedGridSlot
    {
        get => _selectedGridSlot;
        set
        {
            if (SetProperty(ref _selectedGridSlot, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public PresetSummary? SelectedSavedPreset
    {
        get => _selectedSavedPreset;
        set
        {
            if (SetProperty(ref _selectedSavedPreset, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public InstanceSummary? SelectedRecentInstance
    {
        get => _selectedRecentInstance;
        set
        {
            if (SetProperty(ref _selectedRecentInstance, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public int SelectedPageIndex
    {
        get => _selectedPageIndex;
        set => SetProperty(ref _selectedPageIndex, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public string LogText
    {
        get => _logText;
        private set => SetProperty(ref _logText, value);
    }

    public string EventTitle => string.IsNullOrWhiteSpace(Preset.Name) ? "Untitled LAN race" : Preset.Name;
    public string ServerStateText => _processController.State switch
    {
        ServerProcessState.Running => $"RUNNING  •  PID {_processController.ProcessId}",
        ServerProcessState.Stopping => "STOPPING",
        _ => "OFFLINE",
    };
    public bool IsServerRunning => _processController.State == ServerProcessState.Running;
    public string SelectedTrackDetails => SelectedTrack is null
        ? "No track selected"
        : $"{SelectedTrack.Country}  •  {SelectedTrack.PitBoxes} pit boxes  •  {(SelectedTrack.HasFastLane ? "AI line ready" : "no AI line")}";
    public string EffectiveGridSummary => SelectedTrack is null
        ? $"{Grid.Count} requested slots"
        : $"{Math.Min(Grid.Count, SelectedTrack.PitBoxes)} effective / {Grid.Count} requested  •  {SelectedTrack.PitBoxes} pit boxes";
    public string LastInstanceSummary => _lastInstance is null
        ? "No instance staged in this session."
        : $"{_lastInstance.SlotCount} slots • {_lastInstance.BotSlotCount} bot-capable • {(_lastInstance.PhysicsCacheHit ? "physics cache hit" : "physics prepared")}";

    public AsyncRelayCommand RefreshContentCommand { get; }
    public RelayCommand SavePresetCommand { get; }
    public AsyncRelayCommand LoadPresetCommand { get; }
    public AsyncRelayCommand ImportCmPresetCommand { get; }
    public RelayCommand ExportCmPresetCommand { get; }
    public RelayCommand AddSlotCommand { get; }
    public RelayCommand RemoveSlotCommand { get; }
    public RelayCommand MoveSlotUpCommand { get; }
    public RelayCommand MoveSlotDownCommand { get; }
    public RelayCommand FillGridCommand { get; }
    public RelayCommand RandomizeSkinsCommand { get; }
    public RelayCommand MakeAllReplaceableCommand { get; }
    public RelayCommand ValidateCommand { get; }
    public AsyncRelayCommand StageCommand { get; }
    public AsyncRelayCommand LaunchCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public AsyncRelayCommand RestartCommand { get; }
    public RelayCommand OpenInstanceCommand { get; }
    public RelayCommand OpenContentManagerCommand { get; }
    public RelayCommand ClearLogCommand { get; }

    public async Task InitializeAsync(ApplicationSettings? settings = null)
    {
        try
        {
            IsBusy = true;
            StatusText = "Locating Assetto Corsa…";
            _paths.EnsureCreated();
            _presetStore = new PresetStore(_paths);
            RefreshSavedPresets();
            RefreshRecentInstances();
            foreach (var address in NetworkAddressService.GetPrivateIpv4Addresses())
            {
                NetworkAddresses.Add(address);
            }

            var acRoot = InstallationLocator.FindAssettoCorsaRoot() ?? Preset.AssettoCorsaRoot;
            var payload = FindServerPayload() ?? string.Empty;
            if (settings?.LoadMostRecentPresetOnStartup == true && SavedPresets.FirstOrDefault() is { } recent)
            {
                Preset = _presetStore.Load(recent.Path);
                Preset.AssettoCorsaRoot = Directory.Exists(Preset.AssettoCorsaRoot) ? Preset.AssettoCorsaRoot : acRoot;
                Preset.ServerPayloadPath = File.Exists(Path.Combine(Preset.ServerPayloadPath, "AssettoServer.exe"))
                    ? Preset.ServerPayloadPath
                    : payload;
                SelectedSavedPreset = recent;
            }
            else
            {
                Preset = RaceControlPreset.CreateDefault(acRoot, payload);
                Preset.Network.BindAddress = NetworkAddressService.GetPreferredPrivateIpv4();
            }
            await RefreshContentInternalAsync();
            SelectedPageIndex = settings?.RememberLastPage == true
                ? Math.Clamp(settings.LastPageIndex, 0, 5)
                : 0;
            StatusText = $"Found {Cars.Count} cars and {Tracks.Count} track layouts.";
        }
        catch (Exception exception)
        {
            HandleException("Startup failed", exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SetAssettoCorsaRootAsync(string path)
    {
        Preset.AssettoCorsaRoot = path;
        OnPropertyChanged(nameof(Preset));
        await RefreshContentAsync();
    }

    public void SetServerPayloadPath(string path)
    {
        Preset.ServerPayloadPath = path;
        OnPropertyChanged(nameof(Preset));
        Validate();
    }

    public Task RequestStopAsync() => StopServerAsync();

    public async Task CreateNewEventAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var newPreset = RaceControlPreset.CreateDefault(Preset.AssettoCorsaRoot, Preset.ServerPayloadPath);
            newPreset.Network.BindAddress = Preset.Network.BindAddress;
            Preset = newPreset;
            await RefreshContentInternalAsync();
            SelectedPageIndex = 0;
            StatusText = "Created a new unsaved LAN race.";
        }
        catch (Exception exception)
        {
            HandleException("Could not create a new event", exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshContentAsync()
    {
        try
        {
            IsBusy = true;
            await RefreshContentInternalAsync();
            StatusText = $"Content refreshed: {Cars.Count} cars, {Tracks.Count} layouts.";
        }
        catch (Exception exception)
        {
            HandleException("Could not read Assetto Corsa content", exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshContentInternalAsync()
    {
        StatusText = "Reading installed cars and tracks…";
        _catalog = await _scanner.ScanAsync(Preset.AssettoCorsaRoot);
        Replace(Cars, _catalog.Cars);
        Replace(Tracks, _catalog.Tracks);
        Replace(Weather, _catalog.Weather);
        ApplyPresetToUi(Preset);
        Validate();
    }

    private void ApplyPresetToUi(RaceControlPreset preset)
    {
        var selectedWeather = Weather.FirstOrDefault(weather =>
                weather.Id.Equals(preset.Conditions.WeatherId, StringComparison.OrdinalIgnoreCase))
            ?? Weather.FirstOrDefault(weather => weather.Id.Equals("3_clear", StringComparison.OrdinalIgnoreCase))
            ?? Weather.FirstOrDefault();
        preset.Conditions.WeatherId = selectedWeather?.Id ?? "3_clear";
        Preset = preset;
        _selectedWeather = selectedWeather;
        OnPropertyChanged(nameof(SelectedWeather));
        SelectedTrack = Tracks.FirstOrDefault(track =>
            track.TrackId.Equals(preset.TrackId, StringComparison.OrdinalIgnoreCase)
            && track.LayoutId.Equals(preset.TrackLayoutId, StringComparison.OrdinalIgnoreCase))
            ?? Tracks.FirstOrDefault();
        if (SelectedTrack is not null)
        {
            preset.TrackId = SelectedTrack.TrackId;
            preset.TrackLayoutId = SelectedTrack.LayoutId;
        }

        Grid.Clear();
        var sourceSlots = preset.Grid.Count > 0 ? preset.Grid : RaceControlPreset.CreateDefault(preset.AssettoCorsaRoot, preset.ServerPayloadPath).Grid;
        foreach (var slot in sourceSlots)
        {
            Grid.Add(new GridSlotViewModel(slot, Cars, Grid.Count + 1));
        }
        EnsureTwoSlots();
        SelectedGridSlot = Grid.FirstOrDefault();
        OnPropertyChanged(nameof(EventTitle));
        OnPropertyChanged(nameof(EffectiveGridSummary));
    }

    private void SyncGridToPreset()
    {
        Preset.Grid = Grid.Select(slot => slot.ToPreset()).ToList();
        OnPropertyChanged(nameof(EffectiveGridSummary));
        OnPropertyChanged(nameof(EventTitle));
    }

    private void SavePreset()
    {
        try
        {
            SyncGridToPreset();
            var path = _presetStore?.Save(Preset) ?? throw new InvalidOperationException("Preset storage is not initialized.");
            RefreshSavedPresets();
            SelectedSavedPreset = SavedPresets.FirstOrDefault(summary => summary.Id == Preset.Id);
            StatusText = $"Saved {Path.GetFileName(path)}";
        }
        catch (Exception exception)
        {
            HandleException("Could not save preset", exception);
        }
    }

    private async Task LoadSelectedPresetAsync()
    {
        if (SelectedSavedPreset is null || _presetStore is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var loaded = _presetStore.Load(SelectedSavedPreset.Path);
            Preset = loaded;
            await RefreshContentInternalAsync();
            StatusText = $"Loaded {loaded.Name}.";
        }
        catch (Exception exception)
        {
            HandleException("Could not load preset", exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ImportLatestCmPresetAsync()
    {
        try
        {
            IsBusy = true;
            var cmPreset = _cmPresetService.List(Preset.AssettoCorsaRoot).FirstOrDefault()
                ?? throw new InvalidOperationException("No Content Manager server preset was found.");
            var imported = _cmPresetService.Import(cmPreset, Preset.AssettoCorsaRoot, Preset.ServerPayloadPath);
            imported.Name = $"{cmPreset.Name} (Race Control)";
            imported.Network.BindAddress = NetworkAddressService.GetPreferredPrivateIpv4();
            Preset = imported;
            await RefreshContentInternalAsync();
            StatusText = $"Imported Content Manager preset {cmPreset.Name}.";
        }
        catch (Exception exception)
        {
            HandleException("Content Manager import failed", exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ExportCmPreset()
    {
        try
        {
            SyncGridToPreset();
            var destination = _cmPresetService.ExportNew(Preset, _catalog!, _renderer);
            StatusText = $"Exported a new Content Manager preset: {Path.GetFileName(destination)}";
        }
        catch (Exception exception)
        {
            HandleException("Content Manager export failed", exception);
        }
    }

    private void AddSlot()
    {
        var source = SelectedGridSlot?.ToPreset() ?? Grid.LastOrDefault()?.ToPreset() ?? new GridSlotPreset();
        source.DriverName = $"{Preset.Bots.NamePrefix} {Grid.Count + 1:00}";
        source.Mode = SlotMode.Auto;
        var row = new GridSlotViewModel(source, Cars, Grid.Count + 1);
        Grid.Add(row);
        SelectedGridSlot = row;
        OnGridChanged();
    }

    private void RemoveSelectedSlot()
    {
        if (SelectedGridSlot is null || Grid.Count <= 2)
        {
            return;
        }

        var index = Grid.IndexOf(SelectedGridSlot);
        Grid.Remove(SelectedGridSlot);
        ReindexGrid();
        SelectedGridSlot = Grid[Math.Min(index, Grid.Count - 1)];
        OnGridChanged();
    }

    private void MoveSelectedSlot(int offset)
    {
        if (SelectedGridSlot is null)
        {
            return;
        }

        var current = Grid.IndexOf(SelectedGridSlot);
        var destination = current + offset;
        if (destination < 0 || destination >= Grid.Count)
        {
            return;
        }

        Grid.Move(current, destination);
        ReindexGrid();
        RaiseCommandStates();
    }

    private bool CanMoveSelectedSlot(int offset)
    {
        if (SelectedGridSlot is null || IsBusy)
        {
            return false;
        }

        var destination = Grid.IndexOf(SelectedGridSlot) + offset;
        return destination >= 0 && destination < Grid.Count;
    }

    private void FillGridToPitCapacity()
    {
        if (SelectedTrack is null)
        {
            return;
        }

        while (Grid.Count < Math.Min(SelectedTrack.PitBoxes, 254))
        {
            AddSlot();
        }
        while (Grid.Count > SelectedTrack.PitBoxes && Grid.Count > 2)
        {
            Grid.RemoveAt(Grid.Count - 1);
        }
        ReindexGrid();
        OnGridChanged();
    }

    private void RandomizeSkins()
    {
        foreach (var slot in Grid)
        {
            if (slot.Skins.Count > 0)
            {
                slot.SelectedSkin = slot.Skins[Random.Shared.Next(slot.Skins.Count)];
            }
        }
    }

    private void MakeAllReplaceable()
    {
        Preset.Bots.Enabled = true;
        OnPropertyChanged(nameof(Preset));
        foreach (var slot in Grid)
        {
            slot.Mode = SlotMode.Auto;
        }
        StatusText = "All slots are now occupied by bots until humans claim them.";
    }

    private void EnsureTwoSlots()
    {
        while (Grid.Count < 2 && Cars.Count > 0)
        {
            var source = Grid.LastOrDefault()?.ToPreset() ?? new GridSlotPreset { CarId = Cars[0].Id };
            source.DriverName = $"{Preset.Bots.NamePrefix} {Grid.Count + 1:00}";
            source.Mode = SlotMode.Auto;
            Grid.Add(new GridSlotViewModel(source, Cars, Grid.Count + 1));
        }
    }

    private void OnGridChanged()
    {
        ReindexGrid();
        OnPropertyChanged(nameof(EffectiveGridSummary));
        RaiseCommandStates();
    }

    private void ReindexGrid()
    {
        for (var index = 0; index < Grid.Count; index++)
        {
            Grid[index].Index = index + 1;
        }
    }

    private void Validate()
    {
        if (_catalog is null)
        {
            return;
        }

        SyncGridToPreset();
        var result = _validator.Validate(Preset, _catalog);
        Replace(ValidationMessages, result.Messages.Select(message => new ValidationMessageViewModel(message.Severity, message.Field, message.Message)));
        StatusText = result.IsValid
            ? result.WarningCount == 0 ? "Configuration is ready." : $"Ready with {result.WarningCount} warning(s)."
            : $"Fix {result.ErrorCount} configuration error(s) before launch.";
    }

    private async Task StageOnlyAsync()
    {
        try
        {
            IsBusy = true;
            _lastInstance = await StageAsync();
            SelectedPageIndex = 5;
        }
        catch (Exception exception)
        {
            HandleException("Staging failed", exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StageAndLaunchAsync()
    {
        try
        {
            IsBusy = true;
            _lastInstance = await StageAsync();
            _processController.Start(_lastInstance.ExecutablePath, _lastInstance.RootPath, _lastInstance.PresetName, _lastInstance.ShutdownFilePath);
            StatusText = $"Server is running on {Preset.Network.BindAddress}:{Preset.Network.HttpPort}.";
            SelectedPageIndex = 5;
        }
        catch (Exception exception)
        {
            HandleException("Server launch failed", exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<StagedInstance> StageAsync()
    {
        if (_catalog is null)
        {
            throw new InvalidOperationException("Assetto Corsa content has not been scanned.");
        }

        SyncGridToPreset();
        var result = _validator.Validate(Preset, _catalog);
        Replace(ValidationMessages, result.Messages.Select(message => new ValidationMessageViewModel(message.Severity, message.Field, message.Message)));
        if (!result.IsValid)
        {
            SelectedPageIndex = 5;
            throw new InvalidOperationException($"Fix {result.ErrorCount} validation error(s) first.");
        }

        SavePreset();
        ProgressValue = 0;
        ProgressText = "Starting…";
        var progress = new Progress<StagingProgress>(update =>
        {
            ProgressText = update.Message;
            ProgressValue = (update.Fraction ?? 0) * 100;
            AppendLog($"[{update.Stage}] {update.Message}");
        });
        var stager = new ServerInstanceStager(_paths, _validator, _renderer);
        var instance = await stager.StageAsync(Preset, _catalog, progress);
        RefreshRecentInstances();
        SelectedRecentInstance = RecentInstances.FirstOrDefault(summary =>
            summary.RootPath.Equals(instance.RootPath, StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(LastInstanceSummary));
        RaiseCommandStates();
        return instance;
    }

    private async Task StopServerAsync()
    {
        if (_lastInstance is null)
        {
            return;
        }

        try
        {
            await _processController.StopAsync(_lastInstance.ShutdownFilePath);
            StatusText = "Server stopped.";
        }
        catch (Exception exception)
        {
            HandleException("Could not stop server", exception);
        }
    }

    private async Task RestartServerAsync()
    {
        if (_lastInstance is null)
        {
            return;
        }

        try
        {
            await _processController.RestartAsync(
                _lastInstance.ExecutablePath,
                _lastInstance.RootPath,
                _lastInstance.PresetName,
                _lastInstance.ShutdownFilePath);
            StatusText = "Server restarted.";
        }
        catch (Exception exception)
        {
            HandleException("Could not restart server", exception);
        }
    }

    private void OpenInstance()
    {
        var path = SelectedRecentInstance?.RootPath ?? _lastInstance?.RootPath;
        if (path is not null)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
    }

    private void OpenContentManager()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AcTools Content Manager");
        var executable = new[]
        {
            Path.Combine(directory, "Content Manager.exe"),
            Path.Combine(directory, "Content Manager", "Content Manager.exe"),
        }.FirstOrDefault(File.Exists);
        if (executable is null)
        {
            StatusText = "Content Manager executable was not found in its local application folder.";
            return;
        }

        Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
    }

    private void RefreshSavedPresets()
    {
        if (_presetStore is not null)
        {
            Replace(SavedPresets, _presetStore.List());
        }
    }

    private void RefreshRecentInstances()
    {
        Replace(RecentInstances, new InstanceCatalog(_paths).List());
        SelectedRecentInstance ??= RecentInstances.FirstOrDefault();
    }

    private void AppendLog(string line)
    {
        var updated = string.IsNullOrEmpty(LogText) ? line : LogText + Environment.NewLine + line;
        LogText = updated.Length > 150_000 ? updated[^120_000..] : updated;
    }

    private void HandleException(string context, Exception exception)
    {
        StatusText = $"{context}: {exception.Message}";
        AppendLog($"ERROR: {context}: {exception}");
        SelectedPageIndex = 5;
    }

    private void RaiseCommandStates()
    {
        RefreshContentCommand.RaiseCanExecuteChanged();
        SavePresetCommand.RaiseCanExecuteChanged();
        LoadPresetCommand.RaiseCanExecuteChanged();
        ImportCmPresetCommand.RaiseCanExecuteChanged();
        ExportCmPresetCommand.RaiseCanExecuteChanged();
        AddSlotCommand.RaiseCanExecuteChanged();
        RemoveSlotCommand.RaiseCanExecuteChanged();
        MoveSlotUpCommand.RaiseCanExecuteChanged();
        MoveSlotDownCommand.RaiseCanExecuteChanged();
        FillGridCommand.RaiseCanExecuteChanged();
        RandomizeSkinsCommand.RaiseCanExecuteChanged();
        MakeAllReplaceableCommand.RaiseCanExecuteChanged();
        ValidateCommand.RaiseCanExecuteChanged();
        StageCommand.RaiseCanExecuteChanged();
        LaunchCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        RestartCommand.RaiseCanExecuteChanged();
        OpenInstanceCommand.RaiseCanExecuteChanged();
    }

    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }

    private static string? FindServerPayload()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "lib", "Server");
        if (File.Exists(Path.Combine(bundled, "AssettoServer.exe")))
        {
            return bundled;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var packaged = Path.Combine(directory.FullName, "out-race-control", "lib", "Server");
            if (File.Exists(Path.Combine(packaged, "AssettoServer.exe")))
            {
                return packaged;
            }

            var candidate = Path.Combine(directory.FullName, "out-win-x64");
            if (File.Exists(Path.Combine(candidate, "AssettoServer.exe")))
            {
                return candidate;
            }
            directory = directory.Parent;
        }

        return null;
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }

    public void Dispose() => _processController.Dispose();
}
