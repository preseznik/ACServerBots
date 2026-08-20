namespace AssettoServer.RaceControl.Core.Models;

public sealed class RaceControlPreset
{
    public int SchemaVersion { get; set; } = 1;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New LAN race";
    public string ServerName { get; set; } = "AssettoServer LAN Race";
    public string AssettoCorsaRoot { get; set; } = @"C:\Program Files (x86)\Steam\steamapps\common\assettocorsa";
    public string ServerPayloadPath { get; set; } = string.Empty;
    public string TrackId { get; set; } = "magione";
    public string TrackLayoutId { get; set; } = string.Empty;
    public List<GridSlotPreset> Grid { get; set; } = [];
    public SessionOptions Sessions { get; set; } = new();
    public RuleOptions Rules { get; set; } = new();
    public ConditionOptions Conditions { get; set; } = new();
    public BotOptions Bots { get; set; } = new();
    public NetworkOptions Network { get; set; } = new();

    public static RaceControlPreset CreateDefault(string acRoot, string serverPayloadPath)
    {
        return new RaceControlPreset
        {
            AssettoCorsaRoot = acRoot,
            ServerPayloadPath = serverPayloadPath,
            Grid =
            [
                new() { DriverName = "Bot 01", Mode = SlotMode.Auto },
                new() { DriverName = "Bot 02", Mode = SlotMode.Auto },
            ],
        };
    }
}

public sealed class GridSlotPreset
{
    public string CarId { get; set; } = "bmw_m3_e30";
    public string SkinId { get; set; } = string.Empty;
    public string DriverName { get; set; } = "Bot";
    public string TeamName { get; set; } = "Race Control";
    public string NationCode { get; set; } = string.Empty;
    public int BallastKg { get; set; }
    public int RestrictorPercent { get; set; }
    public SlotMode Mode { get; set; } = SlotMode.Auto;
}

public enum SlotMode
{
    Auto,
    Fixed,
    None,
}

public sealed class SessionOptions
{
    public bool PracticeEnabled { get; set; } = true;
    public int PracticeMinutes { get; set; } = 10;
    public bool QualifyingEnabled { get; set; }
    public int QualifyingMinutes { get; set; } = 10;
    public int RaceLaps { get; set; } = 3;
    public int RaceOverTimeSeconds { get; set; } = 60;
    public int ResultScreenSeconds { get; set; } = 30;
    public bool ReverseGrid { get; set; }
}

public sealed class RuleOptions
{
    public int FuelRatePercent { get; set; }
    public int DamageRatePercent { get; set; }
    public int TyreWearRatePercent { get; set; }
    public int AllowedTyresOut { get; set; } = 2;
    public int AbsAllowed { get; set; } = 1;
    public int TractionControlAllowed { get; set; } = 1;
    public bool StabilityControlAllowed { get; set; }
    public bool AutoClutchAllowed { get; set; } = true;
    public bool TyreBlanketsAllowed { get; set; } = true;
    public bool VirtualMirrorAllowed { get; set; } = true;
}

public sealed class ConditionOptions
{
    public string WeatherId { get; set; } = "3_clear";
    public int SunAngleDegrees { get; set; } = 16;
    public int AmbientTemperatureCelsius { get; set; } = 22;
    public int RoadTemperatureCelsius { get; set; } = 28;
    public int WindMinKmh { get; set; }
    public int WindMaxKmh { get; set; }
    public int WindDirectionDegrees { get; set; }
    public int StartingGripPercent { get; set; } = 98;
    public int GripRandomnessPercent { get; set; } = 2;
    public int GripTransferPercent { get; set; } = 80;
    public int LapsPerGripIncrease { get; set; } = 10;
}

public sealed class BotOptions
{
    public bool Enabled { get; set; } = true;
    public double Difficulty { get; set; } = 0.75;
    public double Aggression { get; set; } = 0.50;
    public string NamePrefix { get; set; } = "Bot";
    public int UpdateHz { get; set; } = 60;
    public PhysicsFidelity PhysicsFidelity { get; set; } = PhysicsFidelity.Balanced;
    public double SurfaceFriction { get; set; } = 1.0;
    public bool AllowMidRaceTakeover { get; set; } = true;
    public bool RestartWhenFirstHumanConnects { get; set; } = true;
    public int StartSplinePointId { get; set; }
    public double GridSpacingMeters { get; set; } = 9;
}

public enum PhysicsFidelity
{
    Efficient,
    Balanced,
    High,
}

public sealed class NetworkOptions
{
    public string BindAddress { get; set; } = "127.0.0.1";
    public ushort TcpPort { get; set; } = 9600;
    public ushort UdpPort { get; set; } = 9600;
    public ushort HttpPort { get; set; } = 8081;
    public string JoinPassword { get; set; } = string.Empty;
    public string AdminPassword { get; set; } = string.Empty;
    public bool LanOnly { get; set; } = true;
}
