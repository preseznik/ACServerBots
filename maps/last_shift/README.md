# Last Shift

Original municipal bus depot for Assetto Corsa / Race Control FPS mode.
Track ID: `last_shift`. Current milestone: **0.3.1-surface-roof-fixes**, 2026-10-05.

The roughly 90 x 70 m layout targets an initial 8-12 player test. It contains a
two-bay maintenance hall, an open bus yard, a covered wash lane with a crossover,
and a dispatch building with internal and external stairs to the upper landing.
There are 12 protected candidate spawn positions and four enterable fleet buses.
All geometry is original; no Nuketown content is used. Photographic CC0 materials
from Poly Haven cover the asphalt, concrete, brick, corrugated roofs, worn fleet
paint and rusty steel.

The detail pass adds eight streetlamps, a staff shuttle stop with timetable and
steel bench, wayfinding and traffic signs, bollards, wheelie bins and electrical
cabinets. The depot has a closed vehicle gate and an external road, concrete
working floors, gutters, downpipes, shutter guides, vents, window frames and
stair handrails. Bus fittings include folded open doors, rub rails, bumpers,
lighting units and roof ventilators.

Each bus has two open doorways with continuous boarding ramps, a low interior
floor, seats, a clear central aisle and open window apertures. The window openings
allow shots in both directions; lower body panels and the roof remain solid.
Wheels have shallow tyre geometry and flat textured sidewall/rim faces, with no
modelled hubcap hardware. The original wheel image and its provenance live under
`textures/generated/`.

All depot and bus lettering is rasterized into one shared 2048-pixel sign atlas.
Each sign face is a flat quad with an authored atlas rectangle. The WASH board
hangs from two rods attached to the canopy. Cover now belongs to a workshop
parts-receiving annex, a staff rest shelter, wash-water/electrical equipment,
refuelling equipment and planting beds. Low pavements and kerbs connect the
workshop, dispatch entrance and staff stop.

The maintenance stations have drawer fronts, tools, a vice, compressor, service
cart, parts bins, stored tyres and an extinguisher. The wash station has controls,
detergent and a hose reel; dispatch desks have telephones, binders and chairs.
This is a fictional working depot, with original geometry and fictional signage.

The 0.3.1 repair removes the duplicate front face behind each sign's bitmap,
relocates the bay KEEP CLEAR notices and depot banner, and cuts floor finishes
into adjoining visible faces instead of stacked coatings. It also removes the
buried brick sill cap at the upper doorway and the duplicate bus-floor finish.
The external dispatch landing gains a continuous north parapet and a return
guard beside the stairs. The utility shed gains parapets, a twin-fan condenser,
ducting, exhaust, a hatch and an aerial while retaining its roof movement
exclusion. These are source/geometry repairs; distance-dependent AC/CSP flicker
still requires a fresh in-game check.

## Source and build

- `layout.json`: map identity, bus positions and authored spawn transforms.
- `../../tools/build_last_shift.py`: original modular geometry, collision,
  metadata, render cameras, materials, minimap and movement/shot probes.
- `../../tools/Build-LastShift.ps1`: Blender build, actual server-code validation,
  installable archive and SHA-256 receipt.
- `../../tools/LastShiftGate/`: isolated .NET 10 gate linking current server source.
- `textures/manifest.json` and `textures/SOURCES.txt`: texture sources, artists,
  licenses, physical tile dimensions and verified download hashes.
- `../../tools/validate_last_shift_materials.py`: embedded image, shader-slot,
  normal/tangent and hidden-collider validation.
- `../../tools/last_shift_detail_api.py`: shared metric fittings, building/bus
  detail, lighting positions and material/cell batching to reduce draw calls.
- `../../tools/last_shift_street_props.py` and `last_shift_workshop_props.py`:
  coherent furnishing clusters placed around existing circulation routes.
- `../../tools/last_shift_buses.py`: open bus shells, seats, door ramps, textured
  wheels and bus-specific movement/shot probes.
- `../../tools/last_shift_decals.py`: exact text rasterization with Pillow,
  deterministic atlas packing, one bitmap front per sign and backing-face removal.
- `../../tools/last_shift_site_layout.py`: attached service facilities, staff
  shelter, suspended WASH board and pedestrian pavements.
- `../../tools/last_shift_surfaces.py`: cuts overlapping floor finishes into
  adjacent visible faces and removes buried sill/duplicate bus-floor faces.
- `../../tools/last_shift_rooftops.py`: landing guards, roof parapets and equipment,
  with landing movement/shot probes.
- `detail-probes.json`: 24 additional clearance routes for doorway shoulders,
  stair lanes and circulation around furniture.
- `bus-clearance-probes.json`: independent routes through both shoulders of each
  bus entrance; the detail gate checks their inclusion in the movement probes.
- `../../tools/last_shift_detail_checks.py`: shipped proxy dimensions/flags,
  source hashes, detail inventory, sign atlas rectangles, wheel orientation and
  geometry budget checks.

From the repository root:

```powershell
pwsh -NoProfile -File tools/Build-LastShift.ps1
```

