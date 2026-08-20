# AssettoServer Race Control

Race Control is the native Windows event editor and launcher for this fork's LAN race bots. It reads cars, skins, tracks, layouts, weather, previews, pit capacity, AI lines, checksum data, and collision inputs from the local Assetto Corsa installation. It never edits installed game content.

The application targets .NET 10 WPF and opens in Visual Studio 2026 through `AssettoServer.slnx`. Its three projects are:

- `AssettoServer.RaceControl`: native WPF interface;
- `AssettoServer.RaceControl.Core`: content, validation, configuration, staging, persistence, and process services;
- `AssettoServer.RaceControl.Tests`: focused NUnit regressions.

## Workflow

1. Open **Event & Content**. Race Control detects the standard Steam installation and bundled standalone server. Browse to different locations if necessary.
2. Choose a track layout. The UI reports its pit capacity and whether a usable `fast_lane.ai` exists.
3. Build the **Grid**. Every entry can select any installed car and skin. `Auto` means a bot occupies the slot until a human claims it, `Fixed` is bot-only, and `None` is human-only. New and default entries are `Auto`.
4. Set sessions, rules, weather, bots, rigid-body fidelity, and LAN ports. Clear **Enable server-authoritative race bots** to stage every entry as human-only without losing the saved per-slot modes.
5. Open **Launch & Live**, validate, then use **Stage & Start**. Content Manager clients discover the event under `Drive -> Online -> LAN`.

If more entries are requested than the track has pit boxes, validation shows a warning and staging keeps the first entries that fit. This is not a launch blocker.

## Content Manager interoperation

**Import latest CM preset** reads the most recently modified `server_cfg.ini` and `entry_list.ini` pair under `<Assetto Corsa>\server\presets`. A single imported entry is cloned to the two-slot minimum. Import does not modify the CM preset.

**Export new CM preset** writes a new `RACE_CONTROL_*` directory and never overwrites an existing CM preset. The native app is the authoritative editor for bot-specific options because ordinary CM presets do not model the race-bot extension.

## Files and safety

Mutable application data lives under `%LocalAppData%\AssettoServer Race Control`:

- `Presets`: JSON event presets;
- `Instances`: timestamped, isolated runnable servers;
- `Cache\Physics`: prepared track/grid/car collision assets keyed by input and server versions;
- `Logs`: reserved for exported logs.

Instances contain only the published server, generated configuration, required `data.acd` checksum sources, the selected `fast_lane.ai`, and a compressed prepared physics asset. Passwords are present in the local generated `server_cfg.ini`, as they are in CM presets, but are excluded from the instance manifest and UI logs.

V1 accepts only loopback or RFC1918 IPv4 listeners. Lobby registration, Steam authentication, UPnP, and automatic port forwarding are forced off. Stopping from the GUI creates a per-instance control file; the server observes it and shuts its host down gracefully. A process-tree termination is used only if graceful shutdown exceeds ten seconds.

## Build and publish

From a PowerShell prompt in the repository:

```powershell
dotnet test .\AssettoServer.RaceControl.Tests\AssettoServer.RaceControl.Tests.csproj -c Release
.\tools\Publish-RaceControl.ps1
```

The portable self-contained output is `out-race-control-win-x64`. It includes the matching self-contained server under `Server`, the AGPL license, third-party notices, and these build/use instructions. Source and build instructions must accompany any distributed or network-accessible modified AGPL build.

For a local installed-content smoke test using two bots at Magione:

```powershell
.\tools\Test-RaceControlLocal.ps1
```

The smoke test scans the installed catalog, stages an isolated two-slot instance, prepares its rigid-body inputs, starts it on loopback-only acceptance ports, requests graceful shutdown, and retains the logs under `.artifacts\race-control-local-acceptance`.

## Current limits

- Race Control is a portable build, not yet an MSIX/MSI installer.
- Installed mod metadata can be incomplete. Such content remains visible, but validation blocks missing server checksums, AI lines, models, or bot colliders required by the selected mode.
- The server derives bounded vehicle-controller values from `ui_car.json`; exact collision geometry does not turn the server into Kunos' proprietary tyre, suspension, aero, or damage simulation.
- Public hosting, automatic firewall rule changes, port forwarding, mid-race spectator administration, and remote server management are intentionally out of scope for this LAN release.
