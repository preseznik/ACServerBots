using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AssettoServer.Network.ClientMessages;
using AssettoServer.Network.Tcp;
using AssettoServer.Server.Configuration;
using AssettoServer.Server.Configuration.Kunos;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace AssettoServer.Server.Fps;

public sealed class FpsWorld : IHostedService
{
    private readonly object _sync = new();
    private readonly ACServer _server;
    private readonly ACServerConfiguration _configuration;
    private readonly EntryCarManager _entryCarManager;
    private readonly CSPClientMessageTypeManager _messageTypes;
    private FpsSimulation? _simulation;
    private uint _snapshotSequence;
    private int _snapshotTicks;
    private int _matchTicks;
    private bool _finalSent;
    private readonly HashSet<byte> _clientsWithAcceptedInput = [];
    private readonly HashSet<byte> _clientsWithActiveInput = [];
    private readonly Dictionary<byte, int> _neutralInputCounts = [];
    private readonly Dictionary<byte, uint> _knownSpawnCounts = [];
    private readonly Dictionary<byte, Vector3> _lastDiagnosticPositions = [];
    private readonly Dictionary<byte, long> _lastClientViewmodelDiagnosticTicks = [];

    public FpsWorld(ACServer server, ACServerConfiguration configuration,
        EntryCarManager entryCarManager, CSPClientMessageTypeManager messageTypes,
        CSPServerScriptProvider scriptProvider)
    {
        _server = server;
        _configuration = configuration;
        _entryCarManager = entryCarManager;
        _messageTypes = messageTypes;
        _messageTypes.RegisterOnlineEvent<FpsInputPacket>(OnInput);
        _messageTypes.RegisterOnlineEvent<FpsReadyPacket>(OnReady);
        _messageTypes.RegisterOnlineEvent<FpsClientDiagnosticPacket>(OnClientDiagnostic);

        using var script = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("AssettoServer.Server.Fps.fps.lua")
            ?? throw new InvalidOperationException("Embedded FPS client script is missing");
        scriptProvider.AddScript(script, "fps.lua");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        string geometryPath = Path.GetFullPath(Path.Combine(_configuration.BaseFolder,
            _configuration.Extra.Fps.Arena.GeometryPath));
        string presetRoot = Path.GetFullPath(_configuration.BaseFolder) + Path.DirectorySeparatorChar;
        if (!geometryPath.StartsWith(presetRoot, StringComparison.OrdinalIgnoreCase))
            throw new ConfigurationException("FPS Arena GeometryPath must stay inside the server preset directory");
        if (!File.Exists(geometryPath))
            throw new ConfigurationException($"FPS arena physical geometry was not found: {geometryPath}");
        var geometry = FpsArenaGeometryAsset.Load(geometryPath);
        var surface = new FpsArenaSurface(geometry.Triangles);
        var slots = _configuration.EntryList.Cars.Take(_configuration.Server.MaxClients)
            .Select((entry, index) => new FpsSimulationSlot((byte)index,
                entry.DriverName ?? $"Player {index + 1}", entry.FpsRole,
                entry.AiDifficulty is >= 0 and <= 1 ? entry.AiDifficulty : null,
                entry.AiAggression is >= 0 and <= 1 ? entry.AiAggression : null));
        _simulation = new FpsSimulation(_configuration.Extra.Fps, slots, surface: surface);
        _server.Update += OnUpdate;
        _entryCarManager.ClientConnected += OnClientConnected;
        _entryCarManager.ClientDisconnected += OnClientDisconnected;
        Log.Information("FPS deathmatch world started: {Actors} actors, {Minutes} minutes, {Kills} kills, {Triangles} physical arena triangles",
            _simulation.Actors.Count, _configuration.Extra.Fps.TimeLimitMinutes,
            _configuration.Extra.Fps.KillLimit, surface.TriangleCount);
        foreach (var actor in _simulation.Actors.Where(actor => actor.Active).OrderBy(actor => actor.Id))
        {
            _knownSpawnCounts[actor.Id] = actor.SpawnCount;
            Log.Debug(
                "FPS actor initial spawn: actor={ActorId}, role={Role}, human={Human}, spawn={SpawnCount}, position={Position}, yaw={Yaw:F3}, health={Health}",
                actor.Id, actor.Role, actor.HumanControlled, actor.SpawnCount, actor.Position,
                actor.Yaw, actor.Health);
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _server.Update -= OnUpdate;
        _entryCarManager.ClientConnected -= OnClientConnected;
        _entryCarManager.ClientDisconnected -= OnClientDisconnected;
        return Task.CompletedTask;
    }

    private void OnClientConnected(ACTcpClient client, EventArgs args)
    {
        if (client.EntryCar.FpsRole == FpsSlotRole.Spectator) return;
        bool claimed;
        lock (_sync)
        {
            claimed = _simulation?.ClaimHuman(client.SessionId) == true;
            if (claimed)
            {
                var actor = _simulation!.Actors.Single(actor => actor.Id == client.SessionId);
                _knownSpawnCounts[actor.Id] = actor.SpawnCount;
                _lastDiagnosticPositions[actor.Id] = actor.Position;
                client.Logger.Information(
                    "FPS human actor spawned: actor={ActorId}, spawn={SpawnCount}, position={Position}, yaw={Yaw:F3}, health={Health}, active={Active}, dead={Dead}",
                    actor.Id, actor.SpawnCount, actor.Position, actor.Yaw, actor.Health,
                    actor.Active, actor.Dead);
            }
        }
        if (claimed)
            client.Logger.Information("FPS participant claimed actor {ActorId}", client.SessionId);
        else
            client.Logger.Warning("FPS participant could not claim actor {ActorId}", client.SessionId);
    }

    private void OnClientDisconnected(ACTcpClient client, EventArgs args)
    {
        lock (_sync)
        {
            var actor = _simulation?.Actors.SingleOrDefault(actor => actor.Id == client.SessionId);
            if (actor is not null)
                client.Logger.Information(
                    "FPS human actor disconnecting: actor={ActorId}, position={Position}, inputSequence={Sequence}, inputMove={Move}, active={Active}, dead={Dead}",
                    actor.Id, actor.Position, actor.LastInputSequence, actor.Input.Move,
                    actor.Active, actor.Dead);
            _simulation?.ReleaseHuman(client.SessionId);
            _clientsWithAcceptedInput.Remove(client.SessionId);
            _clientsWithActiveInput.Remove(client.SessionId);
            _neutralInputCounts.Remove(client.SessionId);
            _lastClientViewmodelDiagnosticTicks.Remove(client.SessionId);
        }
    }

    private void OnClientDiagnostic(ACTcpClient client, FpsClientDiagnosticPacket packet)
    {
        long now = Stopwatch.GetTimestamp();
        lock (_sync)
        {
            if (_lastClientViewmodelDiagnosticTicks.TryGetValue(client.SessionId, out long previous)
                && Stopwatch.GetElapsedTime(previous, now) < TimeSpan.FromMilliseconds(500))
                return;
            _lastClientViewmodelDiagnosticTicks[client.SessionId] = now;
        }

        client.Logger.Information(
            "FPS client viewmodel diagnostic: pipeline={Pipeline}, flags={Flags}, updates={Completions}/{Attempts}, callbacks=frameBegin:{FrameBegin},draw3D:{Draw3D},drawUI:{DrawUI}, directDraw={DirectCompletions}/{DirectAttempts}, pending={DirectPending}, failures={DirectFailures}, stage={Stage}, intendedPosition={Position}",
            packet.Pipeline, packet.Flags, packet.Completions, packet.Attempts,
            packet.FrameBeginCalls, packet.Draw3DCalls, packet.DrawUiCalls,
            packet.DirectDrawCompletions, packet.DirectDrawAttempts, packet.DirectDrawPending,
            packet.DirectDrawFailures, packet.Stage, packet.Position);
    }

    private void OnInput(ACTcpClient client, FpsInputPacket packet)
    {
        if (client.EntryCar.FpsRole == FpsSlotRole.Spectator) return;
        lock (_sync)
        {
            bool accepted = _simulation?.ApplyInput(client.SessionId, new FpsInputCommand(packet.Sequence,
                packet.Move, packet.Yaw, packet.Pitch, packet.Buttons)) == true;
            if (!accepted)
            {
                client.Logger.Verbose("Rejected stale or invalid FPS input sequence {Sequence}", packet.Sequence);
            }
            else if (_clientsWithAcceptedInput.Add(client.SessionId))
            {
                client.Logger.Information(
                    "FPS input stream active for actor {ActorId}: move={Move}, yaw={Yaw:F3}, pitch={Pitch:F3}, buttons={Buttons}",
                    client.SessionId, packet.Move, packet.Yaw, packet.Pitch, packet.Buttons);
            }

            if (accepted && (packet.Move.LengthSquared() > 0.0001f || packet.Buttons != FpsInputButtons.None))
            {
                if (_clientsWithActiveInput.Add(client.SessionId))
                {
                    var actor = _simulation?.Actors.SingleOrDefault(actor => actor.Id == client.SessionId);
                    client.Logger.Information(
                        "FPS controls became active for actor {ActorId}: sequence={Sequence}, move={Move}, yaw={Yaw:F3}, pitch={Pitch:F3}, buttons={Buttons}, positionBeforeEffect={Position}",
                        client.SessionId, packet.Sequence, packet.Move, packet.Yaw, packet.Pitch,
                        packet.Buttons, actor?.Position);
                }
                _neutralInputCounts.Remove(client.SessionId);
            }
            else if (accepted && !_clientsWithActiveInput.Contains(client.SessionId))
            {
                int neutralPackets = _neutralInputCounts.GetValueOrDefault(client.SessionId) + 1;
                _neutralInputCounts[client.SessionId] = neutralPackets;
                if (neutralPackets == 40)
                    client.Logger.Warning("FPS actor {ActorId} sent 40 neutral input packets; check client input capture",
                        client.SessionId);
            }
        }
    }

    private void OnReady(ACTcpClient client, FpsReadyPacket packet)
    {
        if (packet.Protocol != 1)
        {
            client.Logger.Warning("FPS client protocol {Protocol} is not supported", packet.Protocol);
            _ = client.DisconnectAsync();
            return;
        }

        client.Logger.Information("FPS client ready with protocol {Protocol}", packet.Protocol);

        lock (_sync)
        {
            if (_simulation is null) return;
            foreach (var actor in _simulation.Actors.OrderBy(actor => actor.Id))
            {
                client.SendPacket(new FpsRosterPacket
                {
                    ActorId = actor.Id,
                    Role = (byte)actor.Role,
                    Name = actor.Name.Length <= 32 ? actor.Name : actor.Name[..32],
                });
            }
            SendMatch(client);
            SendSnapshots(client);
        }
    }

    private void OnUpdate(ACServer sender, EventArgs args)
    {
        lock (_sync)
        {
            if (_simulation is null) return;
            _simulation.Step(1f / _configuration.Server.RefreshRateHz);
            LogSpawnChanges();
            foreach (var hit in _simulation.HitEvents)
            {
                Broadcast(new FpsHitPacket
                {
                    AttackerId = hit.AttackerId,
                    VictimId = hit.VictimId,
                    RemainingHealth = hit.RemainingHealth,
                });
            }
            foreach (var shot in _simulation.ShotEvents)
            {
                Broadcast(new FpsShotPacket
                {
                    ShooterId = shot.ShooterId,
                    Sequence = shot.Sequence,
                    Origin = shot.Origin,
                    Direction = shot.Direction,
                    Distance = shot.Distance,
                });
            }
            foreach (var kill in _simulation.KillEvents)
            {
                Broadcast(new FpsKillPacket
                {
                    KillerId = kill.KillerId,
                    VictimId = kill.VictimId,
                    KillerKills = kill.KillerKills,
                    VictimDeaths = kill.VictimDeaths,
                });
            }

            int snapshotInterval = Math.Max(1, _configuration.Server.RefreshRateHz / 20);
            if (++_snapshotTicks >= snapshotInterval)
            {
                _snapshotTicks = 0;
                SendSnapshots();
            }

            if (++_matchTicks >= _configuration.Server.RefreshRateHz)
            {
                _matchTicks = 0;
                LogHumanActorEffects();
                BroadcastMatch();
            }

            if (_simulation.MatchState == FpsMatchState.Finished && !_finalSent)
            {
                _finalSent = true;
                BroadcastMatch();
                Log.Information("FPS deathmatch finished; winner session {Winner}", _simulation.WinnerId);
            }
        }
    }

    private void LogSpawnChanges()
    {
        if (_simulation is null) return;
        foreach (var actor in _simulation.Actors.Where(actor => actor.Active))
        {
            if (_knownSpawnCounts.GetValueOrDefault(actor.Id) == actor.SpawnCount) continue;
            _knownSpawnCounts[actor.Id] = actor.SpawnCount;
            _lastDiagnosticPositions[actor.Id] = actor.Position;
            Log.Information(
                "FPS actor spawned or respawned: actor={ActorId}, role={Role}, human={Human}, spawn={SpawnCount}, position={Position}, yaw={Yaw:F3}, health={Health}, protection={Protection:F2}",
                actor.Id, actor.Role, actor.HumanControlled, actor.SpawnCount, actor.Position,
                actor.Yaw, actor.Health, actor.SpawnProtectionRemaining);
        }
    }

    private void LogHumanActorEffects()
    {
        if (_simulation is null) return;
        foreach (var actor in _simulation.Actors.Where(actor => actor.HumanControlled)
                     .OrderBy(actor => actor.Id))
        {
            var previous = _lastDiagnosticPositions.GetValueOrDefault(actor.Id, actor.Position);
            var delta = actor.Position - previous;
            _lastDiagnosticPositions[actor.Id] = actor.Position;
            Log.Information(
                "FPS actor input effect: actor={ActorId}, position={Position}, delta={Delta}, distance={Distance:F3}, inputSequence={Sequence}, inputMove={Move}, inputButtons={Buttons}, hasInput={HasInput}, active={Active}, dead={Dead}, match={MatchState}",
                actor.Id, actor.Position, delta, delta.Length(), actor.LastInputSequence,
                actor.Input.Move, actor.Input.Buttons, actor.HasInput, actor.Active, actor.Dead,
                _simulation.MatchState);
        }
    }

    private void SendSnapshots(ACTcpClient? only = null)
    {
        if (_simulation is null) return;
        var actors = _simulation.Actors.OrderBy(actor => actor.Id).ToArray();
        for (int offset = 0; offset < actors.Length; offset += FpsSnapshotPacket.Capacity)
        {
            var packet = new FpsSnapshotPacket { Sequence = ++_snapshotSequence };
            packet.Count = (byte)Math.Min(FpsSnapshotPacket.Capacity, actors.Length - offset);
            for (int index = 0; index < packet.Count; index++)
            {
                var actor = actors[offset + index];
                packet.ActorIds[index] = actor.Id;
                packet.Flags[index] = (byte)((actor.Active ? 1 : 0) | (actor.Dead ? 2 : 0)
                    | (actor.HumanControlled ? 4 : 0) | (actor.SpawnProtectionRemaining > 0 ? 8 : 0)
                    | (actor.IsGrounded ? 16 : 0) | (actor.IsCrouching ? 32 : 0)
                    | (actor.GeometryBlocked ? 64 : 0) | (actor.IsProne ? 128 : 0));
                packet.Positions[index] = actor.Position;
                packet.Yaws[index] = actor.Yaw;
                packet.Pitches[index] = actor.Pitch;
                packet.Health[index] = (ushort)Math.Max(0, actor.Health);
                packet.Kills[index] = actor.Kills;
                packet.Deaths[index] = actor.Deaths;
            }

            if (only is not null) only.SendPacketUdp(in packet);
            else
            {
                foreach (var client in _entryCarManager.ConnectedCars.Values.Select(car => car.Client).OfType<ACTcpClient>())
                    client.SendPacketUdp(in packet);
            }
        }
    }

    private void SendMatch(ACTcpClient client)
    {
        if (_simulation is null) return;
        client.SendPacket(new FpsMatchPacket
        {
            State = (byte)_simulation.MatchState,
            RemainingSeconds = _simulation.RemainingSeconds,
            KillLimit = (ushort)_configuration.Extra.Fps.KillLimit,
            WinnerId = _simulation.WinnerId,
        });
    }

    private void BroadcastMatch()
    {
        foreach (var client in _entryCarManager.ConnectedCars.Values.Select(car => car.Client).OfType<ACTcpClient>())
            SendMatch(client);
    }

    private void Broadcast<TPacket>(TPacket packet) where TPacket : Shared.Network.Packets.Outgoing.IOutgoingNetworkPacket
    {
        foreach (var client in _entryCarManager.ConnectedCars.Values.Select(car => car.Client).OfType<ACTcpClient>())
            client.SendPacket(packet);
    }
}