Requires the locally installed Blender 5.1 and .NET 10 SDK. The script uses the
repository-pinned Blender Assetto Corsa exporter. Sign rasterization needs Python
with Pillow and a TrueType font; `LAST_SHIFT_RASTER_PYTHON` and
`LAST_SHIFT_SIGN_FONT` override the local defaults. Rebuilding reuses the saved
wheel image and surface textures; it does not generate or download new artwork.
Map output stays in `artifacts/last-shift-v031/` by default:

```text
last_shift.blend                 editable generated authoring snapshot
last-shift-layout.json           semantic geometry and gameplay probes
overview.png / yard.png          Blender material previews, not AC screenshots
workshop.png / layout.png        interior and cutaway overhead previews
dispatch.png                    close view of the textured dispatch building
staff-stop.png / workbench.png  ground-level detail reviews
yard-dusk.png                   Blender preview of fixture placement at dusk
bus-entry.png / bus-interior.png open doors, boarding ramps and interior aisle
bus-wheel.png                   flat textured wheel-face review
wash-sign.png / parts-annex.png  mounted sign and attached service-area reviews
bay-signs.png                   relocated bay notices and sign-facing review
landing-threshold.png           repaired upper-door threshold
landing-rooftop.png             landing guard and utility-roof equipment review
texture-cache/LS_SIGN_ATLAS.png  generated shared sign atlas
texture-cache/LS_SIGN_ATLAS.json exact sign strings, atlas rectangles and provenance
content/tracks/last_shift/       standalone AC track
validation/validation.json       exact KN5 input hashes and offline check results
validation/materials.json        exact KN5 material and tangent validation
validation/details.json          source hashes, proxy bounds and geometry budgets
package-validation.json          ZIP entry hashes checked against the staged track
last_shift_0.3.1-surface-roof-fixes.zip  archive rooted at last_shift/
SHA256SUMS.txt                   archive checksum
```

Treat the Python script and layout JSON as the reproducible source. Rebuilding
replaces the generated `.blend`; save manual Blender experiments separately.
`-SkipRenders` reuses existing previews, so use a full build after visual changes.
`-SkipValidation` is for diagnosis only and does not establish acceptance.

Diffuse textures are 2K and tangent normals are 1K, embedded as real PNG data in
the visual KN5. AC uses `ksPerPixelNM` with the source DirectX normals; Blender
previews use the OpenGL normals and roughness maps. The installed AC shader has
no roughness texture slot, so AC specular settings are authored per material.
Uniform paint markings, tyre tread and glass use flat finishes. Sign lettering
and wheel faces use embedded diffuse images through `ksPerPixel`; their image
sources and hashes are also recorded in the packaged `texture-sources.json` and
`TEXTURE_CREDITS.txt`.
UVs follow source tile dimensions in metres. Original downloads are unchanged;
the build converts diffuse JPEGs to a generated PNG cache for export.

