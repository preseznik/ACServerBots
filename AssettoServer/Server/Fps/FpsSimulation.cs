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

internal enum FpsStance : byte
{
    Standing,
    Crouching,
    Prone,
}

internal enum FpsBotMode : byte
{
    Acquire,
    Chase,
    Search,
    Strafe,
    Fire,
    Reload,
    Recover,
}

internal sealed record FpsSimulationSlot(byte Id, string Name, FpsSlotRole Role,
    float? Difficulty = null, float? Aggression = null);

internal readonly record struct FpsInputCommand(uint Sequence, Vector2 Move, float Yaw,
    float Pitch, FpsInputButtons Buttons);

internal readonly record struct FpsKillEvent(byte KillerId, byte VictimId, ushort KillerKills,
    ushort VictimDeaths);
internal readonly record struct FpsHitEvent(byte AttackerId, byte VictimId, ushort RemainingHealth);
internal enum FpsShotImpact : byte
{
    None,
    World,
    Actor,
}
internal readonly record struct FpsShotEvent(byte ShooterId, uint Sequence, Vector3 Origin,
    Vector3 Direction, float Distance, FpsShotImpact Impact, byte TargetId);
internal readonly record struct FpsBotDiagnosticEvent(byte ActorId, FpsBotMode Mode,
    string Message, bool Warning = false);

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
    public float WeaponHeat { get; set; }
    public int AmmoInMagazine { get; set; }
    public int ReserveMagazines { get; set; }
    public float ReloadRemaining { get; set; }
    public bool ReloadHeld { get; set; }
    public uint ShotSequence { get; set; }
    public uint LastInputSequence { get; set; }
    public bool HasInput { get; set; }
    public FpsInputCommand Input { get; set; }
    public uint SpawnCount { get; set; }
    public float GroundY { get; set; }
    public float VerticalVelocity { get; set; }
    public bool JumpHeld { get; set; }
    public float JumpHeldSeconds { get; set; }
    public bool TraversalConsumedForJumpHold { get; set; }
    public bool IsGrounded { get; set; }
    public FpsStance Stance { get; set; }
    public bool IsCrouching => Stance == FpsStance.Crouching;
    public bool IsProne => Stance == FpsStance.Prone;
    public bool CrouchHeld { get; set; }
    public float CrouchHeldSeconds { get; set; }
    public bool CrouchLatched { get; set; }
    public bool GeometryBlocked { get; set; }
    public Vector2 CollisionNormal { get; set; }
    public Vector3 LastSafePosition { get; set; }
    public float LastSafeGroundY { get; set; }
    public bool LastSafeWasGrounded { get; set; }
    public FpsStance LastSafeStance { get; set; }
    public bool HasLastSafePosition { get; set; }
    public Vector2 HorizontalVelocity { get; set; }
    public bool IsMantling { get; set; }
    public Vector3 MantleStart { get; set; }
    public Vector3 MantleTarget { get; set; }
    public float MantleElapsed { get; set; }
    public float MantleArcHeight { get; set; }
    public FpsStance MantleFinishStance { get; set; }
    public float Difficulty { get; init; }
    public float Aggression { get; init; }
    public float FinalScoreAttainedAtSeconds { get; set; }
    public FpsBotMode BotMode { get; set; }
    public byte BotTargetId { get; set; } = byte.MaxValue;
    public Vector3 BotLastKnownTargetPosition { get; set; }
    public float BotTargetVisibleSeconds { get; set; }
    public float BotSearchRemaining { get; set; }
    public float BotPlanRemaining { get; set; }
    public List<FpsNavigationStep> BotPath { get; } = [];
    public int BotPathIndex { get; set; }
    public float BotReactionRemaining { get; set; }
    public int BotBurstShotsRemaining { get; set; }
    public float BotBurstPauseRemaining { get; set; }
    public float BotStrafeRemaining { get; set; }
    public float BotStrafeDirection { get; set; } = 1;
    public Vector3 BotStuckAnchor { get; set; }
    public float BotStuckElapsed { get; set; }
    public int BotStuckFailures { get; set; }
    public bool BotWantsMovement { get; set; }
}

internal sealed class FpsSimulation
{
    private const float WalkSpeed = 6;
    private const float SprintSpeed = 9;
    private const float CrouchSpeed = 3.4f;
    private const float ProneSpeed = 1.8f;
    private const float ProneHoldSeconds = 0.65f;
    private const float MantleDuration = 0.45f;
    internal const float MantleHoldSeconds = 0.2f;
    private const float AirControlPerSecond = 1.5f;
    private const float JumpSpeed = 7.25f;
    private const float Gravity = 15;
    private const float RifleRange = 120;
    private const float RifleDamage = 34;
    private const float RifleInterval = 0.12f;
    private const float RifleTargetRadius = 0.42f;
    private const float RifleOcclusionEpsilon = 0.02f;
    private const float RifleMaximumSpreadRadians = 0.018f;
    private const float RifleHeatPerShot = 0.18f;
    private const float RifleHeatRecoveryPerSecond = 0.45f;
    internal const int RifleMagazineCapacity = 40;
    internal const int RifleInitialReserveMagazines = 4;
    internal const float RifleReloadSeconds = 1.8f;
    private readonly FpsConfiguration _configuration;
    private readonly Dictionary<byte, FpsActorState> _actors;
    private readonly List<FpsKillEvent> _killEvents = [];
    private readonly List<FpsHitEvent> _hitEvents = [];
    private readonly List<FpsShotEvent> _shotEvents = [];
    private readonly List<FpsBotDiagnosticEvent> _botDiagnosticEvents = [];
    private readonly FpsArenaSurface? _surface;
    private readonly FpsArenaNavigationAsset? _navigation;
    private readonly int _seed;
    private int _nextSpawn;

    public FpsMatchState MatchState { get; private set; } = FpsMatchState.Running;
    public float RemainingSeconds { get; private set; }
    public float ElapsedSeconds { get; private set; }
    public byte WinnerId { get; private set; } = byte.MaxValue;
    public IReadOnlyCollection<FpsActorState> Actors => _actors.Values;
    public IReadOnlyList<FpsKillEvent> KillEvents => _killEvents;
    public IReadOnlyList<FpsHitEvent> HitEvents => _hitEvents;
    public IReadOnlyList<FpsShotEvent> ShotEvents => _shotEvents;
    public IReadOnlyList<FpsBotDiagnosticEvent> BotDiagnosticEvents => _botDiagnosticEvents;

