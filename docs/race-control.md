# AssettoServer Race Control

Race Control is the native Windows event editor and launcher for this fork's LAN race bots. It reads cars, skins, tracks, layouts, weather, previews, pit capacity, AI lines, checksum data, and collision inputs from the local Assetto Corsa installation. It never edits installed game content.

The installed-content catalog is cached per Assetto Corsa installation under `%LocalAppData%\AssettoServer Race Control\Cache`. After the first scan, startup loads that cache immediately and checks for added, removed, or changed cars and track layouts in the background. The refreshed catalog replaces the cache atomically and preserves any grid edits made while scanning. Use **Refresh content** to force an immediate foreground rescan.

The application targets .NET 10 WPF and opens in Visual Studio 2026 through `AssettoServer.slnx`. Its three projects are:

- `AssettoServer.RaceControl`: native WPF interface;
- `AssettoServer.RaceControl.Core`: content, validation, configuration, staging, persistence, and process services;
- `AssettoServer.RaceControl.Tests`: focused NUnit regressions.

## Workflow

1. Race Control detects the standard Steam installation and bundled standalone server. To use different locations, open the cog menu and select **Local Installations**.
2. Choose a track layout. The UI reports its pit capacity and whether a usable `fast_lane.ai` exists.
3. Build the **Grid**. Every entry can select any installed car and skin. `Auto` means a bot occupies the slot until a human claims it, `Fixed` is bot-only, `None` is a human-only racing slot, and `Spectator` reserves a camera-only connection. New and default entries are `Auto`.
4. Set sessions, rules, weather, bots, rigid-body fidelity, and LAN ports. Clear **Enable server-authoritative race bots** to stage every entry as human-only without losing the saved per-slot modes.
5. Open **Launch & Live**, validate, then use **Start new**. Content Manager clients discover the event under `Drive -> Online -> LAN`.

**Race Bots -> Player slot selection** controls which eligible `Auto` slot is claimed when several slots use the connecting player's selected car. **First** uses the lowest configured grid entry, **Last** uses the highest, and **Random** chooses independently on every connection. Occupied, fixed-bot, human-only, and otherwise ineligible slots are skipped; explicit GUID reservations and CSP slot requests retain priority. Before race start this controls grid placement. With mid-race bot takeover enabled, it instead selects the active bot to replace at its current track position.

**Grid -> Populate by characteristics** builds a requested number of `Auto` racing entries from installed, bot-capable cars. Filters support exact `ui_car.json` class, maximum horsepower, exact model year, maximum horsepower per tonne, or the complete eligible catalog. Cars without metadata required by the selected filter are excluded rather than guessed. When fewer slots than cars match, Race Control samples across the sorted catalog instead of taking only the first names; larger grids cycle matching models and their installed skins. Population replaces racing rows but retains configured spectator reservations. Protocol capacity is enforced immediately, while a grid larger than the selected layout's pit capacity remains visible and is trimmed from the end during staging as usual.

**Grid -> Favorite grids** saves and reloads the grid independently from the event preset. A favorite includes car, skin, driver and team names, slot modes, ballast, restrictor, nation, and spectator rows; loading one does not change the track, sessions, conditions, bot settings, or network configuration. Favorites are stored under `%LocalAppData%\AssettoServer Race Control\Grids`. Saving with an existing name updates that favorite.

**Race Bots -> Racecraft** uses 0..1 sliders for the field's baseline **Skill** and **Aggression**. Their 0..100% variance sliders generate stable per-slot values above and below each baseline, so a grid remains mixed but reproducible across restarts and accelerated simulations. Variance is relative to the baseline and values are clamped to 0..1. The Grid's optional **Skill** and **Aggression** cells override the generated value for that specific car slot; leave a cell blank to keep automatic variance. Per-slot racecraft is retained by event presets, favorite grids, and Race Control Content Manager exports. Skill affects pace and corner-speed planning, while aggression affects passing distance, commitment, extension, yielding, and gap acceptance.

`Spectator` entries are appended after the racing roster when the server configuration is rendered. They count toward Assetto Corsa's 254-client protocol limit and must name an installed carrier car, but they do not consume pit boxes, physical grid poses, rigid-body bot geometry, race positions, lap packets, or race-completion responsibility. A spectator client must advertise CSP's `SPECTATING_AWARE` feature; exact CSP slot requests can claim a spectator entry even while the race session is closed to new drivers. Unmodified clients cannot claim these slots.

