using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AssettoServer.Network.Tcp;
using AssettoServer.Server.Configuration;
using AssettoServer.Server.Configuration.Kunos;
using AssettoServer.Server.Configuration.Extra;
using AssettoServer.Server.Weather;
using AssettoServer.Shared.Model;
using AssettoServer.Shared.Network.Packets.Incoming;
using AssettoServer.Shared.Network.Packets.Outgoing;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace AssettoServer.Server;

public class SessionManager : BackgroundService, IHostedLifecycleService
{
    private readonly ACServerConfiguration _configuration;
    private readonly Func<SessionConfiguration, SessionState> _sessionStateFactory;
    private readonly Stopwatch _timeSource = new();
    private readonly EntryCarManager _entryCarManager;
    private readonly Lazy<WeatherManager> _weatherManager;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly object _firstHumanRestartLock = new();
    private readonly FirstHumanSessionRestartGate _firstHumanRestartGate = new();
    private bool _firstHumanRestartPending;

    public int CurrentSessionIndex { get; private set; } = -1;
    public bool IsLastRaceInverted { get; private set; } = false;
    public bool MustInvertGrid { get; private set; } = false;
    public SessionState CurrentSession { get; private set; } = null!;

    public long ServerTimeMilliseconds => _timeSource.ElapsedMilliseconds;

    public bool IsOpen => CurrentSession.Configuration.IsOpen switch
    {
        IsOpenMode.Open => true,
        IsOpenMode.CloseAtStart => !CurrentSession.IsCutoffReached,
        _ => false,
    };

    public bool IsMidRaceBotTakeoverSession =>
        _configuration.Extra.AiParams is
            { Behavior: Configuration.Extra.AiBehaviorMode.Race, Race.AllowMidRaceBotTakeover: true }
        && CurrentSession.Configuration.Type == SessionType.Race;

    public bool CanTakeOverBotSlot(EntryCar entryCar)
    {
        if (!IsMidRaceBotTakeoverSession)
            return false;

        EntryCarResult? result = null;
        CurrentSession.Results?.TryGetValue(entryCar.SessionId, out result);
        var raceIsActive = CurrentSession.EndTimeMilliseconds == 0
                           && result is { HasCompletedLastLap: false, IsDnf: false };
        return RaceParticipantPolicy.CanTakeOverBotSlot(
            true, raceIsActive, entryCar.AiMode, entryCar.AiControlled);
    }

    /// <summary>
    /// Fires when a new session is started
    /// </summary>
    public event EventHandler<SessionManager, SessionChangedEventArgs>? SessionChanged;

