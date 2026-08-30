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

## Hybrid HUD ownership

Client pack version 6 installs one background-loaded CSP app at `apps/lua/asrc_fps_hud`. The online
script publishes presentation state through the local shared structure `asrc.fps.hud.v1`; no gameplay
packet or server protocol changes are involved.

While both sides exchange a current version-1 heartbeat, the app draws the modular FPS HUD through
`ui.onExclusiveHUD()` and suppresses regular AC UI and third-party apps only in active gameplay. The
app returns normal control in pre-match menus, results, replay, and non-FPS sessions. In `pause` mode,
the server-delivered online script owns a match-specific menu and standings panel; its options action
can explicitly yield to the native AC/CSP menu. If the app is absent, disabled, incompatible, or silent
for more than 0.5 seconds, the online script resumes its complete exclusive gameplay HUD. A bridge
mismatch is logged once and must never produce a blank frame.

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
