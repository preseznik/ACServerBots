using System.Collections.ObjectModel;
using AssettoServer.RaceControl.Core.Models;
using AssettoServer.RaceControl.Infrastructure;

namespace AssettoServer.RaceControl.ViewModels;

public sealed class GridSlotViewModel : ObservableObject
{
    private readonly IReadOnlyList<AcCar> _cars;
    private int _index;
    private AcCar? _selectedCar;
    private AcSkin? _selectedSkin;
    private ObservableCollection<AcSkin> _skins = [];
    private string _driverName;
    private string _teamName;
    private string _nationCode;
    private int _ballastKg;
    private int _restrictorPercent;
    private double? _difficulty;
    private double? _aggression;
    private SlotMode _mode;

    public GridSlotViewModel(GridSlotPreset slot, IReadOnlyList<AcCar> cars, int index)
    {
        _cars = cars;
        _index = index;
        _driverName = slot.DriverName;
        _teamName = slot.TeamName;
        _nationCode = slot.NationCode;
        _ballastKg = slot.BallastKg;
        _restrictorPercent = slot.RestrictorPercent;
        _difficulty = slot.Difficulty;
        _aggression = slot.Aggression;
        _mode = slot.Mode;
        _selectedCar = cars.FirstOrDefault(car => car.Id.Equals(slot.CarId, StringComparison.OrdinalIgnoreCase)) ?? cars.FirstOrDefault();
        RefreshSkins(slot.SkinId);
    }

    public int Index
    {
        get => _index;
        set => SetProperty(ref _index, value);
    }

    public IReadOnlyList<AcCar> Cars => _cars;
    public ObservableCollection<AcSkin> Skins
    {
        get => _skins;
        private set => SetProperty(ref _skins, value);
    }

    public AcCar? SelectedCar
    {
        get => _selectedCar;
        set
        {
            if (SetProperty(ref _selectedCar, value))
            {
                RefreshSkins(null);
                OnPropertyChanged(nameof(SelectedCarId));
                OnPropertyChanged(nameof(CarDetails));
            }
        }
    }

    public string? SelectedCarId
    {
        get => SelectedCar?.Id;
        set => SelectedCar = _cars.FirstOrDefault(car => car.Id.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    public AcSkin? SelectedSkin
    {
        get => _selectedSkin;
        set
        {
            if (SetProperty(ref _selectedSkin, value))
            {
                OnPropertyChanged(nameof(SelectedSkinId));
            }
        }
    }

    public string? SelectedSkinId
    {
        get => SelectedSkin?.Id;
        set => SelectedSkin = Skins.FirstOrDefault(skin => skin.Id.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    public string DriverName
    {
        get => _driverName;
        set => SetProperty(ref _driverName, value);
    }

    public string TeamName
    {
        get => _teamName;
        set => SetProperty(ref _teamName, value);
    }

    public string NationCode
    {
        get => _nationCode;
        set => SetProperty(ref _nationCode, value);
    }

    public int BallastKg
    {
        get => _ballastKg;
        set => SetProperty(ref _ballastKg, value);
    }

    public int RestrictorPercent
    {
        get => _restrictorPercent;
        set => SetProperty(ref _restrictorPercent, value);
    }

    public double? Difficulty
    {
        get => _difficulty;
        set => SetProperty(ref _difficulty, value);
    }

    public double? Aggression
    {
        get => _aggression;
        set => SetProperty(ref _aggression, value);
    }

    public SlotMode Mode
    {
        get => _mode;
        set => SetProperty(ref _mode, value);
    }

    public string CarDetails => SelectedCar is null
        ? string.Empty
        : $"{SelectedCar.MassKg:0} kg  •  {SelectedCar.PowerHp:0} hp  •  {SelectedCar.TopSpeedKmh:0} km/h";

    public GridSlotPreset ToPreset() => new()
    {
        CarId = SelectedCar?.Id ?? string.Empty,
        SkinId = SelectedSkin?.Id ?? string.Empty,
        DriverName = DriverName,
        TeamName = TeamName,
        NationCode = NationCode,
        BallastKg = BallastKg,
        RestrictorPercent = RestrictorPercent,
        Difficulty = Difficulty,
        Aggression = Aggression,
        Mode = Mode,
    };

    private void RefreshSkins(string? selectedSkinId)
    {
        Skins = _selectedCar is null
            ? []
            : new ObservableCollection<AcSkin>(_selectedCar.Skins);
        SelectedSkin = Skins.FirstOrDefault(skin => skin.Id.Equals(selectedSkinId, StringComparison.OrdinalIgnoreCase))
            ?? Skins.FirstOrDefault();
    }
}
