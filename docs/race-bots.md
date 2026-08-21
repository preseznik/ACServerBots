# LAN race bots

This fork adds an experimental `Race` behavior beside AssettoServer's existing `Traffic` AI. It does not expose Assetto Corsa's offline AI session. The standalone server owns one dynamic rigid body per bot slot and sends it through AssettoServer's normal car position protocol, so unmodified Assetto Corsa clients can join through Content Manager.

The fork is based on upstream commit `6ce86addc1b1c70caf018a7b39f6d7bc9aa9493f`. The Git remote named `upstream` points to `https://github.com/compujuckel/AssettoServer.git`. AssettoServer and this modification remain AGPL-3.0; anyone receiving or interacting with a hosted modified build must be offered the corresponding modified source and build instructions.

## Behavior and limits

`AiParams.Behavior` defaults to `Traffic`, leaving existing configurations unchanged. `Race` mode:

- freezes the starting roster when the server enters the race session;
- keeps `AI=fixed` as bots and `AI=none` as human-only slots;
- can optionally expose only active, unfinished `AI=auto` bots for replacement by players joining during a race;
- creates exactly one bot state per frozen bot slot and places each participant on its track-defined `AC_START_n` transform in the session's actual grid order;
- extracts the selected track's physical KN5 triangle meshes, each selected car's real `collider.kn5`, and its standard `WHEEL_LF/RF/LR/RR` contact transforms into a prepared local physics asset;
- holds dynamic bodies stationary until the server start time, then advances a shared BEPU rigid-body world with gravity, road contact, pitch/roll, and bot-to-bot collision response;
- applies bounded attitude control and speed-dependent downforce to resist rollover and crest launches without bypassing rigid-body contacts; propulsion is cut when a car is no longer safely upright;
- resets a bot to its current spline target after one second continuously overturned or materially off track, preserving its lap state while excluding the teleport from progress accounting;
- represents connected humans as solver kinematic bodies, so bots physically react to their reported position and collider;
- offers `Efficient`, `Balanced`, and `High` solver fidelity without reducing collision geometry;
- selects a vehicle profile by car model and uses its mass, power, top speed, reported 0–100 time, braking, grip, tyre diameter, RPM range, and gear count;
- uses `fast_lane.ai` radius and side-width fields for corner speed, following, proactive committed passes around AI or human cars, AI obstacle avoidance, and collision recovery; direct world-space sensing handles nearby cars while closed-spline progress supplies stable ahead/behind ordering through bends;
- assigns deterministic per-slot base-line and pace variation, reserves the chosen passing lane against cars ahead and behind, waits for measured lateral separation before accelerating, and requires full longitudinal clearance before returning to line; low aggression increases the following gap and commitment time but no longer disables passing;
- accepts a bot lap only after at least 85% forward travel around a closed spline and a forward start-line wrap;
- publishes bot laps in the normal classification packet and includes bot identities in final results;
- marks a disconnected racing human DNF and does not replace that driver until the next practice session.

With `AllowMidRaceBotTakeover: true`, a successful handshake atomically despawns the selected bot before assigning its slot to the player. The bot is removed from the classification and the player starts with a fresh result from the normal online spawn path. Standard clients cannot inherit the bot's moving physics state, so this is a pit-lane entry rather than a seamless moving-car takeover. A player that disconnects is still a DNF and does not turn back into a bot during that race. `AI=fixed` and `AI=none` slots are never offered as ordinary mid-race takeover slots.

With `RestartSessionOnFirstHumanConnect: true`, the first human joining an otherwise bot-filled server restarts the current session after initial client synchronization. In a race this produces a fresh grid, countdown, lap state, and classification. Additional humans do not restart it. The behavior re-arms after the last human disconnects, so a later first human does not inherit an unattended bot session. If multiple clients connect together, the restart waits until all currently connected clients have sent their first update.

