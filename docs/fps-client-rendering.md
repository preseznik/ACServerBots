# FPS client rendering handoff

This document is the source of truth for the CSP client-side FPS scene and HUD. The server remains
authoritative for movement, shots, damage, scoring, ammunition, death, and respawn. Client scene
nodes and HUD widgets are presentation only.

## Confirmed remote-avatar desynchronization fix

### Failure signature

- Bot entities moved, acquired targets, and fired correctly on the server.
- Bot models appeared at their initial spawn and never moved or rotated.
- Shots aimed at the visible model hit scenery because the authoritative capsule was elsewhere.
- Server diagnostics and snapshots continued to report changing bot positions.

### Root cause

The native-scene smoothing pass called `math.lerpAngle()`. That helper does not exist in CSP's
online-script Lua runtime. Remote actor updates run in a protected call, so the error did not stop the
whole FPS client: actor target/render state was updated, but the exception occurred before
`root:setPosition()` and `root:setOrientation()`. The result looked like network desynchronization even
though only the presentation node had stopped receiving transforms.

Pipeline `native-scene-v21-angle-lerp-fix` replaced both unsupported calls with a local shortest-arc
helper:

```lua
local function lerpAngle(current, target, mix)
  local delta = (target - current + math.pi) % (math.pi * 2) - math.pi
  return current + delta * mix
end
```

`FpsClientScriptTests` requires the helper and pipeline identifier and rejects any reintroduction of
`math.lerpAngle`. The fix passed the automated FPS client/staging gates and was subsequently confirmed
in a live Fire Pit match: visible bot models now follow and face their authoritative entities.

## Native scene ownership

- Remote avatar target poses arrive in the 20 Hz authoritative snapshot.
- `script.update()` interpolates and applies native scene-node transforms early, before expensive local
  prediction and collision work can exhaust the online-script update budget.
- The local third-person avatar uses the same persistent native-node path.
- The first-person rifle remains camera-relative and is transformed in `frameBegin()`.
- Native models keep normal depth and motion history. Do not replace them with repeated transparent
  render callbacks; that path caused ghosting and jitter in earlier builds.
- Floating-origin compensation is applied exactly once when setting persistent world-node positions.
- The Modern operator is an animated child of this same proven root. `setAnimation()` and
  `blendAnimation()` may change the child pose only; they never own world position or yaw. See the
  [Modern visual theme handoff](fps-modern-theme.md) for the exporter, fallback, and preview520 gate.

## Pistol asset ownership

The MP5 SMG is no longer a rifle alias. `tools/Build-FpsMp5Assets.ps1`
extracts Rotuma's downloaded MP5 FBX from the external `.resources` tree into
a guarded temporary directory, aligns the trigger and grip to the proven carbine
hand pose, caps first-person textures at 2048² and world textures at 512², and
exports separate body and magazine meshes. The 27,948-triangle viewmodel reuses
the complete carbine arm mesh, 49-bone skeleton, and six source animation ranges;
two constant bones keep the MP5 rigid while normal and empty reload clips extract
and reinsert its magazine. The 14,248-triangle world model is gun-only and grip
anchored. Blocks and Modern select both variants by authoritative weapon ID 2;
Modern hides its baked carbine while the MP5 attachment is active. Base asset
archive v20 and client pack 29 carry the models, six animations, and
`compact-smg-attribution.txt`.

