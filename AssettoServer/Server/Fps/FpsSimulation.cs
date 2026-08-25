using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AssettoServer.Network.ClientMessages;
using AssettoServer.Server.Configuration.Extra;
using AssettoServer.Server.Configuration.Kunos;

namespace AssettoServer.Server.Fps;

internal enum FpsMatchState : byte
{
    Waiting,
    Running,
    Finished,
}

internal sealed record FpsSimulationSlot(byte Id, string Name, FpsSlotRole Role,
    float? Difficulty = null, float? Aggression = null);

internal readonly record struct FpsInputCommand(uint Sequence, Vector2 Move, float Yaw,
    float Pitch, FpsInputButtons Buttons);

internal readonly record struct FpsKillEvent(byte KillerId, byte VictimId, ushort KillerKills,
    ushort VictimDeaths);
internal readonly record struct FpsHitEvent(byte AttackerId, byte VictimId, ushort RemainingHealth);

internal sealed class FpsActorState
{
    public required byte Id { get; init; }
    public required string Name { get; init; }
    public required FpsSlotRole Role { get; init; }
    public Vector3 Position { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public int Health { get; set; }
    public ushort Kills { get; set; }
    public ushort Deaths { get; set; }
    public bool Active { get; set; }
    public bool HumanControlled { get; set; }
    public bool Dead { get; set; }
    public float SpawnProtectionRemaining { get; set; }
    public float RespawnRemaining { get; set; }
    public float FireCooldown { get; set; }
    public uint LastInputSequence { get; set; }
    public bool HasInput { get; set; }
    public FpsInputCommand Input { get; set; }
    public uint SpawnCount { get; set; }
    public float Difficulty { get; init; }
    public float Aggression { get; init; }
    public float FinalScoreAttainedAtSeconds { get; set; }
}

internal sealed class FpsSimulation
{
    private const float WalkSpeed = 6;
    private const float SprintSpeed = 9;
    private const float RifleRange = 120;
    private const float RifleDamage = 34;
    private const float RifleInterval = 0.12f;
    private readonly FpsConfiguration _configuration;
    private readonly Random _random;
    private readonly Dictionary<byte, FpsActorState> _actors;
    private readonly List<FpsKillEvent> _killEvents = [];
    private readonly List<FpsHitEvent> _hitEvents = [];
    private int _nextSpawn;

    public FpsMatchState MatchState { get; private set; } = FpsMatchState.Running;
    public float RemainingSeconds { get; private set; }
    public float ElapsedSeconds { get; private set; }
    public byte WinnerId { get; private set; } = byte.MaxValue;
    public IReadOnlyCollection<FpsActorState> Actors => _actors.Values;
    public IReadOnlyList<FpsKillEvent> KillEvents => _killEvents;
    public IReadOnlyList<FpsHitEvent> HitEvents => _hitEvents;

    public FpsSimulation(FpsConfiguration configuration, IEnumerable<FpsSimulationSlot> slots,
        int seed = 1)
    {
        _configuration = configuration;
        _random = new Random(seed);
        RemainingSeconds = Math.Max(1, configuration.TimeLimitMinutes) * 60;
        _actors = slots.Where(slot => slot.Role != FpsSlotRole.Spectator).ToDictionary(slot => slot.Id,
            slot => new FpsActorState
            {
                Id = slot.Id,
                Name = string.IsNullOrWhiteSpace(slot.Name) ? $"Player {slot.Id + 1}" : slot.Name,
                Role = slot.Role,
                Health = configuration.Bots.Health,
                Active = slot.Role is FpsSlotRole.Auto or FpsSlotRole.Bot,
                HumanControlled = false,
                Difficulty = Math.Clamp(slot.Difficulty ?? Vary(configuration.Bots.Difficulty,
                    configuration.Bots.DifficultyVariancePercent, slot.Id, 17), 0, 1),
                Aggression = Math.Clamp(slot.Aggression ?? Vary(configuration.Bots.Aggression,
                    configuration.Bots.AggressionVariancePercent, slot.Id, 43), 0, 1),
            });

        foreach (var actor in _actors.Values.Where(actor => actor.Active)) Spawn(actor);
    }

    public bool ClaimHuman(byte actorId)
    {
        if (!_actors.TryGetValue(actorId, out var actor) || actor.Role == FpsSlotRole.Bot) return false;
        actor.HumanControlled = true;
        actor.Active = true;
        Spawn(actor);
        return true;
    }

