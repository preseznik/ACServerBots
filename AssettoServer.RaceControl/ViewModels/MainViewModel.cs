using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
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

public sealed record TimeOfDayOption(int Hour, string Label);
public sealed record GridPopulationCategoryOption(GridPopulationCategory Value, string Label);
public sealed record SlotModeOption(SlotMode Value, string Label);

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly RaceControlPaths _paths = new();
    private readonly AcContentScanner _scanner = new();
    private readonly GridPopulationService _gridPopulationService = new();
    private readonly AcContentCatalogCache _catalogCache;
    private readonly RaceControlValidator _validator = new();
    private readonly ServerConfigurationRenderer _renderer = new();
    private readonly CmPresetService _cmPresetService = new();
    private readonly ServerProcessController _processController = new();
    private readonly CancellationTokenSource _liveMonitorCancellation = new();
    private readonly FpsArenaStore _fpsArenaStore;
    private PresetStore? _presetStore;
    private SavedGridStore? _savedGridStore;
    private RaceControlPreset _preset = new();
    private AcContentCatalog? _catalog;
    private readonly List<AcTrackLayout> _allTracks = [];
    private RaceControlPreset? _racingDraft;
    private RaceControlPreset? _fpsDraft;
    private string? _catalogRoot;
    private CancellationTokenSource? _contentRefreshCancellation;
    private Task? _contentRefreshTask;
    private int _contentRefreshGeneration;
    private AcTrackLayout? _selectedTrack;
    private bool _showPreparedFpsArenasOnly;
    private AcWeather? _selectedWeather;
    private GridSlotViewModel? _selectedGridSlot;
    private PresetSummary? _selectedSavedPreset;
    private SavedGridSummary? _selectedSavedGrid;
    private string _savedGridName = string.Empty;
    private GridPopulationCategoryOption _selectedGridPopulationCategory;
    private string _gridPopulationClass = string.Empty;
    private int _gridPopulationCount = 8;
    private double _gridPopulationMaximumHorsepower = 500;
    private int _gridPopulationYear = 2000;
    private double _gridPopulationMaximumPowerToWeight = 350;
    private InstanceSummary? _selectedRecentInstance;
    private StagedInstance? _lastInstance;
    private bool _isBusy;
    private int _selectedPageIndex;
    private string _statusText = "Ready";
    private string _progressText = string.Empty;
    private double _progressValue;
    private string _logText = string.Empty;
    private LiveRaceSnapshot? _liveSnapshot;
    private LiveTrackMap? _liveTrack;
    private LiveRaceCar? _selectedLiveCar;
    private string _liveControlStatus = "Start a server or simulation to open live telemetry.";
    private bool _fullTrackView = true;
    private double _liveZoomMeters = 180;
    private int _simulationSeed = 1;
    private string _simulationLimitMode = "Minutes";
    private int _simulationLimitValue = 45;
    private double _simulationTimeScale = 10;
    private CancellationTokenSource? _simulationTimeScaleUpdateCancellation;
    private SimulationRaceSummary? _simulationResults;
    private bool _showSimulationResults;
    private Guid? _pendingLiveCommandId;
    private int? _takeoverSessionId;
    private int _manualInputWriteInProgress;
    private int _liveTrackGeneration;
    private readonly Task _liveMonitorTask;

    public MainViewModel()
    {
        _selectedGridPopulationCategory = GridPopulationCategories[0];
        _catalogCache = new AcContentCatalogCache(_paths.CacheDirectory);
        _fpsArenaStore = new FpsArenaStore(_paths);
        RefreshContentCommand = new AsyncRelayCommand(RefreshContentAsync, () => !IsBusy);
        SavePresetCommand = new RelayCommand(SavePreset, () => !IsBusy);
        LoadPresetCommand = new AsyncRelayCommand(LoadSelectedPresetAsync, () => SelectedSavedPreset is not null && !IsBusy);
        ImportCmPresetCommand = new AsyncRelayCommand(ImportLatestCmPresetAsync, () => !IsFpsMode && !IsBusy);
        ExportCmPresetCommand = new RelayCommand(ExportCmPreset, () => !IsFpsMode && _catalog is not null && !IsBusy);
        AddSlotCommand = new RelayCommand(AddSlot, () => Cars.Count > 0 && Grid.Count < (IsFpsMode ? 32 : 254) && !IsBusy);
        RemoveSlotCommand = new RelayCommand(RemoveSelectedSlot, CanRemoveSelectedSlot);
        MoveSlotUpCommand = new RelayCommand(() => MoveSelectedSlot(-1), () => CanMoveSelectedSlot(-1));
        MoveSlotDownCommand = new RelayCommand(() => MoveSelectedSlot(1), () => CanMoveSelectedSlot(1));
        FillGridCommand = new RelayCommand(FillGridToPitCapacity, () => SelectedTrack is { PitBoxes: > 1 } && Cars.Count > 0 && !IsBusy);
        RandomizeSkinsCommand = new RelayCommand(RandomizeSkins, () => Grid.Count > 0 && !IsBusy);
        MakeAllReplaceableCommand = new RelayCommand(MakeAllReplaceable, () => Grid.Count > 0 && !IsBusy);
        PopulateGridCommand = new RelayCommand(PopulateGrid, CanPopulateGrid);
        SaveGridCommand = new RelayCommand(SaveGrid, () => !IsBusy && Grid.Count > 0
            && !string.IsNullOrWhiteSpace(SavedGridName));
        LoadGridCommand = new RelayCommand(LoadSavedGrid,
            () => !IsBusy && SelectedSavedGrid is not null && Cars.Count > 0);
        DeleteGridCommand = new RelayCommand(DeleteSavedGrid,
            () => !IsBusy && SelectedSavedGrid is not null);
        ValidateCommand = new RelayCommand(Validate, () => _catalog is not null && !IsBusy);
        StageCommand = new AsyncRelayCommand(StageOnlyAsync,
            () => _catalog is not null && !IsBusy && _processController.State == ServerProcessState.Stopped);
        LaunchCommand = new AsyncRelayCommand(StageAndLaunchAsync, () => _catalog is not null && !IsBusy && _processController.State == ServerProcessState.Stopped);
        StopCommand = new AsyncRelayCommand(StopServerAsync, () => _processController.State != ServerProcessState.Stopped);
        RestartCommand = new AsyncRelayCommand(RestartServerAsync, () => _lastInstance is not null && _processController.State == ServerProcessState.Running);
        SimulateRaceCommand = new AsyncRelayCommand(SimulateRaceAsync,
            () => !IsFpsMode && _catalog is not null && !IsBusy && _processController.State == ServerProcessState.Stopped);
        PrepareFpsArenaCommand = new AsyncRelayCommand(PrepareFpsArenaAsync,
            () => IsFpsMode && SelectedTrack is not null && !IsBusy);
        ExportFpsClientPackCommand = new AsyncRelayCommand(ExportFpsClientPackAsync,
            () => IsFpsMode && !IsBusy);
        StartRaceCommand = new AsyncRelayCommand(() => SendLiveRaceCommandAsync(LiveRaceCommand.Start), CanControlLiveRace);
        StopRaceCommand = new AsyncRelayCommand(() => SendLiveRaceCommandAsync(LiveRaceCommand.Stop), CanControlLiveRace);
        RestartRaceCommand = new AsyncRelayCommand(() => SendLiveRaceCommandAsync(LiveRaceCommand.Restart), CanControlLiveRace);
        StopGoSelectedBotCommand = new AsyncRelayCommand(StopGoSelectedBotAsync, CanControlSelectedBot);
        TeleportSelectedBotCommand = new AsyncRelayCommand(TeleportSelectedBotAsync, CanControlSelectedBot);
        TakeOverSelectedBotCommand = new AsyncRelayCommand(ToggleSelectedBotTakeoverAsync, CanToggleTakeover);
        DismissSimulationResultsCommand = new RelayCommand(() => ShowSimulationResults = false,
            () => SimulationResults is not null && ShowSimulationResults);
        ExportServerPackageCommand = new AsyncRelayCommand(ExportServerPackageAsync,
            () => !IsBusy && _processController.State == ServerProcessState.Stopped
                  && File.Exists(Path.Combine(_paths.WorkingInstanceDirectory,
                      "race-control-instance.json")));
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
        _liveMonitorTask = MonitorLiveRaceAsync(_liveMonitorCancellation.Token);
    }

    public RaceControlPreset Preset
    {
        get => _preset;
        private set
        {
            if (SetProperty(ref _preset, value))
            {
                OnPropertyChanged(nameof(EventTitle));
                NotifyModeProperties();
            }
        }
    }

    public ObservableCollection<AcCar> Cars { get; } = [];
    public ObservableCollection<AcTrackLayout> Tracks { get; } = [];
    public ObservableCollection<AcWeather> Weather { get; } = [];
    public ObservableCollection<GridSlotViewModel> Grid { get; } = [];
    public ObservableCollection<ValidationMessageViewModel> ValidationMessages { get; } = [];
    public ObservableCollection<PresetSummary> SavedPresets { get; } = [];
    public ObservableCollection<SavedGridSummary> SavedGrids { get; } = [];
    public ObservableCollection<string> CarClassOptions { get; } = [];
    public ObservableCollection<InstanceSummary> RecentInstances { get; } = [];
    public ObservableCollection<string> NetworkAddresses { get; } = [];
    public ObservableCollection<LiveRaceCar> LiveCars { get; } = [];
    public IReadOnlyList<EventMode> EventModes { get; } = Enum.GetValues<EventMode>();
    public IReadOnlyList<SlotModeOption> SlotModeOptions => IsFpsMode
        ?
        [
            new(SlotMode.Auto, "Auto"),
            new(SlotMode.Fixed, "Bot"),
            new(SlotMode.None, "Human"),
            new(SlotMode.Spectator, "Spectator"),
        ]
        :
        [
            new(SlotMode.Auto, "Auto"),
            new(SlotMode.Fixed, "Fixed"),
            new(SlotMode.None, "None"),
            new(SlotMode.Spectator, "Spectator"),
        ];
    public IReadOnlyList<PhysicsFidelity> PhysicsFidelities { get; } = Enum.GetValues<PhysicsFidelity>();
    public IReadOnlyList<PlayerJoinSlotSelection> PlayerJoinSlotSelections { get; } =
        Enum.GetValues<PlayerJoinSlotSelection>();
    public IReadOnlyList<TimeOfDayOption> TimeOfDayOptions { get; } = Enumerable.Range(0, 24)
        .Select(hour => new TimeOfDayOption(hour, $"{hour:00}:00"))
        .ToArray();
    public IReadOnlyList<GridPopulationCategoryOption> GridPopulationCategories { get; } =
    [
        new(GridPopulationCategory.Any, "Any bot-capable car"),
        new(GridPopulationCategory.Class, "Car class"),
        new(GridPopulationCategory.MaximumHorsepower, "Maximum horsepower"),
        new(GridPopulationCategory.ModelYear, "Model year"),
        new(GridPopulationCategory.MaximumPowerToWeight, "Maximum power-to-weight"),
    ];

    public AcTrackLayout? SelectedTrack
    {
        get => _selectedTrack;
        set
        {
            if (SetProperty(ref _selectedTrack, value) && value is not null)
            {
                Preset.TrackId = value.TrackId;
                Preset.TrackLayoutId = value.LayoutId;
                if (IsFpsMode)
                    Preset.Fps.Arena = _fpsArenaStore.Load(value.TrackId, value.LayoutId);
                OnPropertyChanged(nameof(SelectedTrackDetails));
                OnPropertyChanged(nameof(FpsArenaStatus));
                RaiseCommandStates();
            }
        }
    }

    public EventMode EventMode
    {
        get => Preset.Mode;
        set => SwitchEventMode(value);
    }

    public bool IsFpsMode => Preset.Mode == EventMode.Fps;
    public string ContentSectionTitle => IsFpsMode ? "Map" : "Circuit";
    public string TrackSelectionLabel => IsFpsMode ? "Arena and layout" : "Track and layout";
    public string GridTabTitle => IsFpsMode ? "LOBBY SETUP" : "GRID";
    public string GridSectionTitle => IsFpsMode ? "FPS participants" : "Race grid";
    public string FillGridLabel => IsFpsMode ? "Fill spots" : "Fill pit boxes";
    public string FillGridToolTip => IsFpsMode
        ? "Add or remove entries until the lobby matches the prepared arena's participant capacity."
        : "Add or remove entries until the grid matches the selected layout's pit-box capacity.";
    public string SessionSectionTitle => IsFpsMode ? "DEATHMATCH" : "SESSIONS & RULES";
    public string BotSectionTitle => IsFpsMode ? "FPS BOTS" : "RACE BOTS";
    public string LiveTabTitle => IsFpsMode ? "LIVE MATCH" : "LIVE RACE";
    public string LiveSessionTitle => IsFpsMode ? "Current match" : "Race session";
    public string StartRaceLabel => IsFpsMode ? "START MATCH" : "START RACE";
    public string StopRaceLabel => IsFpsMode ? "Stop match" : "Stop race";
    public string RestartRaceLabel => IsFpsMode ? "Restart match" : "Restart race";
    public string StopServerToolTip => IsFpsMode
        ? "Gracefully stop the whole AssettoServer process. This is separate from stopping the current match."
        : "Gracefully stop the whole AssettoServer process. This is separate from stopping the current race session.";
    public string StartRaceToolTip => IsFpsMode
        ? "Immediately start the configured deathmatch, even with no human players connected."
        : "Immediately select the configured race session and begin its normal grid countdown, even with no human players connected.";
    public string StopRaceToolTip => IsFpsMode
        ? "End the active match, record its current results, hold bots, and leave the server online."
        : "End the active race, classify unfinished participants as DNF, hold bots, and leave the server online.";
    public string RestartRaceToolTip => IsFpsMode
        ? "Reset the configured deathmatch and start it again."
        : "Reset the configured race session and start its grid countdown again, regardless of connected-player count.";
    public string SlotModeHelp => IsFpsMode
        ? "Auto = bot until claimed • Bot = unclaimable server bot • Human = player-only • Spectator = camera-only"
        : "Auto = bot until claimed • Fixed = bot only • None = human racer • Spectator = camera-only connection";

    public bool ShowPreparedFpsArenasOnly
    {
        get => _showPreparedFpsArenasOnly;
        set
        {
            if (!SetProperty(ref _showPreparedFpsArenasOnly, value)) return;
            RefreshTrackFilter();
        }
    }

    public string FpsArenaStatus => !IsFpsMode
        ? string.Empty
        : Preset.Fps.Arena is { PreparationVersion: FpsArenaDefinition.CurrentPreparationVersion } arena
            ? $"Prepared FPS arena • {arena.SpawnPoints.Count} safe spawns • sidecar v{arena.PreparationVersion}"
            : "Not prepared for FPS. Prepare this arena before validation or launch.";

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

    public SavedGridSummary? SelectedSavedGrid
    {
        get => _selectedSavedGrid;
        set
        {
            if (SetProperty(ref _selectedSavedGrid, value))
            {
                if (value is not null)
                    SavedGridName = value.Name;
                RaiseCommandStates();
            }
        }
    }

    public string SavedGridName
    {
        get => _savedGridName;
        set
        {
            if (SetProperty(ref _savedGridName, value))
                SaveGridCommand.RaiseCanExecuteChanged();
        }
    }

    public GridPopulationCategoryOption SelectedGridPopulationCategory
    {
        get => _selectedGridPopulationCategory;
        set
        {
            if (value is null || !SetProperty(ref _selectedGridPopulationCategory, value))
                return;
            OnPropertyChanged(nameof(IsGridPopulationClassEnabled));
            OnPropertyChanged(nameof(IsGridPopulationCriterionEnabled));
            OnPropertyChanged(nameof(GridPopulationCriterionLabel));
            OnPropertyChanged(nameof(GridPopulationCriterionValue));
            PopulateGridCommand.RaiseCanExecuteChanged();
        }
    }

    public string GridPopulationClass
    {
        get => _gridPopulationClass;
        set
        {
            if (SetProperty(ref _gridPopulationClass, value))
                PopulateGridCommand.RaiseCanExecuteChanged();
        }
    }

    public int GridPopulationCount
    {
        get => _gridPopulationCount;
        set
        {
            if (SetProperty(ref _gridPopulationCount, value))
                PopulateGridCommand.RaiseCanExecuteChanged();
        }
    }

    public double GridPopulationCriterionValue
    {
        get => SelectedGridPopulationCategory.Value switch
        {
            GridPopulationCategory.MaximumHorsepower => _gridPopulationMaximumHorsepower,
            GridPopulationCategory.ModelYear => _gridPopulationYear,
            GridPopulationCategory.MaximumPowerToWeight => _gridPopulationMaximumPowerToWeight,
            _ => 0,
        };
        set
        {
            switch (SelectedGridPopulationCategory.Value)
            {
                case GridPopulationCategory.MaximumHorsepower:
                    if (!SetProperty(ref _gridPopulationMaximumHorsepower, value,
                            nameof(GridPopulationCriterionValue))) return;
                    break;
                case GridPopulationCategory.ModelYear:
                    int year = (int)Math.Round(value, MidpointRounding.AwayFromZero);
                    if (!SetProperty(ref _gridPopulationYear, year,
                            nameof(GridPopulationCriterionValue))) return;
                    break;
                case GridPopulationCategory.MaximumPowerToWeight:
                    if (!SetProperty(ref _gridPopulationMaximumPowerToWeight, value,
                            nameof(GridPopulationCriterionValue))) return;
                    break;
                default:
                    return;
            }
            PopulateGridCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsGridPopulationClassEnabled =>
        SelectedGridPopulationCategory.Value == GridPopulationCategory.Class;
    public bool IsGridPopulationCriterionEnabled =>
        SelectedGridPopulationCategory.Value is GridPopulationCategory.MaximumHorsepower
            or GridPopulationCategory.ModelYear
            or GridPopulationCategory.MaximumPowerToWeight;
    public string GridPopulationCriterionLabel => SelectedGridPopulationCategory.Value switch
    {
        GridPopulationCategory.MaximumHorsepower => "Limit (hp)",
        GridPopulationCategory.ModelYear => "Year",
        GridPopulationCategory.MaximumPowerToWeight => "Limit (hp/tonne)",
        _ => "Value",
    };

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

    public LiveRaceSnapshot? LiveSnapshot
    {
        get => _liveSnapshot;
        private set
        {
            if (!SetProperty(ref _liveSnapshot, value))
                return;
            OnPropertyChanged(nameof(LiveSessionSummary));
            OnPropertyChanged(nameof(LiveTimingSummary));
            OnPropertyChanged(nameof(IsSimulationProgressVisible));
            OnPropertyChanged(nameof(SimulationProgressValue));
            OnPropertyChanged(nameof(SimulationProgressText));
        }
    }

    public LiveTrackMap? LiveTrack
    {
        get => _liveTrack;
        private set => SetProperty(ref _liveTrack, value);
    }

    public LiveRaceCar? SelectedLiveCar
    {
        get => _selectedLiveCar;
        set
        {
            if (_takeoverSessionId.HasValue && value?.SessionId != _takeoverSessionId.Value)
                return;
            if (SetProperty(ref _selectedLiveCar, value))
            {
                OnPropertyChanged(nameof(SelectedLiveCarSessionId));
                NotifySelectedBotControlChanged();
            }
        }
    }

    public int SelectedLiveCarSessionId
    {
        get => SelectedLiveCar?.SessionId ?? -1;
        set
        {
            var car = LiveCars.FirstOrDefault(candidate => candidate.SessionId == value);
            if (car != null)
                SelectedLiveCar = car;
        }
    }

    public bool FullTrackView
    {
        get => _fullTrackView;
        set => SetProperty(ref _fullTrackView, value);
    }

    public double LiveZoomMeters
    {
        get => _liveZoomMeters;
        set => SetProperty(ref _liveZoomMeters, value);
    }

    public int SimulationSeed
    {
        get => _simulationSeed;
        set => SetProperty(ref _simulationSeed, Math.Max(1, value));
    }

    public IReadOnlyList<string> SimulationLimitModes { get; } = ["Minutes", "Laps"];

    public string SimulationLimitMode
    {
        get => _simulationLimitMode;
        set
        {
            string normalized = string.Equals(value, "Laps", StringComparison.OrdinalIgnoreCase)
                ? "Laps"
                : "Minutes";
            if (!SetProperty(ref _simulationLimitMode, normalized))
                return;
            SimulationLimitValue = _simulationLimitValue;
            OnPropertyChanged(nameof(SimulationLimitValueLabel));
        }
    }

    public int SimulationLimitValue
    {
        get => _simulationLimitValue;
        set => SetProperty(ref _simulationLimitValue,
            Math.Clamp(value, 1, SimulationLimitMode == "Laps" ? 999 : 1440));
    }

    public string SimulationLimitValueLabel => SimulationLimitMode == "Laps" ? "Laps" : "Minutes";

    public double SimulationTimeScale
    {
        get => _simulationTimeScale;
        set
        {
            if (SetProperty(ref _simulationTimeScale, Math.Clamp(value, 1, 100)))
                ScheduleSimulationTimeScaleUpdate();
        }
    }

    public string LiveControlStatus
    {
        get => _liveControlStatus;
        private set => SetProperty(ref _liveControlStatus, value);
    }

    public SimulationRaceSummary? SimulationResults
    {
        get => _simulationResults;
        private set
        {
            if (!SetProperty(ref _simulationResults, value))
                return;
            ShowSimulationResults = value is not null;
            OnPropertyChanged(nameof(SimulationResultsTitle));
            OnPropertyChanged(nameof(SimulationResultsOverview));
            DismissSimulationResultsCommand.RaiseCanExecuteChanged();
        }
    }

    public bool ShowSimulationResults
    {
        get => _showSimulationResults;
        private set
        {
            if (!SetProperty(ref _showSimulationResults, value))
                return;
            OnPropertyChanged(nameof(IsSimulationResultsVisible));
            DismissSimulationResultsCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsSimulationResultsVisible => SimulationResults is not null && ShowSimulationResults;
    public string SimulationResultsTitle => SimulationResults?.Outcome ?? "SIMULATION RESULTS";
    public string SimulationResultsOverview => SimulationResults?.Overview ?? string.Empty;
    public bool IsSelectedBotControllable => IsServerRunning && SelectedLiveCar is { IsBot: true, IsActive: true };
    public bool SelectedBotIsStopped => SelectedLiveCar?.IsStoppedByRaceControl == true;
    public string StopGoSelectedBotText => SelectedBotIsStopped ? "GO" : "STOP";
    public bool IsBotTakeoverActive => _takeoverSessionId.HasValue;
    public string TakeOverSelectedBotText => IsBotTakeoverActive ? "RELEASE CONTROL" : "TAKE OVER";
    public string BotControlStatus => IsBotTakeoverActive
        ? "Arrow keys or Xbox controller: steer, throttle and brake"
        : SelectedLiveCar?.ControlMode switch
        {
            "stopped" => "Stopped by Race Control",
            "manual" => "Manual control active",
            _ => "AI control active",
        };

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
        ServerProcessState.Running => $"{(_processController.IsSimulation ? "SIMULATING" : "RUNNING")}  •  PID {_processController.ProcessId}",
        ServerProcessState.Stopping => "STOPPING",
        _ => "OFFLINE",
    };
    public bool IsServerRunning => _processController.State == ServerProcessState.Running;
    public string LiveSessionSummary => LiveSnapshot is null
        ? "No live server telemetry"
        : $"{LiveSnapshot.Session.Name}  •  {LiveSnapshot.Session.Phase.ToUpperInvariant()}  •  "
          + $"{LiveSnapshot.Cars.Count(car => car.IsActive)} active cars";
    public string LiveTimingSummary => LiveSnapshot is null
        ? "Waiting for a staged server instance"
        : LiveSnapshot.Session.Phase == "countdown"
            ? $"Race starts in {TimeSpan.FromMilliseconds(LiveSnapshot.Session.CountdownMilliseconds):mm\\:ss}"
            : LiveSnapshot.IsSimulation
                ? $"Accelerated simulation  •  {LiveSnapshot.RealTimeFactor:F1}× achieved"
                  + $" / {LiveSnapshot.TargetRealTimeFactor:F0}× target"
                : $"Server time {TimeSpan.FromMilliseconds(LiveSnapshot.SimulatedMilliseconds):hh\\:mm\\:ss}";
    public bool IsSimulationProgressVisible => LiveSnapshot?.IsSimulation == true;
    public double SimulationProgressValue => LiveSnapshot?.SimulationProgressPercent ?? 0;
    public string SimulationProgressText
    {
        get
        {
            if (LiveSnapshot is not { IsSimulation: true } snapshot)
                return string.Empty;
            string progress = snapshot.MaximumSimulatedLaps > 0
                ? $"{snapshot.LeadingLapProgress:F1} / {snapshot.MaximumSimulatedLaps} leader laps"
                : $"{FormatCompactDuration(snapshot.SimulatedMilliseconds)} / "
                  + $"{FormatCompactDuration(snapshot.MaximumSimulatedMilliseconds)} virtual";
            double factor = snapshot.TargetRealTimeFactor > 0
                ? snapshot.TargetRealTimeFactor
                : Math.Max(1, snapshot.RealTimeFactor);
            long remainingWallMilliseconds = (long)(snapshot.EstimatedRemainingSimulatedMilliseconds / factor);
            string estimate = remainingWallMilliseconds > 0
                ? $"about {FormatCompactDuration(remainingWallMilliseconds)} wall time left"
                : "estimating remaining wall time";
            return $"{progress}  •  {snapshot.SimulationProgressPercent:F0}%  •  {estimate}";
        }
    }
    public string SelectedTrackDetails => SelectedTrack is null
        ? (IsFpsMode ? "No arena selected" : "No track selected")
        : IsFpsMode
            ? $"{SelectedTrack.Country}  •  {SelectedTrack.PitBoxes} carrier slots  •  {FpsArenaStatus}"
            : $"{SelectedTrack.Country}  •  {SelectedTrack.PitBoxes} pit boxes  •  {(SelectedTrack.HasFastLane ? "AI line ready" : "no AI line")}";
    public string EffectiveGridSummary
    {
        get
        {
            int active = Grid.Count(slot => slot.Mode != SlotMode.Spectator);
            int spectators = Grid.Count - active;
            if (IsFpsMode)
                return $"{active} participants • {Grid.Count(slot => slot.Mode is SlotMode.Auto or SlotMode.Fixed)} bot-capable • {Grid.Count(slot => slot.Mode == SlotMode.None)} human-only • {spectators} spectators";
            return SelectedTrack is null
                ? $"{active} racers + {spectators} spectators"
                : $"{Math.Min(active, SelectedTrack.PitBoxes)} racing / {active} requested  •  {spectators} spectators  •  {SelectedTrack.PitBoxes} pit boxes";
        }
    }
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
    public RelayCommand PopulateGridCommand { get; }
    public RelayCommand SaveGridCommand { get; }
    public RelayCommand LoadGridCommand { get; }
    public RelayCommand DeleteGridCommand { get; }
    public RelayCommand ValidateCommand { get; }
    public AsyncRelayCommand StageCommand { get; }
    public AsyncRelayCommand LaunchCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public AsyncRelayCommand RestartCommand { get; }
    public AsyncRelayCommand SimulateRaceCommand { get; }
    public AsyncRelayCommand PrepareFpsArenaCommand { get; }
    public AsyncRelayCommand ExportFpsClientPackCommand { get; }
    public AsyncRelayCommand StartRaceCommand { get; }
    public AsyncRelayCommand StopRaceCommand { get; }
    public AsyncRelayCommand RestartRaceCommand { get; }
    public AsyncRelayCommand StopGoSelectedBotCommand { get; }
    public AsyncRelayCommand TeleportSelectedBotCommand { get; }
    public AsyncRelayCommand TakeOverSelectedBotCommand { get; }
    public RelayCommand DismissSimulationResultsCommand { get; }
    public AsyncRelayCommand ExportServerPackageCommand { get; }
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
            _savedGridStore = new SavedGridStore(_paths);
            RefreshSavedPresets();
            RefreshSavedGrids();
            RefreshRecentInstances();
            foreach (var address in NetworkAddressService.GetPrivateIpv4Addresses())
            {
                NetworkAddresses.Add(address);
            }

            string detectedAcRoot = InstallationLocator.FindAssettoCorsaRoot() ?? Preset.AssettoCorsaRoot;
            string detectedPayload = FindServerPayload() ?? string.Empty;
            string? configuredAcRoot = ExistingDirectory(settings?.AssettoCorsaRoot);
            string? configuredPayload = ExistingServerPayload(settings?.ServerPayloadPath);
            if (settings?.LoadMostRecentPresetOnStartup == true && _presetStore.List().FirstOrDefault() is { } recent)
            {
                Preset = _presetStore.Load(recent.Path);
                Preset.AssettoCorsaRoot = configuredAcRoot
                    ?? ExistingDirectory(Preset.AssettoCorsaRoot)
                    ?? detectedAcRoot;
                Preset.ServerPayloadPath = configuredPayload
                    ?? ExistingServerPayload(Preset.ServerPayloadPath)
                    ?? detectedPayload;
                SelectedSavedPreset = recent;
            }
            else
            {
                Preset = RaceControlPreset.CreateDefault(configuredAcRoot ?? detectedAcRoot,
                    configuredPayload ?? detectedPayload);
                Preset.Network.BindAddress = NetworkAddressService.GetPreferredPrivateIpv4();
            }
            RememberModeDraft(Preset);
            RefreshSavedPresets();
            if (TryApplyCachedContent(preserveUi: false))
            {
                StatusText = $"Loaded {Cars.Count} cars and {Tracks.Count} track layouts from cache. Checking for changes…";
                StartBackgroundContentRefresh(Preset.AssettoCorsaRoot);
            }
            else
            {
                await RefreshContentInternalAsync(preserveUi: false);
                StatusText = $"Found {Cars.Count} cars and {Tracks.Count} track layouts.";
            }
            SelectedPageIndex = settings?.RememberLastPage == true
                ? Math.Clamp(settings.LastPageIndex, 0, 6)
                : 0;
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
        CancelBackgroundContentRefresh();
        Preset.AssettoCorsaRoot = path;
        OnPropertyChanged(nameof(Preset));
        if (TryApplyCachedContent(preserveUi: true))
        {
            StatusText = $"Loaded {Cars.Count} cars and {Tracks.Count} track layouts from cache. Checking for changes…";
            StartBackgroundContentRefresh(path);
        }
        else
        {
            await RefreshContentAsync();
        }
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
            var newPreset = CreateModePreset(Preset.Mode, Preset);
            newPreset.Network.BindAddress = Preset.Network.BindAddress;
            Preset = newPreset;
            RememberModeDraft(newPreset);
            Replace(Tracks, FilteredTracks());
            await ApplyCurrentCatalogOrRefreshAsync();
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
            CancelBackgroundContentRefresh();
            await RefreshContentInternalAsync(preserveUi: true);
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

    private async Task RefreshContentInternalAsync(bool preserveUi = false, CancellationToken cancellationToken = default)
    {
        StatusText = "Reading installed cars and tracks…";
        var root = Preset.AssettoCorsaRoot;
        var catalog = await _scanner.ScanAsync(root, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ApplyCatalog(root, catalog, preserveUi);
        SaveCatalog(root, catalog);
    }

    private async Task ApplyCurrentCatalogOrRefreshAsync()
    {
        if (_catalog is not null && PathsEqual(_catalogRoot, Preset.AssettoCorsaRoot))
        {
            ApplyPresetToUi(Preset);
            Validate();
            return;
        }

        CancelBackgroundContentRefresh();
        if (TryApplyCachedContent(preserveUi: false))
        {
            StartBackgroundContentRefresh(Preset.AssettoCorsaRoot);
            return;
        }

        await RefreshContentInternalAsync(preserveUi: false);
    }

    private bool TryApplyCachedContent(bool preserveUi)
    {
        var catalog = _catalogCache.TryLoad(Preset.AssettoCorsaRoot);
        if (catalog is null)
        {
            return false;
        }

        ApplyCatalog(Preset.AssettoCorsaRoot, catalog, preserveUi);
        return true;
    }

    private void ApplyCatalog(string assettoCorsaRoot, AcContentCatalog catalog, bool preserveUi)
    {
        if (preserveUi && _catalog is not null)
        {
            SyncGridToPreset();
        }

        _catalog = catalog;
        _catalogRoot = NormalizePath(assettoCorsaRoot);
        Replace(Cars, catalog.Cars);
        RefreshCarClassOptions();
        _allTracks.Clear();
        _allTracks.AddRange(catalog.Tracks);
        Replace(Tracks, FilteredTracks());
        Replace(Weather, catalog.Weather);
        ApplyPresetToUi(Preset);
        Validate();
        RaiseCommandStates();
    }

    private void StartBackgroundContentRefresh(string assettoCorsaRoot)
    {
        CancelBackgroundContentRefresh();
        var cancellation = new CancellationTokenSource();
        _contentRefreshCancellation = cancellation;
        var generation = ++_contentRefreshGeneration;
        _contentRefreshTask = RefreshContentInBackgroundAsync(
            NormalizePath(assettoCorsaRoot), generation, cancellation);
    }

    private async Task RefreshContentInBackgroundAsync(
        string assettoCorsaRoot,
        int generation,
        CancellationTokenSource cancellation)
    {
        try
        {
            var catalog = await _scanner.ScanAsync(assettoCorsaRoot, cancellation.Token);
            if (cancellation.IsCancellationRequested
                || generation != _contentRefreshGeneration
                || !PathsEqual(assettoCorsaRoot, Preset.AssettoCorsaRoot))
            {
                return;
            }

            ApplyCatalog(assettoCorsaRoot, catalog, preserveUi: true);
            SaveCatalog(assettoCorsaRoot, catalog);
            StatusText = $"Content scan complete: {Cars.Count} cars, {Tracks.Count} track layouts.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A manual refresh, root change, or application shutdown superseded this scan.
        }
        catch (Exception exception)
        {
            if (cancellation.IsCancellationRequested
                || generation != _contentRefreshGeneration
                || !PathsEqual(assettoCorsaRoot, Preset.AssettoCorsaRoot))
            {
                return;
            }

            StatusText = $"Using cached content; background scan failed: {exception.Message}";
            AppendLog($"WARNING: Background content scan failed: {exception}");
        }
        finally
        {
            if (ReferenceEquals(_contentRefreshCancellation, cancellation))
            {
                _contentRefreshCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private void CancelBackgroundContentRefresh()
    {
        _contentRefreshGeneration++;
        var cancellation = _contentRefreshCancellation;
        _contentRefreshCancellation = null;
        cancellation?.Cancel();
    }

    private void SaveCatalog(string assettoCorsaRoot, AcContentCatalog catalog)
    {
        try
        {
            _catalogCache.Save(assettoCorsaRoot, catalog);
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or System.Text.Json.JsonException
                                          or NotSupportedException)
        {
            AppendLog($"WARNING: Could not update the installed-content cache: {exception.Message}");
        }
    }

    private static bool PathsEqual(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left)
               && !string.IsNullOrWhiteSpace(right)
               && string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string? ExistingDirectory(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) ? path : null;

    private static string? ExistingServerPayload(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && File.Exists(Path.Combine(path, "AssettoServer.exe"))
            ? path
            : null;

    private void ApplyPresetToUi(RaceControlPreset preset)
    {
        var selectedWeather = Weather.FirstOrDefault(weather =>
                weather.Id.Equals(preset.Conditions.WeatherId, StringComparison.OrdinalIgnoreCase))
            ?? Weather.FirstOrDefault(weather => weather.Id.Equals("3_clear", StringComparison.OrdinalIgnoreCase))
            ?? Weather.FirstOrDefault();
        preset.Conditions.WeatherId = selectedWeather?.Id ?? "3_clear";
        Preset = preset;
        if (preset.Mode == EventMode.Fps)
            preset.Fps.Arena = _fpsArenaStore.Load(preset.TrackId, preset.TrackLayoutId);
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
            Grid.Add(CreateGridSlotViewModel(slot, Grid.Count + 1));
        }
        EnsureTwoSlots();
        SelectedGridSlot = Grid.FirstOrDefault();
        OnPropertyChanged(nameof(EventTitle));
        OnPropertyChanged(nameof(EffectiveGridSummary));
        OnPropertyChanged(nameof(SelectedTrackDetails));
        OnPropertyChanged(nameof(FpsArenaStatus));
        NotifyModeProperties();
    }

    private IEnumerable<AcTrackLayout> FilteredTracks()
    {
        IEnumerable<AcTrackLayout> tracks = _allTracks;
        if (IsFpsMode && ShowPreparedFpsArenasOnly)
            tracks = tracks.Where(track => _fpsArenaStore.IsPrepared(track.TrackId, track.LayoutId));
        return tracks;
    }

    private void RefreshTrackFilter()
    {
        string trackId = Preset.TrackId;
        string layoutId = Preset.TrackLayoutId;
        Replace(Tracks, FilteredTracks());
        SelectedTrack = Tracks.FirstOrDefault(track =>
                            track.TrackId.Equals(trackId, StringComparison.OrdinalIgnoreCase)
                            && track.LayoutId.Equals(layoutId, StringComparison.OrdinalIgnoreCase))
                        ?? Tracks.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedTrackDetails));
        OnPropertyChanged(nameof(FpsArenaStatus));
        RaiseCommandStates();
    }

    private void SwitchEventMode(EventMode mode)
    {
        if (Preset.Mode == mode) return;

        InvalidateLiveTrackCache();
        SyncGridToPreset();
        RememberModeDraft(Preset);
        var target = mode == EventMode.Fps ? _fpsDraft : _racingDraft;
        target ??= CreateModePreset(mode, Preset);
        target.Mode = mode;
        if (mode == EventMode.Fps)
            _fpsDraft = target;
        else
            _racingDraft = target;

        Preset = target;
        Replace(Tracks, FilteredTracks());
        ApplyPresetToUi(target);
        RefreshSavedPresets();
        Validate();
        RaiseCommandStates();
    }

    private void RememberModeDraft(RaceControlPreset preset)
    {
        if (preset.Mode == EventMode.Fps)
            _fpsDraft = preset;
        else
            _racingDraft = preset;
    }

    private static RaceControlPreset CreateModePreset(EventMode mode, RaceControlPreset source)
    {
        var preset = RaceControlPreset.CreateDefault(source.AssettoCorsaRoot, source.ServerPayloadPath);
        preset.Mode = mode;
        preset.Name = mode == EventMode.Fps ? "New LAN deathmatch" : "New LAN race";
        preset.ServerName = mode == EventMode.Fps ? "AssettoServer LAN Deathmatch" : "AssettoServer LAN Race";
        preset.TrackId = source.TrackId;
        preset.TrackLayoutId = source.TrackLayoutId;
        preset.Conditions = new ConditionOptions
        {
            WeatherId = source.Conditions.WeatherId,
            SunAngleDegrees = source.Conditions.SunAngleDegrees,
            AmbientTemperatureCelsius = source.Conditions.AmbientTemperatureCelsius,
            RoadTemperatureCelsius = source.Conditions.RoadTemperatureCelsius,
            WindMinKmh = source.Conditions.WindMinKmh,
            WindMaxKmh = source.Conditions.WindMaxKmh,
            WindDirectionDegrees = source.Conditions.WindDirectionDegrees,
            StartingGripPercent = source.Conditions.StartingGripPercent,
            GripRandomnessPercent = source.Conditions.GripRandomnessPercent,
            GripTransferPercent = source.Conditions.GripTransferPercent,
            LapsPerGripIncrease = source.Conditions.LapsPerGripIncrease,
        };
        preset.Network = new NetworkOptions
        {
            BindAddress = source.Network.BindAddress,
            TcpPort = source.Network.TcpPort,
            UdpPort = source.Network.UdpPort,
            HttpPort = source.Network.HttpPort,
            JoinPassword = source.Network.JoinPassword,
            AdminPassword = source.Network.AdminPassword,
            LanOnly = source.Network.LanOnly,
        };
        if (mode == EventMode.Fps)
        {
            int count = Math.Clamp(source.Grid.Count(slot => slot.Mode != SlotMode.Spectator), 2, 8);
            preset.Grid = Enumerable.Range(1, count).Select(index => new GridSlotPreset
            {
                CarId = preset.Fps.CarrierCarId,
                DriverName = $"Operative {index:00}",
                TeamName = "Deathmatch",
                Mode = SlotMode.Auto,
            }).ToList();
        }
        return preset;
    }

    private void NotifyModeProperties()
    {
        OnPropertyChanged(nameof(EventMode));
        OnPropertyChanged(nameof(IsFpsMode));
        OnPropertyChanged(nameof(ContentSectionTitle));
        OnPropertyChanged(nameof(TrackSelectionLabel));
        OnPropertyChanged(nameof(GridTabTitle));
        OnPropertyChanged(nameof(GridSectionTitle));
        OnPropertyChanged(nameof(FillGridLabel));
        OnPropertyChanged(nameof(FillGridToolTip));
        OnPropertyChanged(nameof(SessionSectionTitle));
        OnPropertyChanged(nameof(BotSectionTitle));
        OnPropertyChanged(nameof(LiveTabTitle));
        OnPropertyChanged(nameof(LiveSessionTitle));
        OnPropertyChanged(nameof(StartRaceLabel));
        OnPropertyChanged(nameof(StopRaceLabel));
        OnPropertyChanged(nameof(RestartRaceLabel));
        OnPropertyChanged(nameof(StopServerToolTip));
        OnPropertyChanged(nameof(StartRaceToolTip));
        OnPropertyChanged(nameof(StopRaceToolTip));
        OnPropertyChanged(nameof(RestartRaceToolTip));
        OnPropertyChanged(nameof(SlotModeHelp));
        OnPropertyChanged(nameof(SlotModeOptions));
        OnPropertyChanged(nameof(SelectedTrackDetails));
        OnPropertyChanged(nameof(FpsArenaStatus));
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
            string assettoCorsaRoot = Preset.AssettoCorsaRoot;
            string serverPayloadPath = Preset.ServerPayloadPath;
            var loaded = _presetStore.Load(SelectedSavedPreset.Path);
            loaded.AssettoCorsaRoot = assettoCorsaRoot;
            loaded.ServerPayloadPath = serverPayloadPath;
            Preset = loaded;
            RememberModeDraft(loaded);
            Replace(Tracks, FilteredTracks());
            await ApplyCurrentCatalogOrRefreshAsync();
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
            imported.Mode = EventMode.Racing;
            imported.Network.BindAddress = NetworkAddressService.GetPreferredPrivateIpv4();
            Preset = imported;
            _racingDraft = imported;
            await ApplyCurrentCatalogOrRefreshAsync();
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

    private async Task PrepareFpsArenaAsync()
    {
        if (SelectedTrack is null) return;
        try
        {
            IsBusy = true;
            StatusText = $"Preparing FPS arena {SelectedTrack.DisplayName}…";
            var progress = new Progress<StagingProgress>(update =>
            {
                ProgressText = update.Message;
                ProgressValue = (update.Fraction ?? 0) * 100;
                AppendLog($"[{update.Stage}] {update.Message}");
            });
            Preset.Fps.Arena = await new FpsArenaPreparationService(_fpsArenaStore)
                .PrepareAsync(Preset, progress);
            OnPropertyChanged(nameof(FpsArenaStatus));
            OnPropertyChanged(nameof(SelectedTrackDetails));
            if (ShowPreparedFpsArenasOnly) RefreshTrackFilter();
            Validate();
            StatusText = $"Prepared {SelectedTrack.DisplayName} as an FPS compatibility arena.";
        }
        catch (Exception exception)
        {
            HandleException("FPS arena preparation failed", exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportFpsClientPackAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export FPS compatibility client pack",
            Filter = "ZIP archive (*.zip)|*.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = "asrc-fps-compatibility-client-v4.zip",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            IsBusy = true;
            byte[] rifleViewmodel = FpsClientPackAssets.GetRifleViewmodel();
            byte[] rifleWorldModel = FpsClientPackAssets.GetRifleWorldModel();
            byte[] rifleDiffuse = FpsClientPackAssets.GetRifleDiffuse();
            byte[] operatorSkin = FpsClientPackAssets.GetOperatorSkin();
            await using var stream = new FileStream(dialog.FileName, FileMode.Create, FileAccess.Write, FileShare.None,
                64 * 1024, useAsync: true);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
            var manifestEntry = archive.CreateEntry("asrc-fps-client.json", CompressionLevel.Optimal);
            await using (var manifestStream = manifestEntry.Open())
            {
                await JsonSerializer.SerializeAsync(manifestStream, new
                {
                    protocol = 1,
                    clientPackVersion = 4,
                    compatibilityGate = true,
                    minimumCspVersion = "0.3.0-preview520",
                    carrierCar = Preset.Fps.CarrierCarId,
                    nativeHooks = false,
                    weapon = new
                    {
                        id = "asrc_assault_rifle_v1",
                        ammunition = "40-round magazine with four reserves",
                        fireIntervalSeconds = 0.12,
                        damage = 34,
                        rangeMetres = 120,
                        packagedViewmodel = true,
                        viewmodelPath = FpsClientPackAssets.RifleViewmodelPath,
                        viewmodelSha256 = FpsClientPackAssets.Sha256(rifleViewmodel),
                        worldModelPath = FpsClientPackAssets.RifleWorldModelPath,
                        worldModelSha256 = FpsClientPackAssets.Sha256(rifleWorldModel),
                        diffusePath = FpsClientPackAssets.RifleDiffusePath,
                        diffuseSha256 = FpsClientPackAssets.Sha256(rifleDiffuse),
                    },
                    operatorSkinPath = FpsClientPackAssets.OperatorSkinPath,
                    operatorSkinSha256 = FpsClientPackAssets.Sha256(operatorSkin),
                }, new JsonSerializerOptions { WriteIndented = true });
            }
            var readmeEntry = archive.CreateEntry("README.txt", CompressionLevel.Optimal);
            await using (var writer = new StreamWriter(readmeEntry.Open()))
            {
                await writer.WriteAsync("""
                    AssettoServer Race Control FPS compatibility gate

                    Requirements:
                    - Assetto Corsa with CSP 0.3.0-preview520 or newer compatible preview.
                    - The carrier car named in asrc-fps-client.json must be installed.
                    - Join through Content Manager Online > LAN.

                    The server delivers the CSP online script automatically. Extract this ZIP into
                    the Assetto Corsa installation root. It installs the project-owned assault-rifle
                    models and operator UV skin under content/objects3D/asrc_fps, plus rifle sound
                    under extension/audio/asrc_fps. Existing files are not replaced outside those
                    folders. FPS avatars use the packaged procedural operator, not Kunos assets.
                    FPS mode requests a 0.03 m camera near clip at runtime. If a CSP build or global
                    graphics override prevents that request, the client log reports the observed
                    near-clip value and method so wall clipping is diagnosable.
                    No acs.exe modification or native hook is installed.
                    """);
            }
            var rifleViewmodelEntry = archive.CreateEntry(FpsClientPackAssets.RifleViewmodelPath,
                CompressionLevel.Optimal);
            await using (var rifleViewmodelStream = rifleViewmodelEntry.Open())
                await rifleViewmodelStream.WriteAsync(rifleViewmodel);
            var rifleWorldModelEntry = archive.CreateEntry(FpsClientPackAssets.RifleWorldModelPath,
                CompressionLevel.Optimal);
            await using (var rifleWorldModelStream = rifleWorldModelEntry.Open())
                await rifleWorldModelStream.WriteAsync(rifleWorldModel);
            var rifleDiffuseEntry = archive.CreateEntry(FpsClientPackAssets.RifleDiffusePath,
                CompressionLevel.Optimal);
            await using (var rifleDiffuseStream = rifleDiffuseEntry.Open())
                await rifleDiffuseStream.WriteAsync(rifleDiffuse);
            var operatorSkinEntry = archive.CreateEntry(FpsClientPackAssets.OperatorSkinPath,
                CompressionLevel.Optimal);
            await using (var operatorSkinStream = operatorSkinEntry.Open())
                await operatorSkinStream.WriteAsync(operatorSkin);
            var rifleAudioEntry = archive.CreateEntry("extension/audio/asrc_fps/rifle.wav",
                CompressionLevel.Optimal);
            await using (var rifleAudioStream = rifleAudioEntry.Open())
                await rifleAudioStream.WriteAsync(FpsClientPackAssets.CreateRifleWave());
            StatusText = $"Exported FPS compatibility client pack: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception exception)
        {
            HandleException("FPS client-pack export failed", exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportServerPackageAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export standalone server package",
            Filter = "ZIP archive (*.zip)|*.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"{FileNameSanitizer.Slug(Preset.Name)}-server-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            IsBusy = true;
            StatusText = "Packaging the current standalone server…";
            AppendLog($"[Export] Creating complete server package at {dialog.FileName}…");
            await new InstancePackageExporter().ExportAsync(_paths.WorkingInstanceDirectory,
                dialog.FileName);
            StatusText = $"Exported standalone server package: {Path.GetFileName(dialog.FileName)}";
            AppendLog($"[Export] Complete server package written to {dialog.FileName}");
        }
        catch (Exception exception)
        {
            HandleException("Server package export failed", exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AddSlot()
    {
        var source = SelectedGridSlot?.ToPreset() ?? Grid.LastOrDefault()?.ToPreset() ?? new GridSlotPreset();
        source.CarId = IsFpsMode ? Preset.Fps.CarrierCarId : source.CarId;
        source.DriverName = IsFpsMode
            ? $"Operative {Grid.Count + 1:00}"
            : $"{Preset.Bots.NamePrefix} {Grid.Count + 1:00}";
        source.TeamName = IsFpsMode ? "Deathmatch" : source.TeamName;
        source.Mode = SlotMode.Auto;
        var row = CreateGridSlotViewModel(source, Grid.Count + 1);
        var firstSpectatorIndex = Grid.ToList().FindIndex(slot => slot.Mode == SlotMode.Spectator);
        if (firstSpectatorIndex >= 0)
            Grid.Insert(firstSpectatorIndex, row);
        else
            Grid.Add(row);
        SelectedGridSlot = row;
        OnGridChanged();
    }

    private void RemoveSelectedSlot()
    {
        var selected = SelectedGridSlot;
        if (selected is null || !CanRemoveSelectedSlot())
        {
            return;
        }

        var index = Grid.IndexOf(selected);
        selected.PropertyChanged -= OnGridSlotPropertyChanged;
        Grid.Remove(selected);
        ReindexGrid();
        SelectedGridSlot = Grid[Math.Min(index, Grid.Count - 1)];
        OnGridChanged();
    }

    private bool CanRemoveSelectedSlot()
    {
        if (SelectedGridSlot is null || IsBusy)
            return false;
        return SelectedGridSlot.Mode == SlotMode.Spectator
               || Grid.Count(slot => slot.Mode != SlotMode.Spectator) > 2;
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

        int capacity = IsFpsMode ? Math.Min(32, SelectedTrack.PitBoxes) : SelectedTrack.PitBoxes;
        while (Grid.Count(slot => slot.Mode != SlotMode.Spectator) < capacity
               && Grid.Count < (IsFpsMode ? 32 : 254))
        {
            AddSlot();
        }
        while (Grid.Count(slot => slot.Mode != SlotMode.Spectator) > capacity)
        {
            var lastRacingSlot = Grid.Last(slot => slot.Mode != SlotMode.Spectator);
            lastRacingSlot.PropertyChanged -= OnGridSlotPropertyChanged;
            Grid.Remove(lastRacingSlot);
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
        if (!IsFpsMode) Preset.Bots.Enabled = true;
        OnPropertyChanged(nameof(Preset));
        foreach (var slot in Grid.Where(slot => slot.Mode != SlotMode.Spectator))
        {
            slot.Mode = SlotMode.Auto;
        }
        StatusText = IsFpsMode
            ? "All scored FPS slots are bots until humans claim them; spectator reservations were retained."
            : "All racing slots are now occupied by bots until humans claim them; spectator reservations were retained.";
    }

    private bool CanPopulateGrid()
    {
        if (IsFpsMode || _catalog is null || IsBusy || GridPopulationCount is < 2 or > 254)
            return false;
        return SelectedGridPopulationCategory.Value switch
        {
            GridPopulationCategory.Class => !string.IsNullOrWhiteSpace(GridPopulationClass),
            GridPopulationCategory.MaximumHorsepower => GridPopulationCriterionValue > 0,
            GridPopulationCategory.ModelYear => GridPopulationCriterionValue is >= 1886 and <= 2200,
            GridPopulationCategory.MaximumPowerToWeight => GridPopulationCriterionValue > 0,
            _ => true,
        };
    }

    private void PopulateGrid()
    {
        if (_catalog is null)
            return;
        try
        {
            var spectators = Grid.Where(slot => slot.Mode == SlotMode.Spectator)
                .Select(slot => CloneGridSlot(slot.ToPreset()))
                .ToArray();
            int maximumRacers = 254 - spectators.Length;
            if (maximumRacers < 2)
                throw new InvalidOperationException("Remove spectator entries before populating a racing grid.");
            int requested = Math.Min(GridPopulationCount, maximumRacers);
            var request = new GridPopulationRequest(
                requested,
                SelectedGridPopulationCategory.Value,
                GridPopulationClass,
                SelectedGridPopulationCategory.Value == GridPopulationCategory.MaximumHorsepower
                    ? GridPopulationCriterionValue : null,
                SelectedGridPopulationCategory.Value == GridPopulationCategory.ModelYear
                    ? (int)GridPopulationCriterionValue : null,
                SelectedGridPopulationCategory.Value == GridPopulationCategory.MaximumPowerToWeight
                    ? GridPopulationCriterionValue : null,
                Preset.Bots.NamePrefix);
            var result = _gridPopulationService.Populate(_catalog, request);
            if (result.Slots.Count == 0)
            {
                StatusText = "No bot-capable installed cars match that grid filter.";
                return;
            }

            ReplaceGrid(result.Slots.Concat(spectators));
            string capacityNote = requested < GridPopulationCount
                ? $" Protocol capacity limited the request to {requested}."
                : SelectedTrack is { PitBoxes: > 0 } && requested > SelectedTrack.PitBoxes
                    ? $" The selected layout has {SelectedTrack.PitBoxes} pit boxes; staging will keep the first entries that fit."
                    : string.Empty;
            StatusText = $"Populated {requested} replaceable racing slots from {result.EligibleCarCount} matching cars.{capacityNote}";
        }
        catch (Exception exception)
        {
            HandleException("Could not populate grid", exception);
        }
    }

    private void SaveGrid()
    {
        try
        {
            var store = _savedGridStore
                        ?? throw new InvalidOperationException("Saved-grid storage is not initialized.");
            var existing = SavedGrids.FirstOrDefault(grid =>
                grid.Name.Equals(SavedGridName.Trim(), StringComparison.OrdinalIgnoreCase));
            var grid = new SavedGridPreset
            {
                Id = existing?.Id ?? Guid.NewGuid(),
                Name = SavedGridName,
                Slots = Grid.Select(slot => CloneGridSlot(slot.ToPreset())).ToList(),
            };
            store.Save(grid);
            RefreshSavedGrids();
            SelectedSavedGrid = SavedGrids.FirstOrDefault(summary => summary.Id == grid.Id);
            StatusText = $"Saved grid {grid.Name}.";
        }
        catch (Exception exception)
        {
            HandleException("Could not save grid", exception);
        }
    }

    private void LoadSavedGrid()
    {
        if (SelectedSavedGrid is null || _savedGridStore is null)
            return;
        try
        {
            var saved = _savedGridStore.Load(SelectedSavedGrid.Path);
            var installedIds = Cars.Select(car => car.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = saved.Slots.Select(slot => slot.CarId)
                .Where(id => !installedIds.Contains(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (missing.Length > 0)
                throw new InvalidDataException($"The saved grid requires missing car(s): {string.Join(", ", missing)}");

            ReplaceGrid(saved.Slots.Select(CloneGridSlot));
            SavedGridName = saved.Name;
            StatusText = $"Loaded grid {saved.Name}; event, track, sessions, and network settings were retained.";
        }
        catch (Exception exception)
        {
            HandleException("Could not load grid", exception);
        }
    }

    private void DeleteSavedGrid()
    {
        if (SelectedSavedGrid is null || _savedGridStore is null)
            return;
        try
        {
            string name = SelectedSavedGrid.Name;
            _savedGridStore.Delete(SelectedSavedGrid.Path);
            SelectedSavedGrid = null;
            SavedGridName = string.Empty;
            RefreshSavedGrids();
            StatusText = $"Deleted saved grid {name}.";
        }
        catch (Exception exception)
        {
            HandleException("Could not delete grid", exception);
        }
    }

    private void ReplaceGrid(IEnumerable<GridSlotPreset> slots)
    {
        foreach (var row in Grid)
            row.PropertyChanged -= OnGridSlotPropertyChanged;
        Grid.Clear();
        foreach (var slot in slots.Take(254))
            Grid.Add(CreateGridSlotViewModel(slot, Grid.Count + 1));
        EnsureTwoSlots();
        SelectedGridSlot = Grid.FirstOrDefault(slot => slot.Mode != SlotMode.Spectator)
                           ?? Grid.FirstOrDefault();
        OnGridChanged();
    }

    private static GridSlotPreset CloneGridSlot(GridSlotPreset slot) => new()
    {
        CarId = slot.CarId,
        SkinId = slot.SkinId,
        DriverName = slot.DriverName,
        TeamName = slot.TeamName,
        NationCode = slot.NationCode,
        BallastKg = slot.BallastKg,
        RestrictorPercent = slot.RestrictorPercent,
        Difficulty = slot.Difficulty,
        Aggression = slot.Aggression,
        Mode = slot.Mode,
    };

    private void EnsureTwoSlots()
    {
        while (Grid.Count(slot => slot.Mode != SlotMode.Spectator) < 2 && Cars.Count > 0)
        {
            var source = Grid.LastOrDefault()?.ToPreset() ?? new GridSlotPreset { CarId = Cars[0].Id };
            source.CarId = IsFpsMode ? Preset.Fps.CarrierCarId : source.CarId;
            source.DriverName = IsFpsMode
                ? $"Operative {Grid.Count + 1:00}"
                : $"{Preset.Bots.NamePrefix} {Grid.Count + 1:00}";
            source.TeamName = IsFpsMode ? "Deathmatch" : source.TeamName;
            source.Mode = SlotMode.Auto;
            var row = CreateGridSlotViewModel(source, Grid.Count + 1);
            var firstSpectatorIndex = Grid.ToList().FindIndex(slot => slot.Mode == SlotMode.Spectator);
            if (firstSpectatorIndex >= 0)
                Grid.Insert(firstSpectatorIndex, row);
            else
                Grid.Add(row);
        }
    }

    private void OnGridChanged()
    {
        ReindexGrid();
        OnPropertyChanged(nameof(EffectiveGridSummary));
        RaiseCommandStates();
    }

    private GridSlotViewModel CreateGridSlotViewModel(GridSlotPreset slot, int index)
    {
        var row = new GridSlotViewModel(slot, Cars, index);
        row.PropertyChanged += OnGridSlotPropertyChanged;
        return row;
    }

    private void OnGridSlotPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(GridSlotViewModel.Mode))
            return;
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
            int recoveredServers = await _processController.StopOrphanedServersAsync(
                _paths.InstancesDirectory);
            if (recoveredServers > 0)
                StatusText = $"Stopped {recoveredServers} previous server process(es); staging the working server…";
            _lastInstance = await StageAsync();
            InvalidateLiveTrackCache();
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
            int recoveredServers = await _processController.StopOrphanedServersAsync(
                _paths.InstancesDirectory);
            if (recoveredServers > 0)
                StatusText = $"Stopped {recoveredServers} previous server process(es); staging the new race…";
            _lastInstance = await StageAsync();
            InvalidateLiveTrackCache();
            SimulationResults = null;
            var liveClient = new LiveRaceControlClient(_lastInstance.RootPath);
            _processController.Start(_lastInstance.ExecutablePath, _lastInstance.RootPath,
                _lastInstance.PresetName, _lastInstance.ShutdownFilePath,
                liveClient.ControlDirectory);
            StatusText = $"Server is running on {Preset.Network.BindAddress}:{Preset.Network.HttpPort}.";
            LiveControlStatus = "Waiting for authoritative server telemetry…";
            SelectedPageIndex = 6;
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
            bool restartSimulation = _processController.IsSimulation;
            if (restartSimulation)
                SimulationResults = null;
            InvalidateLiveTrackCache();
            var liveClient = new LiveRaceControlClient(_lastInstance.RootPath);
            await _processController.RestartAsync(
                _lastInstance.ExecutablePath,
                _lastInstance.RootPath,
                _lastInstance.PresetName,
                _lastInstance.ShutdownFilePath,
                liveClient.ControlDirectory,
                restartSimulation ? CreateSimulationLaunchOptions(_lastInstance.RootPath) : null);
            StatusText = restartSimulation ? "Race simulation restarted." : "Server restarted.";
            LiveControlStatus = "Waiting for restarted server telemetry…";
            SelectedPageIndex = 6;
        }
        catch (Exception exception)
        {
            HandleException("Could not restart server", exception);
        }
    }

    private async Task SimulateRaceAsync()
    {
        try
        {
            IsBusy = true;
            SyncGridToPreset();
            if (!Preset.Bots.Enabled)
                throw new InvalidOperationException("Enable race bots before starting an accelerated simulation.");
            if (Preset.Grid.Count(slot => slot.Mode is SlotMode.Auto or SlotMode.Fixed) < 2)
                throw new InvalidOperationException("Accelerated simulation requires at least two bot-capable grid slots.");

            int recoveredServers = await _processController.StopOrphanedServersAsync(
                _paths.InstancesDirectory);
            if (recoveredServers > 0)
                StatusText = $"Stopped {recoveredServers} previous server process(es); staging the simulation…";
            _lastInstance = await StageAsync();
            InvalidateLiveTrackCache();
            SimulationResults = null;
            var liveClient = new LiveRaceControlClient(_lastInstance.RootPath);
            _processController.Start(_lastInstance.ExecutablePath, _lastInstance.RootPath,
                _lastInstance.PresetName, _lastInstance.ShutdownFilePath,
                liveClient.ControlDirectory, CreateSimulationLaunchOptions(_lastInstance.RootPath));
            StatusText = $"Accelerated race simulation started with seed {SimulationSeed} "
                         + $"for {SimulationLimitValue} {SimulationLimitMode.ToLowerInvariant()}.";
            LiveControlStatus = "Waiting for simulation telemetry…";
            SelectedPageIndex = 6;
        }
        catch (Exception exception)
        {
            HandleException("Race simulation failed", exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private RaceSimulationLaunchOptions CreateSimulationLaunchOptions(string instanceRoot) => new(
        OutputDirectory: LiveRaceControlClient.GetSimulationOutputDirectory(instanceRoot),
        Seed: SimulationSeed,
        MaximumSimulatedMinutes: SimulationLimitMode == "Laps" ? 0 : SimulationLimitValue,
        MaximumWallSeconds: 300,
        SampleIntervalMilliseconds: 500,
        TimeScale: SimulationTimeScale,
        MaximumSimulatedLaps: SimulationLimitMode == "Laps" ? SimulationLimitValue : 0);

    private bool CanControlLiveRace() =>
        _lastInstance is not null && _processController.State == ServerProcessState.Running;

    private bool CanControlSelectedBot() => IsSelectedBotControllable;
    private bool CanToggleTakeover() => IsServerRunning
                                        && (_takeoverSessionId.HasValue
                                            || SelectedLiveCar is { IsBot: true, IsActive: true });

    private async Task SendLiveRaceCommandAsync(LiveRaceCommand command)
    {
        if (_lastInstance is null)
            return;
        try
        {
            var client = new LiveRaceControlClient(_lastInstance.RootPath);
            _pendingLiveCommandId = await client.SendCommandAsync(command);
            if (command == LiveRaceCommand.Restart)
                InvalidateLiveTrackCache();
            string sessionKind = IsFpsMode ? "Match" : "Race";
            LiveControlStatus = $"{sessionKind} {command.ToString().ToLowerInvariant()} requested…";
        }
        catch (Exception exception)
        {
            HandleException($"Could not {command.ToString().ToLowerInvariant()} race", exception);
        }
    }

    private async Task StopGoSelectedBotAsync()
    {
        if (SelectedLiveCar is not { IsBot: true } car || _lastInstance is null)
            return;
        try
        {
            bool stop = !car.IsStoppedByRaceControl;
            var client = new LiveRaceControlClient(_lastInstance.RootPath);
            _pendingLiveCommandId = await client.SendBotStopAsync(car.SessionId, stop);
            LiveControlStatus = stop ? $"Stopping {car.Name}…" : $"Returning {car.Name} to AI control…";
            if (stop && _takeoverSessionId == car.SessionId)
                SetTakeoverSession(null);
        }
        catch (Exception exception)
        {
            HandleException("Could not change bot stop state", exception);
        }
    }

    private async Task TeleportSelectedBotAsync()
    {
        if (SelectedLiveCar is not { IsBot: true } car || _lastInstance is null)
            return;
        try
        {
            var client = new LiveRaceControlClient(_lastInstance.RootPath);
            _pendingLiveCommandId = await client.SendBotTeleportToP1Async(car.SessionId);
            LiveControlStatus = $"Teleporting {car.Name} ahead of the current leader…";
        }
        catch (Exception exception)
        {
            HandleException("Could not teleport bot", exception);
        }
    }

    private async Task ToggleSelectedBotTakeoverAsync()
    {
        if (_lastInstance is null)
            return;
        int? sessionId = _takeoverSessionId ?? SelectedLiveCar?.SessionId;
        if (!sessionId.HasValue)
            return;
        bool takeOver = !_takeoverSessionId.HasValue;
        try
        {
            var client = new LiveRaceControlClient(_lastInstance.RootPath);
            _pendingLiveCommandId = await client.SendBotTakeoverAsync(sessionId.Value, takeOver);
            SetTakeoverSession(takeOver ? sessionId : null);
            LiveControlStatus = takeOver
                ? "Manual bot control requested…"
                : "Returning bot to AI control…";
        }
        catch (Exception exception)
        {
            HandleException("Could not change manual bot control", exception);
        }
    }

    public async Task UpdateTakeoverInputAsync(float steering, float throttle, float brake)
    {
        if (!_takeoverSessionId.HasValue || _lastInstance is null
                                         || _processController.State != ServerProcessState.Running
                                         || Interlocked.Exchange(ref _manualInputWriteInProgress, 1) != 0)
            return;
        try
        {
            var client = new LiveRaceControlClient(_lastInstance.RootPath);
            await client.WriteManualInputAsync(_takeoverSessionId.Value, steering, throttle, brake);
        }
        catch (Exception exception)
        {
            LiveControlStatus = $"Manual input failed: {exception.Message}";
        }
        finally
        {
            Volatile.Write(ref _manualInputWriteInProgress, 0);
        }
    }

    public void ReleaseTakeoverFromInput() => TakeOverSelectedBotCommand.Execute(null);

    private void SetTakeoverSession(int? sessionId)
    {
        if (_takeoverSessionId == sessionId)
            return;
        _takeoverSessionId = sessionId;
        OnPropertyChanged(nameof(IsBotTakeoverActive));
        OnPropertyChanged(nameof(TakeOverSelectedBotText));
        OnPropertyChanged(nameof(BotControlStatus));
        TakeOverSelectedBotCommand.RaiseCanExecuteChanged();
    }

    private void ScheduleSimulationTimeScaleUpdate()
    {
        _simulationTimeScaleUpdateCancellation?.Cancel();
        _simulationTimeScaleUpdateCancellation?.Dispose();
        _simulationTimeScaleUpdateCancellation = null;
        if (_lastInstance is null || !_processController.IsSimulation
                                  || _processController.State != ServerProcessState.Running)
            return;

        var cancellation = new CancellationTokenSource();
        _simulationTimeScaleUpdateCancellation = cancellation;
        _ = SendSimulationTimeScaleAfterDelayAsync(SimulationTimeScale, cancellation.Token);
    }

    private async Task SendSimulationTimeScaleAfterDelayAsync(double timeScale,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(150, cancellationToken);
            if (_lastInstance is null || !_processController.IsSimulation
                                      || _processController.State != ServerProcessState.Running)
                return;
            var client = new LiveRaceControlClient(_lastInstance.RootPath);
            _pendingLiveCommandId = await client.SendSimulationTimeScaleAsync(timeScale, cancellationToken);
            LiveControlStatus = $"Changing simulation time acceleration to {timeScale:F0}×…";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            RunOnUi(() =>
            {
                LiveControlStatus = $"Could not change simulation speed: {exception.Message}";
                AppendLog($"[Live] {LiveControlStatus}");
            });
        }
    }

    private async Task MonitorLiveRaceAsync(CancellationToken cancellationToken)
    {
        string? observedInstance = null;
        int observedTrackGeneration = -1;
        var trackCache = new LiveTrackFileCache();
        DateTimeOffset? observedResultsAt = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var instance = _lastInstance;
                if (instance != null)
                {
                    int trackGeneration = Volatile.Read(ref _liveTrackGeneration);
                    if (!instance.RootPath.Equals(observedInstance, StringComparison.OrdinalIgnoreCase)
                        || trackGeneration != observedTrackGeneration)
                    {
                        observedInstance = instance.RootPath;
                        observedTrackGeneration = trackGeneration;
                        trackCache.Invalidate();
                        observedResultsAt = null;
                        RunOnUi(() =>
                        {
                            LiveSnapshot = null;
                            LiveTrack = null;
                            LiveCars.Clear();
                            SelectedLiveCar = null;
                            SimulationResults = null;
                            OnPropertyChanged(nameof(LiveSessionSummary));
                            OnPropertyChanged(nameof(LiveTimingSummary));
                        });
                    }

                    var client = new LiveRaceControlClient(instance.RootPath);
                    var snapshot = client.TryReadSnapshot();
                    LiveTrackMap? track = trackCache.TryReadChanged(client);
                    SimulationRaceSummary? results = null;
                    if (snapshot is { IsSimulation: true, ServerRunning: false })
                    {
                        var candidate = client.TryReadSimulationSummary();
                        if (candidate != null && candidate.CompletedAt != observedResultsAt)
                        {
                            observedResultsAt = candidate.CompletedAt;
                            results = candidate;
                        }
                    }
                    if (snapshot != null || track != null || results != null)
                        RunOnUi(() =>
                        {
                            ApplyLiveUpdate(snapshot, track);
                            if (track != null)
                                AppendLog($"[Live] Loaded {track.Track}/{track.Layout} track map with {track.Points.Count} points.");
                            if (results != null)
                                SimulationResults = results;
                        });
                }

                // The normal live map is intentionally inexpensive. During takeover, poll at
                // the display target so authoritative poses can drive a responsive chase view.
                await Task.Delay(IsBotTakeoverActive ? 16 : 100, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void InvalidateLiveTrackCache() => Interlocked.Increment(ref _liveTrackGeneration);

    private void ApplyLiveUpdate(LiveRaceSnapshot? snapshot, LiveTrackMap? track)
    {
        if (track != null)
            LiveTrack = track;
        if (snapshot == null)
            return;

        int selectedSessionId = SelectedLiveCar?.SessionId ?? -1;
        LiveSnapshot = snapshot;
        Replace(LiveCars, snapshot.Cars);
        SelectedLiveCar = LiveCars.FirstOrDefault(car => car.SessionId == selectedSessionId)
                          ?? LiveCars.FirstOrDefault(car => car.IsActive)
                          ?? LiveCars.FirstOrDefault();
        if (_pendingLiveCommandId.HasValue
            && snapshot.LastCommand?.Id == _pendingLiveCommandId.Value)
        {
            LiveControlStatus = snapshot.LastCommand.Message;
            if (snapshot.LastCommand.Command == "bot_takeover"
                && snapshot.LastCommand.Status != "accepted")
                SetTakeoverSession(null);
            _pendingLiveCommandId = null;
        }
        else if (!snapshot.ServerRunning)
        {
            SetTakeoverSession(null);
            LiveControlStatus = "Server is offline. The last authoritative frame remains visible.";
        }
        else if (snapshot.IsSimulation)
        {
            LiveControlStatus = $"Simulation running at {snapshot.RealTimeFactor:F1}× real time.";
        }
        else
        {
            LiveControlStatus = "Live server telemetry connected.";
        }
        if (_takeoverSessionId.HasValue)
        {
            var controlled = LiveCars.FirstOrDefault(car => car.SessionId == _takeoverSessionId.Value);
            if (controlled is null || !controlled.IsBot || controlled.IsStoppedByRaceControl)
                SetTakeoverSession(null);
        }
        NotifySelectedBotControlChanged();
        OnPropertyChanged(nameof(LiveSessionSummary));
        OnPropertyChanged(nameof(LiveTimingSummary));
    }

    private void OpenInstance()
    {
        var path = SelectedRecentInstance?.RootPath ?? _lastInstance?.RootPath;
        if (path is not null)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
    }

    private static string FormatCompactDuration(long milliseconds)
    {
        var duration = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes}:{duration.Seconds:00}";
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
            Replace(SavedPresets, _presetStore.List(Preset.Mode));
            if (SelectedSavedPreset?.Mode != Preset.Mode)
                SelectedSavedPreset = null;
        }
    }

    private void RefreshSavedGrids()
    {
        if (_savedGridStore is not null)
            Replace(SavedGrids, _savedGridStore.List());
    }

    private void RefreshCarClassOptions()
    {
        string selected = GridPopulationClass;
        Replace(CarClassOptions, Cars.Select(car => car.ClassName.Trim())
            .Where(className => !string.IsNullOrWhiteSpace(className))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase));
        GridPopulationClass = CarClassOptions.FirstOrDefault(className =>
                                  className.Equals(selected, StringComparison.OrdinalIgnoreCase))
                              ?? CarClassOptions.FirstOrDefault()
                              ?? string.Empty;
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
        PopulateGridCommand.RaiseCanExecuteChanged();
        SaveGridCommand.RaiseCanExecuteChanged();
        LoadGridCommand.RaiseCanExecuteChanged();
        DeleteGridCommand.RaiseCanExecuteChanged();
        ValidateCommand.RaiseCanExecuteChanged();
        StageCommand.RaiseCanExecuteChanged();
        LaunchCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        RestartCommand.RaiseCanExecuteChanged();
        SimulateRaceCommand.RaiseCanExecuteChanged();
        PrepareFpsArenaCommand.RaiseCanExecuteChanged();
        ExportFpsClientPackCommand.RaiseCanExecuteChanged();
        StartRaceCommand.RaiseCanExecuteChanged();
        StopRaceCommand.RaiseCanExecuteChanged();
        RestartRaceCommand.RaiseCanExecuteChanged();
        StopGoSelectedBotCommand.RaiseCanExecuteChanged();
        TeleportSelectedBotCommand.RaiseCanExecuteChanged();
        TakeOverSelectedBotCommand.RaiseCanExecuteChanged();
        ExportServerPackageCommand.RaiseCanExecuteChanged();
        OpenInstanceCommand.RaiseCanExecuteChanged();
    }

    private void NotifySelectedBotControlChanged()
    {
        OnPropertyChanged(nameof(IsSelectedBotControllable));
        OnPropertyChanged(nameof(SelectedBotIsStopped));
        OnPropertyChanged(nameof(StopGoSelectedBotText));
        OnPropertyChanged(nameof(BotControlStatus));
        StopGoSelectedBotCommand.RaiseCanExecuteChanged();
        TeleportSelectedBotCommand.RaiseCanExecuteChanged();
        TakeOverSelectedBotCommand.RaiseCanExecuteChanged();
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

    public void Dispose()
    {
        CancelBackgroundContentRefresh();
        _simulationTimeScaleUpdateCancellation?.Cancel();
        _simulationTimeScaleUpdateCancellation?.Dispose();
        _liveMonitorCancellation.Cancel();
        _liveMonitorCancellation.Dispose();
        _processController.Dispose();
        GC.KeepAlive(_liveMonitorTask);
        GC.KeepAlive(_contentRefreshTask);
    }
}
