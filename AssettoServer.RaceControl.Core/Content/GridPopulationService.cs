using AssettoServer.RaceControl.Core.Models;

namespace AssettoServer.RaceControl.Core.Content;

public enum GridPopulationCategory
{
    Any,
    Class,
    MaximumHorsepower,
    ModelYear,
    MaximumPowerToWeight,
}

public sealed record GridPopulationRequest(
    int Count,
    GridPopulationCategory Category,
    string? ClassName = null,
    double? MaximumHorsepower = null,
    int? ModelYear = null,
    double? MaximumPowerToWeightHpPerTonne = null,
    string NamePrefix = "Bot");

public sealed record GridPopulationResult(
    IReadOnlyList<GridSlotPreset> Slots,
    int EligibleCarCount);

public sealed class GridPopulationService
{
    public GridPopulationResult Populate(AcContentCatalog catalog, GridPopulationRequest request)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (request.Count is < 1 or > 254)
            throw new ArgumentOutOfRangeException(nameof(request), "Grid size must be between 1 and 254.");

        ValidateCriterion(request);
        var eligible = catalog.Cars
            .Where(IsBotCapable)
            .Where(car => Matches(car, request))
            .OrderBy(car => car.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(car => car.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (eligible.Length == 0)
            return new GridPopulationResult([], 0);

        var slots = new List<GridSlotPreset>(request.Count);
        for (int index = 0; index < request.Count; index++)
        {
            // Sample across the full sorted catalog when fewer slots than cars are requested.
            // When more are requested, cycle models and then their installed skins.
            int carIndex = request.Count <= eligible.Length
                ? (int)((long)index * eligible.Length / request.Count)
                : index % eligible.Length;
            var car = eligible[carIndex];
            int repeat = index / eligible.Length;
            var skin = car.Skins[repeat % car.Skins.Count];
            slots.Add(new GridSlotPreset
            {
                CarId = car.Id,
                SkinId = skin.Id,
                DriverName = $"{NormalizePrefix(request.NamePrefix)} {index + 1:00}",
                TeamName = "Race Control",
                Mode = SlotMode.Auto,
            });
        }

        return new GridPopulationResult(slots, eligible.Length);
    }

    private static bool IsBotCapable(AcCar car) =>
        car.HasData && car.HasCollider && car.Skins.Count > 0;

    private static bool Matches(AcCar car, GridPopulationRequest request) => request.Category switch
    {
        GridPopulationCategory.Any => true,
        GridPopulationCategory.Class => car.ClassName.Equals(request.ClassName,
            StringComparison.OrdinalIgnoreCase),
        GridPopulationCategory.MaximumHorsepower => car.PowerHp is > 0
            && car.PowerHp <= request.MaximumHorsepower,
        GridPopulationCategory.ModelYear => car.Year == request.ModelYear,
        GridPopulationCategory.MaximumPowerToWeight => car.PowerToWeightHpPerTonne is > 0
            && car.PowerToWeightHpPerTonne <= request.MaximumPowerToWeightHpPerTonne,
        _ => false,
    };

    private static void ValidateCriterion(GridPopulationRequest request)
    {
        switch (request.Category)
        {
            case GridPopulationCategory.Class when string.IsNullOrWhiteSpace(request.ClassName):
                throw new ArgumentException("Choose a car class.", nameof(request));
            case GridPopulationCategory.MaximumHorsepower when request.MaximumHorsepower is not > 0:
                throw new ArgumentException("Maximum horsepower must be positive.", nameof(request));
            case GridPopulationCategory.ModelYear when request.ModelYear is not (>= 1886 and <= 2200):
                throw new ArgumentException("Model year must be between 1886 and 2200.", nameof(request));
            case GridPopulationCategory.MaximumPowerToWeight when request.MaximumPowerToWeightHpPerTonne is not > 0:
                throw new ArgumentException("Maximum power-to-weight must be positive.", nameof(request));
        }
    }

    private static string NormalizePrefix(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Bot" : value.Trim();
}
