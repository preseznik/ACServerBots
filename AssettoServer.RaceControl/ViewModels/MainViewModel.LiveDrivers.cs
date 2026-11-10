using AssettoServer.RaceControl.Core.Runtime;
using AssettoServer.RaceControl.Core.Staging;

namespace AssettoServer.RaceControl.ViewModels;

/// <summary>Shows who is actually driving each Grid row while the server runs.</summary>
public sealed partial class MainViewModel
{
    // Grid rows in server session id order, captured when the instance was staged.
    private IReadOnlyList<GridSlotViewModel> _liveSlotRows = [];

    private void CaptureLiveSlotRows(StagedInstance instance)
    {
        ClearLiveDriverNames();
        var order = instance.SourceSlotIndexes ?? [];
        // Staging syncs the preset from Grid in order, so preset row i is Grid[i] at this moment.
        _liveSlotRows = order.Where(index => index >= 0 && index < Grid.Count).Select(index => Grid[index]).ToArray();
    }

    private void UpdateLiveDriverNames(LiveRaceSnapshot snapshot)
    {
        if (_liveSlotRows.Count == 0)
            return;
        var humans = snapshot.ServerRunning
            ? snapshot.Cars.Where(car => !car.IsBot && car.IsConnected && !string.IsNullOrWhiteSpace(car.Name))
                .GroupBy(car => car.SessionId)
                .ToDictionary(group => group.Key, group => group.First().Name)
            : [];
        for (int sessionId = 0; sessionId < _liveSlotRows.Count; sessionId++)
            _liveSlotRows[sessionId].LiveDriverName = humans.GetValueOrDefault(sessionId);
    }

    private void ClearLiveDriverNames()
    {
        foreach (var row in _liveSlotRows)
            row.LiveDriverName = null;
    }
}