Street fixtures have night-controlled CSP lights; interior task/entrance lights
remain enabled. Positions are generated from the same authored fixtures used in
Blender. Light setup follows the [CSP track-light documentation](https://github.com/ac-custom-shaders-patch/acc-extension-config/wiki/Tracks-%E2%80%93-Lights).
Blender previews do not reproduce AC's renderer exactly. Lighting and performance
still need direct AC/CSP review.

## AC / FPS contract

The visual and non-renderable collision KN5s are separate. Visual mesh names
deliberately avoid automatic FPS collision keywords. Physical road/wall proxies
provide explicit cover and smooth stair ramps; detailed visual treads do not snag
the standing capsule. The depot floor and remote carrier pad use AC's physical
mesh naming convention.

`AC_START_0..11` are in the depot. `AC_PIT_0..11`, `AC_CREW_0..11`, hotlap and
minimal timing markers are on the separate carrier pad at AC X approximately
200-240 m, outside the prepared FPS bounds. There are no racing AI files.

The playable polygon uses `FPS_BOUNDARY_0..3`. `FPS_MOVEBOX` volumes exclude
bus, building, utility-shed and added canopy roofs from traversal without adding
shot collision. Bus roof exclusions start above the usable interior. Use the
current server with FPS preparation version
5 / movement-volume support and freshly prepare this track in Race Control.

AC coordinates are `[X,Y,Z]`, metres, with Y up. The minimap is 1200 square:
`pixelX=(X+54)/0.09`, `pixelY=(Z+54)/0.09`, matching `data/map.ini`.

## Validation and remaining work

The final 0.3.1 candidate passes all 673 geometry/movement checks and its material
and detail gates. Fresh receipts in `artifacts/last-shift-v031/validation/` pin
the exact model hashes. The visual KN5 has 30,504 triangles, 633 meshes and 17
materials; the collision KN5 has 5,048 triangles across 424 meshes. The detail
gate checks all 66 sign fronts/backings and includes targeted near-coplanar
overlap and rendered-floor coverage checks around the repaired surfaces.

The offline gate checks authored starts/yaw, spawn reachability, bidirectional
doors/stairs, shot collision, remote carrier support and roof exclusions. Added
bus probes cover entry ramps, door shoulders, the seat aisle, standing headroom
and shots through open windows in both directions. Site routes cover the shelter
and pedestrian approaches. Substantial furniture and poles have simple collision;
small fittings remain decorative. The material/detail gates inspect actual KN5
images, shader bindings, tangent frames, sign quads and UV rectangles, and wheel
orientation/size. Asset budgets are regression limits, not frame-rate guarantees.

Use the isolated headless wrapper at
`artifacts/last-shift-v031/Run-ServerSmoke.ps1` after the full build passes.
It calls `tools/Run-LastShiftClientSmoke.ps1 -PrepareOnly` with the current build
directory, starts only its copied server, records actor movement for 30 seconds
and shuts down that server. It does not launch AC or change installed content or
user configuration. Receipts and snapshots are retained beneath the new build's
`client-smoke/<run-id>/` directory.

The final 0.3.1 candidate passed a fresh 30-second smoke on 2026-10-05: all 12
bots moved, all 12 spawns were connected, rifle fire was accepted and shutdown
was graceful. The prepared arena contains 5,036 collision triangles and 31,956
navigation nodes in six components, with 239,873 walk links and 1,852 traversal
links. Prepared-arena counts exclude geometry outside its bounds and are distinct
from the full collision KN5 counts above. Its receipt, snapshots and logs are in
`artifacts/last-shift-v031/client-smoke/20261005-170828-d58bd6c4/`.
`headless-receipt.json` pins geometry/navigation hashes and records that no owned
test process or smoke port remained active. No AC client was launched by this test.

`artifacts/last-shift-v031/Verify-Package.ps1` verified all 16 ZIP entries against
the staged files, the version, unique paths and expected final archive SHA-256.
The result is in `artifacts/last-shift-v031/package-validation.json`. Static and
headless success does not verify the reported distance-dependent flicker in AC/CSP.

The previous 0.3.0 candidate passed its smoke on 2026-10-05: all 12 bots moved,
all 12 spawns were connected, rifle fire was accepted and the server shut down
gracefully. Its prepared world contains 4,784 collision triangles and 31,968
navigation nodes in six components, with 239,976 walk links and 1,854 traversal
links. The receipt, before/after snapshots and server logs are under
`artifacts/last-shift-reworked/client-smoke/20261005-160110-a4e461ff/`;
`headless-receipt.json` pins the exact prepared geometry/navigation hashes.
No owned test process or smoke port remained active, and the existing user
server was preserved. This headless result leaves AC/CSP visual acceptance open.

Prior milestones have separate evidence: the 0.2.0 detailed server smoke is in
`artifacts/last-shift-detailed/client-smoke/20261004-213348-3944ac16/headless-receipt.json`,
and the original greybox result is in
`artifacts/last-shift-smoke/acceptance-summary.json`. Those results do not validate
the current surface and roof repair.

A separate bounded AC/CSP launch of the previous 0.1.1 textured candidate loaded Last Shift and
completed the server's client-ready protocol 7 handshake. Logs are retained under
`artifacts/last-shift-textured/client-smoke/20261003-082905-14eb30ef/`.
The desktop tool's app-approval request timed out, so no in-game frame was
inspected and this is not visual/playtest acceptance. The owned game/server
processes exited and `race.ini` was restored byte-for-byte. Blender previews
remain the visual evidence for this pass.

The gameplay review must check camera/movement, stairs, bus entry/aisles,
window firing, cover and shot alignment, bot circulation, spawn safety, readable
signs, lighting and performance in AC/CSP. The reported sign/floor flicker must
be retested at several distances and while moving in the actual game. Offline
checks, headless bots and Blender previews do not establish that visual result
or human playtest acceptance.

No runtime gameplay code or existing Nuketown map is changed by this milestone.

`tools/Install-LastShift.ps1` defaults to the 0.3.1 build and installs it as
`content/tracks/last_shift`. Installation checks all three validation receipts
against the current models. Replacing an existing copy requires
`-ReplaceExisting -PreviousInstallReceipt <receipt>` and refuses extra, missing
or edited installed files. The destination, file hashes and verified backup are
recorded in the new build's `install-receipt.json`; staging alone does not prove
installation. After installation, select **Last Shift** in Race Control and
prepare it again as an FPS arena so the current collision/navigation is used.

Version 0.3.1 was installed on 2026-10-05 with all 16 installed files hash-verified.
`artifacts/last-shift-v031/install-receipt.json` records that installation and
the verified 0.3.0 backup at
`artifacts/last-shift-v031/backups/last_shift-20261005-170944/`.

The prior 0.3.0-reworked candidate was installed on 2026-10-05 and all 16 installed
files were hash-verified. The previous 0.2.0 copy is retained under
`artifacts/last-shift-reworked/backups/last_shift-20261005-160513/`.
See `artifacts/last-shift-reworked/install-receipt.json` and
`artifacts/last-shift-reworked/acceptance-summary.json`. Race Control held the
thumbnail files open during the first attempt; all old files were confirmed
unchanged before the successful retry after it closed.
