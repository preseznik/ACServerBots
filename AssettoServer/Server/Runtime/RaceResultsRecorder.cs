using System;
using System.Collections.Generic;
using System.Linq;

namespace AssettoServer.Server.Runtime;

/// <summary>One car's line in a race classification.</summary>
public sealed record RaceResultLine(
    int SessionId,
    int Position,
    string Name,
    IReadOnlyList<string> Drivers,
    string Team,
    string Model,
    bool IsBot,
    uint Laps,
    uint TotalTimeMilliseconds,
    uint BestLapMilliseconds,
    bool Finished,
    bool Dnf);

/// <summary>Identifies one run of a race: a restart gets a new start time and therefore a new run.</summary>
public readonly record struct RaceRunKey(int SessionIndex, long StartTimeMilliseconds);

/// <summary>A classification ready to save. <see cref="Status"/> is finished, stopped, ended or server_stopped.</summary>
public sealed record RaceResultsCapture(
    RaceRunKey Key,
    DateTimeOffset StartedAt,
    DateTimeOffset RecordedAt,
    string SessionName,
    int TargetLaps,
    string Status,
    IReadOnlyList<RaceResultLine> Results);

/// <summary>
/// Follows real races and decides when their classification should be saved, so results outlive the
/// session: when the race finishes (and again as later cars cross the line), when Race Control stops
/// it, when the session moves on before everyone finished, and when the server shuts down mid-race.
/// Races where nobody completed a lap are not saved.
/// </summary>
public sealed class RaceResultsRecorder
{
    private RaceRunKey? _key;
    private DateTimeOffset _startedAt;
    private string _sessionName = string.Empty;
    private int _targetLaps;
    private string _status = "racing";
    private IReadOnlyList<RaceResultLine> _lines = [];
    private string? _signature;
    private string? _savedSignature;

    /// <param name="key">The running race, or null when no race is under way (other session, countdown).</param>
    /// <param name="phase">Bridge phase: racing, finished or stopped.</param>
    public IReadOnlyList<RaceResultsCapture> Observe(RaceRunKey? key, string sessionName, int targetLaps,
        string phase, DateTimeOffset now, Func<IReadOnlyList<RaceResultLine>> classification)
    {
        var captures = new List<RaceResultsCapture>(1);
        if (key != _key)
        {
            if (Close("ended", now) is { } previous)
                captures.Add(previous);
            _key = key;
            _startedAt = now;
            _status = "racing";
            _lines = [];
            _signature = null;
            _savedSignature = null;
        }

        if (key is null)
            return captures;

        _sessionName = sessionName;
        _targetLaps = targetLaps;
        _status = phase is "finished" or "stopped" ? phase : "racing";
        var lines = classification();
        string signature = Signature(lines, _status);
        if (signature != _signature)
        {
            _lines = lines;
            _signature = signature;
        }

        if (_status != "racing" && TryCapture(_status, now) is { } capture)
            captures.Add(capture);
        return captures;
    }

    /// <summary>Saves the current race if it was left unsaved, e.g. when the server stops mid-race.</summary>
    public RaceResultsCapture? Flush(DateTimeOffset now) => Close("server_stopped", now);

    private RaceResultsCapture? Close(string unfinishedStatus, DateTimeOffset now)
        => _key is null ? null : TryCapture(_status == "racing" ? unfinishedStatus : _status, now);

    private RaceResultsCapture? TryCapture(string status, DateTimeOffset now)
    {
        if (_key is not { } key || _signature == _savedSignature || !_lines.Any(line => line.Laps > 0))
            return null;
        _savedSignature = _signature;
        return new RaceResultsCapture(key, _startedAt, now, _sessionName, _targetLaps, status, _lines);
    }

    private static string Signature(IReadOnlyList<RaceResultLine> lines, string status)
        => status + "|" + string.Join(";", lines.Select(line =>
            $"{line.SessionId}:{line.Position}:{line.Laps}:{line.TotalTimeMilliseconds}:{line.BestLapMilliseconds}:"
            + $"{(line.Finished ? 1 : 0)}{(line.Dnf ? 1 : 0)}:{line.Name}:{string.Join('/', line.Drivers)}"));
}