Track contact and collision geometry are no longer height approximations: they come from the installed KN5 files and are solved as rigid-body contacts. Each bot uses a dynamic chassis plus four independently simulated wheel contacts on spring/line suspension with a physical compression bump stop and extension limit scaled from the installed wheel radius. Wheels use continuous track collision, while the chassis remains a secondary road contact so a lost wheel contact cannot let the vehicle cross the one-sided mesh. The server ray-queries the prepared drivable KN5 mesh at the car's actual X/Z footprint, uses that exact surface plus a small wheel-height-scaled visual clearance for the network render height, and reports a vertical velocity consistent with the authored road slope. This prevents AC interpolation from combining a surface-clamped position with raw suspension velocity. Small support errors are corrected with bounded upward velocity rather than position teleports; hard relocation is reserved for a near-one-metre mesh/contact failure. Fidelity changes solver iterations, substeps, threading, and chassis CCD without changing the suspension tune or other vehicle behavior. A post-step guard immediately reattaches a bot to its current track point if a mesh seam produces an implausible upward impulse, excessive height error, or inversion; normal rigid-body contact remains authoritative inside those bounds. This is still not the proprietary Kunos vehicle simulation. Longitudinal and lateral tyre forces are supplied by the race controller from installed car metadata; the server does not reproduce Kunos tyre slip curves, detailed aero, damage, pit strategy, weather adaptation, or offline AI behavior. Connected humans remain authoritative on their clients and therefore act as kinematic obstacles in the server world: bots receive contact response, but the server cannot apply the equal collision impulse back into an unmodified human client. Mixed models are supported, but control-profile quality depends on installed car metadata.

All bots are released on the same race-start tick. A shared half-second launch window prevents stationary grid rows from creating a one-car-at-a-time start. Collision avoidance then resumes, while new pass commitments remain disabled for four seconds so each rigid body can settle from its staggered grid transform onto its assigned base line.

Race lookahead combines curvature braking with the speed profile embedded in the installed `fast_lane.ai`. The authored speed is a hard upper bound scaled by race difficulty. This matters on vertical crests as well as corners: AC's line can prescribe a safe speed even when horizontal curvature alone looks straight. Legacy or optimized splines without authored speeds continue to use curvature and vehicle-profile limits.

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
    UpdateHz: 60
    Physics:
      Fidelity: Balanced
      AssetFile: race-physics.bin
      Friction: 1.15
    AllowMidRaceBotTakeover: true
    RestartSessionOnFirstHumanConnect: true
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

`Difficulty` and `Aggression` must be in `0..1`, the update rate must be `10..120`, the start point must belong to a closed usable spline, and the prepared asset must contain enough contiguous `AC_START_0..n` transforms for the roster. `Fidelity` selects solver iterations, substeps, worker threads, and continuous collision detection: `Efficient` is the lowest CPU setting, `Balanced` is the default, and `High` enables swept continuous collision detection with the strongest solver. All levels use the same exact track triangles and car colliders. Vehicle profile models must be unique and their physical inputs are bounded during configuration validation. Race mode also requires visible AI and a private IPv4 listener. Dynamic hourly traffic density cannot be combined with race mode. Mid-race takeover and first-human restart are disabled by default in generic configuration; the CM LAN stager enables both when bots are active. Takeover additionally requires `RACE IS_OPEN=1` and at least one `AI=auto` slot.

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

The launcher reads Content Manager's server preset directly from `<Assetto Corsa>\server\presets`, waits until `server_cfg.ini` and `entry_list.ini` have stopped changing, snapshots them, stages the standalone server, prepares `race-physics.bin` from the installed track/car KN5 files, and launches it. No Pack export or zip is required. Physics preparation is mandatory when bots are enabled and fails closed if the selected layout has no usable physical mesh or contiguous grid transforms, or a selected car lacks `collider.kn5` or the four standard AC wheel transforms. Grid transforms are projected onto the extracted road surface before countdown holding. The physical chassis is then supported by four constrained wheel bodies instead of rigid wheel shapes welded to the chassis, so road crests and kerbs are absorbed by suspension rather than acting as ramps. Network updates use a separate AC reference point derived from each model's average wheel-center height, so rendering does not move the collision body. Incoming human positions are converted back to the same grounded origin for contact simulation. It guarantees at least two replaceable slots in the isolated snapshot: if CM contains only one entry, that car entry is cloned without changing CM. Every staged slot defaults to `AI=auto`, so a bot occupies it until a human claims it before the grid freezes or through the enabled mid-race takeover path. Pass `-MinimumSlots` to create more than two slots.

