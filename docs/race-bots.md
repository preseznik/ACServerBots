# LAN race bots

This fork adds an experimental `Race` behavior beside AssettoServer's existing `Traffic` AI. It does not expose Assetto Corsa's offline AI session. The standalone server owns one dynamic rigid body per bot slot and sends it through AssettoServer's normal car position protocol, so unmodified Assetto Corsa clients can join through Content Manager.

The fork is based on upstream commit `6ce86addc1b1c70caf018a7b39f6d7bc9aa9493f`. The Git remote named `upstream` points to `https://github.com/compujuckel/AssettoServer.git`. AssettoServer and this modification remain AGPL-3.0; anyone receiving or interacting with a hosted modified build must be offered the corresponding modified source and build instructions.

## Behavior and limits

`AiParams.Behavior` defaults to `Traffic`, leaving existing configurations unchanged. `Race` mode:

- freezes the starting roster when the server enters the race session;
- keeps `AI=fixed` as bots and `AI=none` as human-only slots;
- can optionally expose only active, unfinished `AI=auto` bots for replacement by players joining during a race;
- creates exactly one bot state per frozen bot slot and places each participant on its track-defined `AC_START_n` transform in the session's actual grid order;
- extracts the selected track's physical KN5 triangle meshes, retains only the bank-aware road layer connected to the selected `fast_lane.ai`, and prepares each car from its real `collider.kn5`, standard `WHEEL_LF/RF/LR/RR` transforms, and authoritative `tyres.ini` data;
- holds dynamic bodies stationary until the server start time, then advances a shared BEPU rigid-body world with gravity, four-corner raycast suspension, pitch/roll, validated barrier contact, and bot-to-bot collision response;
- reaches lane and avoidance targets with a wheelbase-aware steering controller: engine and brake force remains on the chassis' forward axis, yaw is limited by available tyre grip, and no direct sideways propulsion is applied;
- applies bounded attitude control and speed-dependent downforce to resist rollover and crest launches without bypassing rigid-body contacts; propulsion is cut when a car is no longer safely upright;
- resets a bot to its current spline target after one second continuously overturned or materially off track, preserving its lap state while excluding the teleport from progress accounting;
- represents connected humans as solver kinematic bodies, so bots physically react to their reported position and collider;
- offers `Efficient`, `Balanced`, and `High` solver fidelity without reducing collision geometry;
- selects a vehicle profile by car model and uses its mass, power, top speed, reported 0–100 time, braking, grip, tyre diameter, RPM range, and gear count;
- uses `fast_lane.ai` radius and side-width fields for corner speed, following, proactive committed passes around AI or human cars, AI obstacle avoidance, and collision recovery; direct world-space sensing handles nearby cars while closed-spline progress supplies stable ahead/behind ordering through bends;
- assigns deterministic per-slot base-line and pace variation, then adds continuous distance-based line drift so the field does not collapse onto one spline trace;
- lets any unobstructed bot commit a smooth steering-limited pass around a slower or stopped human or bot, continuously maintains the selected side relative to that opponent, and gives only the separated passer bounded route-speed headroom;
- reserves the chosen pass corridor against cars ahead and behind, rejects a reciprocal counter-pass while that pair is still side by side, waits for measured physical separation before accelerating, and requires 16 metres of longitudinal clearance before returning to line; a pair-specific cooldown prevents immediate pass/re-pass oscillation without blocking either bot from racing other opponents;
- accepts a bot lap only after at least 85% forward travel around a closed spline and a forward start-line wrap;
- publishes bot laps in the normal classification packet and includes bot identities in final results;
- marks a disconnected racing human DNF and does not replace that driver until the next practice session.

With `AllowMidRaceBotTakeover: true`, a successful handshake atomically despawns the selected bot before assigning its slot to the player. The bot is removed from the classification and the player starts with a fresh result from the normal online spawn path. Standard clients cannot inherit the bot's moving physics state, so this is a pit-lane entry rather than a seamless moving-car takeover. A player that disconnects is still a DNF and does not turn back into a bot during that race. `AI=fixed` and `AI=none` slots are never offered as ordinary mid-race takeover slots.

With `RestartSessionOnFirstHumanConnect: true`, the first human joining an otherwise bot-filled server restarts the current session after initial client synchronization. In a race this produces a fresh grid, countdown, lap state, and classification. Additional humans do not restart it. The behavior re-arms after the last human disconnects, so a later first human does not inherit an unattended bot session. If multiple clients connect together, the restart waits until all currently connected clients have sent their first update.

