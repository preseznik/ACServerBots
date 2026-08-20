# LAN race bots

This fork adds an experimental `Race` behavior beside AssettoServer's existing `Traffic` AI. It does not expose Assetto Corsa's offline AI session. The standalone server owns one kinematic state per bot slot and sends it through AssettoServer's normal car position protocol, so unmodified Assetto Corsa clients can join through Content Manager.

The fork is based on upstream commit `6ce86addc1b1c70caf018a7b39f6d7bc9aa9493f`. The Git remote named `upstream` points to `https://github.com/compujuckel/AssettoServer.git`. AssettoServer and this modification remain AGPL-3.0; anyone receiving or interacting with a hosted modified build must be offered the corresponding modified source and build instructions.

## Behavior and limits

`AiParams.Behavior` defaults to `Traffic`, leaving existing configurations unchanged. `Race` mode:

- freezes `AI=auto` claims when the server enters the race session;
- keeps `AI=fixed` as bots and `AI=none` as human-only slots;
- rejects race-session joins through the normal closed-session gate;
- creates exactly one bot state per frozen bot slot and places it behind the human grid allocation;
- holds bots stationary until the server start time and advances them with a bounded fixed-step accumulator;
- uses `fast_lane.ai` radius and side-width fields for corner speed, following, a committed lateral overtake, AI obstacle avoidance, and collision recovery;
- accepts a bot lap only after at least 85% forward travel around a closed spline and a forward start-line wrap;
- publishes bot laps in the normal classification packet and includes bot identities in final results;
- marks a disconnected racing human DNF and does not replace that driver until the next practice session.

This is not Kunos physics. The bot model does not reproduce tyre, suspension, aero, damage, pit strategy, weather adaptation, or offline AI behavior. The first supported event is stock Magione with homogeneous `bmw_m3_e30` entries. The client protocol milestone is a classified human/bot race; do not expand into a client patch or a replacement physics engine if that milestone is unstable.

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
```

`Difficulty` and `Aggression` must be in `0..1`, spacing must be positive, the update rate must be `10..120`, the start point must belong to a closed usable spline, and the configured grid must fit on it. Race mode also requires visible AI and a private IPv4 listener. Dynamic hourly traffic density cannot be combined with race mode.

## Build and stage

Prerequisite: a .NET 9 or newer SDK. The repository pins the upstream target framework and publishes a self-contained Win64 executable.

```powershell
$env:DOTNET_ROLL_FORWARD = 'Major'
dotnet test AssettoServer.slnx --configuration Release
dotnet publish AssettoServer\AssettoServer.csproj --configuration Release --runtime win-x64 --self-contained true

.\tools\Stage-CmRaceBotServer.ps1 `
  -CmServerPack 'C:\path\to\content-manager-server-pack.zip' `
  -AssettoCorsaRoot 'C:\Program Files (x86)\Steam\steamapps\common\assettocorsa' `
  -Force

.\tools\Start-LanRaceBots.ps1
```

The staging script treats Content Manager as the preset authoring source. It accepts a directory or zip, locates `server_cfg.ini` and `entry_list.ini`, validates the selected cars, skins, `data.acd`, `fast_lane.ai`, and track pit count, then creates an isolated runtime. It copies only server-required checksum/spline data from the installed game. It does not edit Assetto Corsa or Content Manager.

The staged event forces lobby registration and UPnP off, binds HTTP/TCP/UDP to the selected private LAN address, advertises eight slots, assigns the first two as human-only and the next six as fixed bots, removes qualifying, and writes the five-minute practice/three-lap race settings. Content Manager clients should find it under `Drive -> Online -> LAN`.

## Acceptance

Automated acceptance covers configuration, closed-spline/grid math, countdown holding, forward lap wraps, wrong-way/double-crossing rejection, roster claims/freeze, DNF policy, classification ordering, packet rows, cornering, following, overtaking, and collision recovery. A self-contained publish and a headless startup prove server packaging/configuration only.

Release acceptance still requires two real LAN clients using Content Manager. Both clients must see all eight cars start together, stable bot motion and classification updates, coherent finishes/DNFs, final results, and the return to practice. Physical contact quality and on-track behavior cannot be certified by unit or headless tests.
