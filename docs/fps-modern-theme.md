# FPS Modern visual theme handoff

This document owns the generated asset and CSP runtime contract for the optional **Modern** FPS
visual theme. **Blocks** remains the default and retains the existing procedural operator and rifle
without changes. Theme selection is server-wide, is written as `Extra.Fps.Theme`, and is captured
when Race Control stages the server. Editing the selector during a match affects only the next stage
or restart.

## Source and redistribution record

The user confirmed redistribution rights for generated derivatives of these supplied sources:

- operator: `F:\Coding\Codex\.resources\AssettoCorsaMods\FPS\Characters\army-officer`;
- first- and third-person carbine: `F:\Coding\Codex\.resources\AssettoCorsaMods\FPS\Weapons\fps-animated-carbine`.

The `m4a1` source is deliberately not used. Its unrigged source is far beyond the replicated-actor
budget and would add a second weapon pipeline without improving this milestone. Source files remain
outside the repository. Only generated KN5, KSANIM, and provenance metadata are shipped.

The pinned KN5 exporter retains its existing GPL notice. The KSANIM writer is adapted from revision
`920bac087de1caad32ae63725dc6fa302c9b9c18` of `jwl-7/blender-assetto-corsa-tools` and retains
GPL-3.0-or-later provenance in the source and generated manifest.

## Deterministic build

Run from the repository root:

```powershell
.\tools\Build-FpsModernAssets.ps1
```

The wrapper uses Blender 5.1 and the repository-pinned exporter. It removes stale generated outputs,
imports one selected officer appearance, removes unused accessories and source datablocks, reduces
the replicated carbine, writes KN5 `SkinnedMesh` records, and exports deterministic 30 FPS KSANIM
clips. A Blender process can return exit code zero after a Python exception, so the wrapper also
requires `validation.status = passed` in `asrc-modern-assets.json`; file existence alone is not a
successful build.

`tools/validate_fps_modern_assets.py` independently parses every generated KN5 and KSANIM. It checks
KN5 version and structure, node and inverse-bind matrices, finite vertices, four normalized weights,
valid bone indices, skinned shaders, material references, triangle counts, 2K texture limits,
KSANIM track compatibility, a non-rest rifle-ready grip, finite frames, planar root lock, file
hashes, and shipping budgets. Two successive builds currently produce identical hashes for all 30
files.

Current generated budgets:

| Asset | Triangles | Materials | Bones | Animated meshes |
|---|---:|---:|---:|---:|
| Operator plus world carbine | 36,322 | 4 | 68 | 4 |
| First-person carbine and arms | 25,111 | 3 | 50 | 3 |
| Dropped carbine pickup | 6,000 | 1 | 0 | 0 |

The viewmodel contains separate, named skinned meshes for the arms, the complete rifle, and the
32-polygon optic lens. The lens uses a generated 512 px transparent texture with a 14 px red core and
soft 18 px glow, replacing the oversized source ring. It uses alpha blending without depth writes and
is validated as a transparent render node so the sight remains visibly open in ADS. CSP
preview520 dropped rigid rifle children from a dynamically loaded animated KN5 even though those
nodes were valid in the binary, so the exporter now combines the source rifle parts and binds them
to a dedicated constant deform bone. This forces the weapon through the same proven SkinnedMesh
render path as the arms. The binary validator rejects duplicate node names, missing animation
targets, fewer than three skinned viewmodel meshes, a missing named rifle or optic mesh, an opaque
optic material, or trivial rifle bounds. Animated object wrappers and geometry children still use
distinct names to prevent a KSANIM transform from being applied twice.

## Animation contract

Every living operator clip starts from one common rifle-ready pose. The build solves both arm chains
with deterministic two-bone IK targets, bakes the wrist rotations and curled finger tracks, and
calibrates the rigidly skinned carbine from the evaluated right-hand deformation. The stock therefore
seats at the shoulder, the right hand closes around the pistol grip, and the left supports the
handguard without letting animation own actor world position. The validator rejects the source
rest/T-pose and open-hand tracks. For visual QA, run `tools/render_fps_operator_pose.py` through
Blender with the repository, officer ZIP, carbine FBX, and an output directory; it emits full-body,
front, side, rear-three-quarter, and close grip PNGs.

The operator set contains aim idle/up/down, forward/backward walk, left/right strafe, sprint,
crouch idle/move, prone idle/crawl, jump start, airborne, land, mantle, vault, fire, reload, and
death. Crouch and prone each solve their two-hand grip after applying the stance-specific torso
transform; they do not reuse standing arm matrices. Their hips are authored on the source rig's
actual Y-up axis. The crouch now grounds at a 1.21 m visual bound with thighs carried forward,
knees folding backward, shins returning under the hips, and the rifle lowered with the torso; prone
remains at 0.77 m. The asset validator rejects missing stance tracks,
insufficient hip/knee/hand changes, and motion clips without a real crouch/crawl cycle. Locomotion
crossfades over roughly 120 ms. Death freezes on its final frame. Snapshot flags provide stance and
grounded state. Offline Blender validation alone is not sufficient for stance grounding: the KN5 to
KSANIM coordinate conversion writes the crouch and prone hips track 50 cm above the standing track
in CSP animation space. Preview520 therefore applies an exact -0.50 m world-up correction to the
dynamically animated KN5 child for those two stances. The confirmed actor root stays at the
authoritative position and never receives a stance offset, so collision, hitboxes, interpolation,
and corpse anchoring remain unchanged. Prone is selected from immediate local stance in third
person and is repeated in the otherwise-unused upper traversal bit when no traversal is active, so
remote CSP clients do not depend solely on bit 7 of the compact flags byte. The same compact
two-bit-per-actor field still distinguishes active mantles from vaults without changing FPS protocol
version 1.

