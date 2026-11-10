using System.Globalization;
using System.Text;

namespace AssettoServer.RaceControl.Core.Runtime;

/// <summary>
/// Final classification of a real race, saved by the server under race-control-live/results.
/// </summary>
public sealed class RaceResultsReport
{
    public int SchemaVersion { get; set; }
    public string Status { get; set; } = string.Empty;
    public string SessionName { get; set; } = string.Empty;
    public string Track { get; set; } = string.Empty;
    public string Layout { get; set; } = string.Empty;
    public int TargetLaps { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public List<RaceResultsRow> Results { get; set; } = [];

    public string Title => Status switch
    {
        "finished" => "RACE RESULTS",
        "stopped" => "RACE STOPPED  •  RESULTS SO FAR",
        "server_stopped" => "SERVER STOPPED  •  RESULTS SO FAR",
        _ => "RACE ENDED EARLY  •  RESULTS SO FAR",
    };

    public string Overview
    {
        get
        {
            string track = string.IsNullOrWhiteSpace(Layout) ? Track : $"{Track} / {Layout}";
            var parts = new List<string> { track, $"{Results.Count} cars" };
            if (TargetLaps > 0)
                parts.Add($"{TargetLaps} laps");
            parts.Add(StartedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
            return string.Join("  •  ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        }
    }

    /// <summary>Fills in each row's gap to the leader. Call after loading.</summary>
    public RaceResultsReport Classify()
    {
        Results = Results.OrderBy(row => row.Position).ToList();
        var leader = Results.FirstOrDefault();
        foreach (var row in Results)
        {
            row.Report = this;
            row.Gap = FormatGap(leader, row);
        }
        return this;
    }

    /// <summary>A readable list for pasting into a chat.</summary>
    public string ToText()
    {
        var text = new StringBuilder();
        text.AppendLine($"{Title} – {Overview}");
        foreach (var row in Results)
        {
            text.Append(CultureInfo.InvariantCulture, $"{row.Position}. {row.Driver}");
            if (!string.IsNullOrWhiteSpace(row.Model))
                text.Append($" ({row.Model})");
            text.Append(CultureInfo.InvariantCulture, $" – {row.Laps} laps");
            if (row.Outcome == "FINISHED")
                text.Append($" – {row.Time}");
            if (row.Gap != "—")
                text.Append($" ({row.Gap})");
            if (row.BestLap != "—")
                text.Append($" – best {row.BestLap}");
            if (row.Outcome != "FINISHED")
                text.Append($" – {row.Outcome}");
            text.AppendLine();
        }
        return text.ToString();
    }

    public string ToCsv()
    {
        var csv = new StringBuilder();
        csv.AppendLine("Position,Driver,Drivers,Team,Car,Bot,Laps,Total time,Gap,Best lap,Result");
        foreach (var row in Results)
        {
            csv.AppendLine(string.Join(",",
                row.Position.ToString(CultureInfo.InvariantCulture),
                Csv(row.Name),
                Csv(string.Join(" / ", row.Drivers)),
                Csv(row.Team),
                Csv(row.Model),
                row.IsBot ? "yes" : "no",
                row.Laps.ToString(CultureInfo.InvariantCulture),
                Csv(row.Time),
                Csv(row.Gap),
                Csv(row.BestLap),
                Csv(row.Outcome)));
        }
        return csv.ToString();
    }

    public string SuggestedFileName
        => $"race-results-{StartedAt.ToLocalTime():yyyy-MM-dd-HHmm}.csv";

    internal static string FormatLapTime(long milliseconds)
    {
        if (milliseconds <= 0 || milliseconds >= 999_999_999)
            return "—";
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        return duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss\.fff", CultureInfo.InvariantCulture)
            : duration.ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture);
    }

    private static string FormatGap(RaceResultsRow? leader, RaceResultsRow row)
    {
        if (leader is null || ReferenceEquals(leader, row) || row.Dnf && row.Laps == 0)
            return "—";
        uint lapsBehind = leader.Laps > row.Laps ? leader.Laps - row.Laps : 0;
        if (lapsBehind > 0)
            return lapsBehind == 1 ? "+1 lap" : $"+{lapsBehind} laps";
        if (row.Finished && leader.Finished && row.TotalTimeMilliseconds >= leader.TotalTimeMilliseconds)
            return "+" + FormatLapTime(Math.Max(1, (long)row.TotalTimeMilliseconds - leader.TotalTimeMilliseconds));
        return "—";
    }

    private static string Csv(string value)
        => value.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
}

public sealed class RaceResultsRow
{
    public int SessionId { get; set; }
    public int Position { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<string> Drivers { get; set; } = [];
    public string Team { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public bool IsBot { get; set; }
    public uint Laps { get; set; }
    public uint TotalTimeMilliseconds { get; set; }
    public uint BestLapMilliseconds { get; set; }
    public bool Finished { get; set; }
    public bool Dnf { get; set; }

    internal RaceResultsReport? Report { get; set; }

    /// <summary>Every driver who shared the car, or the single driver's name.</summary>
    public string Driver => Drivers.Count > 1 ? string.Join(" / ", Drivers) : Name;
    public string DriverAndCar => string.IsNullOrWhiteSpace(Model) ? Driver : $"{Driver}  •  {Model}";
    public string Time => RaceResultsReport.FormatLapTime(TotalTimeMilliseconds);
    public string BestLap => RaceResultsReport.FormatLapTime(BestLapMilliseconds);
    public string Gap { get; set; } = "—";

    public string Outcome => Finished ? "FINISHED"
        : Dnf ? "DNF"
        : Report?.Status == "finished" ? "RUNNING"
        : "NOT FINISHED";
}

public sealed record RaceResultsFile(string Path, DateTime WrittenAtUtc, RaceResultsReport Report);
