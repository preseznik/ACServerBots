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
using AssettoServer.Server.Configuration.Extra;
using AssettoServer.Server.Configuration.Kunos;
using AssettoServer.Server.Weather;
using AssettoServer.Shared.Weather;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace AssettoServer.Server.Fps;

internal readonly record struct FpsLiveActorSnapshot(byte Id, string Name, bool IsBot,
    bool Active, bool Dead, Vector3 Position, Vector3 Velocity, float Yaw, int Health,
    ushort Kills, ushort Deaths, uint Score);

internal sealed record FpsLiveMatchSnapshot(FpsMatchState State, float ElapsedSeconds,
    float RemainingSeconds, int KillLimit, byte WinnerId,
    IReadOnlyList<FpsLiveActorSnapshot> Actors);

internal sealed record FpsLiveArenaSnapshot(Vector3 BoundsMin, Vector3 BoundsMax,
    float CellSize, IReadOnlyList<Vector2> Cells);

public sealed class FpsWorld : IHostedService
{
    private readonly object _sync = new();
    private readonly ACServer _server;
    private readonly ACServerConfiguration _configuration;
    private readonly EntryCarManager _entryCarManager;
    private readonly CSPClientMessageTypeManager _messageTypes;
    private readonly WeatherManager _weatherManager;
    private FpsSimulation? _simulation;
    private FpsLiveArenaSnapshot? _liveArena;
    private uint _snapshotSequence;
    private uint _grenadeSnapshotSequence;
    private int _snapshotTicks;
    private int _matchTicks;
    private bool _finalSent;
    private readonly HashSet<byte> _clientsWithAcceptedInput = [];
    private readonly HashSet<byte> _clientsWithActiveInput = [];
    private readonly HashSet<byte> _clientsWithAcceptedShot = [];
    private readonly HashSet<byte> _botsWithActiveBehavior = [];
    private readonly Dictionary<byte, int> _neutralInputCounts = [];
    private readonly Dictionary<byte, uint> _knownSpawnCounts = [];
    private readonly Dictionary<byte, ulong> _knownLoadoutStates = [];
    private readonly Dictionary<byte, Vector3> _lastDiagnosticPositions = [];
    private readonly Dictionary<byte, (float GroundY, bool Grounded, bool Mantling)>
        _lastMovementStates = [];
    private readonly Dictionary<byte, long> _lastClientViewmodelDiagnosticTicks = [];
    private double _simulationMillisecondsTotal;
    private double _simulationMillisecondsMaximum;
    private FpsStepDiagnostics _maximumStepDiagnostics;
    private int _simulationDurationSamples;
    private long _lastEnvironmentRequestTicks;

    public FpsWorld(ACServer server, ACServerConfiguration configuration,
        EntryCarManager entryCarManager, CSPClientMessageTypeManager messageTypes,
        CSPServerScriptProvider scriptProvider, WeatherManager weatherManager)
    {
        _server = server;
        _configuration = configuration;
        _entryCarManager = entryCarManager;
        _messageTypes = messageTypes;
        _weatherManager = weatherManager;
        _messageTypes.RegisterOnlineEvent<FpsInputPacket>(OnInput);
        _messageTypes.RegisterOnlineEvent<FpsReadyPacket>(OnReady);
        _messageTypes.RegisterOnlineEvent<FpsLoadoutSelectPacket>(OnLoadoutSelect);
        _messageTypes.RegisterOnlineEvent<FpsClientDiagnosticPacket>(OnClientDiagnostic);
        _messageTypes.RegisterOnlineEvent<FpsEnvironmentRequestPacket>(OnEnvironmentRequest);

        using var script = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("AssettoServer.Server.Fps.fps.lua")
            ?? throw new InvalidOperationException("Embedded FPS client script is missing");
        using var reader = new StreamReader(script);
        scriptProvider.AddScript(ConfigureClientScript(reader.ReadToEnd(),
            _configuration.Extra.Fps.Theme), "fps.lua");
    }

    internal const string VisualThemeMarker = "__ASRC_FPS_THEME__";