    public FpsSimulation(FpsConfiguration configuration, IEnumerable<FpsSimulationSlot> slots,
        int seed = 1, FpsArenaSurface? surface = null,
        FpsArenaNavigationAsset? navigation = null)
    {
        _configuration = configuration;
        _surface = surface;
        _navigation = navigation;
        _seed = seed;
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
        _killEvents.Clear();
        _hitEvents.Clear();
        _shotEvents.Clear();
        _botDiagnosticEvents.Clear();
        if (MatchState != FpsMatchState.Running || !float.IsFinite(dt) || dt <= 0) return;
        dt = Math.Min(dt, 0.05f);
        RemainingSeconds = Math.Max(0, RemainingSeconds - dt);
        ElapsedSeconds += dt;

        foreach (var actor in _actors.Values.OrderBy(actor => actor.Id))
        {
            if (!actor.Active) continue;
            actor.FireCooldown = Math.Max(0, actor.FireCooldown - dt);
            actor.WeaponHeat = Math.Max(0,
                actor.WeaponHeat - RifleHeatRecoveryPerSecond * dt);
            StepReload(actor, dt);
            actor.SpawnProtectionRemaining = Math.Max(0, actor.SpawnProtectionRemaining - dt);
            actor.GeometryBlocked = false;
            actor.CollisionNormal = Vector2.Zero;
            if (actor.Dead)
            {
                actor.RespawnRemaining -= dt;
                if (actor.RespawnRemaining <= 0) Spawn(actor);
                continue;
            }

            if (actor.IsMantling)
            {
                StepMantle(actor, dt);
                if (!actor.IsMantling) ValidateSafePose(actor);
                continue;
            }

            ValidateSafePose(actor);
            if (actor.HumanControlled) StepHuman(actor, dt);
            else StepBot(actor, dt);
            StepVertical(actor, dt);
            ValidateSafePose(actor);
        }

        SeparateActors();
        if (RemainingSeconds <= 0 || _actors.Values.Any(actor => actor.Kills >= _configuration.KillLimit))
            FinishMatch();
    }

    private void StepHuman(FpsActorState actor, float dt)
    {
        if (!actor.HasInput) return;
        bool reload = actor.Input.Buttons.HasFlag(FpsInputButtons.Reload);
        if (reload && !actor.ReloadHeld) BeginReload(actor);
        actor.ReloadHeld = reload;
        actor.Yaw = NormalizeAngle(actor.Input.Yaw);
        actor.Pitch = actor.Input.Pitch;
        bool crouch = actor.Input.Buttons.HasFlag(FpsInputButtons.Crouch);
        bool jump = actor.Input.Buttons.HasFlag(FpsInputButtons.Jump);
        if (!jump) actor.TraversalConsumedForJumpHold = false;
        bool jumpConsumed = UpdateStance(actor, crouch, jump, dt);
        Move(actor, actor.Input.Move, actor.Input.Buttons.HasFlag(FpsInputButtons.Sprint), dt);
        bool jumpStarted = jump && !actor.JumpHeld;
        actor.JumpHeldSeconds = jump ? actor.JumpHeldSeconds + dt : 0;
        // Traversal is an intentional held-jump action, not a collision recovery. Probe
        // only after a deliberate hold so an ordinary tap remains an ordinary jump.
        // Continue probing while held so an airborne player can still catch a ledge.
        if (!jumpConsumed && !actor.TraversalConsumedForJumpHold
            && actor.JumpHeldSeconds >= MantleHoldSeconds
            && TryBeginMantle(actor, actor.Input.Move))
        {
            actor.JumpHeld = jump;
            actor.JumpHeldSeconds = 0;
            actor.TraversalConsumedForJumpHold = true;
            if (actor.Input.Buttons.HasFlag(FpsInputButtons.Fire)) TryFire(actor);
            return;
        }
        if (!jumpConsumed && jumpStarted && actor.IsGrounded)
        {
            actor.VerticalVelocity = JumpSpeed;
            actor.IsGrounded = false;
        }
        actor.JumpHeld = jump;
        if (actor.Input.Buttons.HasFlag(FpsInputButtons.Fire)) TryFire(actor);
    }

    private static bool UpdateStance(FpsActorState actor, bool crouch, bool jump, float dt)
    {
        bool crouchPressed = crouch && !actor.CrouchHeld;
        bool jumpPressed = jump && !actor.JumpHeld;
        bool consumedJump = false;
        if (actor.Stance == FpsStance.Prone)
        {
            if (crouchPressed || jumpPressed)
            {
                actor.Stance = FpsStance.Crouching;
                actor.CrouchLatched = true;
                actor.CrouchHeldSeconds = 0;
                consumedJump = jumpPressed;
            }
        }
        else if (actor.Stance == FpsStance.Standing)
        {
            if (crouch)
            {
                actor.Stance = FpsStance.Crouching;
                actor.CrouchHeldSeconds = dt;
                actor.CrouchLatched = false;
            }
        }
        else if (actor.CrouchLatched)
        {
            if (crouchPressed)
            {
                actor.CrouchLatched = false;
                actor.CrouchHeldSeconds = dt;
            }
        }
        else if (crouch)
        {
            actor.CrouchHeldSeconds += dt;
            if (actor.CrouchHeldSeconds >= ProneHoldSeconds)
            {
                actor.Stance = FpsStance.Prone;
                actor.CrouchHeldSeconds = 0;
            }
        }
        else
        {
            actor.Stance = FpsStance.Standing;
            actor.CrouchHeldSeconds = 0;
        }

        actor.CrouchHeld = crouch;
        return consumedJump;
    }

    private static void StepVertical(FpsActorState actor, float dt)
    {
        if (actor.Position.Y <= actor.GroundY && actor.VerticalVelocity <= 0)
        {
            actor.Position = actor.Position with { Y = actor.GroundY };
            actor.VerticalVelocity = 0;
            actor.IsGrounded = true;
            return;
        }

        actor.IsGrounded = false;
        actor.VerticalVelocity -= Gravity * dt;
        float y = actor.Position.Y + actor.VerticalVelocity * dt;
        if (y <= actor.GroundY)
        {
            y = actor.GroundY;
            actor.VerticalVelocity = 0;
            actor.IsGrounded = true;
        }
        actor.Position = actor.Position with { Y = y };
    }

    private static void StepMantle(FpsActorState actor, float dt)
    {
        actor.MantleElapsed += dt;
        float t = Math.Clamp(actor.MantleElapsed / MantleDuration, 0, 1);
        float smooth = t * t * (3 - 2 * t);
        actor.Position = Vector3.Lerp(actor.MantleStart, actor.MantleTarget, smooth)
            + Vector3.UnitY * (MathF.Sin(t * MathF.PI) * actor.MantleArcHeight);
        if (t < 1) return;
        actor.Position = actor.MantleTarget;
        actor.GroundY = actor.MantleTarget.Y;
        actor.VerticalVelocity = 0;
        actor.HorizontalVelocity = Vector2.Zero;
        actor.IsGrounded = true;
        actor.IsMantling = false;
        actor.Stance = actor.MantleFinishStance;
        actor.CrouchLatched = actor.Stance == FpsStance.Crouching;
        actor.CrouchHeldSeconds = 0;
    }

