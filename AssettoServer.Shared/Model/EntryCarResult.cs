
namespace AssettoServer.Shared.Model;

public class EntryCarResult
{
    public ulong Guid { get; set; } = 0;
    public string Name { get; set; } = "";
    public string Team { get; set; } = "";
    public string NationCode { get; set; } = "";
    public uint BestLap { get; set; } = 999999999;
    public uint NumLaps { get; set; } = 0;
    public uint TotalTime { get; set; } = 0;
    public uint LastLap { get; set; } = 999999999;
    public bool HasCompletedLastLap { get; set; } = false;
    public uint RacePos { get; set; } = 0;
    public bool IsDnf { get; set; }
    public bool IsSpectator { get; set; }

    public EntryCarResult(IClient? client)
    {
        if (client == null) return;
        
        Guid = client.Guid;
        Name = client.Name ?? "";
        Team = client.Team ?? "";
        NationCode = client.NationCode ?? "";
    }

    public EntryCarResult(ulong guid, string name, string team = "", string nationCode = "")
    {
        Guid = guid;
        Name = name;
        Team = team;
        NationCode = nationCode;
    }
}
