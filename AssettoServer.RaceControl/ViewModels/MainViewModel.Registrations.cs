using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using AssettoServer.RaceControl.Core.Configuration;
using AssettoServer.RaceControl.Core.Models;
using AssettoServer.RaceControl.Core.Players;
using AssettoServer.RaceControl.Core.Web;
using AssettoServer.RaceControl.Infrastructure;

namespace AssettoServer.RaceControl.ViewModels;

/// <summary>One registered player in the Grid tab's Registrations panel.</summary>
public sealed class RegistrationRowViewModel(PlayerRegistration registration, string preferredCar, string assignedTo)
{
    public PlayerRegistration Registration { get; } = registration;
    public string DisplayName => Registration.DisplayName;
    public string TeamName => string.IsNullOrWhiteSpace(Registration.TeamName) ? "—" : Registration.TeamName;
    public string PreferredCar { get; } = preferredCar;
    public string SteamId => Registration.SteamId;
    public string AssignedTo { get; } = assignedTo;
    public string RegisteredAt => Registration.RegisteredAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    /// <summary>How the player appears in the Grid's per-row pick list.</summary>
    public string PickLabel => (string.IsNullOrWhiteSpace(Registration.TeamName)
                                   ? DisplayName
                                   : $"{DisplayName}  •  {Registration.TeamName}")
                               + (AssignedTo == "—" ? "  (no car yet)" : $"  (in {AssignedTo})");
}

/// <summary>
/// Player pre-registration: event details for the Web GUI's Players page, and the Grid tab's
/// Registrations panel where the host assigns registered players to cars.
/// </summary>
public sealed partial class MainViewModel
{
    private PlayerRegistrationStore? _registrationStore;
    private RegistrationRowViewModel? _selectedRegistration;
    private string _registrationStatus = string.Empty;
    private bool _gridWatchAttached;
    // Last accepted team name per row, so a clashing edit can be undone.
    private readonly Dictionary<GridSlotViewModel, string> _teamByRow = new();
    // The time used once a date is picked, so typing a time first doesn't invent a date.
    private TimeSpan _draftStartTime = new(20, 0, 0);

    public ObservableCollection<RegistrationRowViewModel> Registrations { get; } = [];

    public RelayCommand AssignRegistrationCommand { get; private set; } = null!;
    public RelayCommand AutoAssignRegistrationsCommand { get; private set; } = null!;
    public RelayCommand RemoveRegistrationCommand { get; private set; } = null!;

    public RegistrationRowViewModel? SelectedRegistration
    {
        get => _selectedRegistration;
        set
        {
            if (SetProperty(ref _selectedRegistration, value))
                RaiseRegistrationCommands();
        }
    }

    public string RegistrationStatus
    {
        get => _registrationStatus;
        private set => SetProperty(ref _registrationStatus, value);
    }

    private bool _registrationsExpanded;

    public bool HasRegistrations => Registrations.Count > 0;
    public string RegistrationPickerHint => HasRegistrations ? "Pick a player…" : "No sign-ups yet";

    /// <summary>The Grid's Registrations panel opens by itself when the first players sign up.</summary>
    public bool RegistrationsExpanded
    {
        get => _registrationsExpanded;
        set => SetProperty(ref _registrationsExpanded, value);
    }

    public string RegistrationsTitle => Registrations.Count == 0
        ? "Registrations"
        : $"Registrations ({Registrations.Count})";

    // ---- Event registration settings (EVENT & CONTENT tab) ----

    public bool RegistrationOpen
    {
        get => Preset.Registration.IsOpen;
        set
        {
            if (Preset.Registration.IsOpen == value)
                return;
            Preset.Registration.IsOpen = value;
            OnPropertyChanged();
        }
    }

    public DateTime? RegistrationDate
    {
        get => Preset.Registration.ScheduledStart?.LocalDateTime.Date;
        set
        {
            // Keep the chosen time when the date is cleared, so picking a new date restores it.
            _draftStartTime = RegistrationTimeOfDay;
            SetScheduledStart(value, _draftStartTime);
            OnPropertyChanged();
        }
    }

