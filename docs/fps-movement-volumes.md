# Movement-only FPS volumes (Nuketown v0.1.16)

The bus roof is not playable. Do not add invisible KN5 collision walls: ordinary
physical triangles also stop bullets, grenade traces and visibility rays.

The track supplies four empty transforms per upright oriented box:
`FPS_MOVEBOX_<n>_CENTER`, `_X`, `_Y`, `_Z`. The axis points are the centre plus
each half-axis, in world metres. Y must point upward; X/Z must be horizontal
and orthogonal. Indices are contiguous from zero, maximum 256 volumes. Missing,
duplicate, nonfinite, degenerate, nonorthogonal or non-upright definitions fail
preparation instead of silently disabling the boundary.

`FpsArenaAssetBuilder` reads the transforms independently of collision meshes.
FPS preparation version 5 invalidates old geometry/navigation caches and bundled
seeds. Geometry binary version 2 appends the movement boxes after the unchanged
physical triangle payload; the new reader also accepts legacy version 1 maps.
Configuration and network packet schemas are unchanged. Old servers do not
enforce these volumes: use the updated server and re-prepare the arena.

`FpsArenaSurface` checks the boxes for actor occupancy and movement sweeps, not
in `TryRaycast` or ground-height queries. Boxes create neither support surfaces
nor roofs of their own. Navigation omits occupied nodes and crossing traversal
links. Server vertical movement and mantle arcs also check the volumes. Existing
server-only collision-direction snapshots constrain client prediction; no Lua
or client-pack changes are required by this feature.

The volume follows bus placement 270, yaw -75 degrees, local XY bounds
[-6.42,6.00] x [-1.70,1.70] metres and local Z [2.50,30.00]. Its lower world Y
is about 2.314 m, above street standing headroom. The high top prevents access
from nearby roofs. This is a movement restriction for humans and bots, not an
out-of-bounds death volume. The bus model and all shot collision stay intact.

Regression tools in the conversion workspace:
`tools/validate_bo2_movement_patch.py` requires exactly four new transforms and
unchanged visual triangles, materials, textures, previous nodes and collision.
`tools/Bo2BusGate` compiles the real server movement/navigation code and compares
prepared maps: 135 old roof nodes become zero, no crossing links, six unchanged
clear overhead shot rays, twelve blocked airborne entry probes and ten unchanged
street-side movement probes. The old passage/hedge/spawn probes remain required.
`FpsMovementBoxTests` covers parser rejection, rotation, stance, jumps, mantles,
navigation, shooting, serialization and legacy binary compatibility.

Live CSP traversal/prediction and two-real-client play still need manual
confirmation; unit tests and isolated server gates are not that evidence.
