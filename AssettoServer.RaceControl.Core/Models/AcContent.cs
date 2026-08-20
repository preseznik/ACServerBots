namespace AssettoServer.RaceControl.Core.Models;

public sealed record AcContentCatalog(
    IReadOnlyList<AcCar> Cars,
    IReadOnlyList<AcTrackLayout> Tracks,
    IReadOnlyList<AcWeather> Weather,
    DateTimeOffset ScannedAt);

public sealed record AcCar(
    string Id,
    string Name,
    string Brand,
    string ClassName,
    string Country,
    IReadOnlyList<string> Tags,
    string RootPath,
    string? BadgePath,
    IReadOnlyList<AcSkin> Skins,
    bool HasData,
    bool HasCollider,
    string? DataAcdPath,
    string? ColliderPath,
    double? MassKg,
    double? PowerHp,
    double? TorqueNm,
    double? TopSpeedKmh)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Brand) ? Name : $"{Brand} {Name}";
}

public sealed record AcSkin(string Id, string Name, string? PreviewPath, string? LiveryPath);

public sealed record AcTrackLayout(
    string TrackId,
    string LayoutId,
    string Name,
    string LayoutName,
    string Country,
    string City,
    int PitBoxes,
    string RootPath,
    string? UiPath,
    string? PreviewPath,
    string? OutlinePath,
    string ModelsIniPath,
    string FastLanePath)
{
    public string Key => string.IsNullOrEmpty(LayoutId) ? TrackId : $"{TrackId}/{LayoutId}";
    public string DisplayName => string.IsNullOrWhiteSpace(LayoutName) ? Name : $"{Name} — {LayoutName}";
    public bool HasModels => File.Exists(ModelsIniPath);
    public bool HasFastLane => File.Exists(FastLanePath);
}

public sealed record AcWeather(string Id, string Name, string RootPath, string? PreviewPath, int? WeatherFxType = null);