Road geometry is no longer treated as a single centre-height approximation. Physics preparation rasterizes the selected `fast_lane.ai` into a bank-aware height corridor, then rejects physical triangles from other decks, undersides, adjacent roads, and steep wall-like surfaces. Preparation reports source/selected triangle counts and centerline coverage and fails closed below 70 percent coverage. AC grid transforms are grounded on that route surface first; if a legal staggered grid column lies outside the retained triangle samples, preparation may use the selected layout's physical road only when it is within 1.5 metres below the authored `AC_START_n` transform. This bounded fallback supports stock layouts such as Vallelunga Classic without allowing a missing upper deck to snap to unrelated road geometry below it. Single-level layouts keep their route-matched KN5 road and nearby wall-like barrier faces. If the selected corridor contains materially separated physical decks, preparation projects the spline centreline back onto the matching physical road deck before building its continuous banked suspension ribbon, then suppresses the ambiguous raw barrier mesh; lane bounds and recovery remain active, while car-to-car and human-to-bot contacts are unchanged. The height correction is smoothed without replacing the spline's authored bank normal. A one-metre non-raceable suspension shoulder sits outside the authored lane bounds so a wheel cannot fall through an artificial ribbon edge during a bounded correction; AI planning never treats that shoulder as passing room. This prevents multilevel mod tracks from feeding duplicate floors or invisible collision walls into the chassis solver without making the car follow an inaccurate spline height. Each bot keeps one dynamic chassis and raycasts the prepared support road below every installed wheel transform. Independent spring and damper impulses are applied at those four contact points, with compression and extension bounded from the actual front/rear radii in `tyres.ini`. The launcher reads unpacked `data` first and otherwise decodes only `tyres.ini` and `car.ini` from `data.acd` in memory; installed files are never extracted or modified. The network reference height compensates for AC's client-side `GRAPHICS_OFFSET` and the difference between visual wheel-node height and actual tyre radius; this changes only the transmitted render reference, not the physical chassis. Sampling across each tyre is selected by fidelity, while a slope-aware residual filter rejects one-frame triangle-edge steps and bridges at most 80 ms of genuine contact dropout. The chassis does not collide directly with drivable triangle soup, so KN5 seams cannot launch it. Network position comes directly from the resulting chassis pose and the per-model AC reference height—there is no per-tick vertical snap or road clamp. Recovery relocation is reserved for an actual inversion, implausible excess vertical velocity, or material off-course state, with a cooldown preventing a malformed surface from causing a teleport loop. Fidelity changes solver iterations, substeps, threading, chassis CCD, and tyre ray sample count without changing the suspension tune. This is still not the proprietary Kunos vehicle simulation. Longitudinal and lateral tyre forces are supplied by the race controller from installed car metadata; the server does not reproduce Kunos tyre slip curves, detailed aero, damage, pit strategy, weather adaptation, or offline AI behavior. Connected humans remain authoritative on their clients and therefore act as kinematic obstacles in the server world: bots receive contact response, but the server cannot apply the equal collision impulse back into an unmodified human client. Mixed models are supported, but control-profile quality depends on installed car metadata.

All bots are released on the same race-start tick. A shared half-second launch window prevents stationary grid rows from creating a one-car-at-a-time start. Each bot follows the position and forward heading of its authored grid transform for at least 25 metres; the AI spline cannot turn it away from that launch path early. It then blends the path heading into the track course over at least another 40 metres so a blocked bot still follows the first corner. Lateral movement toward its independent racing line uses half the normal line-change rate and waits whenever another participant occupies the nearby target corridor. Pass commitments remain disabled until both transitions are complete.

Once moving, a bot changes lane by looking ahead toward the requested line, applying rate-limited steering, and rotating its velocity through bounded lateral grip. The controller derives wheelbase from the installed car's four wheel transforms and reports its steering command through the normal AC steering fields, so clients can animate front-wheel steering. This is a bounded nonholonomic controller rather than Kunos tyre physics: it prevents commanded crabwalking, but real-client visual motion and wheel-animation direction remain part of LAN acceptance.

Race-line and pass decisions are participant-neutral. Nearby bot positions are compared in the shared spline-relative lane frame, while connected humans continue to use direct world-space projection. Passing is the race response to a slower competitor; following is only the temporary braking state while a safe route is being established. A stationary lead car is planned around from at least 30 metres and cannot trap the field at the normal following gap, including while a bot is still completing its gradual post-grid merge. The planner samples the narrowest usable corridor ahead, derives clearance from both prepared car colliders, and shortens rear-lane reservation for a stationary queue so the next racer can escape. Only the first stopped car in a queue is treated as the root obstacle, and the passing car itself is excluded from occupancy checks. Moving passes use both colliders plus a 0.35-metre margin, with a 2.5-metre lower bound and a separate 3.1-metre obstacle-discovery corridor; acceleration is allowed once 2.75 metres of lateral separation is sustained. Stopped-obstacle passes use the actual collider-width clearance plus a smaller bounded margin, allowing legal moves on narrow tracks without making wide cars overlap. The chosen route is still a committed steering path: no lateral teleport or traffic-style lane snap is used.

