# FPS perks and field upgrades

## Playing

Open **PERKS** between GEAR and OPERATOR in the loadout menu. Choose one field
upgrade and up to three distinct perks, then confirm. Click a slot before choosing
a perk; click its selected card again to clear it. Moving a perk to another slot
clears its former slot. Changes made while alive apply on the next spawn.

- Keyboard **X** or controller **LB + RB** activates the selected field upgrade.
- Keyboard **F** or controller **X** uses a nearby friendly Munitions Box.
- Hold RB alone to cook a grenade as before. A short RB tap still throws. The
  shoulder-button chord has a 120 ms grace window; adding LB after cooking has
  already started does not cancel the grenade.
- The strip above the ammunition panel shows the selected upgrade, ready/recharge
  state, and remaining Dead Silence time.

These are initial balance values, kept in server code rather than adding a new
configuration surface for every constant:

| Field upgrade | Behaviour |
| --- | --- |
| Munitions Box | Dropped 1.2 m ahead on supported ground. Within 2 m and clear line of sight, refills both weapons' magazines/reserves and the selected grenade type to its carrying capacity. Four resupplies; each player may use that box once per life. Own/friendly players only. Does not consume a use if already full. |
| Dead Silence | Suppresses footsteps and gunfire audio and enemy radar reveals for 15 seconds. |
| Trophy System | Intercepts up to three hostile thrown frag/sticky grenades within 6 m and clear line of sight. Intercepted grenades disappear without exploding. Does not intercept own/team grenades or grenades still held by a player. |

Upgrades start empty when joining or starting a new round and charge over **120
seconds of alive gameplay**. Score boosts charge by **0.15 seconds per point**:
a 100-point kill adds 15 seconds and a 50-point assist adds 7.5 seconds. Boosts
cannot make an upgrade ready earlier than **60 seconds** into a charge cycle.
Death pauses charging and respawning preserves progress, including a ready charge;
switching the selected upgrade does not grant a free use. Using an upgrade resets
its charge. The HUD icon starts gray and fills from bottom to top as charge grows.
Deployable devices
last 60 seconds, have 100 health, and can be destroyed by enemy gunfire (or their
owner). Deployment blocked by a wall or unsupported ground consumes no charge.
Each owner has at most one physical device; replacement, leaving the server and
round restart clean it up. Death alone does not remove an existing device.

| Perk | Behaviour |
| --- | --- |
| Slipstream | +15% movement speed, including stance, sprint and ADS movement. |
| Ninja | Footsteps at 15% normal volume. |
| Vision | Red/orange screen-space rectangles around enemies for two seconds after spawning, including through cover and excluding the opening countdown. No character mesh or outline shader is required. |
| Engineer | Red outlines on hostile deployed field upgrades. |
| Flack Jacket | Takes 25% normal grenade damage, capped at current health minus one. A single explosion cannot kill the wearer, including an overcooked grenade. Repeated explosions can leave the wearer at 1 HP; bullets remain lethal. |
| Fast Hands | Weapon-switch action lock 0.12 s instead of 0.35 s; client equip presentation 0.19 s instead of 0.55 s. Does not accelerate reloads. |
| Gear Head | Carries two grenades of the selected type, not one frag plus one sticky. Resupply restores both. |

Existing loadouts default to Munitions Box and empty perk slots. Server allowed
lists govern selection; unrecognised, duplicate or disallowed perks are rejected.
Bots inherit configured passive perks but do not autonomously deploy upgrades.

## Implementation and versions

- `FpsFieldUpgrades.cs`: server-authoritative cooldowns, devices, interaction,
  interception, hit detection and device lifecycle.
- `FpsSimulation.cs` / `FpsItems.cs`: perk effects, spawn/loadout state, damage,
  movement and ammo. The existing firing and grenade simulation remain authoritative.
- `FpsPackets.cs` / `FpsWorld.cs`: validated selections, replicated perk masks,
  timed abilities and reliable device events. Clients cannot grant themselves
  charges, ammo, silence or extra grenades by changing HUD state.
- `fps.lua`: loadout cards, input chord, client movement prediction, audio/radar
  presentation, device models and grouped outline rendering. Stale timed state
  expires; death, menus and round reset clear outlines.
