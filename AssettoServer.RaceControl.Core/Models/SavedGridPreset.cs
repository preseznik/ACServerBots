namespace AssettoServer.RaceControl.Core.Models;

public sealed class SavedGridPreset
{
    public int SchemaVersion { get; set; } = 1;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Saved grid";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public List<GridSlotPreset> Slots { get; set; } = [];
}

public sealed record SavedGridSummary(Guid Id, string Name, string Path, DateTimeOffset UpdatedAt);
