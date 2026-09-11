# Quaternius operator locomotion

Modern uses Quaternius Universal Animation Library Source v3.0 for standing
locomotion. Officer and Ghost share ten converted clips: forward walk, eight
jogging directions and forward sprint. First-person assets, operator geometry,
skins, movement speeds, hitboxes and the FPS wire protocol are unchanged.

## Inputs and reproducible build

The supplied extracted source remains at
`F:\Coding\Codex\.resources\AssettoCorsaMods\FPS\Characters\Universal Animation Library[Source]`.
`UAL1.blend` SHA-256 is
`7f37b87e27a55705d32f8e5f186e5855781e97626db7abb85ce6936ecfff08ae`.
Its `License.txt` declares CC0-1.0; its hash and the provider URL are recorded
in the generated asset manifest. Source files are read with Blender scripts
disabled. No source controls, meshes or textures are included in this stage.

Run `tools/Build-FpsLocomotion.ps1` to rebuild animations independently. It uses
the Officer/Ghost reference blends in `.artifacts/ghost-work`, stages output in
`.artifacts/quaternius-work/assets`, validates it, and replaces only generated
KSANIM files and the manifest. `-ProofOnly -Render` generates previews without
changing shipped assets. `tools/Build-FpsModernAssets.ps1` invokes the same stage
after geometry and skin generation, so full rebuilds retain the new animations.

World-space rest deltas map the source rig to the canonical 68 bones. Root travel
is removed while pelvis sway, leg articulation and ankle motion are retained.
A constant per-clip sole correction grounds the Officer mesh without removing
flight phases. The chest absorbs part of the unarmed motion; baked arm IK preserves
existing two-hand attachment positions and curled fingers. Arm stretch is bounded
to 8%, and grip error above 5 mm fails the build. No runtime IK is needed.

Direction uses measured root travel. Quaternius's `Jog_Left` travels +X; the FPS
client's positive right axis is +X, so source left/right labels are mapped
accordingly. Root travel supplies stride lengths: approximately 1.29 m walk,
4.97 m forward jog, 2.98 m lateral/backward jog and 5.47 m sprint. Endpoints are
closed exactly and the source's shared foot phase is retained.

## Runtime contract

`asrc-modern-assets.json.operatorAnimations` records file, duration, track count
and coverage. Locomotion adds stride, source action, direction, loop and conversion
metrics. The client checks this catalog before loading actors; package hashes
cover the catalog and clips.

Horizontal velocity is smoothed independently of vertical motion. Forward walk
blends into jog between 2.4 and 3.6 m/s. Sprint uses hysteresis at 7.2/6.7 m/s and
blends over 6.7–8.3 m/s; normal 6 m/s movement remains a jog. Other directions use
matching jogging clips. Adjacent directions blend continuously, and distance
travelled advances one shared foot phase using the blended stride length.

Spawn, model replacement, hard reconciliation, teleport and long frame gaps clear
movement history. Crouch, prone, traversal, airborne and death retain their clips.
Fire and reload contain 56 upper-body tracks, excluding hips, root and both legs;
they no longer reset the leg cycle. Existing weapon attachment positions remain.

Modern archive revision is 12, client pack is 52, FPS protocol is 6 and HUD bridge
is 14. Publish development builds only to `out-race-control`.

## Validation and remaining live acceptance

- Both operators pass evaluated-geometry checks at every frame of the ten new
  clips: 588 poses total. Maximum measured grip error is 0.125 mm.
- Binary checks cover all 25 operator and six first-person clips, shared bind
  matrices, finite transforms, loop closure, bounded hips, fixed actor root,
  moving ankles, material/texture budgets and hashes.
- `tools/test_fps_locomotion.py --lua-runtime .artifacts/lua-runtime` exercises
  real Lua at 30/60/144 FPS, all directions, gait thresholds, phase continuity,
  jitter, vertical motion, overlays, stance transitions and resets.
- `tools/test_fps_animation_validation.py` rejects corrupt roots, hip travel,
  loop pops, frozen ankles, non-finite frames, foreign bones and unsafe overlays.
- `tools/render_fps_locomotion_comparison.py` renders old/new KSANIM files at their
  actual steady-state playback cadences. Local comparisons and geometry reports
  live in `.artifacts/quaternius-work`.

These checks do not establish two-client CSP acceptance or client frame-time
performance. Live acceptance must cover both teams/models, every weapon, movement
while firing/reloading, death/respawn and repeated match restarts. Compare warmed-up,
identical 8/16/32-actor scenes against the prior build and record p95 client frame
time and memory; the frame-time target is at most +10%. Terrain foot IK, new
distance LODs, turn-in-place and custom first-person arms remain outside this change.
