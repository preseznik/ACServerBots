# LAN race bots

This fork adds an experimental `Race` behavior beside AssettoServer's existing `Traffic` AI. It does not expose Assetto Corsa's offline AI session. The standalone server owns one spline-constrained state per bot slot and sends it through AssettoServer's normal car position protocol, so unmodified Assetto Corsa clients can join through Content Manager.

The fork is based on upstream commit `6ce86addc1b1c70caf018a7b39f6d7bc9aa9493f`. The Git remote named `upstream` points to `https://github.com/compujuckel/AssettoServer.git`. AssettoServer and this modification remain AGPL-3.0; anyone receiving or interacting with a hosted modified build must be offered the corresponding modified source and build instructions.

## Behavior and limits

`AiParams.Behavior` defaults to `Traffic`, leaving existing configurations unchanged. `Race` mode:

- freezes the starting roster when the server enters the race session;
- keeps `AI=fixed` as bots and `AI=none` as human-only slots;
- can optionally expose only active, unfinished `AI=auto` bots for replacement by players joining during a race;
- creates exactly one bot state per frozen bot slot and places it behind the human grid allocation;
- holds bots stationary until the server start time and advances them with a bounded fixed-step accumulator;
- selects a vehicle profile by car model and uses its mass, power, top speed, reported 0–100 time, braking, grip, tyre diameter, RPM range, and gear count;
- uses `fast_lane.ai` radius and side-width fields for corner speed, following, a committed lateral overtake, AI obstacle avoidance, and collision recovery;
- accepts a bot lap only after at least 85% forward travel around a closed spline and a forward start-line wrap;
- publishes bot laps in the normal classification packet and includes bot identities in final results;
- marks a disconnected racing human DNF and does not replace that driver until the next practice session.

With `AllowMidRaceBotTakeover: true`, a successful handshake atomically despawns the selected bot before assigning its slot to the player. The bot is removed from the classification and the player starts with a fresh result from the normal online spawn path. Standard clients cannot inherit the bot's moving physics state, so this is a pit-lane entry rather than a seamless moving-car takeover. A player that disconnects is still a DNF and does not turn back into a bot during that race. `AI=fixed` and `AI=none` slots are never offered as ordinary mid-race takeover slots.

This is not Kunos physics. Bots remain constrained to the racing spline and the vehicle model does not reproduce tyre slip, suspension, detailed aero, damage, pit strategy, weather adaptation, or offline AI behavior. Mixed models are supported through locally derived profiles, but profile quality depends on installed car metadata. The first acceptance event remains stock Magione with homogeneous `bmw_m3_e30` entries. The client protocol milestone is a classified human/bot race; do not expand into a client patch or a replacement physics engine if that milestone is unstable.

## Configuration

```yaml
NetworkBindAddress: 192.168.1.10
UseSteamAuth: false
EnableUPnP: false
EnableAi: true
AiParams:
  Behavior: Race
  AutoAssignTrafficCars: false
  HideAiCars: false
  NamePrefix: Bot
  MaxSpeedKph: 160
  Race:
    Difficulty: 0.75
    Aggression: 0.50
    StartSplinePointId: 0
    GridSpacingMeters: 9
    UpdateHz: 60
    AllowMidRaceBotTakeover: true
    VehicleProfiles:
      - Model: bmw_m3_e30
        Source: ui_car.json
        MassKg: 1200
        PowerKw: 177.5
        TopSpeedKph: 248
        ZeroToHundredSeconds: 7.4
        MaxBrakeDeceleration: 8.5
        LateralGripG: 1
        TyreDiameterMeters: 0.65
        EngineIdleRpm: 900
        EngineMaxRpm: 7250
        GearCount: 6
```

`Difficulty` and `Aggression` must be in `0..1`, spacing must be positive, the update rate must be `10..120`, the start point must belong to a closed usable spline, and the configured grid must fit on it. Vehicle profile models must be unique and their physical inputs are bounded during configuration validation. Race mode also requires visible AI and a private IPv4 listener. Dynamic hourly traffic density cannot be combined with race mode. Mid-race takeover is disabled by default; enabling it additionally requires `RACE IS_OPEN=1` and at least one `AI=auto` slot.

## Build and stage

Prerequisite: a .NET 9 or newer SDK. The repository pins the upstream target framework and publishes a self-contained Win64 executable.

