using System.IO;
using AssettoServer.RaceControl.Core.Models;
using AssettoServer.RaceControl.Core.Web;

namespace AssettoServer.RaceControl.ViewModels;

/// <summary>The Web GUI's Overview page: event, track and car pictures, teams and the entry list.</summary>
public sealed partial class MainViewModel
{
    private const string TrackImageKey = "track";
    private const string TrackOutlineKey = "outline";

    public WebOverview GetWebOverview()
    {
        var rows = Grid.Where(slot => slot.Mode != SlotMode.Spectator).Take(254).ToArray();
        var entries = rows.Select(slot => new WebOverviewEntry(
                slot.Index,
                slot.DriverDisplayName,
                slot.TeamName,
                slot.SelectedCar?.DisplayName ?? slot.SelectedCarId ?? string.Empty,
                CarImageKey(slot),
                slot.HasLiveDriver,
                RoleLabel(slot)))
            .ToArray();
        var cars = rows
            .Where(slot => slot.SelectedCar is not null)
            .GroupBy(slot => slot.SelectedCar!.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => new WebOverviewCar(group.First().SelectedCar!.DisplayName, CarImageKey(group.First()), group.Count()))
            .OrderByDescending(car => car.Count)
            .ThenBy(car => car.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        var teams = rows
            .Where(slot => !string.IsNullOrWhiteSpace(slot.TeamName))
            .GroupBy(slot => slot.TeamName.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new WebOverviewTeam(group.Key,
                group.Select(slot => slot.DriverDisplayName).Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.CurrentCultureIgnoreCase).ToArray(),
                group.Count()))
            .OrderByDescending(team => team.Cars)
            .ThenBy(team => team.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        var track = SelectedTrack;
        return new WebOverview(
            EventTitle,
            IsFpsMode ? "FPS" : "Racing",
            track?.Name ?? Preset.TrackId,
            track?.LayoutName ?? Preset.TrackLayoutId,
            track?.Country ?? string.Empty,
            ExistingFile(track?.PreviewPath) is null ? null : TrackImageKey,
            ExistingFile(track?.OutlinePath) is null ? null : TrackOutlineKey,
            cars,
            teams,
            entries);
    }

    /// <summary>Resolves an overview image key to a file from the scanned content only.</summary>
    public string? GetWebMediaPath(string key)
    {
        if (key == TrackImageKey)
            return ExistingFile(SelectedTrack?.PreviewPath);
        if (key == TrackOutlineKey)
            return ExistingFile(SelectedTrack?.OutlinePath);

        var parts = key.Split(':');
        if (parts is not ["car", var carId, var skinId])
            return null;
        var car = Cars.FirstOrDefault(candidate => candidate.Id.Equals(carId, StringComparison.OrdinalIgnoreCase));
        if (car is null)
            return null;
        var skin = car.Skins.FirstOrDefault(candidate => candidate.Id.Equals(skinId, StringComparison.OrdinalIgnoreCase));
        return ExistingFile(skin?.PreviewPath)
               ?? car.Skins.Select(candidate => ExistingFile(candidate.PreviewPath)).FirstOrDefault(path => path is not null)
               ?? ExistingFile(car.BadgePath);
    }

    private static string? CarImageKey(GridSlotViewModel slot)
        => slot.SelectedCar is { } car ? $"car:{car.Id}:{slot.SelectedSkinId ?? string.Empty}" : null;

    private static string RoleLabel(GridSlotViewModel slot)
    {
        if (slot.IsReserved)
            return "Reserved";
        return slot.Mode switch
        {
            SlotMode.Auto => "Bot until a player joins",
            SlotMode.Fixed => "Bot",
            SlotMode.None => "Player",
            _ => slot.Mode.ToString(),
        };
    }

    private static string? ExistingFile(string? path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;
}