    public void ReleaseHuman(byte actorId)
    {
        if (!_actors.TryGetValue(actorId, out var actor)) return;
        actor.HumanControlled = false;
        actor.HasInput = false;
        actor.Active = actor.Role == FpsSlotRole.Auto;
        if (actor.Active) Spawn(actor);
    }

    public bool ApplyInput(byte actorId, in FpsInputCommand command)
    {
        if (!_actors.TryGetValue(actorId, out var actor) || !actor.HumanControlled || actor.Dead
            || !Finite(command.Move) || !float.IsFinite(command.Yaw) || !float.IsFinite(command.Pitch)
            || command.Move.LengthSquared() > 1.05f
            || (actor.HasInput && !IsNewer(command.Sequence, actor.LastInputSequence)))
            return false;

        actor.LastInputSequence = command.Sequence;
        actor.HasInput = true;
        actor.Input = command with { Pitch = Math.Clamp(command.Pitch, -1.45f, 1.45f) };
        return true;
    }

    public void Step(float dt)
    {
        if (MatchState != FpsMatchState.Running || !float.IsFinite(dt) || dt <= 0) return;
        dt = Math.Min(dt, 0.05f);
        _killEvents.Clear();
        _hitEvents.Clear();
        RemainingSeconds = Math.Max(0, RemainingSeconds - dt);
        ElapsedSeconds += dt;

        foreach (var actor in _actors.Values.OrderBy(actor => actor.Id))
        {
            if (!actor.Active) continue;
            actor.FireCooldown = Math.Max(0, actor.FireCooldown - dt);
            actor.SpawnProtectionRemaining = Math.Max(0, actor.SpawnProtectionRemaining - dt);
            if (actor.Dead)
            {
                actor.RespawnRemaining -= dt;
                if (actor.RespawnRemaining <= 0) Spawn(actor);
                continue;
            }

            if (actor.HumanControlled) StepHuman(actor, dt);
            else StepBot(actor, dt);
        }

        SeparateActors();
        if (RemainingSeconds <= 0 || _actors.Values.Any(actor => actor.Kills >= _configuration.KillLimit))
            FinishMatch();
    }

    private void StepHuman(FpsActorState actor, float dt)
    {
        if (!actor.HasInput) return;
        actor.Yaw = NormalizeAngle(actor.Input.Yaw);
        actor.Pitch = actor.Input.Pitch;
        Move(actor, actor.Input.Move, actor.Input.Buttons.HasFlag(FpsInputButtons.Sprint), dt);
        if (actor.Input.Buttons.HasFlag(FpsInputButtons.Fire)) TryFire(actor);
    }

    private void StepBot(FpsActorState actor, float dt)
    {
        var target = _actors.Values.Where(other => other.Active && !other.Dead && other.Id != actor.Id)
            .MinBy(other => Vector3.DistanceSquared(actor.Position, other.Position));
        if (target is null) return;

        var delta = target.Position - actor.Position;
        var planar = new Vector2(delta.X, delta.Z);
        float distance = planar.Length();
        if (distance < 0.001f) return;
        actor.Yaw = MathF.Atan2(delta.X, delta.Z);
        actor.Pitch = Math.Clamp(MathF.Atan2(delta.Y + 0.9f, Math.Max(0.01f, distance)), -1.2f, 1.2f);
        float strafe = MathF.Sin((actor.Id + 1) * 1.7f + RemainingSeconds * (0.4f + actor.Aggression)) * 0.35f;
        Move(actor, distance > 9 ? new Vector2(strafe, 1) : new Vector2(strafe, 0),
            actor.Aggression > 0.65f, dt);
        float reactionChance = Math.Clamp((0.25f + actor.Difficulty * 0.75f) * dt * 12, 0, 1);
        if (distance < RifleRange && _random.NextSingle() < reactionChance) TryFire(actor);
    }

    private void Move(FpsActorState actor, Vector2 input, bool sprint, float dt)
    {
        if (input.LengthSquared() > 1) input = Vector2.Normalize(input);
        var forward = new Vector2(MathF.Sin(actor.Yaw), MathF.Cos(actor.Yaw));
        var right = new Vector2(forward.Y, -forward.X);
        var movement = forward * input.Y + right * input.X;
        float speed = sprint ? SprintSpeed : WalkSpeed;
        var position = actor.Position + new Vector3(movement.X, 0, movement.Y) * speed * dt;
        var min = _configuration.Arena.BoundsMin;
        var max = _configuration.Arena.BoundsMax;
        actor.Position = new Vector3(Math.Clamp(position.X, min.X, max.X), actor.Position.Y,
            Math.Clamp(position.Z, min.Z, max.Z));
    }