Race Control keeps one reusable runnable server rather than copying the self-contained payload for every start. Staging replaces `Instances\Current` only after preserving the previous run's compact configuration, logs, telemetry summary/events, track map, and gzip-compressed telemetry samples under `History`. Use **Export package…** when a complete portable server ZIP is actually needed; export is available while the server is stopped and does not disturb the working instance.

## FPS Deathmatch compatibility gate

**Event mode** switches between the existing Racing editor and an isolated **FPS** editor. Each mode retains its unsaved draft while the application is open and has a separately filtered saved-preset list. FPS mode renames the circuit selector to Map / Arena and layout, replaces sessions with Deathmatch settings, exposes FPS-specific difficulty, aggression, variance, and 50–200 HP controls, and displays grid roles as `Auto`, `Bot`, `Human`, and `Spectator`. Racing presets and behavior are not migrated or rewritten when modes are switched.

FPS mode is currently a deliberate vertical-slice compatibility gate, not a completed shooter release. Choose Magione, use **Prepare as FPS arena**, and stage a two-or-more-slot event. Preparation reads installed collision and `AC_START_n` transforms, writes bounded prototype spawns to `%LocalAppData%\AssettoServer Race Control\FpsArenas`, and never modifies the installed track. **Show FPS arenas only** filters against those sidecars. **Export FPS Client Pack** writes the CSP version, carrier-car requirement, and installation notes; the actual online Lua client is delivered by AssettoServer.

The gate runs over one long AC Practice session. Every participant occupies a normal AC carrier slot, while the embedded CSP online script immobilizes and hides carrier cars, owns the first-person camera, captures keyboard/mouse or Xbox-controller input, renders the locally installed pit-crew prototype, and draws the crosshair, health, clock, kill feed, scoreboard, and final ranking. The server runs a separate fixed-step `FpsWorld`: inputs are authenticated to the sender's assigned slot, stale or impossible inputs are rejected, actor snapshots are sent at 20 Hz, local motion is predicted and reconciled, firing and damage are server-authoritative, and reliable events carry hits, kills, score, and match state. No client-reported hit is accepted and no `acs.exe` hook is installed.

The temporary pit-crew avatar is loaded from each player's own installation and is not redistributed. A project-owned `asrc_fps_avatar` package, full static-world capsule collision, navmesh preparation, production bots, and broader arena tooling remain intentionally gated behind two real LAN clients completing five consecutive kill/respawn cycles with stable camera ownership and identical scores. If that manual gate fails, development stops here instead of adding native executable hooks.

If more entries are requested than the track has pit boxes, validation shows a warning and staging keeps the first entries that fit. This is not a launch blocker.

**Event & Content -> Conditions -> Time of day** selects the starting hour from `00:00` through `23:00`. Race Control converts that clock value to Assetto Corsa's native `SUN_ANGLE` representation when staging or exporting a Content Manager preset, and converts imported `SUN_ANGLE` values back to the nearest displayed hour. Existing presets that store only a sun angle remain compatible.

## Menus and settings

The native menu bar groups event actions under **File**, appearance under **View**, process actions under **Server**, and documentation under **Help**. The cog button in the header and `Ctrl+,` both open application settings. `Ctrl+N` creates a new race and `Ctrl+S` saves the current preset.

**View -> Theme** switches between System, Light, and Dark immediately. System resolves the current Windows application theme when Race Control starts. The selected palette covers the window chrome, navigation, page backgrounds, cards, inputs, menus, grids, popups, console, and native title bars.

The Settings window separates **General** preferences from **Local Installations**. General controls compact grid rows, loading the most recent saved race at startup, returning to the last open page, and confirmation before an active server is stopped on exit. Local Installations selects the Assetto Corsa content root and standalone AssettoServer payload. These machine-specific paths take precedence over paths embedded in older race presets, so loading an event cannot silently switch the app to another installation. All application-wide preferences are saved in `%LocalAppData%\AssettoServer Race Control\settings.json`.

## Live Race view

`LIVE RACE` provides a server-authoritative top-down view of every active bot and connected human. Click a car marker or use the car list to select it, then clear **Full track view** to follow it. Adjust the follow-view width with its slider or the mouse wheel. Scrolling inward from the full-track camera enters the selected-car view; scrolling outward past the maximum returns to fit-to-track.

