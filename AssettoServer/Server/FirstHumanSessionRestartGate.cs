namespace AssettoServer.Server;

internal sealed class FirstHumanSessionRestartGate
{
    public bool IsArmed { get; private set; } = true;

    public bool TrySchedule(bool enabled, int connectedHumanCount, bool remainingSlotsAreBots)
    {
        if (!enabled || !IsArmed || connectedHumanCount != 1 || !remainingSlotsAreBots)
            return false;

        IsArmed = false;
        return true;
    }

    public void UpdateConnectedHumanCount(int connectedHumanCount)
    {
        if (connectedHumanCount == 0)
            IsArmed = true;
    }
}