    private bool TryBeginMantle(FpsActorState actor, Vector2 input)
    {
        if (_surface is null || actor.IsMantling) return false;
        if (input.LengthSquared() > 1) input = Vector2.Normalize(input);
        var forward = new Vector2(MathF.Sin(actor.Yaw), MathF.Cos(actor.Yaw));
        var right = new Vector2(forward.Y, -forward.X);
        var direction = forward * input.Y + right * input.X;
        if (direction.LengthSquared() < 0.01f) direction = forward;
        FpsStance startingStance = actor.Stance;
        Vector3 target;
        float arcHeight;
        FpsStance finishStance;
        if (_surface.TryFindMantle(actor.Position, direction, actor.GroundY,
                out target, out _))
        {
            arcHeight = 0.18f;
            // The crouched capsule is used while crossing the ledge, but a clear landing
            // must restore the stance the player approached with. Permanently latching a
            // normal standing player into crouch made every mantle feel like a speed bug
            // until crouch was tapped again.
            finishStance = startingStance == FpsStance.Standing
                           && !_surface.IsPositionBlocked(target, target.Y,
                               CollisionHeight(FpsStance.Standing))
                ? FpsStance.Standing
                : FpsStance.Crouching;
        }
        else if (_surface.TryFindVault(actor.Position, direction, actor.GroundY,
                     out target, out _))
        {
            arcHeight = FpsArenaSurface.MaximumVaultHeight;
            finishStance = startingStance;
        }
        else
        {
            return false;
        }
        actor.IsMantling = true;
        actor.MantleStart = actor.Position;
        actor.MantleTarget = target;
        actor.MantleElapsed = 0;
        actor.MantleArcHeight = arcHeight;
        actor.MantleFinishStance = finishStance;
        actor.VerticalVelocity = 0;
        actor.HorizontalVelocity = Vector2.Zero;
        actor.IsGrounded = false;
        actor.Stance = FpsStance.Crouching;
        return true;
    }

    private void StepBot(FpsActorState actor, float dt)
    {
        actor.BotPlanRemaining = Math.Max(0, actor.BotPlanRemaining - dt);
        actor.BotBurstPauseRemaining = Math.Max(0, actor.BotBurstPauseRemaining - dt);
        actor.BotStrafeRemaining = Math.Max(0, actor.BotStrafeRemaining - dt);
        var target = GetBotTarget(actor);
        if (target is null)
        {
            target = AcquireBotTarget(actor);
            if (target is null)
            {
                actor.BotMode = FpsBotMode.Acquire;
                actor.HorizontalVelocity = Vector2.Zero;
                UpdateBotStuck(actor, dt, false);
                return;
            }
            SetBotTarget(actor, target);
        }
        else if (actor.BotPlanRemaining <= 0)
        {
            var alternative = AcquireBotTarget(actor);
            float currentDistance = PlanarDistance(actor.Position, target.Position);
            float switchThreshold = Lerp(0.7f, 0.9f, actor.Aggression);
            if (alternative is not null && alternative.Id != target.Id
                && PlanarDistance(actor.Position, alternative.Position)
                < currentDistance * switchThreshold)
            {
                target = alternative;
                SetBotTarget(actor, target);
            }
        }

        bool visible = HasLineOfSight(actor, target);
        if (visible)
        {
            if (actor.BotTargetVisibleSeconds <= 0)
                actor.BotReactionRemaining = Math.Max(actor.BotReactionRemaining,
                    BotReactionDelaySeconds(actor.Difficulty));
            actor.BotLastKnownTargetPosition = target.Position;
            actor.BotSearchRemaining = Lerp(2, 8, actor.Aggression);
            actor.BotTargetVisibleSeconds += dt;
            actor.BotReactionRemaining = Math.Max(0, actor.BotReactionRemaining - dt);
        }
        else
        {
            actor.BotTargetVisibleSeconds = 0;
            actor.BotSearchRemaining = Math.Max(0, actor.BotSearchRemaining - dt);
            if (actor.BotSearchRemaining <= 0)
            {
                actor.BotTargetId = byte.MaxValue;
                actor.BotPath.Clear();
                actor.BotMode = FpsBotMode.Acquire;
                UpdateBotStuck(actor, dt, false);
                return;
            }
        }

        AimBot(actor, visible ? target.Position : actor.BotLastKnownTargetPosition,
            visible ? target.Stance : FpsStance.Standing, dt);
        float distance = PlanarDistance(actor.Position, target.Position);
        float preferredDistance = Lerp(24, 8, actor.Aggression);
        bool canFire = visible && actor.BotReactionRemaining <= 0
                       && target.SpawnProtectionRemaining <= 0
                       && distance <= RifleRange && BotAimIsReady(actor, target);
        bool wantsMovement;
        if (actor.ReloadRemaining > 0)
        {
            actor.BotMode = FpsBotMode.Reload;
            wantsMovement = StrafeBot(actor, target, dt, preferredDistance);
            if (!wantsMovement)
            {
                actor.BotMode = visible ? FpsBotMode.Chase : FpsBotMode.Search;
                wantsMovement = NavigateBot(actor, visible ? target.Position
                    : actor.BotLastKnownTargetPosition, dt);
            }
        }
        else if (canFire && distance <= preferredDistance * 1.35f)
        {
            actor.BotMode = FpsBotMode.Fire;
            wantsMovement = StrafeBot(actor, target, dt, preferredDistance);
            if (!wantsMovement)
            {
                actor.BotMode = FpsBotMode.Chase;
                wantsMovement = NavigateBot(actor, target.Position, dt);
            }
            StepBotFire(actor, target);
        }
        else
        {
            actor.BotMode = visible ? FpsBotMode.Chase : FpsBotMode.Search;
            wantsMovement = NavigateBot(actor, visible ? target.Position
                : actor.BotLastKnownTargetPosition, dt);
            if (canFire) StepBotFire(actor, target);
        }
        UpdateBotStuck(actor, dt, wantsMovement);
    }

    private FpsActorState? GetBotTarget(FpsActorState actor)
    {
        if (actor.BotTargetId != byte.MaxValue
            && _actors.GetValueOrDefault(actor.BotTargetId) is { Active: true, Dead: false } target
            && IsViableBotTarget(actor, target))
            return target;
        actor.BotTargetId = byte.MaxValue;
        actor.BotPath.Clear();
        actor.BotPathIndex = 0;
        return null;
    }

    private FpsActorState? AcquireBotTarget(FpsActorState actor) => _actors.Values
        .Where(candidate => candidate.Active && !candidate.Dead && candidate.Id != actor.Id
                            && IsViableBotTarget(actor, candidate))
        .OrderBy(candidate => PlanarDistance(actor.Position, candidate.Position))
        .ThenBy(candidate => candidate.Id)
        .FirstOrDefault();