Stopped queues coordinate their escape routes rather than letting every follower independently choose the same gap. At most one bot per side reserves an overlapping stopped cluster, reciprocal stopped-pass targets are rejected, and unreserved followers retain a bounded 2.5 m/s crawl instead of commanding zero speed. A blocked passer may replan or reverse only twice before abandoning that route and observing a cooldown. As a final on-track safeguard, if at least four automatic bots and 75 percent of the active automatic field remain below 0.75 m/s for six seconds, the foremost unblocked recovery candidate is moved to the first clear spline point 18 to 60 metres ahead. Only one candidate is recovered per cooldown. Countdown holding, Race Control pause, finished/DNF participants, human cars, manual takeover, and explicit STOP commands are excluded from this watchdog.

Local contact knots do not always stop enough cars to trigger the field watchdog. An automatic bot that remains below 0.25 m/s for eight seconds is therefore moved to the next clear route point, with recoveries spaced out so a whole pack cannot teleport at once. Race yielding also retains a bounded crawl speed instead of preserving a near-zero speed captured when a pass began in contact. Explicit STOP and manual takeover remain authoritative and are never overridden.

Race lookahead combines curvature braking with the speed profile embedded in the installed `fast_lane.ai`. It builds a continuous approach-speed envelope and keeps the current bend constrained until the car has exited it, instead of dropping the limit as soon as instantaneous speed dips below the target. Maximum difficulty retains a small controller reserve, and a committed pass can use at most two percent additional corner speed; straight-line passing authority is unchanged. This matters on vertical crests as well as corners: AC's line can prescribe a safe speed even when horizontal curvature alone looks straight. Legacy or optimized splines without authored speeds continue to use curvature and vehicle-profile limits.

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

### Accelerated bot-only simulation

`--simulate-race` runs the configured bot race without TCP, UDP, HTTP, Steam, lobby, or UPnP listeners. A manual server clock advances at the configured race `UpdateHz`; the fixed physics step is unchanged. Use `--simulation-max-minutes N` for a virtual-time limit or `--simulation-max-laps N` to stop when the leading active bot reaches that lap count. By default the update loop executes as quickly as the CPU permits. `--simulation-time-scale 10` instead applies a wall-clock target from 1x to 100x, sleeping between fixed ticks when the machine is ahead; the achieved factor can be lower when physics cannot keep up. Race Control can update this target during a run through its private command directory. The pacing clock rebases at each change instead of trying to recover the prior run-wide average. Simulation mode disables the parallel Bepu dispatcher and seeds each bot's bounded line and pace personality, so a track/seed/configuration combination is reproducible while a different seed produces a different race. It does not increase vehicle speed or deliberately script crashes.

Each run writes `events.jsonl`, `samples.jsonl`, and `summary.json`. Samples include a session generation plus each bot's lap, spline point, position, velocity, target speed, line offset, obstacle distance, steering, slip, road-height error, suspension compression, grounded-wheel count, raw-surface discontinuity count, actual upward speed, slope-relative excess upward speed, upright state, recoveries, pass state, and pass counters. Session changes snapshot the completed race before statistics are cleared, so a later practice session cannot overwrite race classification with zeroes. Accepted STOP/GO commands are preserved as explicit stopped-obstacle episodes with the stopped slot, duration, committed and completed passes, and contact manifolds. Aggregate counters remain cumulative across session restarts. The versioned summary records classification, run-wide physics maxima, contacts, anomalies, simulated/wall duration, target and achieved real-time factors, and per-car elapsed time, average speed, top speed, contact episodes, recovery count, post-launch full-stop count, and stopped duration.

When launched by Race Control, the accelerated run also publishes a wall-clock-throttled live snapshot for the `LIVE RACE` viewport. The snapshot contains complete vehicle position, orientation, forward vector, velocity, and track elevation. This does not slow or alter fixed-step simulation; the display samples authoritative state while the simulation continues as fast as the CPU permits.

Race Control may temporarily stop, relocate, or manually drive an active server bot through its private per-instance command directory. Manual steering and longitudinal input feed the same bounded rigid-body controller used by AI, while AI planning continues in the background for a clean hand-back. A 750 ms stale-input timeout brakes the car. These administrative controls are not exposed through the LAN HTTP listener and cannot take authority from an unmodified human client.

Run a staged preset directly:

```powershell
AssettoServer.exe --preset race-control --simulate-race `
  --simulation-output .\simulation-red-bull-seed-7 `
  --simulation-seed 7 `
  --simulation-time-scale 10 `
  --simulation-max-minutes 45 `
  --simulation-max-wall-seconds 300
