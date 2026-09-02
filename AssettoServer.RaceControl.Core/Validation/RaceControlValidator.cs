using System.Net;
using System.Net.Sockets;
using AssettoServer.RaceControl.Core.Models;

namespace AssettoServer.RaceControl.Core.Validation;

public sealed class RaceControlValidator
{
    public ValidationResult Validate(RaceControlPreset preset, AcContentCatalog? catalog)
    {
        ArgumentNullException.ThrowIfNull(preset);
        var messages = new List<ValidationMessage>();
        ErrorIf(messages, string.IsNullOrWhiteSpace(preset.Name), "Name", "Give this event a name.");
        ErrorIf(messages, !Directory.Exists(preset.AssettoCorsaRoot), "AssettoCorsaRoot", "Assetto Corsa installation was not found.");
        ErrorIf(messages, !Directory.Exists(preset.ServerPayloadPath), "ServerPayloadPath", "Published AssettoServer payload was not found.");
        ErrorIf(messages, !File.Exists(Path.Combine(preset.ServerPayloadPath, "AssettoServer.exe")), "ServerPayloadPath", "The payload does not contain AssettoServer.exe.");

        var isFps = preset.Mode == EventMode.Fps;
        int racingSlotCount = preset.Grid.Count(slot => slot.Mode != SlotMode.Spectator);
        int spectatorSlotCount = preset.Grid.Count - racingSlotCount;
        if (racingSlotCount < 2)
        {
            messages.Add(new(ValidationSeverity.Error, "Grid", "At least two racing grid slots are required."));
        }

        if (preset.Grid.Count > 254)
        {
            messages.Add(new(ValidationSeverity.Error, "Grid", "Assetto Corsa supports at most 254 server slots."));
        }
        if (isFps && racingSlotCount > 32)
        {
            messages.Add(new(ValidationSeverity.Error, "Grid", "FPS V1 supports at most 32 scored participants."));
        }

        var track = catalog?.Tracks.FirstOrDefault(candidate =>
            candidate.TrackId.Equals(preset.TrackId, StringComparison.OrdinalIgnoreCase)
            && candidate.LayoutId.Equals(preset.TrackLayoutId, StringComparison.OrdinalIgnoreCase));
        if (catalog is not null && track is null)
        {
            messages.Add(new(ValidationSeverity.Error, "Track", "The selected track layout is not installed."));
        }
        else if (track is not null)
        {
            ErrorIf(messages, !track.HasModels, "Track", "The selected layout has no models INI.");
            if (track.PitBoxes < 2)
            {
                messages.Add(new(ValidationSeverity.Error, "Track", "The selected layout exposes fewer than two pit boxes."));
            }
            else if (racingSlotCount > track.PitBoxes)
            {
                messages.Add(new(
                    ValidationSeverity.Warning,
                    "Grid",
                    $"Only the first {track.PitBoxes} of {racingSlotCount} racing entries will be staged for this layout; spectator entries do not consume pit boxes."));
            }

            if (!isFps && preset.Bots.Enabled)
            {
                ErrorIf(messages, !track.HasFastLane, "Track", "Race bots require this layout's fast_lane.ai.");
                if (track.RaceBotPreflight is { } preflight)
                {
                    ErrorIf(messages, !preflight.HasReadableClosedSpline, "Track",
                        $"Race bots require a readable closed fast_lane.ai: {preflight.Failure ?? "invalid spline"}.");
                    ErrorIf(messages, preflight.MissingModelFiles.Count > 0, "Track",
                        $"Track models are missing: {string.Join(", ", preflight.MissingModelFiles)}.");
                }
            }
        }

        if (catalog is not null)
        {
            if (catalog.Weather.Count == 0)
            {
                messages.Add(new(ValidationSeverity.Error, "Weather", "No installed weather presets were found."));
            }
            else if (string.IsNullOrWhiteSpace(preset.Conditions.WeatherId)
                || catalog.Weather.All(weather => !weather.Id.Equals(preset.Conditions.WeatherId, StringComparison.OrdinalIgnoreCase)))
            {
                messages.Add(new(ValidationSeverity.Warning, "Weather", "The selected weather is unavailable; an installed fallback weather will be staged instead."));
            }
        }

        if (isFps)
        {
            ValidateFps(messages, preset, catalog);
        }

        for (var index = 0; index < preset.Grid.Count; index++)
        {
            var slot = preset.Grid[index];
            if (isFps)
            {
                ErrorIf(messages, slot.Difficulty is < 0 or > 1, $"Grid[{index}]", "FPS bot skill must be between 0 and 1, or blank for automatic variance.");
                ErrorIf(messages, slot.Aggression is < 0 or > 1, $"Grid[{index}]", "FPS bot aggression must be between 0 and 1, or blank for automatic variance.");
                continue;
            }

            var car = catalog?.Cars.FirstOrDefault(candidate => candidate.Id.Equals(slot.CarId, StringComparison.OrdinalIgnoreCase));
            if (catalog is not null && car is null)
            {
                messages.Add(new(ValidationSeverity.Error, $"Grid[{index}]", $"Car '{slot.CarId}' is not installed."));
                continue;
            }

            if (car is not null)
            {
                ErrorIf(messages, car.DataAcdPath is null, $"Grid[{index}]", $"{car.DisplayName} has no data.acd checksum source.");
                ErrorIf(messages, preset.Bots.Enabled
                                  && (slot.Mode is SlotMode.Auto or SlotMode.Fixed)
                                  && !car.HasCollider,
                    $"Grid[{index}]", $"{car.DisplayName} has no collider.kn5 for rigid-body bots.");
                ErrorIf(messages,
                    !string.IsNullOrWhiteSpace(slot.SkinId) && car.Skins.All(skin => !skin.Id.Equals(slot.SkinId, StringComparison.OrdinalIgnoreCase)),
                    $"Grid[{index}]",
                    $"Skin '{slot.SkinId}' is not installed for {car.DisplayName}.");
            }

            ErrorIf(messages, string.IsNullOrWhiteSpace(slot.CarId), $"Grid[{index}]", "Select a car.");
            ErrorIf(messages, slot.BallastKg is < 0 or > 1000, $"Grid[{index}]", "Ballast must be between 0 and 1000 kg.");
            ErrorIf(messages, slot.RestrictorPercent is < 0 or > 100, $"Grid[{index}]", "Restrictor must be between 0 and 100 percent.");
            ErrorIf(messages, slot.Difficulty is < 0 or > 1, $"Grid[{index}]", "Bot skill must be between 0 and 1, or blank for automatic variance.");
            ErrorIf(messages, slot.Aggression is < 0 or > 1, $"Grid[{index}]", "Bot aggression must be between 0 and 1, or blank for automatic variance.");
        }

        ErrorIf(messages, !isFps && preset.Sessions.PracticeEnabled && preset.Sessions.PracticeMinutes is < 1 or > 1440, "Sessions", "Practice duration must be 1..1440 minutes.");
        ErrorIf(messages, !isFps && preset.Sessions.QualifyingEnabled && preset.Sessions.QualifyingMinutes is < 1 or > 1440, "Sessions", "Qualifying duration must be 1..1440 minutes.");
        ErrorIf(messages, !isFps && preset.Sessions.RaceLaps is < 1 or > 999, "Sessions", "Race laps must be 1..999.");
        ErrorIf(messages, preset.Conditions.TimeOfDayHour is < 0 or > 23,
            "Conditions", "Time of day must be between 00:00 and 23:00.");

        ErrorIf(messages, !isFps && preset.Bots.Difficulty is < 0 or > 1, "Bots", "Difficulty must be between 0 and 1.");
        ErrorIf(messages, !isFps && preset.Bots.DifficultyVariancePercent is < 0 or > 100, "Bots", "Skill variance must be between 0 and 100 percent.");
        ErrorIf(messages, !isFps && preset.Bots.Aggression is < 0 or > 1, "Bots", "Aggression must be between 0 and 1.");
        ErrorIf(messages, !isFps && preset.Bots.AggressionVariancePercent is < 0 or > 100, "Bots", "Aggression variance must be between 0 and 100 percent.");
        ErrorIf(messages, !isFps && preset.Bots.UpdateHz is < 10 or > 120, "Bots", "Bot update rate must be 10..120 Hz.");
        ErrorIf(messages, !isFps && preset.Bots.GridSpacingMeters <= 0, "Bots", "Grid spacing must be positive.");
        ErrorIf(messages, !isFps && preset.Bots.SurfaceFriction is < 0.1 or > 3, "Bots", "Surface friction must be 0.1..3.");
        ErrorIf(messages, !isFps && !Enum.IsDefined(preset.Bots.JoinSlotSelection), "Bots",
            "Choose First, Last, or Random for player slot selection.");

        ErrorIf(messages, !TryPrivateAddress(preset.Network.BindAddress, out var isLoopback), "Network", "Use a wildcard, loopback, or private IPv4 LAN address.");
        if (isLoopback)
        {
            messages.Add(new(ValidationSeverity.Warning, "Network", "Loopback only allows clients on this PC. Select the host's LAN address for other computers."));
        }
        if (preset.Network.HttpPort == preset.Network.TcpPort)
        {
            messages.Add(new(ValidationSeverity.Error, "Network", "HTTP and TCP ports cannot be the same."));
        }

        if (!isFps && !preset.Bots.Enabled)
        {
            messages.Add(new(ValidationSeverity.Information, "Bots", "Bots are disabled; every staged racing slot will be human-only."));
        }

        if (spectatorSlotCount > 0)
        {
            messages.Add(new(ValidationSeverity.Information, "Grid",
                $"{spectatorSlotCount} spectator connection slot(s) will be staged after the racing grid and require CSP spectating support."));
        }

        return new ValidationResult(messages);
    }