    /// <summary>Start time in 24-hour HH:mm, local time. Shows 20:00 until a time is chosen.</summary>
    public string RegistrationTime
    {
        get => RegistrationTimeOfDay.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        set
        {
            if (StartTimeText.TryParse(value, out var time))
            {
                _draftStartTime = time;
                if (RegistrationDate is { } date)
                    SetScheduledStart(date, time);
            }
            else
            {
                StatusText = $"Start time must be 24-hour HH:mm, for example 20:00 or 09:30 (got \"{value}\").";
            }
            OnPropertyChanged();
        }
    }


    public string RegistrationNote
    {
        get => Preset.Registration.Note;
        set
        {
            Preset.Registration.Note = (value ?? string.Empty).Length > 500 ? value![..500] : value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    private TimeSpan RegistrationTimeOfDay => Preset.Registration.ScheduledStart?.LocalDateTime.TimeOfDay ?? _draftStartTime;

    private void SetScheduledStart(DateTime? date, TimeSpan time)
        => Preset.Registration.ScheduledStart = date is { } day
            ? new DateTimeOffset(DateTime.SpecifyKind(day.Date + time, DateTimeKind.Local))
            : null;

    private void NotifyRegistrationSettings()
    {
        OnPropertyChanged(nameof(RegistrationOpen));
        OnPropertyChanged(nameof(RegistrationDate));
        OnPropertyChanged(nameof(RegistrationTime));
        OnPropertyChanged(nameof(RegistrationNote));
        RefreshRegistrations();
    }

    // ---- Wiring ----

    /// <summary>Connects the shared registration store; the app owns it so the Web GUI sees the same data.</summary>
    public void AttachRegistrations(PlayerRegistrationStore store)
    {
        _registrationStore = store;
        AssignRegistrationCommand = new RelayCommand(AssignSelectedRegistration,
            () => SelectedRegistration is not null && SelectedGridSlot is { Mode: not SlotMode.Spectator } && !IsFpsMode);
        AutoAssignRegistrationsCommand = new RelayCommand(AutoAssignRegistrations,
            () => Registrations.Count > 0 && !IsFpsMode);
        RemoveRegistrationCommand = new RelayCommand(RemoveSelectedRegistration, () => SelectedRegistration is not null);
        store.Changed += (_, presetId) => RunOnUi(() =>
        {
            if (presetId == Preset.Id)
                RefreshRegistrations();
        });
        PropertyChanged += OnRegistrationContextChanged;
        if (!_gridWatchAttached)
        {
            Grid.CollectionChanged += OnGridRowsChanged;
            foreach (var slot in Grid)
            {
                slot.PropertyChanged += OnGridSlotChanged;
                _teamByRow[slot] = slot.TeamName;
            }
            _gridWatchAttached = true;
        }
        RefreshRegistrations();
    }

    private void OnRegistrationContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Preset))
            NotifyRegistrationSettings();
        else if (e.PropertyName is nameof(SelectedGridSlot) or nameof(IsFpsMode))
            RaiseRegistrationCommands();
    }

    private void OnGridRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (GridSlotViewModel slot in e.OldItems ?? Array.Empty<GridSlotViewModel>())
        {
            slot.PropertyChanged -= OnGridSlotChanged;
            _teamByRow.Remove(slot);
        }
        foreach (GridSlotViewModel slot in e.NewItems ?? Array.Empty<GridSlotViewModel>())
        {
            slot.PropertyChanged += OnGridSlotChanged;
            _teamByRow[slot] = slot.TeamName;
        }
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _teamByRow.Clear();
            foreach (var slot in Grid)
            {
                slot.PropertyChanged -= OnGridSlotChanged;
                slot.PropertyChanged += OnGridSlotChanged;
                _teamByRow[slot] = slot.TeamName;
            }
        }
        RefreshRegistrations();
    }

    private void OnGridSlotChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GridSlotViewModel.TeamName) && sender is GridSlotViewModel slot)
        {
            EnforceOneCarPerTeam(slot);
            return;
        }
        if (e.PropertyName is nameof(GridSlotViewModel.DriverSteamIdsText) or nameof(GridSlotViewModel.SelectedCarId))
            RefreshRegistrations();
    }

    private bool IsDriverChangeRace => !IsFpsMode && Preset.Sessions.DriverSwapGraceMinutes > 0;

    private static TeamCarRow TeamRow(GridSlotViewModel slot)
        => new(slot.Index, slot.TeamName, slot.Mode == SlotMode.Spectator);

    /// <summary>
    /// With driver changes a team is the drivers sharing one car, so a team name that another car
    /// already has is refused (the bots' "Race Control" label excepted).
    /// </summary>
    private void EnforceOneCarPerTeam(GridSlotViewModel slot)
    {
        string previous = _teamByRow.GetValueOrDefault(slot, TeamCars.BotTeamName);
        if (slot.TeamName == previous)
            return;
        if (IsDriverChangeRace && TeamCars.ConflictFor(TeamRow(slot), Grid.Select(TeamRow)) is int car)
        {
            StatusText = $"Team \"{slot.TeamName.Trim()}\" already has car #{car}. With driver changes a team shares one car: "
                         + $"put this driver's Steam ID on car #{car} instead, or use another team name.";
            // Undo after the grid has finished committing the edit, or the cell keeps the refused name.
            Application.Current?.Dispatcher.BeginInvoke(() => slot.TeamName = previous, DispatcherPriority.Background);
            return;
        }
        _teamByRow[slot] = slot.TeamName;
    }

    private void RaiseRegistrationCommands()
    {
        AssignRegistrationCommand?.RaiseCanExecuteChanged();
        AutoAssignRegistrationsCommand?.RaiseCanExecuteChanged();
        RemoveRegistrationCommand?.RaiseCanExecuteChanged();
    }

    // ---- Web GUI ----

    /// <summary>The event for the Web GUI's Players page. Racing events only: FPS slots can't be reserved.</summary>
    public PlayerEventInfo? GetPlayerEvent()
    {
        if (IsFpsMode)
            return null;
        var cars = Grid
            .Where(slot => slot.Mode != SlotMode.Spectator && slot.SelectedCar is not null)
            .Select(slot => slot.SelectedCar!)
            .DistinctBy(car => car.Id, StringComparer.OrdinalIgnoreCase)
            .Select(car => new PlayerEventCar(car.Id, string.IsNullOrWhiteSpace(car.Name) ? car.Id : car.Name))
            .OrderBy(car => car.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        string track = SelectedTrack?.Name is { Length: > 0 } name ? name : Preset.TrackId;
        string layout = SelectedTrack?.LayoutName ?? Preset.TrackLayoutId;
        return new PlayerEventInfo(Preset.Id, EventTitle, track, layout, Preset.Registration.IsOpen,
            Preset.Registration.ScheduledStart, Preset.Registration.Note, cars,
            Preset.Sessions.DriverSwapGraceMinutes > 0);
    }

    // ---- Registrations panel (GRID tab) ----

    private void RefreshRegistrations()
    {
        if (_registrationStore is null)
            return;
        string? selectedId = SelectedRegistration?.SteamId;
        var carNames = Cars.ToDictionary(car => car.Id, car => car.Name, StringComparer.OrdinalIgnoreCase);
        var assigned = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var slot in Grid)
        {
            foreach (var id in SteamIds.Parse(slot.DriverSteamIdsText))
                assigned.TryAdd(id, $"#{slot.Index}  •  {slot.SelectedCar?.Name ?? slot.SelectedCarId}");
        }

        var rows = _registrationStore.List(Preset.Id)
            .OrderBy(registration => registration.RegisteredAt)
            .Select(registration => new RegistrationRowViewModel(registration,
                string.IsNullOrEmpty(registration.PreferredCarId)
                    ? "No preference"
                    : carNames.GetValueOrDefault(registration.PreferredCarId, registration.PreferredCarId),
                assigned.GetValueOrDefault(registration.SteamId, "—")))
            .ToArray();

        bool hadRegistrations = Registrations.Count > 0;
        Registrations.Clear();
        foreach (var row in rows)
            Registrations.Add(row);
        if (!hadRegistrations && rows.Length > 0)
            RegistrationsExpanded = true;
        OnPropertyChanged(nameof(HasRegistrations));
        OnPropertyChanged(nameof(RegistrationPickerHint));
        SelectedRegistration = Registrations.FirstOrDefault(row => row.SteamId == selectedId);
        int unassigned = rows.Count(row => row.AssignedTo == "—");
        RegistrationStatus = rows.Length == 0
            ? Preset.Registration.IsOpen
                ? "Registration is open. Players sign in through Steam on the Web GUI's Players page."
                : "No registrations. Open registration under Event & Content to let players sign up in the Web GUI."
            : unassigned == 0
                ? "Every registered player has a car."
                : $"{unassigned} registered player(s) don't have a car yet.";
        OnPropertyChanged(nameof(RegistrationsTitle));
        RaiseRegistrationCommands();
    }

    private void AssignSelectedRegistration()
    {
        if (SelectedRegistration is { } row && SelectedGridSlot is { } slot)
            AssignRegistrationToSlot(slot, row);
    }

    /// <summary>Puts a registered player (and their teammates) in this grid row.</summary>
    public void AssignRegistrationToSlot(GridSlotViewModel slot, RegistrationRowViewModel row)
    {
        if (_registrationStore is null || IsFpsMode)
            return;
        if (slot.Mode == SlotMode.Spectator)
        {
            StatusText = "Spectator rows can't take a driver. Pick a racing row.";
            return;
        }
        var all = _registrationStore.List(Preset.Id);
        var groups = RegistrationAssigner.ExpandToTeams([row.Registration], all);
        if (groups.Count == 0)
            return;
        ApplyAssignment(RegistrationAssigner.ToSlot(slot.Index, groups[0]));
        StatusText = groups[0].Count > 1
            ? $"Team {groups[0][0].TeamName} ({groups[0].Count} drivers) assigned to car #{slot.Index}."
            : $"{groups[0][0].DisplayName} assigned to car #{slot.Index}.";
    }

    private void AutoAssignRegistrations()
    {
        if (_registrationStore is null)
            return;
        var slots = Grid.Select(slot => new AssignableSlot(slot.Index, slot.SelectedCarId ?? string.Empty,
            SteamIds.Parse(slot.DriverSteamIdsText), slot.Mode == SlotMode.Spectator)).ToArray();
        var result = RegistrationAssigner.AutoAssign(slots, _registrationStore.List(Preset.Id));
        foreach (var assignment in result.Assignments)
            ApplyAssignment(assignment);
        StatusText = result.Unplaced.Count == 0
            ? $"Assigned {result.Assignments.Count} car(s) from registrations."
            : $"Assigned {result.Assignments.Count} car(s); {result.Unplaced.Count} player/team(s) need a free row. Add rows to the grid and run auto-assign again.";
    }

    private void ApplyAssignment(SlotAssignment assignment)
    {
        var target = Grid.FirstOrDefault(slot => slot.Index == assignment.SlotIndex);
        if (target is null || target.Mode == SlotMode.Spectator)
            return;
        // A driver belongs to one car: take them off any other row first. A row left with nobody
        // gets its default bot name and team back instead of keeping the moved player's name.
        foreach (var other in Grid.Where(slot => !ReferenceEquals(slot, target)))
        {
            var ids = SteamIds.Parse(other.DriverSteamIdsText);
            if (ids.RemoveAll(assignment.SteamIds.Contains) == 0)
                continue;
            other.DriverSteamIdsText = SteamIds.Format(ids);
            if (ids.Count == 0)
            {
                if (other.DriverName == assignment.DriverName)
                    other.DriverName = $"Bot {other.Index:00}";
                if (assignment.TeamName.Length > 0 && other.TeamName == assignment.TeamName)
                    other.TeamName = "Race Control";
            }
        }
        target.DriverSteamIdsText = SteamIds.Format(assignment.SteamIds);
        target.DriverName = assignment.DriverName;
        if (assignment.TeamName.Length > 0)
            target.TeamName = assignment.TeamName;
        Validate();
    }

    private void RemoveSelectedRegistration()
    {
        if (_registrationStore is null || SelectedRegistration is not { } row)
            return;
        if (MessageBox.Show($"Remove {row.DisplayName}'s registration? They can register again while registration is open.",
                "Registrations", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        _registrationStore.Remove(Preset.Id, row.SteamId);
        StatusText = $"Removed {row.DisplayName}'s registration. Their Steam ID stays on any car it was assigned to.";
    }
}
