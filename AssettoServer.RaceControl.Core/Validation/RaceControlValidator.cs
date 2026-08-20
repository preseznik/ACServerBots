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

        if (preset.Grid.Count < 2)
        {
            messages.Add(new(ValidationSeverity.Error, "Grid", "At least two grid slots are required."));
        }

        if (preset.Grid.Count > 254)
        {
            messages.Add(new(ValidationSeverity.Error, "Grid", "Assetto Corsa supports at most 254 server slots."));
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
            else if (preset.Grid.Count > track.PitBoxes)
            {
                messages.Add(new(
                    ValidationSeverity.Warning,
                    "Grid",
                    $"Only the first {track.PitBoxes} of {preset.Grid.Count} entries will be staged for this layout."));
            }

            if (preset.Bots.Enabled)
            {
                ErrorIf(messages, !track.HasFastLane, "Track", "Race bots require this layout's fast_lane.ai.");
            }
        }

        for (var index = 0; index < preset.Grid.Count; index++)
        {
            var slot = preset.Grid[index];
            var car = catalog?.Cars.FirstOrDefault(candidate => candidate.Id.Equals(slot.CarId, StringComparison.OrdinalIgnoreCase));
            if (catalog is not null && car is null)
            {
                messages.Add(new(ValidationSeverity.Error, $"Grid[{index}]", $"Car '{slot.CarId}' is not installed."));
                continue;
            }

            if (car is not null)
            {
                ErrorIf(messages, car.DataAcdPath is null, $"Grid[{index}]", $"{car.DisplayName} has no data.acd checksum source.");
                ErrorIf(messages, preset.Bots.Enabled && !car.HasCollider, $"Grid[{index}]", $"{car.DisplayName} has no collider.kn5 for rigid-body bots.");
                ErrorIf(messages,
                    !string.IsNullOrWhiteSpace(slot.SkinId) && car.Skins.All(skin => !skin.Id.Equals(slot.SkinId, StringComparison.OrdinalIgnoreCase)),
                    $"Grid[{index}]",
                    $"Skin '{slot.SkinId}' is not installed for {car.DisplayName}.");
            }

            ErrorIf(messages, string.IsNullOrWhiteSpace(slot.CarId), $"Grid[{index}]", "Select a car.");
            ErrorIf(messages, slot.BallastKg is < 0 or > 1000, $"Grid[{index}]", "Ballast must be between 0 and 1000 kg.");
            ErrorIf(messages, slot.RestrictorPercent is < 0 or > 100, $"Grid[{index}]", "Restrictor must be between 0 and 100 percent.");
        }

        ErrorIf(messages, preset.Sessions.PracticeEnabled && preset.Sessions.PracticeMinutes is < 1 or > 1440, "Sessions", "Practice duration must be 1..1440 minutes.");
        ErrorIf(messages, preset.Sessions.QualifyingEnabled && preset.Sessions.QualifyingMinutes is < 1 or > 1440, "Sessions", "Qualifying duration must be 1..1440 minutes.");
        ErrorIf(messages, preset.Sessions.RaceLaps is < 1 or > 999, "Sessions", "Race laps must be 1..999.");

        ErrorIf(messages, preset.Bots.Difficulty is < 0 or > 1, "Bots", "Difficulty must be between 0 and 1.");
        ErrorIf(messages, preset.Bots.Aggression is < 0 or > 1, "Bots", "Aggression must be between 0 and 1.");
        ErrorIf(messages, preset.Bots.UpdateHz is < 10 or > 120, "Bots", "Bot update rate must be 10..120 Hz.");
        ErrorIf(messages, preset.Bots.GridSpacingMeters <= 0, "Bots", "Grid spacing must be positive.");
        ErrorIf(messages, preset.Bots.SurfaceFriction is < 0.1 or > 3, "Bots", "Surface friction must be 0.1..3.");

        ErrorIf(messages, !TryPrivateAddress(preset.Network.BindAddress, out var isLoopback), "Network", "Use a wildcard, loopback, or private IPv4 LAN address.");
        if (isLoopback)
        {
            messages.Add(new(ValidationSeverity.Warning, "Network", "Loopback only allows clients on this PC. Select the host's LAN address for other computers."));
        }
        if (preset.Network.HttpPort == preset.Network.TcpPort)
        {
            messages.Add(new(ValidationSeverity.Error, "Network", "HTTP and TCP ports cannot be the same."));
        }

        if (!preset.Bots.Enabled)
        {
            messages.Add(new(ValidationSeverity.Information, "Bots", "Bots are disabled; every staged slot will be human-only."));
        }

        return new ValidationResult(messages);
    }

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