- Companion HUD and online fallback share identical ammo/upgrade rendering and
  the same state fields. Both paths must be kept in sync.

Client pack **60**, ready protocol **7**, HUD bridge **17**, HUD app **1.17.2**,
base asset archive **23**. Modern character archive remains unchanged. Restart
Race Control/server and rejoin after installing this version; an old active online
script cannot acquire new packet layouts merely by replacing the HUD app file.

## Art and provenance

The Tripo 3D and Tripo game-asset skills were used for the two props, with Blender
handling local scale/material cleanup and KN5 conversion. Two approved textured
P1 generations cost **40 credits each, 80 total**. No paid conversion or reroll.

| Asset | Tripo task | KN5 triangles | Normalised dimensions (m, X/Y/Z in Blender) |
| --- | --- | --- | --- |
| Munitions Box | `6a78d140-0954-4f63-bdd0-dc371fb5a8d5` | 5395 | 0.431 / 0.600 / 0.539 |
| Trophy System | `cbb0df44-88ba-40ef-a0d6-50b37ba3936d` | 5543 | 0.412 / 0.499 / 0.600 |

Runtime assets are in `AssettoServer.RaceControl.Core/Assets/Fps/`:
`asrc_munitions_box.kn5`, `asrc_trophy_system.kn5`, ten `asrc_upgrade_*.png` /
`asrc_perk_*.png` icons, and `asrc_perks_provenance.txt`. They are embedded in both
the server asset archive and launcher client pack. Generated art is identified
as generated, not falsely attributed to the existing Sketchfab CC-BY assets.

Original GLBs are retained locally:

```text
F:\Coding\Codex\Mods\AssettoCorsaMods\.artifacts\fps-field-upgrades\tripo-out\a-compact-military-videogame-mun-6a78d140\model.glb
F:\Coding\Codex\Mods\AssettoCorsaMods\.artifacts\fps-field-upgrades\tripo-out\a-compact-standalone-trophy-syst-cbb0df44\model.glb
```

Rebuild with Blender 5.1 and `tools/build_fps_field_upgrade_assets.py`, passing
`--munitions` and `--trophy` paths after Blender's `--`. The script emits runtime
KN5s, metrics and previews under `.artifacts/fps-field-upgrades/converted/`.
Source GLBs and previews are local artifacts, not tracked source-control files.

### Exact Tripo prompts

Parameters: chain-free `--for toy -p face_limit=6000`, default textured P1.

Munitions Box:

> A compact military videogame munitions resupply box, one standalone prop. Rugged olive drab hard-shell ammunition case with open hinged lid, neat rows of brass cartridges and two grenade-shaped resupply canisters visible inside, black reinforced corners and metal carrying handles, subtle cyan indicator on front. Broad flat base, readable chunky silhouette, realistic worn painted steel and polymer, no text, no logos, no environment or floor. Low-poly game-ready prop with clean textures, about 60 cm wide.

Trophy System:

> A compact standalone Trophy System defensive gadget for a near-future military videogame. One deployed prop, three short splayed tripod feet on a wide stable base, short upright central stalk, small rotating box-shaped sensor head with two squat cylindrical interceptor pods angled slightly upward, cyan sensor lens. Olive drab armored panels, black mechanical joints, brushed steel details, realistic worn PBR textures. Readable low-poly silhouette. Approximately 60 cm tall. No text, no logos, no people, no ground or environment.

### Exact imagegen prompt set

The **imagegen skill, built-in tool mode**, produced separate transparent raster
icons. Final selected PNGs were copied to the runtime asset folder; no API/CLI
fallback was used. Style references were generated icons, not external franchise art.

Munitions Box (first style reference):