    public SessionManager(ACServerConfiguration configuration,
        Func<SessionConfiguration, SessionState> sessionStateFactory,
        EntryCarManager entryCarManager,
        Lazy<WeatherManager> weatherManager,
        IHostApplicationLifetime applicationLifetime)
    {
        _configuration = configuration;
        _sessionStateFactory = sessionStateFactory;
        _entryCarManager = entryCarManager;
        _weatherManager = weatherManager;
        _applicationLifetime = applicationLifetime;

        _entryCarManager.ClientConnected += OnClientConnected;
        _entryCarManager.ClientDisconnected += OnClientDisconnected;
    }

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));

        while (await timer.WaitForNextTickAsync(token))
        {
            try
            {
                if (IsSessionOver())
                {
                    NextSession();
                }

                switch (CurrentSession.Configuration.Type)
                {
                    case SessionType.Qualifying or SessionType.Practice:
                    {
                        if (CurrentSession is { SessionOverFlag: true, HasSentRaceOverPacket: false })
                        {
                            CalcOverTime();
                            CurrentSession.EndTimeMilliseconds = 60_000 * CurrentSession.Configuration.Time + CurrentSession.StartTimeMilliseconds;
                            if (ServerTimeMilliseconds - CurrentSession.EndTimeMilliseconds > CurrentSession.OverTimeMilliseconds)
                                SendSessionOver();
                        }

                        if (CurrentSession.HasSentRaceOverPacket
                            && ServerTimeMilliseconds > _configuration.Server.ResultScreenTime * 1000L + CurrentSession.OverTimeMilliseconds)
                        {
                            NextSession();
                        }

                        break;
                    }
                    case SessionType.Race:
                    {
                        if (CurrentSession is { EndTimeMilliseconds: not 0L, HasSentRaceOverPacket: false })
                        {
                            CalcOverTime();
                            if (ServerTimeMilliseconds - CurrentSession.EndTimeMilliseconds > CurrentSession.OverTimeMilliseconds)
                                SendSessionOver();
                        }

                        if (CurrentSession.HasSentRaceOverPacket
                            && ServerTimeMilliseconds > _configuration.Server.ResultScreenTime * 1000L + CurrentSession.OverTimeMilliseconds)
                        {
                            NextSession();
                        }

                        break;
                    }
                }

                SendSessionStart();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in session service update");
            }
        }
    }

    public LapCompletedOutgoing? OnLapCompleted(EntryCar entryCar, string participantName, LapCompletedIncoming lap, uint latencyMilliseconds = 0)
    {
        _configuration.Server.DynamicTrack.TotalLapCount++;
        if (!RecordLap(entryCar, participantName, lap, latencyMilliseconds))
            return null;

        var packet = CreateLapCompletedPacket(entryCar.SessionId, lap.LapTime, lap.Cuts);
        _entryCarManager.BroadcastPacket(packet);
        return packet;
    }

    private bool RecordLap(EntryCar entryCar, string participantName, LapCompletedIncoming lap, uint latencyMilliseconds)
    {
        int timestamp = (int)ServerTimeMilliseconds;

        var entryCarResult = CurrentSession.Results?[entryCar.SessionId] ?? throw new InvalidOperationException("Current session does not have results set");

        if (entryCarResult.HasCompletedLastLap)
        {
            Log.Debug("Lap rejected by {ParticipantName}, already finished", participantName);
            return false;
        }

        if (CurrentSession.Configuration.Type == SessionType.Race
            && entryCarResult.NumLaps >= CurrentSession.Configuration.Laps
            && !CurrentSession.Configuration.IsTimedRace)
        {
            Log.Debug("Lap rejected by {ParticipantName}, race over", participantName);
            return false;
        }

        Log.Information("Lap completed by {ParticipantName}, {NumCuts} cuts, laptime {LapTime}", participantName, lap.Cuts, TimeSpan.FromMilliseconds(lap.LapTime).ToString(@"mm\:ss\.ffff"));

        if (CurrentSession.Configuration.Type == SessionType.Race || lap.Cuts == 0)
        {
            entryCarResult.LastLap = lap.LapTime;
            entryCarResult.NumLaps++;
            entryCarResult.TotalTime = (uint)Math.Max(0, CurrentSession.SessionTimeMilliseconds - latencyMilliseconds / 2);

            if (lap.LapTime < entryCarResult.BestLap)
            {
                entryCarResult.BestLap = lap.LapTime;
            }

            var oldLeaderLapCount = CurrentSession.LeaderLapCount;
            if (entryCarResult.NumLaps > CurrentSession.LeaderLapCount)
            {
                CurrentSession.LeaderLapCount = entryCarResult.NumLaps;
            }

            if (CurrentSession.Configuration.Type == SessionType.Race)
            {
                RaceParticipantPolicy.RefreshClassification(CurrentSession.Results);
            }

            if (CurrentSession.SessionOverFlag)
            {
                if (CurrentSession.Configuration is { Type: SessionType.Race, IsTimedRace: true })
                {
                    if (_configuration.Server.HasExtraLap)
                    {
                        if (entryCarResult.NumLaps <= oldLeaderLapCount)
                        {
                            entryCarResult.HasCompletedLastLap = CurrentSession.LeaderHasCompletedLastLap;
                        }
                        else if (CurrentSession.TargetLap > 0)
                        {
                            if (entryCarResult.NumLaps >= CurrentSession.TargetLap)
                            {
                                CurrentSession.LeaderHasCompletedLastLap = true;
                                entryCarResult.HasCompletedLastLap = true;
                            }
                        }
                        else
                        {
                            CurrentSession.TargetLap = entryCarResult.NumLaps + 1;
                        }
                    }
                    else if (entryCarResult.NumLaps <= oldLeaderLapCount)
                    {
                        entryCarResult.HasCompletedLastLap = CurrentSession.LeaderHasCompletedLastLap;
                    }
                    else
                    {
                        CurrentSession.LeaderHasCompletedLastLap = true;
                        entryCarResult.HasCompletedLastLap = true;
                    }
                }
                else
                {
                    entryCarResult.HasCompletedLastLap = true;
                }
            }

            if (CurrentSession.Configuration.Type != SessionType.Race)
            {
                if (CurrentSession.EndTimeMilliseconds != 0)
                {
                    entryCarResult.HasCompletedLastLap = true;
                }
            }
            else if (CurrentSession.Configuration.IsTimedRace)
            {
                if (CurrentSession is { LeaderHasCompletedLastLap: true, EndTimeMilliseconds: 0 })
                {
                    CurrentSession.EndTimeMilliseconds = timestamp;
                }
            }
            else if (entryCarResult.NumLaps != CurrentSession.Configuration.Laps)
            {
                if (CurrentSession.EndTimeMilliseconds == 0)
                    return true;
                entryCarResult.HasCompletedLastLap = true;
            }
            else switch (entryCarResult.HasCompletedLastLap)
            {
                case false:
                    if (CurrentSession.EndTimeMilliseconds == 0)
                        CurrentSession.EndTimeMilliseconds = timestamp;
                    entryCarResult.HasCompletedLastLap = true;
                    break;
                case true when CurrentSession.EndTimeMilliseconds == 0:
                    return true;
                case true:
                    entryCarResult.HasCompletedLastLap = true;
                    break;
            }

            return true;
        }

        if (CurrentSession.EndTimeMilliseconds == 0)
            return true;

        entryCarResult.HasCompletedLastLap = true;
        return false;
    }

    public LapCompletedOutgoing CreateLapCompletedPacket(byte sessionId, uint lapTime, int cuts)
    {
        if (CurrentSession.Results == null)
            throw new InvalidOperationException("Current session does not have results set");

        var laps = BuildClassificationLaps(CurrentSession.Results, CurrentSession.Configuration.Type);

        return new LapCompletedOutgoing
        {
            SessionId = sessionId,
            LapTime = lapTime,
            Cuts = (byte)cuts,
            Laps = laps,
            TrackGrip = _weatherManager.Value.CurrentWeather.TrackGrip
        };
    }

    internal static LapCompletedOutgoing.CompletedLap[] BuildClassificationLaps(
        IReadOnlyDictionary<byte, EntryCarResult> results, SessionType sessionType)
    {
        return results
            .OrderBy(result => string.IsNullOrEmpty(result.Value.Name))
            .ThenBy(result => result.Value.Name)
            .Select(result => new LapCompletedOutgoing.CompletedLap
            {
                SessionId = result.Key,
                LapTime = sessionType == SessionType.Race ? result.Value.TotalTime : result.Value.BestLap,
                NumLaps = (ushort)result.Value.NumLaps,
                HasCompletedLastLap = (byte)(result.Value.HasCompletedLastLap ? 1 : 0),
                RacePos = (byte)result.Value.RacePos,
            })
            .OrderBy(result => result.RacePos)
            .ToArray();
    }

    public void MarkParticipantDnf(EntryCar entryCar, string participantName)
    {
        var results = CurrentSession.Results;
        if (results == null || !results.TryGetValue(entryCar.SessionId, out var result))
            return;

        if (string.IsNullOrEmpty(result.Name))
            result.Name = participantName;
        result.IsDnf = true;
        result.HasCompletedLastLap = true;

        RaceParticipantPolicy.RefreshClassification(results);

        _entryCarManager.BroadcastPacket(CreateLapCompletedPacket(entryCar.SessionId, result.LastLap, 0));
        Log.Information("{ParticipantName} classified DNF after {CompletedLaps} laps", participantName, result.NumLaps);
    }

    private bool IsSessionOver()
    {
        if (CurrentSession.Configuration.Infinite)
        {
            return false;
        }

        if (CurrentSession.Configuration.Type == SessionType.Booking)
        {
            // TODO Currently unused, maybe for later, when i care about sessions without pickup mode :shrug:
            return CurrentSession.TimeLeftMilliseconds == 0;
        }

        if (CurrentSession.Configuration.Type is SessionType.Practice or SessionType.Qualifying)
        {
            return false;
        }

        var connectedCount = _configuration.Extra.AiParams.Behavior == AiBehaviorMode.Race
            ? _entryCarManager.EntryCars.Count(car => car.AiControlled || car.Client is { HasSentFirstUpdate: true })
            : _entryCarManager.ConnectedCars.Count;
        
        switch (CurrentSession.Configuration.IsOpen)
        {
            case IsOpenMode.Closed when connectedCount < 2:
                Log.Information("Skipping race session: didn't reach minimum player count before cutoff ({PlayerCount}/2). Use 'IS_OPEN=1' to allow joining during the race", connectedCount);
                return true;
            case IsOpenMode.Closed:
                return false;
            case IsOpenMode.CloseAtStart when connectedCount >= 2 ||
                                              ServerTimeMilliseconds <= CurrentSession.StartTimeMilliseconds:
                return false;
            case IsOpenMode.Open when connectedCount > 0 ||
                                      ServerTimeMilliseconds <= CurrentSession.StartTimeMilliseconds:
                return false;
        }
        
        Log.Information("Skipping race session: no player connected");
        return true;
    }

    private void CalcOverTime()
    {
        if (_entryCarManager.EntryCars.All(c => c.Client == null && !c.AiControlled))
        {
            CurrentSession.OverTimeMilliseconds = 0;
            return;
        }

        if (CurrentSession.Configuration.Type == SessionType.Race)
        {
            var overTimeMilliseconds = _configuration.Server.RaceOverTime * 1000L;
            if (CurrentSession.OverTimeMilliseconds == 0)
                CurrentSession.OverTimeMilliseconds = overTimeMilliseconds;

            if (CurrentSession.OverTimeMilliseconds == overTimeMilliseconds)
            {
                var participants = _entryCarManager.EntryCars
                    .Where(car => CurrentSession.Results?.ContainsKey(car.SessionId) == true)
                    .Select(car => (
                        Active: car.AiControlled || car.Client is { HasSentFirstUpdate: true },
                        Result: CurrentSession.Results![car.SessionId]));
                if (RaceParticipantPolicy.HasUnfinishedActiveParticipant(participants))
                {
                    return;
                }
            }
        }
        else
        {
            var overTimeMilliseconds = ServerTimeMilliseconds / 100 * _configuration.Server.QualifyMaxWait;
            if (CurrentSession.OverTimeMilliseconds == 0 || CurrentSession.OverTimeMilliseconds > overTimeMilliseconds)
                CurrentSession.OverTimeMilliseconds = overTimeMilliseconds;

            if (_entryCarManager.EntryCars
                .Where(c => c.Client is { HasSentFirstUpdate: true })
                .Any(car => CurrentSession.Results?[car.SessionId] is { HasCompletedLastLap: false }
                            && car.Status.Velocity.LengthSquared() > 5))
            {
                return;
            }
        }

        CurrentSession.OverTimeMilliseconds = 1;
    }

    private void OnClientConnected(ACTcpClient client, EventArgs eventArgs)
    {
        if (IsFirstHumanSessionRestartEnabled)
            client.FirstUpdateSent += OnClientFirstUpdateSent;

        var currentResult = CurrentSession.Results;
        
        if (currentResult != null
            && currentResult.TryGetValue(client.SessionId, out var previousResult)
            && previousResult.Guid != client.Guid)
        {
            var replacement = new EntryCarResult(client);
            if (CurrentSession.Configuration.Type == SessionType.Race)
            {
                CurrentSession.LeaderLapCount = RaceParticipantPolicy.ReplaceParticipant(
                    currentResult, client.SessionId, replacement);
                CurrentSession.LeaderHasCompletedLastLap = currentResult.Values.Any(result =>
                    result.NumLaps == CurrentSession.LeaderLapCount && result.HasCompletedLastLap);
                _entryCarManager.BroadcastPacket(CreateLapCompletedPacket(client.SessionId, 0, 0));

                if ((previousResult.Guid & (1UL << 63)) != 0)
                {
                    Log.Information("{ClientName} replaced {BotName} in race slot {SessionId} and starts with a fresh result",
                        client.Name, previousResult.Name, client.SessionId);
                }
            }
            else
            {
                currentResult[client.SessionId] = replacement;
            }
        }

        if (!IsFirstHumanSessionRestartEnabled)
            return;

        bool remainingSlotsAreBots = _entryCarManager.EntryCars.Any(car => car.Client == null && car.AiControlled)
                                            && _entryCarManager.EntryCars.All(car => car.Client != null || car.AiControlled);
        lock (_firstHumanRestartLock)
        {
            if (_firstHumanRestartGate.TrySchedule(true, _entryCarManager.ConnectedCars.Count, remainingSlotsAreBots))
            {
                _firstHumanRestartPending = true;
                Log.Information("First human joined a bot-only server; current session will restart after client synchronization");
            }
        }
    }

    private bool IsFirstHumanSessionRestartEnabled =>
        _configuration.Extra.AiParams is
            { Behavior: AiBehaviorMode.Race, Race.RestartSessionOnFirstHumanConnect: true };

    private void OnClientFirstUpdateSent(ACTcpClient client, EventArgs eventArgs)
    {
        client.FirstUpdateSent -= OnClientFirstUpdateSent;
        TryRestartPendingFirstHumanSession();
    }

    private void OnClientDisconnected(ACTcpClient client, EventArgs eventArgs)
    {
        client.FirstUpdateSent -= OnClientFirstUpdateSent;

        lock (_firstHumanRestartLock)
        {
            int connectedHumanCount = _entryCarManager.ConnectedCars.Count;
            _firstHumanRestartGate.UpdateConnectedHumanCount(connectedHumanCount);
            if (connectedHumanCount == 0)
                _firstHumanRestartPending = false;
        }

        TryRestartPendingFirstHumanSession();
    }

    private void TryRestartPendingFirstHumanSession()
    {
        lock (_firstHumanRestartLock)
        {
            if (!_firstHumanRestartPending
                || _entryCarManager.ConnectedCars.Count == 0
                || _entryCarManager.ConnectedCars.Values.Any(car => car.Client is not { HasSentFirstUpdate: true }))
            {
                return;
            }

            _firstHumanRestartPending = false;
        }

        if (RestartSession())
        {
            Log.Information("Restarted current session for the first human joining the bot-only server");
            return;
        }

        lock (_firstHumanRestartLock)
        {
            if (_entryCarManager.ConnectedCars.Count > 0)
                _firstHumanRestartPending = true;
        }

        // A second client can secure a slot between the readiness check and RestartSession().
        // Re-check immediately in case that client already finished its first update.
        TryRestartPendingFirstHumanSession();
    }

    public void SetSession(int sessionId)
    {
        // TODO reset sun angle

        var previousSession = CurrentSession;
        Dictionary<byte, EntryCarResult>? previousSessionResults = CurrentSession?.Results; // breaks with CurrentSession.Result don't believe the IDE

        CurrentSession = _sessionStateFactory(_configuration.Sessions[sessionId]);
        CurrentSession.Results = new Dictionary<byte, EntryCarResult>();
        CurrentSession.StartTimeMilliseconds = ServerTimeMilliseconds;

        foreach (var entryCar in _entryCarManager.EntryCars)
        {
            var result = entryCar.Client != null
                ? new EntryCarResult(entryCar.Client)
                : entryCar.AiControlled
                    ? new EntryCarResult((1UL << 63) | entryCar.SessionId, entryCar.AiName ?? $"Bot {entryCar.SessionId}")
                    : new EntryCarResult(null);
            CurrentSession.Results.Add(entryCar.SessionId, result);
        }

        var sessionLength = CurrentSession.Configuration switch
        {
            { Infinite: true } => "Infinite",
            { IsTimedRace: false } => $"{CurrentSession.Configuration.Laps} laps",
            _ => $"{CurrentSession.Configuration.Time} minutes"
        };
        Log.Information("Next session: {SessionName} - Length: {Length}", CurrentSession.Configuration.Name, sessionLength);

        if (CurrentSession.Configuration.Type == SessionType.Race)
        {
            CurrentSession.StartTimeMilliseconds = ServerTimeMilliseconds + (CurrentSession.Configuration.WaitTime * 1000);
        }
        else
        {
            IsLastRaceInverted = false;
        }

        _configuration.Server.DynamicTrack.TransferSession();
        // TODO weather

        int invertedCount = 0;
        if (previousSessionResults == null)
        {
            CurrentSession.Grid = _entryCarManager.EntryCars;
        }
        else
        {
            var grid = previousSessionResults
                .OrderBy(result => result.Value.BestLap)
                .Select(result => _entryCarManager.EntryCars[result.Key])
                .ToList();

            if (MustInvertGrid)
            {
                var inverted = previousSessionResults
                    .Take(_configuration.Server.InvertedGridPositions)
                    .OrderByDescending(result => result.Value.BestLap)
                    .Select(result => _entryCarManager.EntryCars[result.Key])
                    .ToList();

                for (var i = 0; i < inverted.Count; i++)
                {
                    grid[i] = inverted[i];
                }

                Log.Information("Inverted {Slots} grid slots", inverted.Count);

                invertedCount = inverted.Count;
            }

            CurrentSession.Grid = grid;
        }

        SessionChanged?.Invoke(this, new SessionChangedEventArgs(previousSession, CurrentSession, invertedCount));
        SendCurrentSession();

        Log.Information("Switching session to id {Id}", sessionId);
    }

    public bool RestartSession()
    {
        // StallSessionSwitch
        if (_entryCarManager.EntryCars.Any(c => c.Client is { HasSentFirstUpdate: false }))
            return false;

        SetSession(CurrentSessionIndex);
        return true;
    }

    public bool NextSession()
    {
        // StallSessionSwitch
        if (_entryCarManager.EntryCars.Any(c => c.Client is { HasSentFirstUpdate: false }))
            return false;

        MustInvertGrid = false;
        if (_configuration.Sessions.Count - 1 == CurrentSessionIndex)
        {
            if (_configuration.Server.Loop)
            {
                Log.Information("Looping sessions");
            }
            else if (CurrentSession.Configuration.Type != SessionType.Race || _configuration.Server.InvertedGridPositions == 0 || IsLastRaceInverted)
            {
                Log.Information("Set LOOP_MODE=1 in the server_cfg.ini to loop sessions");
                _applicationLifetime.StopApplication();
                return false;
            }

            if (CurrentSession.Configuration.Type == SessionType.Race && _configuration.Server.InvertedGridPositions != 0)
            {
                if (_configuration.Sessions.Count <= 1)
                {
                    MustInvertGrid = true;
                }
                else if (!IsLastRaceInverted)
                {
                    MustInvertGrid = true;
                    IsLastRaceInverted = true;
                    --CurrentSessionIndex;
                }
            }
        }

        if (++CurrentSessionIndex >= _configuration.Sessions.Count)
        {
            CurrentSessionIndex = 0;
        }
        SetSession(CurrentSessionIndex);
        return true;
    }

    public void SendCurrentSession(ACTcpClient? target = null)
    {
        var packet = new CurrentSessionUpdate
        {
            CurrentSession = CurrentSession.Configuration,
            Grid = CurrentSession.Grid,
            TrackGrip = _weatherManager.Value.CurrentWeather.TrackGrip
        };

        if (target == null)
        {
            foreach (var car in _entryCarManager.EntryCars.Where(c => c.Client is { HasSentFirstUpdate: true }))
            {
                packet.StartTime = CurrentSession.StartTimeMilliseconds - car.TimeOffset;
                car.Client?.SendPacket(packet);
            }
        }
        else
        {
            target.SendPacket(packet);
        }
    }

    private void SendSessionStart()
    {
        if (ServerTimeMilliseconds >= CurrentSession.StartTimeMilliseconds + 5000
            && ServerTimeMilliseconds - CurrentSession.LastRaceStartUpdateMilliseconds <= 1000) return;

        foreach (var car in _entryCarManager.EntryCars.Where(c => c.Client is { HasSentFirstUpdate: true }))
        {
            car.Client?.SendPacketUdp(new RaceStart()
            {
                StartTime = (int)(CurrentSession.StartTimeMilliseconds - car.TimeOffset),
                TimeOffset = (uint)(ServerTimeMilliseconds - car.TimeOffset),
                Ping = car.Ping,
            });
        }

        CurrentSession.LastRaceStartUpdateMilliseconds = ServerTimeMilliseconds;
    }

    private void SendSessionOver()
    {
        if (CurrentSession.Results != null)
            _entryCarManager.BroadcastPacket(new RaceOver
            {
                IsRace = CurrentSession.Configuration.Type == SessionType.Race,
                PickupMode = true,
                Results = CurrentSession.Results
            });

        CurrentSession.HasSentRaceOverPacket = true;
        CurrentSession.OverTimeMilliseconds = ServerTimeMilliseconds;
    }

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        _timeSource.Start();
        NextSession();
        
        return Task.CompletedTask;
    }

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