    private bool IsViableBotTarget(FpsActorState actor, FpsActorState candidate)
    {
        return _navigation is null
               || _navigation.AreConnected(actor.Position, candidate.Position);
    }

    private void SetBotTarget(FpsActorState actor, FpsActorState target)
    {
        if (actor.BotTargetId == target.Id) return;
        actor.BotTargetId = target.Id;
        actor.BotLastKnownTargetPosition = target.Position;
        actor.BotReactionRemaining = BotReactionDelaySeconds(actor.Difficulty);
        actor.BotSearchRemaining = Lerp(2, 8, actor.Aggression);
        actor.BotPlanRemaining = 0;
        actor.BotPath.Clear();
        actor.BotPathIndex = 0;
        _botDiagnosticEvents.Add(new FpsBotDiagnosticEvent(actor.Id, FpsBotMode.Acquire,
            $"target={target.Id}"));
    }

    private bool HasLineOfSight(FpsActorState actor, FpsActorState target)
    {
        return TryGetClearTargetRay(actor, target, out _, out _, out _);
    }

    private bool TryGetClearTargetRay(FpsActorState actor, FpsActorState target,
        out Vector3 origin, out Vector3 direction, out float targetDistance)
    {
        origin = actor.Position + Vector3.UnitY * EyeHeight(actor.Stance);
        var targetPoint = target.Position
            + Vector3.UnitY * (CollisionHeight(target.Stance) * 0.55f);
        var delta = targetPoint - origin;
        float centerDistance = delta.Length();
        if (centerDistance < 0.01f || centerDistance > RifleRange)
        {
            direction = Vector3.UnitZ;
            targetDistance = 0;
            return false;
        }
        direction = delta / centerDistance;
        if (!TryRaycastActorCapsule(origin, direction, target.Position,
                CollisionHeight(target.Stance), out targetDistance)
            || targetDistance > RifleRange)
            return false;
        return _surface is null || !_surface.TryRaycast(origin, direction,
            targetDistance + RifleOcclusionEpsilon, out float worldDistance)
            || worldDistance + RifleOcclusionEpsilon >= targetDistance;
    }

    private void AimBot(FpsActorState actor, Vector3 targetPosition, FpsStance targetStance,
        float dt)
    {
        var origin = actor.Position + Vector3.UnitY * EyeHeight(actor.Stance);
        var targetPoint = targetPosition
            + Vector3.UnitY * (CollisionHeight(targetStance) * 0.55f);
        var delta = targetPoint - origin;
        float planar = MathF.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
        float desiredYaw = MathF.Atan2(delta.X, delta.Z);
        float desiredPitch = MathF.Atan2(delta.Y, MathF.Max(0.001f, planar));
        float radiansPerSecond = BotTrackingDegreesPerSecond(actor.Difficulty)
                                 * MathF.PI / 180;
        actor.Yaw = ApproachAngle(actor.Yaw, desiredYaw, radiansPerSecond * dt);
        actor.Pitch = Math.Clamp(Approach(actor.Pitch, desiredPitch, radiansPerSecond * dt),
            -1.45f, 1.45f);
    }

    private static bool BotAimIsReady(FpsActorState actor, FpsActorState target)
    {
        var origin = actor.Position + Vector3.UnitY * EyeHeight(actor.Stance);
        var targetPoint = target.Position
            + Vector3.UnitY * (CollisionHeight(target.Stance) * 0.55f);
        var toTarget = targetPoint - origin;
        if (toTarget.LengthSquared() < 1e-8f) return true;
        float cosPitch = MathF.Cos(actor.Pitch);
        var aim = new Vector3(MathF.Sin(actor.Yaw) * cosPitch,
            MathF.Sin(actor.Pitch), MathF.Cos(actor.Yaw) * cosPitch);
        float tolerance = Lerp(14, 5, actor.Difficulty) * MathF.PI / 180;
        return Vector3.Dot(aim, Vector3.Normalize(toTarget)) >= MathF.Cos(tolerance);
    }

    private bool NavigateBot(FpsActorState actor, Vector3 target, float dt)
    {
        if (_navigation is null)
        {
            var direct = PlanarDirection(actor.Position, target);
            if (direct.LengthSquared() < 0.01f) return false;
            MoveWorld(actor, direct, true, dt);
            return true;
        }

        if (actor.BotPath.Count == 0 && actor.BotPlanRemaining > 0) return false;
        if (actor.BotPlanRemaining <= 0 || actor.BotPathIndex >= actor.BotPath.Count)
        {
            actor.BotPlanRemaining = 0.2f;
            actor.BotPath.Clear();
            actor.BotPath.AddRange(_navigation.FindPath(actor.Position, target));
            actor.BotPathIndex = 0;
            if (actor.BotPath.Count == 0)
            {
                _botDiagnosticEvents.Add(new FpsBotDiagnosticEvent(actor.Id, actor.BotMode,
                    $"path-failed target={actor.BotTargetId}"));
                actor.BotPlanRemaining = 1;
                return false;
            }
        }

        while (actor.BotPathIndex < actor.BotPath.Count
               && PlanarDistance(actor.Position,
                   actor.BotPath[actor.BotPathIndex].Position) < 0.42f
               && MathF.Abs(actor.Position.Y
                   - actor.BotPath[actor.BotPathIndex].Position.Y) < 0.55f)
            actor.BotPathIndex++;
        if (actor.BotPathIndex >= actor.BotPath.Count) return false;
        var step = actor.BotPath[actor.BotPathIndex];
        var direction = PlanarDirection(actor.Position, step.Position);
        if (direction.LengthSquared() < 0.001f) return false;

        if (step.Kind is FpsNavigationLinkKind.Mantle or FpsNavigationLinkKind.Vault
            && actor.IsGrounded && !actor.IsMantling)
        {
            if (!BeginBotTraversal(actor, step))
            {
                actor.BotPlanRemaining = 0;
                actor.BotPath.Clear();
                _botDiagnosticEvents.Add(new FpsBotDiagnosticEvent(actor.Id, actor.BotMode,
                    $"traversal-failed kind={step.Kind}"));
                return false;
            }
            return true;
        }
        if (step.Kind == FpsNavigationLinkKind.Jump && actor.IsGrounded)
        {
            actor.VerticalVelocity = JumpSpeed;
            actor.IsGrounded = false;
        }
        MoveWorld(actor, direction, true, dt);
        return true;
    }