    internal static string ConfigureClientScript(string script, FpsVisualTheme theme)
    {
        if (!Enum.IsDefined(theme))
            throw new ConfigurationException($"Unsupported FPS visual theme: {theme}");
        int marker = script.IndexOf(VisualThemeMarker, StringComparison.Ordinal);
        if (marker < 0 || script.IndexOf(VisualThemeMarker, marker + 1,
                            StringComparison.Ordinal) >= 0)
            throw new InvalidDataException("FPS client visual-theme marker is missing or duplicated");
        return script.Replace(VisualThemeMarker, theme.ToString(), StringComparison.Ordinal);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ValidateLoadouts(_configuration.Extra.Fps.Loadouts);
        string geometryPath = Path.GetFullPath(Path.Combine(_configuration.BaseFolder,
            _configuration.Extra.Fps.Arena.GeometryPath));
        string navigationPath = Path.GetFullPath(Path.Combine(_configuration.BaseFolder,
            _configuration.Extra.Fps.Arena.NavigationPath));
        string presetRoot = Path.GetFullPath(_configuration.BaseFolder) + Path.DirectorySeparatorChar;
        if (!geometryPath.StartsWith(presetRoot, StringComparison.OrdinalIgnoreCase)
            || !navigationPath.StartsWith(presetRoot, StringComparison.OrdinalIgnoreCase))
            throw new ConfigurationException("FPS arena asset paths must stay inside the server preset directory");
        if (!File.Exists(geometryPath))
            throw new ConfigurationException($"FPS arena physical geometry was not found: {geometryPath}");
        if (!File.Exists(navigationPath))
            throw new ConfigurationException($"FPS arena navigation was not found: {navigationPath}");
        var geometry = FpsArenaGeometryAsset.Load(geometryPath);
        var navigation = FpsArenaNavigationAsset.Load(navigationPath);
        var surface = new FpsArenaSurface(geometry.Triangles);
        var slots = _configuration.EntryList.Cars.Take(_configuration.Server.MaxClients)
            .Select((entry, index) => new FpsSimulationSlot((byte)index,
                entry.DriverName ?? $"Player {index + 1}", entry.FpsRole,
                entry.AiDifficulty is >= 0 and <= 1 ? entry.AiDifficulty : null,
                entry.AiAggression is >= 0 and <= 1 ? entry.AiAggression : null));
        _simulation = new FpsSimulation(_configuration.Extra.Fps, slots, surface: surface,
            navigation: navigation);
        _liveArena = new FpsLiveArenaSnapshot(
            _configuration.Extra.Fps.Arena.BoundsMin,
            _configuration.Extra.Fps.Arena.BoundsMax,
            navigation.CellSize,
            navigation.Nodes.Select(node => new Vector2(node.Position.X, node.Position.Z))
                .Distinct().OrderBy(cell => cell.Y).ThenBy(cell => cell.X).ToArray());
        _server.Update += OnUpdate;
        _entryCarManager.ClientConnected += OnClientConnected;
        _entryCarManager.ClientDisconnected += OnClientDisconnected;
        Log.Information("FPS deathmatch world started: {Actors} actors, {Minutes} minutes, {Kills} kills, theme {Theme}, {Triangles} collision triangles in BVH {BvhNodes} nodes/{BvhLeaves} leaves/max {BvhMaximum} triangles, {Nodes} navigation nodes in {Components} components",
            _simulation.Actors.Count, _configuration.Extra.Fps.TimeLimitMinutes,
            _configuration.Extra.Fps.KillLimit, _configuration.Extra.Fps.Theme,
            surface.TriangleCount, surface.BvhNodeCount, surface.BvhLeafCount,
            surface.BvhMaximumLeafTriangles,
            navigation.Nodes.Count, navigation.ComponentCount);
        foreach (var actor in _simulation.Actors.Where(actor => actor.Active).OrderBy(actor => actor.Id))
        {
            _knownSpawnCounts[actor.Id] = actor.SpawnCount;
            _knownLoadoutStates[actor.Id] = LoadoutSignature(actor);
            _lastDiagnosticPositions[actor.Id] = actor.Position;
            _lastMovementStates[actor.Id] = (actor.GroundY, actor.IsGrounded, actor.IsMantling);
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

    internal FpsLiveArenaSnapshot? GetLiveArenaSnapshot() => _liveArena;

    internal FpsLiveMatchSnapshot? GetLiveMatchSnapshot()
    {
        lock (_sync)
            return _simulation is null
                ? null
                : CreateLiveMatchSnapshot(_simulation, _configuration.Extra.Fps.KillLimit);
    }

    internal static FpsLiveMatchSnapshot CreateLiveMatchSnapshot(FpsSimulation simulation,
        int killLimit)
    {
        var actors = simulation.Actors.OrderBy(actor => actor.Id).Select(actor =>
            new FpsLiveActorSnapshot(
                actor.Id,
                actor.Name,
                actor.Active && !actor.HumanControlled,
                actor.Active,
                actor.Dead,
                actor.Position,
                new Vector3(actor.HorizontalVelocity.X, actor.VerticalVelocity,
                    actor.HorizontalVelocity.Y),
                actor.Yaw,
                Math.Max(0, actor.Health),
                actor.Kills,
                actor.Deaths,
                actor.Score)).ToArray();
        return new FpsLiveMatchSnapshot(simulation.MatchState, simulation.ElapsedSeconds,
            simulation.RemainingSeconds, killLimit, simulation.WinnerId, actors);
    }

    private void OnClientConnected(ACTcpClient client, EventArgs args)
    {
        if (client.EntryCar.FpsRole == FpsSlotRole.Spectator) return;
        bool claimed;
        lock (_sync)
        {
            claimed = _simulation?.ClaimHuman(client.SessionId,
                requireLoadoutConfirmation: true) == true;
            if (claimed)
            {
                var actor = _simulation!.Actors.Single(actor => actor.Id == client.SessionId);
                _knownSpawnCounts[actor.Id] = actor.SpawnCount;
                _knownLoadoutStates[actor.Id] = LoadoutSignature(actor);
                _lastDiagnosticPositions[actor.Id] = actor.Position;
                _lastMovementStates[actor.Id] =
                    (actor.GroundY, actor.IsGrounded, actor.IsMantling);
                client.Logger.Information(
                    "FPS human actor claimed and awaiting loadout confirmation: actor={ActorId}, spawn={SpawnCount}, active={Active}",
                    actor.Id, actor.SpawnCount, actor.Active);
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
            _clientsWithAcceptedShot.Remove(client.SessionId);
            _neutralInputCounts.Remove(client.SessionId);
            _lastClientViewmodelDiagnosticTicks.Remove(client.SessionId);
            _lastMovementStates.Remove(client.SessionId);
            _knownLoadoutStates.Remove(client.SessionId);
        }
    }

    private void OnClientDiagnostic(ACTcpClient client, FpsClientDiagnosticPacket packet)
    {
        long now = Stopwatch.GetTimestamp();
        bool remoteActorKnown = false;
        Vector3 authoritativePosition = default;
        float authoritativeYaw = 0;
        lock (_sync)
        {
            if (_lastClientViewmodelDiagnosticTicks.TryGetValue(client.SessionId, out long previous)
                && Stopwatch.GetElapsedTime(previous, now) < TimeSpan.FromMilliseconds(500))
                return;
            _lastClientViewmodelDiagnosticTicks[client.SessionId] = now;
            var remoteActor = packet.RemoteActorId == byte.MaxValue ? null
                : _simulation?.Actors.SingleOrDefault(actor => actor.Id == packet.RemoteActorId);
            if (remoteActor is not null)
            {
                remoteActorKnown = true;
                authoritativePosition = remoteActor.Position;
                authoritativeYaw = remoteActor.Yaw;
            }
        }

        client.Logger.Information(
            "FPS client viewmodel diagnostic: pipeline={Pipeline}, flags={Flags}, updates={Completions}/{Attempts}, callbacks=frameBegin:{FrameBegin},draw3D:{Draw3D},drawUI:{DrawUI}, directDraw={DirectCompletions}/{DirectAttempts}, pending={DirectPending}, failures={DirectFailures}, stage={Stage}, intendedPosition={Position}, remoteActor={RemoteActorId}, remoteKnown={RemoteKnown}, remoteTarget={RemoteTarget}, remoteRender={RemoteRender}, remoteRenderError={RemoteRenderError:F3}, remoteTargetYaw={RemoteTargetYaw:F3}, remoteRenderYaw={RemoteRenderYaw:F3}, authoritativePosition={AuthoritativePosition}, snapshotError={SnapshotError:F3}, authoritativeYaw={AuthoritativeYaw:F3}, yawError={YawError:F3}",
            packet.Pipeline, packet.Flags, packet.Completions, packet.Attempts,
            packet.FrameBeginCalls, packet.Draw3DCalls, packet.DrawUiCalls,
            packet.DirectDrawCompletions, packet.DirectDrawAttempts, packet.DirectDrawPending,
            packet.DirectDrawFailures, packet.Stage, packet.Position, packet.RemoteActorId,
            remoteActorKnown, packet.RemoteTarget, packet.RemoteRender,
            Vector3.Distance(packet.RemoteTarget, packet.RemoteRender),
            packet.RemoteTargetYaw, packet.RemoteRenderYaw, authoritativePosition,
            remoteActorKnown ? Vector3.Distance(authoritativePosition, packet.RemoteTarget) : -1,
            authoritativeYaw, remoteActorKnown
                ? MathF.Abs(MathF.Atan2(MathF.Sin(authoritativeYaw - packet.RemoteTargetYaw),
                    MathF.Cos(authoritativeYaw - packet.RemoteTargetYaw))) : -1);
    }

    private void OnInput(ACTcpClient client, FpsInputPacket packet)
    {
        if (client.EntryCar.FpsRole == FpsSlotRole.Spectator) return;
        lock (_sync)
        {
            bool accepted = _simulation?.ApplyInput(client.SessionId, new FpsInputCommand(packet.Sequence,
                packet.Move, packet.Yaw, packet.Pitch, packet.Buttons,
                packet.SelectedSlot)) == true;
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
        if (packet.Protocol != 2)
        {
            client.Logger.Warning("FPS client protocol {Protocol} is not supported", packet.Protocol);
            _ = client.DisconnectAsync();
            return;
        }

        client.Logger.Information("FPS client ready with protocol {Protocol}", packet.Protocol);

        lock (_sync)
        {
            if (_simulation is null) return;
            SendLoadoutCatalog(client);
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
            SendGrenadeSnapshots(client);
            foreach (var actor in _simulation.Actors.OrderBy(actor => actor.Id))
            {
                client.SendPacket(new FpsAwardPacket
                {
                    ActorId = actor.Id,
                    TotalScore = actor.Score,
                });
                SendLoadoutState(client, actor);
            }
            foreach (var pickup in _simulation.Pickups.OrderBy(pickup => pickup.Id))
            {
                client.SendPacket(new FpsPickupPacket
                {
                    PickupId = pickup.Id,
                    State = FpsPickupState.Spawned,
                    WeaponType = pickup.WeaponType,
                    Position = pickup.Position,
                });
            }
        }
    }

    private void OnLoadoutSelect(ACTcpClient client, FpsLoadoutSelectPacket packet)
    {
        if (client.EntryCar.FpsRole == FpsSlotRole.Spectator) return;
        var loadout = new FpsLoadout((FpsWeaponType)packet.MainWeapon,
            (FpsLethalType)packet.Lethal, (FpsWeaponType)packet.SecondaryWeapon);
        lock (_sync)
        {
            if (_simulation is null) return;
            var result = _simulation.SelectLoadout(client.SessionId, loadout);
            client.SendPacket(new FpsLoadoutResultPacket
            {
                Result = result,
                MainWeapon = packet.MainWeapon,
                Lethal = packet.Lethal,
                SecondaryWeapon = packet.SecondaryWeapon,
            });
            if (result == FpsLoadoutResultCode.Applied)
            {
                var actor = _simulation.Actors.Single(actor => actor.Id == client.SessionId);
                _knownSpawnCounts[actor.Id] = actor.SpawnCount;
                _knownLoadoutStates[actor.Id] = LoadoutSignature(actor);
                _lastDiagnosticPositions[actor.Id] = actor.Position;
                _lastMovementStates[actor.Id] =
                    (actor.GroundY, actor.IsGrounded, actor.IsMantling);
                BroadcastLoadoutState(actor);
            }
            client.Logger.Information(
                "FPS loadout selection {Result}: main={MainWeapon}, lethal={Lethal}, secondary={SecondaryWeapon}",
                result, loadout.MainWeapon, loadout.Lethal, loadout.SecondaryWeapon);
        }
    }

    private void OnEnvironmentRequest(ACTcpClient client, FpsEnvironmentRequestPacket packet)
    {
        if (client.EntryCar.FpsRole == FpsSlotRole.Spectator)
            return;

        int controllerSessionId = _entryCarManager.ConnectedCars.Values
            .Where(entry => entry.FpsRole != FpsSlotRole.Spectator)
            .Select(entry => (int)entry.SessionId)
            .DefaultIfEmpty(-1)
            .Min();
        if (client.SessionId != controllerSessionId)
        {
            client.Logger.Warning(
                "Rejected FPS environment request from actor {ActorId}; controller actor is {ControllerActorId}",
                client.SessionId, controllerSessionId);
            return;
        }

        long now = Stopwatch.GetTimestamp();
        if (_lastEnvironmentRequestTicks != 0
            && Stopwatch.GetElapsedTime(_lastEnvironmentRequestTicks, now) < TimeSpan.FromMilliseconds(250))
            return;
        _lastEnvironmentRequestTicks = now;

        var weatherType = (WeatherFxType)packet.WeatherType;
        bool accepted = packet.TimeOfDaySeconds < 24 * 60 * 60
                        && _weatherManager.SetRaceControlEnvironment(weatherType,
                            (int)packet.TimeOfDaySeconds);
        if (accepted)
        {
            client.Logger.Information(
                "FPS environment changed: weather={WeatherType}, time={TimeOfDaySeconds}",
                weatherType, packet.TimeOfDaySeconds);
            BroadcastMatch();
        }
        else
        {
            client.Logger.Warning(
                "Rejected invalid FPS environment request: weather={WeatherType}, time={TimeOfDaySeconds}",
                packet.WeatherType, packet.TimeOfDaySeconds);
        }
    }

    private void OnUpdate(ACServer sender, EventArgs args)
    {
        lock (_sync)
        {
            if (_simulation is null) return;
            long simulationStart = Stopwatch.GetTimestamp();
            _simulation.Step(1f / _configuration.Server.RefreshRateHz);
            double simulationMilliseconds = Stopwatch.GetElapsedTime(simulationStart).TotalMilliseconds;
            _simulationMillisecondsTotal += simulationMilliseconds;
            if (simulationMilliseconds > _simulationMillisecondsMaximum)
            {
                _simulationMillisecondsMaximum = simulationMilliseconds;
                _maximumStepDiagnostics = _simulation.LastStepDiagnostics;
            }
            _simulationDurationSamples++;
            LogSpawnChanges();
            BroadcastLoadoutChanges();
            LogMovementTransitions();
            foreach (var diagnostic in _simulation.BotDiagnosticEvents)
            {
                if (diagnostic.Message.StartsWith("target=", StringComparison.Ordinal)
                    && _botsWithActiveBehavior.Add(diagnostic.ActorId))
                    Log.Information(
                        "FPS bot behavior active: actor={ActorId}, mode={Mode}, {Message}",
                        diagnostic.ActorId, diagnostic.Mode, diagnostic.Message);
                else if (diagnostic.Warning)
                    Log.Warning("FPS bot diagnostic: actor={ActorId}, mode={Mode}, {Message}",
                        diagnostic.ActorId, diagnostic.Mode, diagnostic.Message);
                else
                    Log.Debug("FPS bot diagnostic: actor={ActorId}, mode={Mode}, {Message}",
                        diagnostic.ActorId, diagnostic.Mode, diagnostic.Message);
            }
            foreach (var hit in _simulation.HitEvents)
            {
                Broadcast(new FpsHitPacket
                {
                    AttackerId = hit.AttackerId,
                    VictimId = hit.VictimId,
                    RemainingHealth = hit.RemainingHealth,
                    ItemId = hit.ItemId,
                });
            }
            foreach (var shot in _simulation.ShotEvents)
            {
                var shooter = _simulation.Actors.Single(actor => actor.Id == shot.ShooterId);
                if (_clientsWithAcceptedShot.Add(shot.ShooterId))
                    Log.Information(
                        "FPS rifle accepted first shot: actor={ActorId}, sequence={Sequence}, impact={Impact}, distance={Distance:F2}, ammo={Ammo}, reserveMagazines={ReserveMagazines}",
                        shot.ShooterId, shot.Sequence, shot.Impact, shot.Distance,
                        shooter.AmmoInMagazine, shooter.ReserveMagazines);
                else
                    Log.Debug(
                        "FPS rifle shot: actor={ActorId}, sequence={Sequence}, impact={Impact}, distance={Distance:F2}, ammo={Ammo}, reserveMagazines={ReserveMagazines}",
                        shot.ShooterId, shot.Sequence, shot.Impact, shot.Distance,
                        shooter.AmmoInMagazine, shooter.ReserveMagazines);
                Broadcast(new FpsShotPacket
                {
                    ShooterId = shot.ShooterId,
                    Sequence = shot.Sequence,
                    Origin = shot.Origin,
                    Direction = shot.Direction,
                    Distance = shot.Distance,
                    Impact = (byte)shot.Impact,
                    TargetId = shot.TargetId,
                    WeaponType = shot.WeaponType,
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
                    ItemId = kill.ItemId,
                });
            }
            foreach (var award in _simulation.AwardEvents)
            {
                Broadcast(new FpsAwardPacket
                {
                    ActorId = award.ActorId,
                    VictimId = award.VictimId,
                    Points = award.Points,
                    TotalScore = award.TotalScore,
                    Flags = (byte)award.Flags,
                });
            }
            foreach (var pickup in _simulation.PickupEvents)
            {
                if (pickup.State == FpsPickupState.Spawned)
                    Log.Debug("FPS weapon pickup spawned: pickup={PickupId}, weapon={WeaponType}, position={Position}",
                        pickup.PickupId, pickup.WeaponType, pickup.Position);
                else if (pickup.CollectorId != byte.MaxValue)
                    Log.Information("FPS weapon pickup collected: pickup={PickupId}, weapon={WeaponType}, actor={ActorId}",
                        pickup.PickupId, pickup.WeaponType, pickup.CollectorId);
                else
                    Log.Debug("FPS weapon pickup removed: pickup={PickupId}, weapon={WeaponType}",
                        pickup.PickupId, pickup.WeaponType);
                Broadcast(new FpsPickupPacket
                {
                    PickupId = pickup.PickupId,
                    State = pickup.State,
                    WeaponType = pickup.WeaponType,
                    CollectorId = pickup.CollectorId,
                    Position = pickup.Position,
                });
            }
            foreach (var explosion in _simulation.GrenadeExplosionEvents)
            {
                Broadcast(new FpsGrenadeExplodedPacket
                {
                    GrenadeId = explosion.GrenadeId,
                    OwnerId = explosion.OwnerId,
                    Type = explosion.Type,
                    Position = explosion.Position,
                });
            }

            int snapshotInterval = Math.Max(1, _configuration.Server.RefreshRateHz / 20);
            if (++_snapshotTicks >= snapshotInterval)
            {
                _snapshotTicks = 0;
                SendSnapshots();
                SendGrenadeSnapshots();
            }

            if (++_matchTicks >= _configuration.Server.RefreshRateHz)
            {
                _matchTicks = 0;
                LogHumanActorEffects();
                LogBotActorEffects();
                BroadcastMatch();
                if (_simulationDurationSamples > 0)
                {
                    Log.Debug(
                        "FPS simulation tick cost: average={Average:F3} ms, maximum={Maximum:F3} ms over {Samples} ticks; maximum phases human={Human:F3} ms bot={Bot:F3} ms post={Post:F3} ms path={Path:F3} ms plans={Plans}; surface queries={Queries} candidates={Candidates} max-query={MaximumCandidates} depenetration={Depenetration}",
                        _simulationMillisecondsTotal / _simulationDurationSamples,
                        _simulationMillisecondsMaximum, _simulationDurationSamples,
                        _maximumStepDiagnostics.HumanMilliseconds,
                        _maximumStepDiagnostics.BotMilliseconds,
                        _maximumStepDiagnostics.PostMilliseconds,
                        _maximumStepDiagnostics.PathMilliseconds,
                        _maximumStepDiagnostics.PathPlans,
                        _maximumStepDiagnostics.Surface.BroadphaseQueries,
                        _maximumStepDiagnostics.Surface.BroadphaseCandidates,
                        _maximumStepDiagnostics.Surface.MaximumCandidatesPerQuery,
                        _maximumStepDiagnostics.Surface.DepenetrationProbes);
                    _simulationMillisecondsTotal = 0;
                    _simulationMillisecondsMaximum = 0;
                    _maximumStepDiagnostics = default;
                    _simulationDurationSamples = 0;
                }
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
                "FPS actor input effect: actor={ActorId}, position={Position}, ground={GroundY:F3}, verticalVelocity={VerticalVelocity:F3}, grounded={Grounded}, mantling={Mantling}, geometryBlocked={GeometryBlocked}, delta={Delta}, distance={Distance:F3}, inputSequence={Sequence}, inputMove={Move}, inputButtons={Buttons}, hasInput={HasInput}, active={Active}, dead={Dead}, ammo={Ammo}, reserveMagazines={ReserveMagazines}, reloadRemaining={ReloadRemaining:F2}, match={MatchState}",
                actor.Id, actor.Position, actor.GroundY, actor.VerticalVelocity,
                actor.IsGrounded, actor.IsMantling, actor.GeometryBlocked,
                delta, delta.Length(), actor.LastInputSequence,
                actor.Input.Move, actor.Input.Buttons, actor.HasInput, actor.Active, actor.Dead,
                actor.AmmoInMagazine, actor.ReserveMagazines, actor.ReloadRemaining,
                _simulation.MatchState);
        }
    }

    private void LogBotActorEffects()
    {
        if (_simulation is null) return;
        foreach (var actor in _simulation.Actors.Where(actor => actor.Active
                                                               && !actor.HumanControlled)
                     .OrderBy(actor => actor.Id))
        {
            var previous = _lastDiagnosticPositions.GetValueOrDefault(actor.Id, actor.Position);
            var delta = actor.Position - previous;
            _lastDiagnosticPositions[actor.Id] = actor.Position;
            Log.Information(
                "FPS bot pose: actor={ActorId}, position={Position}, delta={Delta}, distance={Distance:F3}, yaw={Yaw:F3}, pitch={Pitch:F3}, mode={Mode}, target={TargetId}, path={PathIndex}/{PathCount}, wantsMovement={WantsMovement}, grounded={Grounded}, mantling={Mantling}, geometryBlocked={GeometryBlocked}, health={Health}, ammo={Ammo}",
                actor.Id, actor.Position, delta, delta.Length(), actor.Yaw, actor.Pitch,
                actor.BotMode, actor.BotTargetId, actor.BotPathIndex, actor.BotPath.Count,
                actor.BotWantsMovement, actor.IsGrounded, actor.IsMantling,
                actor.GeometryBlocked, actor.Health, actor.AmmoInMagazine);
        }
    }

    private void LogMovementTransitions()
    {
        if (_simulation is null) return;
        foreach (var actor in _simulation.Actors.Where(actor => actor.HumanControlled))
        {
            var current = (actor.GroundY, actor.IsGrounded, actor.IsMantling);
            if (_lastMovementStates.TryGetValue(actor.Id, out var previous)
                && (MathF.Abs(previous.GroundY - current.GroundY) > 0.05f
                    || previous.Grounded != current.IsGrounded
                    || previous.Mantling != current.IsMantling))
            {
                Log.Debug(
                    "FPS actor movement transition: actor={ActorId}, position={Position}, ground={PreviousGround:F3}->{Ground:F3}, grounded={PreviousGrounded}->{Grounded}, mantling={PreviousMantling}->{Mantling}, verticalVelocity={VerticalVelocity:F3}, inputSequence={Sequence}, inputMove={Move}, inputButtons={Buttons}, geometryBlocked={GeometryBlocked}",
                    actor.Id, actor.Position, previous.GroundY, current.GroundY,
                    previous.Grounded, current.IsGrounded, previous.Mantling,
                    current.IsMantling, actor.VerticalVelocity, actor.LastInputSequence,
                    actor.Input.Move, actor.Input.Buttons, actor.GeometryBlocked);
            }
            _lastMovementStates[actor.Id] = current;
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
                packet.ActionStates |= EncodeActionState(actor, index);
                packet.SpawnCounts[index] = actor.SpawnCount;
                packet.Positions[index] = actor.Position;
                packet.GroundYs[index] = actor.GroundY;
                packet.CollisionDirections[index] = EncodeCollisionDirection(actor.CollisionNormal);
                packet.Yaws[index] = actor.Yaw;
                packet.Pitches[index] = actor.Pitch;
                int health = Math.Clamp(actor.Health, 0, byte.MaxValue);
                int stamina = Math.Clamp((int)MathF.Round(actor.Stamina), 0,
                    (int)FpsSimulation.MaximumStamina);
                packet.Vitals[index] = (ushort)(health | stamina << 8);
                packet.Kills[index] = actor.Kills;
                packet.Deaths[index] = actor.Deaths;
                packet.Ammo[index] = (byte)Math.Clamp(actor.AmmoInMagazine, 0, byte.MaxValue);
                packet.ReserveMagazines[index] = (byte)Math.Clamp(actor.ReserveMagazines, 0,
                    byte.MaxValue);
                packet.ReloadRemaining[index] = actor.ReloadRemaining;
            }

            if (only is not null) only.SendPacketUdp(in packet);
            else
            {
                foreach (var client in _entryCarManager.ConnectedCars.Values.Select(car => car.Client).OfType<ACTcpClient>())
                    client.SendPacketUdp(in packet);
            }
        }
    }

    private void SendGrenadeSnapshots(ACTcpClient? only = null)
    {
        if (_simulation is null) return;
        var grenades = _simulation.Grenades.OrderBy(grenade => grenade.Id).ToArray();
        if (grenades.Length == 0)
        {
            Send(new FpsGrenadeSnapshotPacket { Sequence = ++_grenadeSnapshotSequence });
            return;
        }
        for (int offset = 0; offset < grenades.Length;
             offset += FpsGrenadeSnapshotPacket.Capacity)
        {
            var packet = new FpsGrenadeSnapshotPacket
            {
                Sequence = ++_grenadeSnapshotSequence,
                Count = (byte)Math.Min(FpsGrenadeSnapshotPacket.Capacity,
                    grenades.Length - offset),
            };
            for (int index = 0; index < packet.Count; index++)
            {
                var grenade = grenades[offset + index];
                packet.GrenadeIds[index] = grenade.Id;
                packet.OwnerIds[index] = grenade.OwnerId;
                packet.Types[index] = (byte)grenade.Type;
                packet.Flags[index] = (byte)((grenade.StuckToWorld ? 1 : 0)
                    | (grenade.AttachedActorId != byte.MaxValue ? 2 : 0));
                packet.Positions[index] = grenade.Position;
                packet.Velocities[index] = grenade.Velocity;
                packet.Remaining[index] = grenade.RemainingSeconds;
            }
            Send(packet);
        }

        void Send(FpsGrenadeSnapshotPacket packet)
        {
            if (only is not null) only.SendPacketUdp(in packet);
            else
            {
                foreach (var client in _entryCarManager.ConnectedCars.Values
                             .Select(car => car.Client).OfType<ACTcpClient>())
                    client.SendPacketUdp(in packet);
            }
        }
    }

    private void BroadcastLoadoutChanges()
    {
        if (_simulation is null) return;
        foreach (var actor in _simulation.Actors.Where(actor => actor.Active)
                     .OrderBy(actor => actor.Id))
        {
            ulong signature = LoadoutSignature(actor);
            if (_knownLoadoutStates.GetValueOrDefault(actor.Id) == signature) continue;
            _knownLoadoutStates[actor.Id] = signature;
            BroadcastLoadoutState(actor);
        }
    }

    private void SendLoadoutCatalog(ACTcpClient client)
    {
        var loadouts = _configuration.Extra.Fps.Loadouts;
        client.SendPacket(new FpsLoadoutCatalogPacket
        {
            AllowedMainWeapons = ItemMask(loadouts.AllowedMainWeapons.Select(value => (byte)value)),
            AllowedLethals = ItemMask(loadouts.AllowedLethals.Select(value => (byte)value)),
            AllowedSecondaryWeapons = ItemMask(loadouts.AllowedSecondaryWeapons.Select(value => (byte)value)),
            DefaultMainWeapon = (byte)loadouts.HumanDefault.MainWeapon,
            DefaultLethal = (byte)loadouts.HumanDefault.Lethal,
            DefaultSecondaryWeapon = (byte)loadouts.HumanDefault.SecondaryWeapon,
        });
    }

    private static void SendLoadoutState(ACTcpClient client, FpsActorState actor) =>
        client.SendPacket(CreateLoadoutState(actor));

    private void BroadcastLoadoutState(FpsActorState actor) =>
        Broadcast(CreateLoadoutState(actor));

    private static FpsLoadoutStatePacket CreateLoadoutState(FpsActorState actor) => new()
    {
        ActorId = actor.Id,
        MainWeapon = (byte)actor.Loadout.MainWeapon,
        Lethal = (byte)actor.Loadout.Lethal,
        SecondaryWeapon = (byte)actor.Loadout.SecondaryWeapon,
        ActiveSlot = actor.ActiveWeaponSlot,
        LethalsRemaining = (byte)Math.Clamp(actor.LethalsRemaining, 0, byte.MaxValue),
    };

    private static ulong LoadoutSignature(FpsActorState actor) =>
        (byte)actor.Loadout.MainWeapon
        | (ulong)(byte)actor.Loadout.Lethal << 8
        | (ulong)(byte)actor.Loadout.SecondaryWeapon << 16
        | (ulong)actor.ActiveWeaponSlot << 24
        | (ulong)(byte)Math.Clamp(actor.LethalsRemaining, 0, byte.MaxValue) << 32;

    private static uint ItemMask(IEnumerable<byte> values) =>
        values.Aggregate(0u, (mask, value) => value < 32 ? mask | 1u << value : mask);

    private static void ValidateLoadouts(FpsLoadoutConfiguration loadouts)
    {
        bool invalid = loadouts.AllowedMainWeapons.Count == 0
                       || loadouts.AllowedLethals.Count == 0
                       || loadouts.AllowedSecondaryWeapons.Count == 0
                       || loadouts.AllowedMainWeapons.Any(value => !Enum.IsDefined(value))
                       || loadouts.AllowedLethals.Any(value => !Enum.IsDefined(value))
                       || loadouts.AllowedSecondaryWeapons.Any(value => !Enum.IsDefined(value))
                       || !FpsItems.IsAllowed(loadouts,
                           FpsItems.FromConfiguration(loadouts.HumanDefault))
                       || !FpsItems.IsAllowed(loadouts,
                           FpsItems.FromConfiguration(loadouts.BotDefault));
        if (invalid)
            throw new ConfigurationException(
                "FPS loadouts require non-empty allow-lists and allowed human/bot defaults");
    }

    internal static byte EncodeCollisionDirection(Vector2 direction)
    {
        if (direction.LengthSquared() < 1e-8f) return byte.MaxValue;
        float angle = MathF.Atan2(direction.Y, direction.X);
        float normalized = (angle + MathF.PI) / (2 * MathF.PI);
        return (byte)Math.Clamp((int)MathF.Round(normalized * 254), 0, 254);
    }

    internal static uint EncodeActionState(FpsActorState actor, int index)
    {
        uint state = 0;
        if (actor.IsMantling)
        {
            state |= 1u << index;
            if (actor.MantleArcHeight > 0.5f)
                state |= 1u << (FpsSnapshotPacket.Capacity + index);
        }
        else if (actor.IsProne)
        {
            // The upper traversal bit is otherwise unused outside a mantle/vault.
            // Repeat prone here so CSP preview builds do not have to recover it solely
            // from bit 7 of the compact byte flags field. Packet layout and protocol v1
            // remain unchanged; older clients continue to use the existing flag.
            state |= 1u << (FpsSnapshotPacket.Capacity + index);
        }

        return state;
    }

    private void SendMatch(ACTcpClient client)
    {
        if (_simulation is null) return;
        client.SendPacket(new FpsMatchPacket
        {
            State = (byte)_simulation.MatchState,
            RemainingSeconds = _simulation.RemainingSeconds,
            KillLimit = (ushort)_configuration.Extra.Fps.KillLimit,
            MaximumHealth = (ushort)Math.Clamp(_configuration.Extra.Fps.Bots.Health, 1,
                ushort.MaxValue),
            WinnerId = _simulation.WinnerId,
            WeatherType = (byte)_weatherManager.CurrentWeather.UpcomingType.WeatherFxType,
            TimeOfDaySeconds = (uint)(_weatherManager.CurrentDateTime.Hour * 3600
                                      + _weatherManager.CurrentDateTime.Minute * 60
                                      + _weatherManager.CurrentDateTime.Second),
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
        {
            if (UsesUdpTransport<TPacket>()) client.SendPacketUdp(in packet);
            else client.SendPacket(packet);
        }
    }

    internal static bool UsesUdpTransport<TPacket>()
        where TPacket : Shared.Network.Packets.Outgoing.IOutgoingNetworkPacket
        => PacketTransport<TPacket>.Udp;

    private static class PacketTransport<TPacket>
    {
        public static readonly bool Udp =
            typeof(TPacket).GetCustomAttribute<OnlineEventAttribute>()?.Udp == true;
    }
}