> Create one finished game UI icon for MUNITIONS BOX, an ammunition field upgrade. Original tactical FPS perk icon, not copied from any franchise. Square 1024x1024 raster asset with genuinely transparent background. Centered compact open military supply case with three recognizable cartridges and a small grenade inside. Bold clean readable white and pale steel silhouette with restrained vivid cyan edge accents (#26c6df), dark navy inner separations. Stylized embossed screen-printed insignia, mostly flat shading, crisp thick forms readable at 64 pixels. Occupy central 76% of canvas, generous even transparent padding. Front three-quarter view, no perspective scene, no floor, no shadows outside icon. No text, letters, numbers, branding, outer badge frame or border. Deliver only a single icon.

Other final icons except Slipstream used the generated Munitions Box as a style
reference and the following template, substituting NAME and SUBJECT verbatim:

> Create ONE new standalone game UI icon for NAME. Use the reference ONLY for the visual style, palette, and clean embossed insignia finish. Do not reproduce the ammunition case. New subject: SUBJECT. Original tactical FPS icon, not copied from any franchise. Square raster, genuinely transparent background. Centered bold crisp thick shapes, readable at 64 pixels, central 76% of canvas and generous even transparent padding. Pale steel and white edges, dark navy inner separations, restrained vivid cyan highlights (#26c6df); small muted olive details where appropriate. Stylized screen-printed/embossed insignia, clean mostly flat shading. No text, lettering, numbers, branding, outer border, floor, surrounding scene or external shadow. Single icon only.

| NAME | SUBJECT |
| --- | --- |
| DEAD SILENCE field upgrade | a hooded covert operative face with a small crossed-out sound wave, communicating temporarily silent footsteps and gunfire |
| TROPHY SYSTEM field upgrade | a compact sci-fi defensive gadget on three short tripod feet with a twin sensor head, small cyan shield arc deflecting a grenade |
| NINJA perk | a masked ninja face and one tiny stealth footprint, quiet movement |
| VISION perk | a focused eye with a cyan infrared enemy silhouette reflected inside its pupil |
| ENGINEER perk | a sturdy mechanic wrench crossed with a compact sensor circuit, identifying hostile gadgets |
| FLACK JACKET perk | a heavy armored protective vest with a cyan shield stopping three simple blast fragments |
| FAST HANDS perk | a tactical gloved hand gripping a pistol magazine with two compact swift exchange arrows, fast weapon handling |
| GEAR HEAD perk | two clearly separate compact military grenades side by side inside a simple partial cog silhouette |

Final Slipstream, using the generated Ninja icon as style reference (earlier
checkerboard-backed variants were rejected):

> Create a new companion game HUD icon matching the attached reference's rendering, palette, outline weight and transparent background. Subject: SLIPSTREAM, one sturdy military boot in side profile with three cyan speed streaks trailing behind. Olive drab boot, navy shadows, white metallic trim, bright cyan accents. Isolated centered original icon, no lettering. Transparent background RGBA PNG cutout. Empty background has alpha zero. No backdrop, no pattern, no ground, no shadow. Square game UI asset, same readable compact silhouette as reference.

## Verification and live acceptance

Initial pack-58 validation on 2026-09-12 (superseded by the charge/outline pass):

- Server tests: **282 passed**; Race Control tests: **97 passed**.
- Lua harnesses: perks, loadout menu, ammo HUD, aim target, camera clipping,
  lethal medals, match results, movement prediction, locomotion and operator state passed.
- Independent KN5 parsing verified both complete files, opaque materials, 1K
  embedded textures, triangle counts, and Y=0 bottom-centred runtime pivots.
- Published `out-race-control/AssettoServer Race Control.exe` with pack 58.
- Packaged Modern Deathmatch and Team Deathmatch smoke tests each passed with
  eight named moving actors, rifle fire, served base-v23/modern-v14 archives,
  correct operator assignments, and graceful shutdown.
- Installed and SHA-256-verified all 13 ability assets plus the two HUD app files
  against the published pack's embedded bytes. Previous HUD files are backed up
  under `.artifacts/fps-perks-install-backup-20260912-142927/`.

Automated coverage includes server ability/perk tests, solid-wall deployment,
resupply/interception/shot occlusion, all C#/Lua packet schemas, real Lua controller
chord handling, selection interaction, stale-packet handling, outline lifecycle,
Trophy visual removal, asset alpha, and identical online/companion HUD rendering.
The menu and prop previews are draw-call/Blender renders, **not CSP screenshots**.

In-game acceptance still requires two actual CSP clients:

1. Select each upgrade and three perks; switch weapons and respawn. Confirm pending
   choices are retained and applied only at spawn. Check keyboard/controller inputs.
2. Deploy on floor, stairs, slopes and beside walls. Check scale, grounding, lighting,
   visibility for the other client, 60-second expiry and leaving/rejoining cleanup.
3. Spend ammo from both guns and throw grenades; resupply with F/controller X.
   Gear Head must restore two of the selected type. Verify range, wall and team rules.
4. Throw enemy frag and sticky grenades near Trophy. Check three interceptions,
   no detonation after interception, own/team grenade immunity and wall occlusion.
5. Shoot enemy gadgets until destroyed. Engineer outlines should disappear with them.
6. Check Vision for two seconds after spawn and Engineer enemy-only outlines through
   obstructions; confirm death, menu and round reset do not leave stuck outlines.
7. Listen from another client: Ninja footsteps quieter, Dead Silence footsteps/fire
   suppressed and radar hidden for exactly the active period, then normal again.
8. Compare movement and switching with/without the respective perks. Test Flack Jacket
   at full/low health, overcooking, and lethal gunfire. Recheck existing pistol reloads.

### Pack 59: Vision and earned field charge

Vision and Engineer previously used an empty `mesh:` filter. CSP's installed
scripts use `?` for all names, and its API provides a separate `findSkinnedMeshes`
query for character meshes. Vision now explicitly selects the Modern skinned body
(or rigid meshes for Blocks); Engineer uses the proper wildcard for gadgets.
The two owning outline references are retained and reused, not discarded each
frame. `Vision active: outlined meshes=N` in the CSP log records actual selection
counts for in-game diagnosis; zero would indicate another remaining rendering issue.

Regression harnesses now reject incorrect filters, distinguish skinned/rigid meshes,
check retained reference lifetime, and assert gray-icon shading and bottom-anchored
fill clipping at 0/25/50/75/100% on both HUD paths. Server tests cover first-use delay,
kill/assist charge boosts, the one-minute floor and progress across respawn/loadout
changes. Actual CSP outline visibility still requires an in-game check.

Pack-59 validation: 286 server tests and 97 launcher tests passed, plus all ten Lua
regression harnesses. The updated executable is published in `out-race-control`.
HUD 1.17.1 is installed and hash-verified; the previous two HUD files are retained
under `.artifacts/fps-charge-hud-backup-20260912-173404/`.

### Pack 60: ability transport fix and Vision rectangles

The shared charge/Vision timer event was sent with `[OnlineEvent(Udp = true)]` but
registered without the UDP argument in Lua. The initial targeted send also used
the TCP send method for a UDP-encoded packet. Both now consistently use UDP, just
like the regular actor snapshots. This fixes the missing authoritative ability
updates that left the HUD showing its 120-second fallback and no Vision timer.
The previous packet schema tests did not test transport selection. The strengthened
test reproduced `ASRC_FpsAbilities: C#/Lua TCP/UDP registration mismatch` before
the fix, and now audits actual registrations for all 21 event types.

Vision now draws a contrasting rectangle around each live enemy's projected bounds.
It has no mesh loading dependency, works through cover, follows standing/crouched/
prone bounds and skips dead/friendly/offscreen/behind-camera targets. Menus, death,
scoreboard and timer expiry suppress it. Rendering happens in the online HUD pass
whether the companion HUD app is present or not. The server-authoritative two-second
window and field charge balance are unchanged.

Pack-60 validation: 296 server tests and 97 launcher tests passed, plus all twelve
Lua regression harnesses (including the full 120-second timer replication and
Vision rectangle drawing checks). Published to `out-race-control`; HUD 1.17.2 is
installed and hash-verified. Previous HUD files are backed up under
`.artifacts/fps-charge-hud-backup-20260912-184407/`. These checks do not establish
actual CSP rendering acceptance: restart the server and rejoin, then verify the
charge counts down and Vision rectangles appear for two seconds after spawning.
The published Modern/Deathmatch smoke test also passed: eight moving actors,
authoritative-world startup, rifle fire, both served asset archives and graceful
shutdown.