```

The installed-content matrix runner stages bot-only events and repeats deterministic seeds across representative tracks:

```powershell
.\tools\Test-RaceBotsMatrix.ps1 -Slots 8 -Seeds 1,2,3
```

The local harness can also stop the current leader after the field has settled and require another bot to navigate around it and complete the pass:

```powershell
.\tools\Test-RaceControlLocal.ps1 -Slots 8 -SimulateRace -VerifyStoppedObstaclePassing
```

By default it prefers Magione, Red Bull Ring GP, Nordschleife, and one additional usable installed layout. Use `-TrackKeys magione,ks_red_bull_ring/layout_gp` for an explicit set and `-FailOnAnomaly` for CI-style failure. Per-run artifacts live under `.artifacts\race-bot-matrix\runs`; `matrix-report.md` and `matrix-summary.json` provide the aggregate result. Accelerated runs validate authoritative AI, physics, laps, results, and recovery behavior. Real-time LAN clients remain necessary for handshake, interpolation, rendered ride height, wheel animation, and human/bot contact acceptance.

### One-click Content Manager workflow

Configure and save the event in Content Manager, then double-click:

```text
tools\Start-CmLanRaceBots.cmd
```

The launcher reads Content Manager's server preset directly from `<Assetto Corsa>\server\presets`, waits until `server_cfg.ini` and `entry_list.ini` have stopped changing, snapshots them, stages the standalone server, prepares `race-physics.bin` from the installed track/car KN5 and car-data files, and launches it. No Pack export or zip is required. Physics preparation is mandatory when bots are enabled and fails closed if the selected layout has no usable route-matched physical mesh, insufficient spline coverage, or non-contiguous grid transforms, or a selected car lacks `collider.kn5` or the four standard AC wheel transforms. Grid transforms are projected onto the selected road layer before countdown holding. The physical chassis is supported by four spring/damper ray contacts sampled at those wheel transforms and sized from the actual front/rear tyres, so road crests and kerbs affect the body without exposing it to triangle-edge collision impulses. Network updates use a separate AC reference point derived from actual tyre radius, with a bounded visual-node fallback only when packed mod data cannot be decoded, so rendering does not move the collision body. Incoming human positions are converted back to the same grounded origin for contact simulation. Physics cache keys include the car model, collider, packed/unpacked car data, track models, and spline. It guarantees at least two replaceable slots in the isolated snapshot: if CM contains only one entry, that car entry is cloned without changing CM. Every staged slot defaults to `AI=auto`, so a bot occupies it until a human claims it before the grid freezes or through the enabled mid-race takeover path. Pass `-MinimumSlots` to create more than two slots.

Race Control owns at most one staged server at a time. Before launching a new race it scans running `AssettoServer.exe` processes, but claims only executables located below its own local `Instances` directory. Any such orphan from an interrupted or replaced GUI is sent its instance-specific `shutdown.signal`; if it does not exit within ten seconds, only that process tree is terminated. Content Manager servers and manually launched AssettoServer copies outside the Race Control data directory are not touched.

Restarting a session recreates each AI-controlled remote car on connected clients after its new grid pose is ready. This clears AC's interpolation and wheel-slip history for the old pose without disconnecting the authoritative bot slot or replacing its new-session result, preventing restart-only tyre-smoke clouds. An ordinary first session start does not perform this extra client refresh.

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

Automated acceptance covers configuration, exact grid-transform handling, prepared-geometry serialization and winding, packed/unpacked car-data calibration, separation of tyre radius from visual wheel height, multilevel road-deck rejection, countdown holding, grid-heading preservation, forward-distance and occupancy-gated grid merging, forward lap wraps, wrong-way/double-crossing rejection, roster claims/freeze, mid-race takeover eligibility and fresh results, first-human restart gating/re-arming, DNF policy, classification ordering, packet rows, cornering, following, continuous line variation, collider-width stopped-car escape, target-relative committed passing, pair cooldowns, collision recovery, mixed-model staging, profile-driven acceleration, bounded top speed, braking, gears, and RPM. Prepared track physics accepts AC's numbered physical meshes and explicitly named wall/collision meshes; visual geometry that merely shares a `surfaces.ini` prefix is excluded, duplicate triangles are collapsed, and suspension geometry must match the selected route layer before the Bepu mesh is built. Headless passing acceptance requires a measured lane change, authoritative collider clearance, a completed pass, and bounded vehicle-contact frames; the stopped-leader gate additionally requires the passer to gain race position. It also records maximum upward speed so a short-lived collision launch cannot pass between five-second log samples.

Release acceptance still requires two real LAN clients using Content Manager. Both clients must see all eight cars start together, stable bot motion and classification updates, coherent finishes/DNFs, final results, and the return to practice. Physical contact quality and on-track behavior cannot be certified by unit or headless tests.