    private bool BeginBotTraversal(FpsActorState actor, FpsNavigationStep step)
    {
        if (_surface is not null && _surface.IsPositionBlocked(step.Position,
                step.Position.Y, CollisionHeight(FpsStance.Crouching))) return false;
        actor.IsMantling = true;
        actor.MantleStart = actor.Position;
        actor.MantleTarget = step.Position;
        actor.MantleElapsed = 0;
        actor.MantleArcHeight = step.Kind == FpsNavigationLinkKind.Vault
            ? FpsArenaSurface.MaximumVaultHeight : 0.18f;
        actor.MantleFinishStance = FpsStance.Standing;
        actor.VerticalVelocity = 0;
        actor.HorizontalVelocity = Vector2.Zero;
        actor.IsGrounded = false;
        actor.Stance = FpsStance.Crouching;
        actor.BotPathIndex++;
        return true;
    }

    private bool StrafeBot(FpsActorState actor, FpsActorState target, float dt,
        float preferredDistance)
    {
        if (actor.BotStrafeRemaining <= 0)
        {
            uint interval = (uint)Math.Max(0, (int)ElapsedSeconds);
            actor.BotStrafeDirection = DeterministicNoise(actor.Id, interval, 0x57AFu) >= 0
                ? 1 : -1;
            actor.BotStrafeRemaining = Lerp(0.45f, 1.6f, actor.Aggression);
        }
        var toTarget = PlanarDirection(actor.Position, target.Position);
        if (toTarget.LengthSquared() < 0.01f) return false;
        var movement = new Vector2(toTarget.Y, -toTarget.X) * actor.BotStrafeDirection;
        float distance = PlanarDistance(actor.Position, target.Position);
        if (distance > preferredDistance * 1.1f) movement += toTarget * 0.65f;
        else if (distance < preferredDistance * 0.65f) movement -= toTarget * 0.65f;
        if (movement.LengthSquared() > 1) movement = Vector2.Normalize(movement);
        var previousPosition = actor.Position;
        MoveWorld(actor, movement, actor.Aggression > 0.65f, dt);
        if (!actor.GeometryBlocked
            && PlanarDistance(previousPosition, actor.Position) >= 0.02f)
            return true;
        actor.BotStrafeDirection *= -1;
        actor.BotPlanRemaining = 0;
        actor.BotPath.Clear();
        actor.BotPathIndex = 0;
        return false;
    }

    private void StepBotFire(FpsActorState actor, FpsActorState target)
    {
        if (actor.BotBurstPauseRemaining > 0) return;
        if (actor.BotBurstShotsRemaining <= 0)
            actor.BotBurstShotsRemaining = BotBurstShotCount(actor.Difficulty);
        int ammunition = actor.AmmoInMagazine;
        TryFire(actor, target);
        if (actor.AmmoInMagazine >= ammunition) return;
        actor.BotBurstShotsRemaining--;
        if (actor.BotBurstShotsRemaining > 0) return;
        actor.BotBurstPauseRemaining = BotBurstPauseSeconds(actor.Difficulty);
    }

    private void UpdateBotStuck(FpsActorState actor, float dt, bool wantsMovement)
    {
        actor.BotWantsMovement = wantsMovement;
        if (!wantsMovement || actor.IsMantling || !actor.IsGrounded)
        {
            actor.BotStuckAnchor = actor.Position;
            actor.BotStuckElapsed = 0;
            return;
        }
        actor.BotStuckElapsed += dt;
        if (actor.BotStuckElapsed < 1.5f) return;
        float movement = PlanarDistance(actor.BotStuckAnchor, actor.Position);
        actor.BotStuckAnchor = actor.Position;
        actor.BotStuckElapsed = 0;
        if (movement >= 0.25f)
        {
            actor.BotStuckFailures = 0;
            return;
        }
        actor.BotStuckFailures++;
        actor.BotMode = FpsBotMode.Recover;
        actor.BotPlanRemaining = 0;
        actor.BotPath.Clear();
        _botDiagnosticEvents.Add(new FpsBotDiagnosticEvent(actor.Id, FpsBotMode.Recover,
            $"stuck-replan attempt={actor.BotStuckFailures}"));
        if (actor.BotStuckFailures < 3) return;
        _botDiagnosticEvents.Add(new FpsBotDiagnosticEvent(actor.Id, FpsBotMode.Recover,
            "stuck-respawn", true));
        Spawn(actor);
    }

    private void MoveWorld(FpsActorState actor, Vector2 movement, bool sprint, float dt)
    {
        if (movement.LengthSquared() > 1) movement = Vector2.Normalize(movement);
        float speed = actor.IsProne ? ProneSpeed
            : actor.IsCrouching ? CrouchSpeed : sprint ? SprintSpeed : WalkSpeed;
        speed *= BotMovementSpeedScale(actor.Difficulty);
        var desiredVelocity = movement * speed;
        actor.HorizontalVelocity = actor.IsGrounded
            ? desiredVelocity
            : Vector2.Lerp(actor.HorizontalVelocity, desiredVelocity,
                Math.Clamp(AirControlPerSecond * dt, 0, 1));
        var position = actor.Position
            + new Vector3(actor.HorizontalVelocity.X, 0, actor.HorizontalVelocity.Y) * dt;
        var min = _configuration.Arena.BoundsMin;
        var max = _configuration.Arena.BoundsMax;
        TryMoveActor(actor, new Vector3(Math.Clamp(position.X, min.X, max.X),
            actor.Position.Y, Math.Clamp(position.Z, min.Z, max.Z)));
    }

    private void Move(FpsActorState actor, Vector2 input, bool sprint, float dt)
    {
        if (input.LengthSquared() > 1) input = Vector2.Normalize(input);
        var forward = new Vector2(MathF.Sin(actor.Yaw), MathF.Cos(actor.Yaw));
        var right = new Vector2(forward.Y, -forward.X);
        var movement = forward * input.Y + right * input.X;
        float speed = actor.IsProne ? ProneSpeed
            : actor.IsCrouching ? CrouchSpeed
            : sprint ? SprintSpeed : WalkSpeed;
        var desiredVelocity = movement * speed;
        actor.HorizontalVelocity = actor.IsGrounded
            ? desiredVelocity
            : Vector2.Lerp(actor.HorizontalVelocity, desiredVelocity,
                Math.Clamp(AirControlPerSecond * dt, 0, 1));
        var position = actor.Position
            + new Vector3(actor.HorizontalVelocity.X, 0, actor.HorizontalVelocity.Y) * dt;
        var min = _configuration.Arena.BoundsMin;
        var max = _configuration.Arena.BoundsMax;
        TryMoveActor(actor, new Vector3(Math.Clamp(position.X, min.X, max.X), actor.Position.Y,
            Math.Clamp(position.Z, min.Z, max.Z)));
    }