Selecting an active server bot opens a compact control panel in the map's upper-right corner:

- **STOP** removes its horizontal and angular velocity every physics tick, stopping it at its current location; the button becomes green **GO**, which returns the bot to autonomous AI.
- **TELEPORT P1** relocates it about 12 metres ahead of the current physical leader on the racing spline. Teleported distance is not added to its lap tracker, so this does not award a skipped lap.
- **TAKE OVER** switches the map to a Direct3D 11 chase view and routes arrow-key or Xbox-controller input to that bot's existing rigid-body controller. Left/right or the left stick steer, Up or the right trigger accelerates, and Down/Space or the left trigger brakes. A compact HUD shows authoritative speed, protocol gear, and engine RPM. **RELEASE CONTROL** or Escape returns it to AI. While takeover is active, input, authoritative snapshots, UI polling, and rendering target 60 Hz; normal live-map monitoring retains its lower-overhead rates.

Manual input is local-only, bounded, and fails safe: if fresh input stops arriving for 750 ms, throttle is removed and full braking is applied. Takeover controls only server-owned bots; unmodified Assetto Corsa human clients remain authoritative for their own cars. The 2.5D view is a Race Control visualization of server telemetry, not the Assetto Corsa renderer.

The controls deliberately distinguish the process from the session:

- **Start live server**, **Stop server**, and **Restart server** manage the AssettoServer process.
- **Start race** selects the configured race and begins its normal grid countdown even with zero humans.
- **Stop race** broadcasts race-over, classifies unfinished participants as DNF, holds the bots, and leaves the server online.
- **Restart race** rebuilds the race state and starts a fresh countdown regardless of connected-player count.
- **Simulate race** stages the current setup and runs its bots with deterministic virtual time and no network listeners. Seed and duration are set beside the button. **Limit by** selects virtual minutes or leader laps; in Laps mode the run stops when the leading active car reaches the selected count, while normal configured race completion can still end it earlier. The **Time acceleration** slider sets a 1x-100x target and remains live while the simulation runs. A change is sent through the same local-only command channel and rebases the pacing clock, so large adjustments do not cause a catch-up burst or a long stall. Simulation remains fixed-step and can run slower than the target when the CPU cannot keep up.

The progress bar advances toward the first condition that can end the run: configured race completion, the selected leader-lap limit, or the maximum virtual-time limit. Fractional leader position provides continuous progress in Laps mode. Its remaining wall-time estimate uses the current acceleration target and is therefore approximate; a race can finish sooner than its selected maximum.

When a simulation completes, reaches its limit, or is stopped, the map is covered by its final race classification even if the server has already transitioned to practice. The table shows rank, driver and car, laps, total and best-lap time, average and top speed, contact episodes, full-stop count and stopped duration, and completed/DNF status. The summary keeps contact manifolds and server recoveries as separate fields. Accepted STOP/GO commands remain deliberate stopped-obstacle test episodes; the overview reports completed versus committed passes instead of discarding those commands as artificial data. Starting-grid time is excluded from full stops. **Back to map** dismisses the panel without discarding the result.

**Take over** replaces the flat chase approximation with a Vortice-backed Direct3D 11 scene hosted in WPF. Separate static-track and dynamic-car GPU buffers render a lit road ribbon, green terrain extending beyond the AI-line boundaries, and vehicle geometry; the perspective camera, road elevation, and vehicle transforms use the authoritative position and quaternion streamed by the server. The current renderer uses deliberately simple vehicle geometry rather than pretending to load Kunos materials or meshes; this keeps simulation authority and visual asset loading separate while providing correct spatial motion, slopes, occlusion, and mouse-wheel chase distance. Gravel and other material-specific runoff require track-surface metadata that is not currently part of live telemetry and are therefore not guessed from spline geometry.

Live control is local-only. Race Control passes a private directory inside the staged instance; AssettoServer writes `state.json` and `track.json` there and consumes single-use command files from `commands`. No unauthenticated control API is exposed on the LAN HTTP port.

## Content Manager interoperation

**Import latest CM preset** reads the most recently modified `server_cfg.ini` and `entry_list.ini` pair under `<Assetto Corsa>\server\presets`. A single imported entry is cloned to the two-slot minimum. Import does not modify the CM preset.