The source model is [MP5 Submachine Gun by Rotuma](https://sketchfab.com/3d-models/mp5-submachine-gun-a73b61932a0e4eecb5db5c63c158aa24),
licensed CC BY 4.0. The source archive and textures remain outside the repository.

The Desert Eagle is no longer a rifle alias. `tools/Build-FpsDesertEagleAssets.ps1` reads the
downloaded ELIZION FBX and textures from the external `.resources` tree, caps first-person body and
slide color at 1024², caps the world payload at 512², converts both variants through the pinned KN5
exporter, and then runs a Blender-independent KN5 structure and budget validator. The generated
viewmodel reuses the animated carbine's textured anatomical arms and original 49-bone arm rig,
then adds constant weapon and magazine bones. Project-authored idle, fire, equip, sprint, and reload
tracks preserve the right-hand firing grip, align the complete arm chain behind the pistol, and
extract/reinsert the separately skinned magazine over the authoritative 1.8-second reload. The
support arm is exported but its shoulder chain remains below the view frustum in idle, fire, equip,
and sprint; reload alone brings its wrist to the magazine and returns it off-screen. This avoids
relying on CSP's inconsistent per-skinned-mesh visibility. Each newly loaded pistol first evaluates
reload frame zero during the holder's hidden update, because CSP otherwise leaves constant non-reload
bone channels in the KN5 rest pose after weapon switching. The generator renders hip, ADS, and reload
acceptance previews through the same camera-relative offsets and FOV used by CSP. The pistol uses dedicated
hip/ADS framing instead of the Modern rifle's near-camera offsets. The world model
remains gun-only and anchored at its grip. Both Blocks and Modern request base asset
archive v20 for this loadout item. The HUD image and
loadout model consume one shared CSP remote-assets request for that URL to avoid concurrent cache
finalization. Modern hides its
baked carbine mesh while the pistol is active. Local first person, local/remote third person, and
dropped pickups all choose the model from the authoritative active weapon ID.

The source model is [Desert Eagle by ELIZION](https://sketchfab.com/3d-models/desert-eagle-cabde59f5cf24effaf80536e35d04e95),
licensed CC BY 4.0. Every server archive and exported client pack that carries the KN5 also carries
`desert-eagle-attribution.txt`; the portable launcher additionally ships `THIRD_PARTY_NOTICES.md`.
The source FBX and textures remain outside the repository.

The Desert Eagle uses a 1 cm holder-left ADS correction relative to the Colt so its backward-facing
KN5 moves camera-right on screen and its exported iron-sight axis meets the authoritative
camera-center hit ray. Hip-fire framing and the Colt ADS offset remain unchanged.

The Colt 1911 follows the same ownership and runtime path. `tools/Build-FpsColt1911Assets.ps1`
reads DanaeH's external M1911 FBX and its pistol/magazine texture sets, rotates the source onto
the established weapon axis, inserts the separately authored magazine into the grip, and omits
the loose cartridge display geometry. The resulting 11,303-triangle world KN5 and
25,003-triangle two-arm viewmodel retain distinct body, slide, and magazine nodes. Five
Colt-specific KSANIM files reuse the accepted firing-hand pose, bring the support hand in only for
reload, and drive the Colt magazine. Colt and Desert Eagle use the same calibrated pistol hip
framing and weapon-specific ADS horizontal offsets, but select independent KN5 and KSANIM files
by authoritative weapon ID. Base asset
archive v20 and client pack 29 include the Colt model, animations, and
`colt-1911-attribution.txt` attribution notice.

The source model is [M1911 Pistol with magazine and bullet by DanaeH](https://sketchfab.com/3d-models/m1911-pistol-with-magazine-and-bullet-131085c22ece47a08076d8ddc0b9f21a),
licensed CC BY 4.0. The source FBX and textures remain outside the repository.

## Hybrid HUD ownership

Client pack version 29 installs one background-loaded CSP app at `apps/lua/asrc_fps_hud`. The online
script publishes presentation state through the local shared structure `asrc.fps.hud.v5`. Bridge v5
adds the active main/secondary slot, item IDs, and lethal count; it remains presentation-only.

While both sides exchange a current version-5 heartbeat, the app draws the modular FPS HUD through
`ui.onExclusiveHUD()` and suppresses regular AC UI and third-party apps only in active gameplay. The
app returns normal control in pre-match menus, results, replay, and non-FPS sessions. In `pause` mode,
the server-delivered online script owns a match-specific menu and standings panel; its options action
can explicitly yield to the native AC/CSP menu. If the app is absent, disabled, incompatible, or silent
for more than 0.5 seconds, the online script resumes its complete exclusive gameplay HUD. A bridge
mismatch is logged once and must never produce a blank frame.

Bridge v5 carries ADS presentation, configured maximum health, predicted/authoritative stamina, and
the current loadout presentation.
The companion HUD and the online fallback both suppress the ordinary four-line crosshair while ADS
is active, but retain authoritative hitmarkers and award popups. Both paths use matching lower-corner
panels with health and stamina bars plus actual rendered carbine artwork, ammunition, reserve
magazines, and reload progress. Older HUD apps fail the bridge-version check and automatically yield
to the complete fallback.

The first combat radar is player-up and limited to 40 m. A living, non-protected opponent is revealed
only by a clear client track-geometry raycast or for two seconds after its authoritative shot event.
Death, respawn, or roster replacement clears the reveal state.

The simpler supported alternative is to omit the companion app and retain the entire HUD inside the
online script's exclusive callback. It has fewer packaging concerns but no modular app layer.

## Troubleshooting visible-versus-authoritative mismatches

1. Compare the server's once-per-second bot pose with the client's snapshot target and rendered pose.
2. Confirm the client log reports `native-scene-v21-angle-lerp-fix` and successful remote scene creation.
3. Inspect the first protected native-scene failure, not only the final visible symptom. A missing CSP
   API can leave data tables current while preventing the node transform at the end of the block.
4. Verify transforms are applied from `script.update()`, and that no later callback writes the same
   node back to a stale position.
5. Verify origin shift is added once to persistent world nodes and never to server coordinates stored
   in actor state.
6. Keep the main online-script chunk below CSP's 200-local limit and retain the automated local-count
   guard.
7. After any rendering change, test visible movement, rotation, shooting alignment, death, and respawn
   in a live multi-bot match; compilation alone does not validate CSP scene behavior.