    private void TryMoveActor(FpsActorState actor, Vector3 desired)
    {
        if (_surface is null)
        {
            actor.Position = desired;
            return;
        }

        if (_surface.IsPositionBlocked(actor.Position, actor.GroundY,
                CollisionHeight(actor.Stance)))
        {
            RestoreSafePose(actor);
            actor.GeometryBlocked = true;
            return;
        }

        var previous = actor.Position;
        float actorHeight = CollisionHeight(actor.Stance);
        Vector3 resolved;
        float groundY;
        bool resolvedMove;
        if (actor.IsGrounded)
        {
            resolvedMove = _surface.TryResolveMove(actor.Position, desired, actor.GroundY,
                actorHeight, out resolved, out groundY);
            float supportedDistance = Vector2.Distance(new Vector2(previous.X, previous.Z),
                new Vector2(resolved.X, resolved.Z));
            float requestedPlanarDistance = Vector2.Distance(new Vector2(previous.X, previous.Z),
                new Vector2(desired.X, desired.Z));
            if (requestedPlanarDistance > supportedDistance + 0.01f
                && _surface.TryResolveAirMove(resolved, desired, actorHeight,
                    out var airResolved, out float landingGroundY)
                && Vector2.Distance(new Vector2(previous.X, previous.Z),
                    new Vector2(airResolved.X, airResolved.Z)) > supportedDistance + 0.001f)
            {
                resolved = airResolved;
                groundY = landingGroundY;
                actor.IsGrounded = false;
                actor.VerticalVelocity = MathF.Min(0, actor.VerticalVelocity);
                resolvedMove = true;
            }
        }
        else
        {
            resolvedMove = _surface.TryResolveAirMove(actor.Position, desired, actorHeight,
                out resolved, out groundY);
        }

        if (!resolvedMove)
        {
            actor.GeometryBlocked = true;
            actor.CollisionNormal = NormalizedPlanarDelta(actor.Position, desired);
            actor.HorizontalVelocity = Vector2.Zero;
            return;
        }
        float requestedDistance = Vector2.Distance(new Vector2(previous.X, previous.Z),
            new Vector2(desired.X, desired.Z));
        float resolvedDistance = Vector2.Distance(new Vector2(previous.X, previous.Z),
            new Vector2(resolved.X, resolved.Z));
        if (requestedDistance > 0.001f && resolvedDistance + 0.01f < requestedDistance)
        {
            actor.GeometryBlocked = true;
            actor.CollisionNormal = NormalizedPlanarDelta(resolved, desired);
        }
        actor.GroundY = groundY;
        actor.Position = actor.IsGrounded ? resolved with { Y = groundY } : resolved;
        if (actor.IsGrounded) RememberSafePose(actor);
    }

    private static Vector2 NormalizedPlanarDelta(Vector3 from, Vector3 to)
    {
        var delta = new Vector2(to.X - from.X, to.Z - from.Z);
        return delta.LengthSquared() > 1e-8f ? Vector2.Normalize(delta) : Vector2.Zero;
    }

    private void ValidateSafePose(FpsActorState actor)
    {
        if (_surface is null || actor.Dead || actor.IsMantling) return;
        float actorHeight = CollisionHeight(actor.Stance);
        if (!_surface.IsPositionBlocked(actor.Position, actor.GroundY, actorHeight))
        {
            if (actor.IsGrounded) RememberSafePose(actor);
            return;
        }

        actor.GeometryBlocked = true;
        if (!actor.IsGrounded
            && _surface.TryDepenetrateAir(actor.Position, actorHeight,
                out var resolved, out float groundY))
        {
            actor.Position = resolved;
            actor.GroundY = groundY;
            return;
        }
        RestoreSafePose(actor);
    }

    private void RestoreSafePose(FpsActorState actor)
    {
        if (_surface is null) return;
        if (actor.HasLastSafePosition
            && !_surface.IsPositionBlocked(actor.LastSafePosition, actor.LastSafeGroundY,
                CollisionHeight(actor.LastSafeStance)))
        {
            actor.Position = actor.LastSafePosition;
            actor.GroundY = actor.LastSafeGroundY;
            actor.IsGrounded = actor.LastSafeWasGrounded;
            actor.Stance = actor.LastSafeStance;
        }
        else if (_surface.TryDepenetrate(actor.Position, actor.GroundY,
                     CollisionHeight(actor.Stance), out var resolved, out float groundY))
        {
            actor.Position = resolved;
            actor.GroundY = groundY;
            actor.IsGrounded = true;
            RememberSafePose(actor);
        }
        actor.HorizontalVelocity = Vector2.Zero;
        actor.VerticalVelocity = 0;
    }

    private void RememberSafePose(FpsActorState actor)
    {
        actor.LastSafePosition = actor.Position;
        actor.LastSafeGroundY = actor.GroundY;
        actor.LastSafeWasGrounded = actor.IsGrounded;
        actor.LastSafeStance = actor.Stance;
        actor.HasLastSafePosition = true;
    }

    private static float CollisionHeight(FpsStance stance) => stance switch
    {
        FpsStance.Prone => 0.65f,
        FpsStance.Crouching => 1.15f,
        _ => 1.8f,
    };

    private static float EyeHeight(FpsStance stance) => stance switch
    {
        FpsStance.Prone => 0.42f,
        FpsStance.Crouching => 1.05f,
        _ => 1.65f,
    };

