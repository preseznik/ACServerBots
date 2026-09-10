# FPS operator selection and Ghost conversion

Modern supports Officer (model ID 0, default) and Ghost Nightwar (ID 1). The Operator tab in the
existing loadout menu changes third-person appearance. Save/deploy submits weapons and appearance
together. An accepted initial choice spawns immediately; later choices apply on the next spawn.
Only accepted choices are remembered in local CSP storage. First-person arms and weapon animations
remain shared. Blocks keeps its existing presentation and accepts only model ID 0.

## Ownership and multiplayer contract

- `FpsOperatorModel.cs` owns IDs and the theme allowlist. Unknown IDs are rejected before any
  loadout state changes. Cosmetic selection does not change teams, hitboxes, health, or movement.
- `FpsSimulation.cs` stores active, pending, and assigned-bot appearance separately from weapons.
  Auto/Bot slots alternate Officer/Ghost by sorted actor ID within each team. Assignments survive
  respawn and automatic round restart. Releasing a human-controlled Auto slot restores its bot model.
- Reliable loadout/catalog/roster packets carry the model and allowed/default IDs. The server sends
  the roster to late joiners and broadcasts appearance changes when pending choices become active.
- `fps.lua` owns a small model catalog with paths, portraits, team materials, and stance offsets.
  Team materials become unique before recoloring. A failed Ghost model, skin, or animation load
  falls back to Officer for that actor; unchanged roster packets do not repeatedly retry the failure.

Packet layout and cache versions move together: **FPS protocol 5, client pack 49, Modern archive 10**.
The base weapon archive and shared KSANIM files are unchanged.

## Source and production asset

Original: `F:\Coding\Codex\.resources\AssettoCorsaMods\FPS\Characters\Ghost_IURgXpX\Ghost.blend`.
SHA-256: `93b0d5b59937fb204753ef637b58d535cf5d6c931c5a01b1651016b0a4319449`.
The original is read without executing embedded scripts and is never saved over.

| Metric | Supplied source | Generated Ghost |
|---|---:|---:|
| Character triangles | 138,544 | 33,684 |
| Including shared rifle | — | 39,684 |
| Materials | 77 used | 3 character + 1 rifle |
| Skeleton | 706 bones, 160 deform | Officer's exact 68 exported bones |
| Weights | Up to 13 influences | At most 4, normalized |
| Texture atlas dimensions | Mixed source images | Three sets, at most 2048 × 2048 |
| KN5 size | — | 34,513,182 bytes (34.5 MB) |
| Editable Blender file | 1.51 GB | Approximately 32.9 MB |

`tools/build_fps_ghost_assets.py` constructs the canonical Officer reference using the existing
builder. It maps Ghost's deform chains into anatomical frames on that rig, drops obsolete vertex
groups, normalizes skinning, and simplifies individual parts before atlas baking. Skull mask and
silhouette get more geometry; night-vision equipment is reduced from 33,114 to approximately 1,200
triangles. Synthetic glTF leaf-bone tails must not be used as longitudinal scale references: head,
terminal fingers, and toes use uniform scale instead. This prevents stretched masks and fingertips.

Head, uniform, and gear have separate diffuse/normal/material atlases. Source UVs remain explicitly
connected while new atlas UVs are baked. Material maps retain the existing specular/gloss convention.
Team 2 replaces uniform and gear diffuse maps, preserving the mask. The rifle, attachments, shared
animations, and first-person assets come from the established pipeline.

Build everything with:

```powershell
.\tools\Build-FpsModernAssets.ps1
```

The Ghost builder also accepts `--geometry-only` to inspect binding and stance renders before baking.
Working assets are in `.artifacts/ghost-work`, including `ghost-production.blend`, pose renders,
source metadata, and `ghost-pose-validation.json`. Shipping files and hashes are in
`AssettoServer.RaceControl.Core/Assets/Fps/Modern/asrc-modern-assets.json`.

## Validation and acceptance

- All 250 server tests and 97 Race Control tests passed. Server tests target .NET 9 and were run
  with `DOTNET_ROLL_FORWARD=Major` on the installed .NET 10 runtime; the development package is
  self-contained. Use SDK 10.0.400 and cached assets (`--no-restore`) on this workstation.
- `tools/test_fps_loadout_menu.py` passes rendered menu interaction checks at desktop and small
  resolutions. `tools/test_fps_operator_state.py` passes authoritative roster, late-arrival ordering,
  accepted-choice persistence, sender validation, material isolation, and per-actor fallback checks.
- Server tests cover invalid selection, queued appearance, respawn, team distribution, human/bot
  handover (8, 9, 16, and 32 actors), and repeated round restarts across all four match types.
- `tools/validate_fps_modern_assets.py` passes hashes, geometry, texture dimensions, weights,
  exact Officer/Ghost inverse-bind equality, and compatibility with all 20 shared operator clips.
  Blender also evaluates 60 sampled skinned poses across those clips and rejects invalid geometry.
- The self-contained development package passed Modern Team Deathmatch smoke tests with 8 and 16
  moving bots on `bo2_nuketown_2020`, both authoritative operator assignments, live arena coordinates,
  rifle fire, and graceful shutdown. The eight-bot round held results for 20.03 seconds and restarted
  with zero scores. Both asset endpoints passed; the served Modern v10 archive contains both operators.
- The exported v49 ZIP passed protocol/version checks and all 37 Modern file hashes. The original
  Ghost source hash is unchanged. The public-release permission gate was exercised and stopped correctly.

Reproduce the packaged restart smoke with:

```powershell
.\tools\Test-RaceControlLocal.ps1 -RaceControlBuild out-race-control `
  -FpsGate -FpsTheme Modern -FpsMatchType TeamDeathmatch -Slots 8 `
  -UseBundledArena -Track bo2_nuketown_2020 -Car bmw_m3_e30 -VerifyFpsRestart -SmokeSeconds 60
```

The installed Nuketown carrier grid limits this packaged run to 16 slots; the attempted 32-slot gate
could not start 32 actors. A 32-client acceptance run needs a track with sufficient carrier pit slots.
The simulation itself is tested with 32 actors. Do not bypass track capacity to claim a live 32-client pass.

Live CSP acceptance remains required: two connected clients with different models, both teams,
every weapon, traversal, death, and respawn. Inspect grip, clipping, ground contact, shadows, and
material isolation. Offline renders and unit tests do not establish those results.

Performance acceptance also remains open. Compare identical warmed-up Officer-only and mixed
scenes with 8, 16, and 32 actors; record client frame times and process/GPU memory. Target mixed
p95 frame time no more than 10% higher. Headless server smoke tests do not measure client rendering.
Distance LODs and custom Ghost first-person arms are deferred.

## Redistribution and local packaging

Ghost-specific redistribution permission is **not recorded**. Existing Officer/carbine permission
does not cover Ghost, and the supplied folder has no license. The manifest records this explicitly;
`Build-RaceControlRelease.ps1` stops before creating a public release while permission is missing.
Do not change that flag without recording the actual permission and its scope.

Local development builds go in `F:\Coding\Codex\Mods\AssettoCorsaMods\out-race-control`, using
`tools/Publish-RaceControl.ps1` with its default output directory. Saved launcher settings can point
to `out-race-control\lib\Server`; building into a different directory does not update that selected
server, and can leave the launcher serving an old FPS menu. Verify the staged server's protocol and
Modern archive revision when checking a delivered update.

Generated development assets and packages are for local validation until permission and live
acceptance are complete.