    private void TryFire(FpsActorState attacker)
    {
        if (attacker.FireCooldown > 0) return;
        attacker.FireCooldown = RifleInterval;
        float cosPitch = MathF.Cos(attacker.Pitch);
        var direction = Vector3.Normalize(new Vector3(MathF.Sin(attacker.Yaw) * cosPitch,
            MathF.Sin(attacker.Pitch), MathF.Cos(attacker.Yaw) * cosPitch));
        var origin = attacker.Position + Vector3.UnitY * 1.55f;
        FpsActorState? hit = null;
        float hitDistance = RifleRange;
        foreach (var candidate in _actors.Values)
        {
            if (!candidate.Active || candidate.Dead || candidate.Id == attacker.Id
                || candidate.SpawnProtectionRemaining > 0) continue;
            var center = candidate.Position + Vector3.UnitY * 0.9f;
            var toCenter = center - origin;
            float along = Vector3.Dot(toCenter, direction);
            if (along is <= 0 or > RifleRange || along >= hitDistance) continue;
            float radius = 0.48f;
            if (Vector3.DistanceSquared(origin + direction * along, center) > radius * radius) continue;
            hit = candidate;
            hitDistance = along;
        }

        if (hit is null) return;
        hit.Health -= (int)RifleDamage;
        _hitEvents.Add(new FpsHitEvent(attacker.Id, hit.Id, (ushort)Math.Max(0, hit.Health)));
        if (hit.Health > 0) return;
        hit.Dead = true;
        hit.Health = 0;
        hit.RespawnRemaining = _configuration.RespawnSeconds;
        hit.Deaths++;
        attacker.Kills++;
        attacker.FinalScoreAttainedAtSeconds = ElapsedSeconds;
        _killEvents.Add(new FpsKillEvent(attacker.Id, hit.Id, attacker.Kills, hit.Deaths));
    }

    private void Spawn(FpsActorState actor)
    {
        var spawns = _configuration.Arena.SpawnPoints;
        if (spawns.Count == 0) throw new InvalidOperationException("FPS arena has no spawn points");
        var spawn = spawns[_nextSpawn++ % spawns.Count];
        actor.Position = spawn.Position;
        actor.Yaw = spawn.YawRadians;
        actor.Pitch = 0;
        actor.Health = _configuration.Bots.Health;
        actor.Dead = false;
        actor.RespawnRemaining = 0;
        actor.SpawnProtectionRemaining = _configuration.SpawnProtectionSeconds;
        actor.FireCooldown = 0;
        actor.SpawnCount++;
    }

    private void SeparateActors()
    {
        var active = _actors.Values.Where(actor => actor.Active && !actor.Dead).OrderBy(actor => actor.Id).ToArray();
        for (int i = 0; i < active.Length; i++)
        for (int j = i + 1; j < active.Length; j++)
        {
            var delta = active[j].Position - active[i].Position with { Y = 0 };
            float distance = delta.Length();
            if (distance is >= 0.7f or < 0.0001f) continue;
            var correction = delta / distance * ((0.7f - distance) * 0.5f);
            active[i].Position -= correction;
            active[j].Position += correction;
        }
    }

    private void FinishMatch()
    {
        MatchState = FpsMatchState.Finished;
        WinnerId = _actors.Values.Where(actor => actor.Active)
            .OrderByDescending(actor => actor.Kills)
            .ThenBy(actor => actor.Deaths)
            .ThenBy(actor => actor.FinalScoreAttainedAtSeconds)
            .ThenBy(actor => actor.Id)
            .Select(actor => actor.Id)
            .FirstOrDefault(byte.MaxValue);
    }

    private static bool IsNewer(uint sequence, uint previous) =>
        sequence != previous && unchecked(sequence - previous) < 0x80000000u;
    private static bool Finite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
    private static float NormalizeAngle(float angle) => MathF.IEEERemainder(angle, MathF.Tau);
    private static float Vary(float baseline, float variancePercent, byte id, int salt)
    {
        uint hash = unchecked((uint)(id + 1) * 0x9E3779B1u ^ (uint)salt * 0x85EBCA6Bu);
        hash ^= hash >> 16;
        float signed = (hash & 0xffff) / 32767.5f - 1;
        return baseline + baseline * (variancePercent / 100f) * signed;
    }
}