On the client, the authored death clip is combined with a deterministic full-body collapse. The
clip buckles both knees, twists the torso, releases both arms and hands, and is validated to differ
materially from the rifle-ready pose. The scene root inherits a bounded amount of the actor's last
rendered velocity, falls under gravity to the snapshot's authoritative support height, pivots 84
degrees at the feet, adds a small deterministic lateral roll, settles, and is hidden after 3.75
seconds (or immediately when a new spawn generation arrives). The attached weapon is hidden at
death. This is deliberately an authored skeletal collapse plus rigid scene-root simulation: CSP
preview520 does not provide a safe per-bone physics ragdoll path for dynamically loaded skinned KN5s.
Damage, collision, death, and respawn remain server-authoritative, and both Blocks and Modern use
the same corpse lifetime.

Every lethal hit creates a separate server-authoritative rifle pickup at the victim's support
position. Its rigid 6,000-triangle model falls and settles independently of the corpse. After a 0.4
second collection delay, the first living actor within 1.1 metres who carries the same rifle and has
fewer than four reserve magazines receives exactly one reserve magazine. The defeated owner cannot
collect their own drop. Uncollected pickups expire after 15 seconds, and the server caps the active
set at 32. Spawn and removal use the reliable `ASRC_FpsPickup` event; late joiners receive the current
set before play.

The first-person source ranges are preserved:

- fire: frames 1-7;
- reload: 11-69;
- empty reload: 71-135;
- idle: 180-205;
- equip: 207-223;
- sprint: a project-authored lowered loop based on the idle range.

Reload playback is normalized to the authoritative 1.8-second weapon timer. Animation never changes
gameplay timing, hitboxes, shot origin, recoil, wall retraction, or damage.

## Runtime ownership and fallback

The server injects the validated `Blocks` or `Modern` marker into its delivered online Lua. Modern
downloads `/fps/assets/asrc-fps-modern-v8.zip` through the same `web.loadRemoteAssets()` path as the
existing rifle. CSP caches that payload by URL, so the archive revision must advance whenever any
embedded KN5 or KSANIM changes. Client pack version 29 also installs both themes under the project-owned
`content/objects3D/asrc_fps` tree.

MP5 SMG, Desert Eagle, and Colt 1911 rendering use separate rigid KN5 paths shared with Blocks.
Modern also requests base asset archive v20, hides `ASRC_CARBINE_WORLD` while weapon ID 2, 3, or 4 is
active, and attaches the selected rigid weapon at the operator's weapon root. The SMG viewmodel retains
the complete carbine arm rig and animation ranges, with a dedicated magazine bone used by normal and
empty reload clips. First-person pistols use a dedicated skinned
weapon-specific pistol KN5 which retains the anatomical carbine arms and 49-bone arm rig while replacing
the rifle and optic meshes. Each pistol has five project-authored KSANIM tracks for idle, fire, equip,
sprint, and reload motion. The support shoulder is animated below the view frustum for every clip
except reload, when the left hand follows the separately skinned magazine. The viewmodel holder also
tilts the complete pistol rig down 22 degrees during that reload window. Third-person hand posing remains a
later animation pass; the existing rigid world pistol attachment is unchanged.

The confirmed `native-scene-v21-angle-lerp-fix` actor root remains the sole owner of authoritative
world position and yaw. A Modern model is an animated child of that root. KSANIM never writes world
movement. Spawn, teleport, and large reconciliation corrections reset animation and motion history.
Any Modern download, manifest, KN5, KSANIM, initialization, animation, or scene-update error disposes
Modern nodes, reports `modern-fallback` to the server log, leaves a persistent HUD error, and rebuilds
the playable Blocks path. Blocks does not depend on any Modern asset.

## CSP preview520 proof gate

The file and packet gates are automated, but dynamic skinned KN5 safety still requires a real
Assetto Corsa process. CSP documents `SceneReference:setAnimation()` and `blendAnimation()` in its
[scene API](https://github.com/ac-custom-shaders-patch/acc-lua-sdk/blob/main/lib_scene.lua), while an
older unresolved [dynamic skinned KN5 crash report](https://github.com/ac-custom-shaders-patch/acc-lua-sdk/issues/4)
makes compilation insufficient evidence.

Before calling Modern release-ready on CSP `0.3.0-preview520`, run both an 8-actor and 32-actor Fire
Pit match and verify:

1. join, initial model creation, and disposal do not crash;
2. skin deformation and all stance/traversal/action clips remain aligned with hitboxes;
3. death reaches the full horizontal collapse, the carried weapon disappears, the dropped rifle
   remains independently visible, respawn resets, and repeated respawns do not leak stale models;
4. first-person recoil, wall retraction, tracers, impacts, and center-view hitscan remain unchanged;
5. motion vectors show no ghosting, jitter, or stale teleport history;
6. a missing or corrupted Modern asset produces the persistent Blocks fallback instead of a crash.

Also verify that a corpse falls from elevated geometry, settles without sinking below its support
surface, disappears after 3.75 seconds, and cannot be hit after authoritative death. Verify a second
living rifle user can collect the dropped weapon for exactly one reserve magazine, while a full-ammo
actor and the defeated owner cannot consume it.

Record the CSP build, actor count, arena, and result here when that live gate is completed. If
preview520 crashes in native skinned loading, keep Blocks as the only release theme and treat Modern
as blocked by CSP rather than converting it to a rigid model.