    private static void ValidateFps(List<ValidationMessage> messages, RaceControlPreset preset, AcContentCatalog? catalog)
    {
        var fps = preset.Fps;
        ErrorIf(messages, !Enum.IsDefined(typeof(FpsVisualTheme), fps.Theme), "Fps.Theme",
            "FPS visual theme must be Blocks or Modern.");
        ErrorIf(messages, fps.MatchType != FpsMatchType.Deathmatch, "Fps", "FPS V1 only supports Deathmatch.");
        ErrorIf(messages, fps.TimeLimitMinutes is < 1 or > 1440, "Fps", "Deathmatch duration must be 1..1440 minutes.");
        ErrorIf(messages, fps.KillLimit is < 1 or > 999, "Fps", "Deathmatch kill limit must be 1..999.");
        ErrorIf(messages, fps.RespawnSeconds is < 0 or > 30, "Fps", "Respawn delay must be 0..30 seconds.");
        ErrorIf(messages, fps.SpawnProtectionSeconds is < 0 or > 10, "Fps", "Spawn protection must be 0..10 seconds.");
        ErrorIf(messages, fps.ArenaBoundsPaddingMeters is < 5 or > 100
                          || !double.IsFinite(fps.ArenaBoundsPaddingMeters),
            "Fps.ArenaBoundsPaddingMeters", "FPS arena bounds padding must be 5..100 metres.");
        ErrorIf(messages, fps.Bots.Health is < 50 or > 200, "Fps", "FPS participant health must be 50..200 HP.");
        ErrorIf(messages, fps.Bots.Difficulty is < 0 or > 1, "Fps", "FPS bot difficulty must be between 0 and 1.");
        ErrorIf(messages, fps.Bots.DifficultyVariancePercent is < 0 or > 100, "Fps", "FPS skill variance must be 0..100 percent.");
        ErrorIf(messages, fps.Bots.Aggression is < 0 or > 1, "Fps", "FPS bot aggression must be between 0 and 1.");
        ErrorIf(messages, fps.Bots.AggressionVariancePercent is < 0 or > 100, "Fps", "FPS aggression variance must be 0..100 percent.");

        var carrier = catalog?.Cars.FirstOrDefault(car => car.Id.Equals(fps.CarrierCarId, StringComparison.OrdinalIgnoreCase));
        ErrorIf(messages, catalog is not null && carrier is null, "Fps.CarrierCarId", $"FPS carrier car '{fps.CarrierCarId}' is not installed.");
        ErrorIf(messages, catalog is not null && carrier?.DataAcdPath is null, "Fps.CarrierCarId", "The FPS carrier car has no data.acd checksum source.");

        var arena = fps.Arena;
        ErrorIf(messages, arena is null, "Fps.Arena", "Prepare this layout as an FPS arena before launching it.");
        if (arena is null) return;

        ErrorIf(messages, arena.PreparationVersion != FpsArenaDefinition.CurrentPreparationVersion,
            "Fps.Arena", "The FPS arena was prepared by an incompatible version; prepare it again.");
        ErrorIf(messages, Math.Abs(arena.BoundsPaddingMeters
                                   - fps.ArenaBoundsPaddingMeters) > 0.001,
            "Fps.Arena", "FPS arena bounds padding changed; prepare the arena again.");
        ErrorIf(messages, !arena.TrackId.Equals(preset.TrackId, StringComparison.OrdinalIgnoreCase)
                          || !arena.LayoutId.Equals(preset.TrackLayoutId, StringComparison.OrdinalIgnoreCase),
            "Fps.Arena", "The prepared FPS arena does not match the selected layout.");
        ErrorIf(messages, arena.SpawnPoints.Count < 2, "Fps.Arena", "The FPS arena needs at least two safe spawn points.");
        ErrorIf(messages, arena.Navigation.Version != 1 || arena.Navigation.NodeCount <= 0
                          || arena.Navigation.ComponentCount <= 0
                          || arena.Navigation.ConnectedSpawnCount < 2,
            "Fps.Arena", "The FPS arena navigation is missing or has fewer than two connected spawns; prepare it again.");
        if (arena.Navigation.ConnectedSpawnCount >= 2
            && arena.Navigation.ConnectedSpawnCount < arena.SpawnPoints.Count)
            messages.Add(new(ValidationSeverity.Warning, "Fps.Arena",
                $"{arena.SpawnPoints.Count - arena.Navigation.ConnectedSpawnCount} FPS spawn(s) are isolated from the primary navigation component."));
        if (arena.Collision is not null)
            ErrorIf(messages, arena.Collision.Version != 1
                              || arena.Collision.TriangleCount <= 0
                              || arena.Collision.BvhNodeCount <= 0
                              || arena.Collision.BvhLeafCount <= 0
                              || arena.Collision.MaximumLeafTriangles is < 1 or > 8,
                "Fps.Arena",
                "The FPS arena collision BVH summary is invalid; prepare the arena again.");
        ErrorIf(messages, !Finite(arena.BoundsMin) || !Finite(arena.BoundsMax)
                          || arena.BoundsMin.X >= arena.BoundsMax.X
                          || arena.BoundsMin.Y >= arena.BoundsMax.Y
                          || arena.BoundsMin.Z >= arena.BoundsMax.Z,
            "Fps.Arena", "The FPS arena bounds are invalid.");
        ErrorIf(messages, arena.SpawnPoints.Any(spawn => !Finite(spawn.Position) || !double.IsFinite(spawn.YawRadians)),
            "Fps.Arena", "The FPS arena contains an invalid spawn point.");
    }

    private static bool Finite(FpsPoint point) =>
        double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Z);

    public static bool TryPrivateAddress(string text, out bool isLoopback)
    {
        isLoopback = false;
        if (!IPAddress.TryParse(text, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        isLoopback = bytes[0] == 127;
        return isLoopback || bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168);
    }

    private static void ErrorIf(List<ValidationMessage> messages, bool condition, string field, string message)
    {
        if (condition)
        {
            messages.Add(new(ValidationSeverity.Error, field, message));
        }
    }
}