CM's track, layout, sessions, lap count, weather, assists, fuel, damage, tyre wear, ports, passwords, and entry skins are preserved. If the CM roster exceeds the selected layout's pit capacity, staging removes the trailing `CAR_n` entries from the isolated snapshot until it fits and records them in `race-bot-manifest.json`; the CM preset itself is unchanged. The isolated server overlay disables public lobby registration and UPnP, selects a private LAN listener, opens the race for bot takeover, enables a fresh-session restart for the first human, and applies the race-bot configuration. It never edits the CM preset or installed game files.

Automatic selection is deliberately conservative. A single valid CM preset is selected automatically; if several exist, the double-click launcher displays their directory IDs and asks which one to use. For scripts or shortcuts, select one explicitly:

```powershell
.\tools\Start-CmLanRaceBots.ps1 -CmPresetId SERVER_00
```

If Content Manager uses a custom server-preset directory, pass it with `-CmServerPresetsRoot`. Use `-DisableBots` to make every staged slot human-only; AI and takeover are then disabled and `fast_lane.ai` is not required. Mixed car models are accepted in both modes. With bots enabled, staging derives one profile per model from the installed `ui/ui_car.json`, using bounded defaults when metadata is missing or incomplete. A usable `fast_lane.ai`, installed skins and `data.acd`, enough pit boxes, and a configured race session are still required. Profile values and provenance are recorded in `race-bot-manifest.json`; installed car files are not modified.

Choose server CPU cost without changing the prepared collision geometry:

```powershell
.\tools\Start-CmLanRaceBots.ps1 -PhysicsFidelity Efficient
.\tools\Start-CmLanRaceBots.ps1 -PhysicsFidelity High
```

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

The staging script treats Content Manager as the preset authoring source. It accepts automatic discovery, a directory, or a zip; locates a matching `server_cfg.ini` and `entry_list.ini` pair; validates the selected cars, skins, `data.acd`, `collider.kn5`, `fast_lane.ai`, and track pit count; then creates an isolated runtime. It copies only server-required checksum/spline data and a compressed prepared physics asset from the installed game. It does not edit Assetto Corsa or Content Manager.

The staged event forces lobby registration and UPnP off, binds HTTP/TCP/UDP to the selected private LAN address, advertises eight slots, assigns the first two as human-only and the next six as replaceable `AI=auto` bots, removes qualifying, and writes the five-minute practice/three-lap race settings. The race is advertised as open, but its slot filter only offers active unfinished bots once the race has started. Content Manager clients should find it under `Drive -> Online -> LAN`.

## Acceptance

Automated acceptance covers configuration, exact grid-transform handling, prepared-geometry serialization and winding, countdown holding, forward lap wraps, wrong-way/double-crossing rejection, roster claims/freeze, mid-race takeover eligibility and fresh results, first-human restart gating/re-arming, DNF policy, classification ordering, packet rows, cornering, following, lane variation, committed passing, collision recovery, mixed-model staging, profile-driven acceleration, bounded top speed, braking, gears, and RPM. Prepared track physics accepts AC's numbered physical meshes and explicitly named wall/collision meshes; visual geometry that merely shares a `surfaces.ini` prefix is excluded, and duplicate triangles are collapsed before the Bepu mesh is built. Headless passing acceptance requires a measured lane change, at least two metres of separation, a completed pass, and bounded vehicle-contact frames; it also records maximum upward speed so a short-lived collision launch cannot pass between five-second log samples.

Release acceptance still requires two real LAN clients using Content Manager. Both clients must see all eight cars start together, stable bot motion and classification updates, coherent finishes/DNFs, final results, and the return to practice. Physical contact quality and on-track behavior cannot be certified by unit or headless tests.