    private void TryFire(FpsActorState attacker, FpsActorState? intendedTarget = null)
    {
        if (attacker.FireCooldown > 0 || attacker.ReloadRemaining > 0) return;
        if (attacker.AmmoInMagazine <= 0)
        {
            BeginReload(attacker);
            return;
        }
        attacker.FireCooldown = RifleInterval;
        attacker.AmmoInMagazine--;
        uint shotSequence = ++attacker.ShotSequence;
        float spread = attacker.WeaponHeat * RifleMaximumSpreadRadians;
        float baseYaw = attacker.Yaw;
        float basePitch = attacker.Pitch;
        if (intendedTarget is not null)
        {
            float aimError = BotAimErrorDegrees(attacker.Difficulty) * MathF.PI / 180;
            baseYaw += DeterministicNoise(attacker.Id, shotSequence, 0xB071u) * aimError;
            basePitch += DeterministicNoise(attacker.Id, shotSequence, 0xA19Du)
                * aimError * 0.65f;
        }
        float shotYaw = baseYaw + ShotNoise(attacker.Id, shotSequence, 0xA511E9B3u) * spread;
        float shotPitch = basePitch
            + ShotNoise(attacker.Id, shotSequence, 0x63D83595u) * spread;
        attacker.WeaponHeat = Math.Min(1, attacker.WeaponHeat + RifleHeatPerShot);
        float cosPitch = MathF.Cos(shotPitch);
        var direction = Vector3.Normalize(new Vector3(MathF.Sin(shotYaw) * cosPitch,
            MathF.Sin(shotPitch), MathF.Cos(shotYaw) * cosPitch));
        var origin = attacker.Position + Vector3.UnitY * EyeHeight(attacker.Stance);
        FpsActorState? hit = null;
        float hitDistance = RifleRange;
        bool hitWorld = false;
        if (_surface is not null && _surface.TryRaycast(origin, direction, RifleRange,
                out float surfaceDistance))
        {
            hitDistance = surfaceDistance;
            hitWorld = true;
        }
        foreach (var candidate in _actors.Values)
        {
            if (!candidate.Active || candidate.Dead || candidate.Id == attacker.Id
                || candidate.SpawnProtectionRemaining > 0) continue;
            if (!TryRaycastActorCapsule(origin, direction, candidate.Position,
                    CollisionHeight(candidate.Stance), out float along)
                || along <= 0 || along > RifleRange || along >= hitDistance) continue;
            hit = candidate;
            hitDistance = along;
        }

        _shotEvents.Add(new FpsShotEvent(attacker.Id, shotSequence, origin, direction,
            hitDistance, hit is not null ? FpsShotImpact.Actor
                : hitWorld ? FpsShotImpact.World : FpsShotImpact.None,
            hit?.Id ?? byte.MaxValue));

        if (attacker.AmmoInMagazine == 0) BeginReload(attacker);

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

    private static bool TryRaycastActorCapsule(Vector3 origin, Vector3 direction,
        Vector3 position, float height, out float distance)
    {
        float bestDistance = float.PositiveInfinity;
        float inset = MathF.Min(RifleTargetRadius, height * 0.5f);
        float bottomY = position.Y + inset;
        float topY = position.Y + height - inset;
        float offsetX = origin.X - position.X;
        float offsetZ = origin.Z - position.Z;
        float planarDirectionLengthSquared = direction.X * direction.X
                                             + direction.Z * direction.Z;
        if (planarDirectionLengthSquared > 1e-8f)
        {
            float b = 2 * (offsetX * direction.X + offsetZ * direction.Z);
            float c = offsetX * offsetX + offsetZ * offsetZ
                      - RifleTargetRadius * RifleTargetRadius;
            float discriminant = b * b - 4 * planarDirectionLengthSquared * c;
            if (discriminant >= 0)
            {
                float root = MathF.Sqrt(discriminant);
                float denominator = 2 * planarDirectionLengthSquared;
                SelectCylinderHit((-b - root) / denominator);
                SelectCylinderHit((-b + root) / denominator);
            }
        }

        SelectSphereHit(new Vector3(position.X, bottomY, position.Z));
        if (topY > bottomY + 1e-5f)
            SelectSphereHit(new Vector3(position.X, topY, position.Z));
        distance = bestDistance;
        return float.IsFinite(bestDistance);

        void SelectCylinderHit(float candidateDistance)
        {
            if (candidateDistance < 0 || candidateDistance >= bestDistance) return;
            float y = origin.Y + direction.Y * candidateDistance;
            if (y >= bottomY && y <= topY) bestDistance = candidateDistance;
        }

        void SelectSphereHit(Vector3 center)
        {
            var fromCenter = origin - center;
            float b = Vector3.Dot(fromCenter, direction);
            float c = fromCenter.LengthSquared() - RifleTargetRadius * RifleTargetRadius;
            if (c <= 0)
            {
                bestDistance = 0;
                return;
            }
            float discriminant = b * b - c;
            if (discriminant < 0) return;
            float candidateDistance = -b - MathF.Sqrt(discriminant);
            if (candidateDistance >= 0 && candidateDistance < bestDistance)
                bestDistance = candidateDistance;
        }
    }

    private static void BeginReload(FpsActorState actor)
    {
        if (actor.ReloadRemaining > 0 || actor.AmmoInMagazine >= RifleMagazineCapacity
            || actor.ReserveMagazines <= 0)
            return;
        actor.ReloadRemaining = RifleReloadSeconds;
    }

    private static void StepReload(FpsActorState actor, float dt)
    {
        if (actor.ReloadRemaining <= 0) return;
        actor.ReloadRemaining = Math.Max(0, actor.ReloadRemaining - dt);
        if (actor.ReloadRemaining > 0) return;
        actor.AmmoInMagazine = RifleMagazineCapacity;
        actor.ReserveMagazines--;
    }

    private void Spawn(FpsActorState actor)
    {
        var spawns = _configuration.Arena.SpawnPoints;
        if (spawns.Count == 0) throw new InvalidOperationException("FPS arena has no spawn points");
        var spawn = ChooseSpawn(actor);
        float groundY = spawn.Position.Y;
        if (_surface is not null)
            _surface.TryGetGroundHeight(spawn.Position.X, spawn.Position.Z, spawn.Position.Y,
                out groundY);
        actor.Position = spawn.Position with { Y = groundY };
        actor.GroundY = groundY;
        actor.VerticalVelocity = 0;
        actor.JumpHeld = false;
        actor.JumpHeldSeconds = 0;
        actor.TraversalConsumedForJumpHold = false;
        actor.IsGrounded = true;
        actor.Stance = FpsStance.Standing;
        actor.CrouchHeld = false;
        actor.CrouchHeldSeconds = 0;
        actor.CrouchLatched = false;
        actor.GeometryBlocked = false;
        actor.HorizontalVelocity = Vector2.Zero;
        actor.IsMantling = false;
        actor.MantleElapsed = 0;
        actor.Yaw = spawn.YawRadians;
        actor.Pitch = 0;
        actor.Health = _configuration.Bots.Health;
        actor.Dead = false;
        actor.RespawnRemaining = 0;
        actor.SpawnProtectionRemaining = _configuration.SpawnProtectionSeconds;
        actor.FireCooldown = 0;
        actor.WeaponHeat = 0;
        actor.AmmoInMagazine = RifleMagazineCapacity;
        actor.ReserveMagazines = RifleInitialReserveMagazines;
        actor.ReloadRemaining = 0;
        actor.ReloadHeld = false;
        actor.SpawnCount++;
        actor.BotMode = FpsBotMode.Acquire;
        actor.BotTargetId = byte.MaxValue;
        actor.BotTargetVisibleSeconds = 0;
        actor.BotSearchRemaining = 0;
        actor.BotPlanRemaining = 0;
        actor.BotPath.Clear();
        actor.BotPathIndex = 0;
        actor.BotReactionRemaining = 0;
        actor.BotBurstShotsRemaining = 0;
        actor.BotBurstPauseRemaining = 0;
        actor.BotStrafeRemaining = 0;
        actor.BotStuckAnchor = actor.Position;
        actor.BotStuckElapsed = 0;
        actor.BotStuckFailures = 0;
        actor.BotWantsMovement = false;
        actor.HasLastSafePosition = false;
        bool safe = _surface is null || !_surface.IsPositionBlocked(actor.Position,
            actor.GroundY, CollisionHeight(actor.Stance));
        if (!safe && _surface is not null
            && _surface.TryDepenetrate(actor.Position, actor.GroundY,
                CollisionHeight(actor.Stance), out var resolved, out float safeGroundY))
        {
            actor.Position = resolved;
            actor.GroundY = safeGroundY;
            safe = true;
        }
        if (safe) RememberSafePose(actor);
    }

    private FpsSpawnConfiguration ChooseSpawn(FpsActorState actor)
    {
        var spawns = _configuration.Arena.SpawnPoints;
        var opponents = _actors.Values.Where(candidate => candidate.Id != actor.Id
                                                          && candidate.Active && !candidate.Dead
                                                          && candidate.SpawnCount > 0).ToArray();
        if (opponents.Length == 0)
        {
            for (int offset = 0; offset < spawns.Count; offset++)
            {
                int index = (_nextSpawn + offset) % spawns.Count;
                if (_navigation is not null && index < _navigation.SpawnNodes.Count
                    && _navigation.Nodes[_navigation.SpawnNodes[index]].Component
                    != _navigation.PrimaryComponent)
                    continue;
                _nextSpawn = index + 1;
                return spawns[index];
            }
            return spawns[_nextSpawn++ % spawns.Count];
        }
        int bestIndex = -1;
        float bestScore = float.NegativeInfinity;
        for (int index = 0; index < spawns.Count; index++)
        {
            if (_navigation is not null && index < _navigation.SpawnNodes.Count
                && _navigation.Nodes[_navigation.SpawnNodes[index]].Component
                != _navigation.PrimaryComponent)
                continue;
            var position = spawns[index].Position;
            float minimumDistance = opponents.Min(opponent =>
                PlanarDistance(position, opponent.Position));
            bool visible = opponents.Any(opponent => SpawnHasLineOfSight(position, opponent));
            float score = minimumDistance + (visible ? 0 : 1000);
            if (score <= bestScore) continue;
            bestScore = score;
            bestIndex = index;
        }
        if (bestIndex < 0) bestIndex = _nextSpawn++ % spawns.Count;
        return spawns[bestIndex];
    }

    private bool SpawnHasLineOfSight(Vector3 spawn, FpsActorState opponent)
    {
        if (_surface is null) return true;
        var origin = spawn + Vector3.UnitY * EyeHeight(FpsStance.Standing);
        var targetPoint = opponent.Position
            + Vector3.UnitY * (CollisionHeight(opponent.Stance) * 0.55f);
        var delta = targetPoint - origin;
        float centerDistance = delta.Length();
        if (centerDistance <= 0.01f) return false;
        var direction = delta / centerDistance;
        return TryRaycastActorCapsule(origin, direction, opponent.Position,
                   CollisionHeight(opponent.Stance), out float targetDistance)
               && (!_surface.TryRaycast(origin, direction,
                       targetDistance + RifleOcclusionEpsilon, out float worldDistance)
                   || worldDistance + RifleOcclusionEpsilon >= targetDistance);
    }

    private void SeparateActors()
    {
        var active = _actors.Values.Where(actor => actor.Active && !actor.Dead).OrderBy(actor => actor.Id).ToArray();
        for (int i = 0; i < active.Length; i++)
        for (int j = i + 1; j < active.Length; j++)
        {
            var delta = active[j].Position - active[i].Position with { Y = 0 };
            float distance = delta.Length();
            if (distance >= 0.7f) continue;
            Vector3 direction;
            if (distance < 0.0001f)
            {
                // Deterministically split exact overlaps. Ignoring this case leaves two
                // pursuing bots sharing a capsule and aiming vertically at each other.
                float angle = ((active[i].Id * 31 + active[j].Id * 17) & 15)
                              * MathF.Tau / 16;
                direction = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle));
            }
            else
            {
                direction = delta / distance;
            }
            var correction = direction * ((0.7f - distance) * 0.5f);
            TryMoveActor(active[i], active[i].Position - correction);
            TryMoveActor(active[j], active[j].Position + correction);
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

    private float DeterministicNoise(byte actorId, uint interval, uint salt)
    {
        uint value = unchecked(interval * 0x9E3779B1u
                               + (uint)(actorId + 1) * 0x85EBCA6Bu
                               + (uint)_seed * 0xC2B2AE35u + salt);
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return (value & 0x00FFFFFFu) / 8388607.5f - 1;
    }

    private static Vector2 PlanarDirection(Vector3 from, Vector3 to)
    {
        var direction = new Vector2(to.X - from.X, to.Z - from.Z);
        return direction.LengthSquared() > 1e-8f ? Vector2.Normalize(direction) : Vector2.Zero;
    }

    private static float PlanarDistance(Vector3 first, Vector3 second) =>
        Vector2.Distance(new Vector2(first.X, first.Z), new Vector2(second.X, second.Z));
    private static float Lerp(float first, float second, float amount) =>
        first + (second - first) * Math.Clamp(amount, 0, 1);
    internal static float BotReactionDelaySeconds(float difficulty) =>
        Lerp(1.4f, 0.12f, difficulty);
    internal static float BotAimErrorDegrees(float difficulty) =>
        Lerp(18, 1.5f, difficulty);
    internal static float BotTrackingDegreesPerSecond(float difficulty) =>
        Lerp(45, 360, difficulty);
    internal static int BotBurstShotCount(float difficulty) =>
        Math.Clamp((int)MathF.Round(Lerp(1, 8, difficulty)), 1, 8);
    internal static float BotBurstPauseSeconds(float difficulty) =>
        Lerp(1.4f, 0.18f, difficulty);
    internal static float BotMovementSpeedScale(float difficulty) =>
        Lerp(0.45f, 1, difficulty);
    private static float Approach(float current, float target, float maximumDelta) =>
        current < target ? Math.Min(current + maximumDelta, target)
        : Math.Max(current - maximumDelta, target);
    private static float ApproachAngle(float current, float target, float maximumDelta) =>
        NormalizeAngle(current + Math.Clamp(NormalizeAngle(target - current),
            -maximumDelta, maximumDelta));

    private static bool IsNewer(uint sequence, uint previous) =>
        sequence != previous && unchecked(sequence - previous) < 0x80000000u;
    private static bool Finite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
    private static float NormalizeAngle(float angle) => MathF.IEEERemainder(angle, MathF.Tau);
    private static float ShotNoise(byte actorId, uint sequence, uint salt)
    {
        uint value = unchecked(sequence * 0x9E3779B1u + (uint)(actorId + 1) * 0x85EBCA6Bu + salt);
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return (value & 0x00FFFFFFu) / 8388607.5f - 1;
    }
    private static float Vary(float baseline, float variancePercent, byte id, int salt)
    {
        uint hash = unchecked((uint)(id + 1) * 0x9E3779B1u ^ (uint)salt * 0x85EBCA6Bu);
        hash ^= hash >> 16;
        float signed = (hash & 0xffff) / 32767.5f - 1;
        return baseline + baseline * (variancePercent / 100f) * signed;
    }
}