**Export new CM preset** writes a new `RACE_CONTROL_*` directory and never overwrites an existing CM preset. The native app is the authoritative editor for bot-specific options because ordinary CM presets do not model the race-bot extension.

## Files and safety

Mutable application data lives under `%LocalAppData%\AssettoServer Race Control`:

- `Presets`: JSON event presets;
- `Grids`: reusable named grid favorites;
- `FpsArenas`: FPS arena bounds and safe-spawn sidecars, separate from installed tracks;
- `FpsClientPacks`: reserved for exported companion content after the compatibility gate;
- `Instances\Current`: the single reusable runnable server;
- `History`: compact per-run configuration, logs, results, and compressed telemetry rather than duplicated server binaries and physics assets;
- `Cache\Physics`: prepared track/grid/car collision assets keyed by input and server versions;
- `Logs`: reserved for exported logs.

The current working instance contains the published server, generated configuration, required `data.acd` checksum sources, the selected `fast_lane.ai`, and a compressed prepared physics asset. Compact history deliberately omits the server payload, copied checksum content, and `race-physics.bin`; simulation `samples.jsonl` is retained as `samples.jsonl.gz`. Passwords are present in the locally retained generated `server_cfg.ini`, as they are in CM presets, but are excluded from the instance manifest and UI logs.

This storage model applies to newly staged runs. Legacy timestamped instance directories created by older builds are left untouched so an update never deletes user data implicitly; they can be reviewed or removed separately after confirming they are no longer needed.

V1 accepts only loopback or RFC1918 IPv4 listeners. Lobby registration, Steam authentication, UPnP, and automatic port forwarding are forced off. Stopping from the GUI creates a per-instance control file; the server observes it and shuts its host down gracefully. A process-tree termination is used only if graceful shutdown exceeds ten seconds.

## Build and publish

From a PowerShell prompt in the repository:

```powershell
dotnet test .\AssettoServer.RaceControl.Tests\AssettoServer.RaceControl.Tests.csproj -c Release
.\tools\Publish-RaceControl.ps1
```

The portable self-contained output is always replaced at `out-race-control`; publishing does not create architecture- or change-specific output folders. The root contains the single-file Race Control executable and notices, runtime/server files live under `lib`, framework localization resources live under `lang`, and documentation lives under `docs`. Source and build instructions must accompany any distributed or network-accessible modified AGPL build.

For a local installed-content smoke test using two bots at Magione:

```powershell
.\tools\Test-RaceControlLocal.ps1
```

The smoke test scans the installed catalog, stages an isolated two-slot instance, prepares its rigid-body inputs, starts it on loopback-only acceptance ports, requests graceful shutdown, and retains the logs under `.artifacts\race-control-local-acceptance`.

For repeated bot-only regression races across installed tracks, use:

```powershell
.\tools\Test-RaceBotsMatrix.ps1 -Slots 8 -Seeds 1,2,3
```

The runner uses the bundled server's network-free virtual-time mode, preserves structured per-run telemetry, and writes an aggregate report under `.artifacts\race-bot-matrix`. It never edits installed Assetto Corsa content. The local acceptance runner also supports `-SimulateRace -SimulationTimeScale 10` to verify paced live telemetry and the versioned per-car result fields. Add `-VerifyStoppedObstaclePassing` to stop the settled race leader and require another bot to steer around it, complete the pass, and gain race position before the test can pass.

Use `-AllUsableTracks -FullGrid -Seeds 1 -ContinueOnError` for an installation-wide diagnostic sweep. The runner records preflight-incompatible layouts separately and does not let one failed track abort later runs.

## Current limits

- Race Control is a portable build, not yet an MSIX/MSI installer.
- Installed mod metadata can be incomplete. Such content remains visible, but validation blocks missing server checksums, AI lines, models, or bot colliders required by the selected mode.
- The cached background scan preflights race-bot spline closure/readability and physical `MODEL_*` references. Unsupported content is rejected before a server preparation process starts.
- Track UI metadata can overstate pit capacity. After physics preparation, Race Control reads the actual contiguous `AC_START_n` count and drops trailing staged entries rather than advertising invalid slots.
- The server derives bounded vehicle-controller values from `ui_car.json`; exact collision geometry does not turn the server into Kunos' proprietary tyre, suspension, aero, or damage simulation.
- Public hosting, automatic firewall rule changes, port forwarding, remote spectator administration, and remote server management are intentionally out of scope for this LAN release.
