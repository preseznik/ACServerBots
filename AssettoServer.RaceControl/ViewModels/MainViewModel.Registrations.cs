using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
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
            SetScheduledStart(value, RegistrationTimeOfDay);
            OnPropertyChanged();
        }
    }

    /// <summary>Start time as HH:mm, local time.</summary>
    public string RegistrationTime
    {
        get => Preset.Registration.ScheduledStart?.LocalDateTime.ToString("HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
        set
        {
            if (TimeSpan.TryParseExact(value?.Trim(), [@"h\:mm", @"hh\:mm"], CultureInfo.InvariantCulture, out var time))
                SetScheduledStart(RegistrationDate ?? DateTime.Today, time);
            OnPropertyChanged();
            OnPropertyChanged(nameof(RegistrationDate));
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

    private TimeSpan RegistrationTimeOfDay => Preset.Registration.ScheduledStart?.LocalDateTime.TimeOfDay ?? new TimeSpan(20, 0, 0);

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
                slot.PropertyChanged += OnGridSlotChanged;
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
            slot.PropertyChanged -= OnGridSlotChanged;
        foreach (GridSlotViewModel slot in e.NewItems ?? Array.Empty<GridSlotViewModel>())
            slot.PropertyChanged += OnGridSlotChanged;
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var slot in Grid)
            {
                slot.PropertyChanged -= OnGridSlotChanged;
                slot.PropertyChanged += OnGridSlotChanged;
            }
        }
        RefreshRegistrations();
    }

    private void OnGridSlotChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GridSlotViewModel.DriverSteamIdsText) or nameof(GridSlotViewModel.SelectedCarId))
            RefreshRegistrations();
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

        Registrations.Clear();
        foreach (var row in rows)
            Registrations.Add(row);
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
        if (_registrationStore is null || SelectedRegistration is null || SelectedGridSlot is not { } slot)
            return;
        var all = _registrationStore.List(Preset.Id);
        var groups = RegistrationAssigner.ExpandToTeams([SelectedRegistration.Registration], all);
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
        // A driver belongs to one car: take them off any other row first.
        foreach (var other in Grid.Where(slot => !ReferenceEquals(slot, target)))
        {
            var ids = SteamIds.Parse(other.DriverSteamIdsText);
            if (ids.RemoveAll(assignment.SteamIds.Contains) > 0)
                other.DriverSteamIdsText = SteamIds.Format(ids);
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