```powershell
$env:DOTNET_ROLL_FORWARD = 'Major'
dotnet test AssettoServer.slnx --configuration Release
dotnet publish AssettoServer\AssettoServer.csproj --configuration Release --runtime win-x64 --self-contained true
```

### One-click Content Manager workflow

Configure and save the event in Content Manager, then double-click:

```text
tools\Start-CmLanRaceBots.cmd
```

The launcher reads Content Manager's server preset directly from `<Assetto Corsa>\server\presets`, waits until `server_cfg.ini` and `entry_list.ini` have stopped changing, snapshots them, stages the standalone server, and launches it. No Pack export or zip is required. It guarantees at least two replaceable slots in the isolated snapshot: if CM contains only one entry, that car entry is cloned without changing CM. Every staged slot defaults to `AI=auto`, so a bot occupies it until a human claims it before the grid freezes or through the enabled mid-race takeover path. Pass `-MinimumSlots` to create more than two slots.

CM's track, layout, sessions, lap count, weather, assists, fuel, damage, tyre wear, ports, passwords, and entry skins are preserved. The isolated server overlay disables public lobby registration and UPnP, selects a private LAN listener, opens the race for bot takeover, and applies the race-bot configuration. It never edits the CM preset or installed game files.

Automatic selection is deliberately conservative. A single valid CM preset is selected automatically; if several exist, the double-click launcher displays their directory IDs and asks which one to use. For scripts or shortcuts, select one explicitly:

```powershell
.\tools\Start-CmLanRaceBots.ps1 -CmPresetId SERVER_00
```

If Content Manager uses a custom server-preset directory, pass it with `-CmServerPresetsRoot`. Use `-DisableBots` to make every staged slot human-only; AI and takeover are then disabled and `fast_lane.ai` is not required. Mixed car models are accepted in both modes. With bots enabled, staging derives one profile per model from the installed `ui/ui_car.json`, using bounded defaults when metadata is missing or incomplete. A usable `fast_lane.ai`, installed skins and `data.acd`, enough pit boxes, and a configured race session are still required. Profile values and provenance are recorded in `race-bot-manifest.json`; installed car files are not modified.

Use `-NoLaunch` to validate and stage the current CM preset without starting the server:

```powershell
.\tools\Start-CmLanRaceBots.ps1 -NoLaunch
```

To launch the same CM event without bots:

```powershell
.\tools\Start-CmLanRaceBots.ps1 -DisableBots
```

### Explicit pack workflow

The reproducible pack workflow remains available for the fixed Magione acceptance event:

```powershell

.\tools\Stage-CmRaceBotServer.ps1 `
  -CmServerPack 'C:\path\to\content-manager-server-pack.zip' `
  -AssettoCorsaRoot 'C:\Program Files (x86)\Steam\steamapps\common\assettocorsa' `
  -SlotMode ReservedHumans `
  -Force

.\tools\Start-LanRaceBots.ps1
```

The staging script treats Content Manager as the preset authoring source. It accepts automatic discovery, a directory, or a zip; locates a matching `server_cfg.ini` and `entry_list.ini` pair; validates the selected cars, skins, `data.acd`, `fast_lane.ai`, and track pit count; then creates an isolated runtime. It copies only server-required checksum/spline data from the installed game. It does not edit Assetto Corsa or Content Manager.

The staged event forces lobby registration and UPnP off, binds HTTP/TCP/UDP to the selected private LAN address, advertises eight slots, assigns the first two as human-only and the next six as replaceable `AI=auto` bots, removes qualifying, and writes the five-minute practice/three-lap race settings. The race is advertised as open, but its slot filter only offers active unfinished bots once the race has started. Content Manager clients should find it under `Drive -> Online -> LAN`.

## Acceptance

Automated acceptance covers configuration, closed-spline/grid math, countdown holding, forward lap wraps, wrong-way/double-crossing rejection, roster claims/freeze, mid-race takeover eligibility and fresh results, DNF policy, classification ordering, packet rows, cornering, following, overtaking, collision recovery, mixed-model staging, profile-driven acceleration, bounded top speed, braking, gears, and RPM. A self-contained publish and a headless startup prove server packaging/configuration only.

Release acceptance still requires two real LAN clients using Content Manager. Both clients must see all eight cars start together, stable bot motion and classification updates, coherent finishes/DNFs, final results, and the return to practice. Physical contact quality and on-track behavior cannot be certified by unit or headless tests.
