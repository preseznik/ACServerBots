# Architecture

## End-to-end flow

1. `TrackFormatRegistry` asks both adapters to probe a user-selected folder and picks the highest
   confidence result.
2. The selected adapter hashes every source artifact before decoding anything. Paths are resolved
   through a containment check, never by trusting a source-relative path.
3. Import builds a canonical `TrackProject`: source provenance and hashes, layout/route IDs,
   canonical scene nodes, materials, textures, routes, collision roles, and toolchain identities.
4. The WPF app renders the transient scene cache. Inspector changes become `IEditCommand`
   operations and stable-ID `TrackEditDelta` records. No source file is touched.
5. `.acedit` persistence omits the large geometry cache. Opening a project reimports the source,
   rejects stale hashes during validation, and reapplies supported stable-ID deltas.
6. Validation combines common source/staging rules with adapter-specific capability checks.
7. Staging copies and rehashes every artifact in a temporary sibling directory, applies only
   verified writer operations, writes `.aceditor-stage.json`, and atomically moves the result.
8. Publishing accepts only a manifested staged copy, creates a timestamped backup of the exact
   selected target, and atomically installs the staged tree.

## Project boundaries

- `ACEditor.Core` contains models, adapters, binary readers, commands, validation, safe staging,
  publication, tool discovery, and Blender interchange.
- `ACEditor.App` contains WPF/MVVM state and the Vortice D3D11 `DrawingSurface` viewport.
- `ACEditor.Tests` contains generated fixtures and opt-in installed-content checks.

The editor solution does not reference or modify AssettoServer projects. It follows the existing
RaceControl Vortice approach only at the design/package level.

## Write-disposition contract

Every `SourceArtifact` has one of three dispositions:

- `CopyUnchanged`: always carried byte-for-byte.
- `RewriteKnown`: a writer may replace it only when that edit kind has an implemented encoder and
  reopen validation.
- `Blocked`: any edit that requires it produces `OPAQUE_WRITE_BLOCKED` and no stage is built.

Disposition is capability metadata, not permission to alter the installed source. All writers
operate against the temporary staged tree.

PSSG DDS replacement is the first native staged writer. The project records the selected DDS path
and SHA-256 against a stable `relative.pssg#texture-id` target. Staging verifies that dependency,
changes the copied PSSG with EgoEngineLibrary, saves through a sibling temporary file, reopens and
extracts every changed texture, then replaces only the staged PSSG. A failed save or reopen discards
the temporary stage. PSSG mesh replacement is deliberately separate and remains blocked.

## DCC boundary

The editor owns assembly and semantics, not vertex sculpting. `BlenderRoundTripService` exports
selected cached meshes as a GLB with stable source IDs, normals, UV0, and material identities. The
generated script imports the GLB into a dedicated `.blend`. Reimport inspection compares hashes
and stable node inventories; a missing stable node blocks the round trip. Connecting the verified
Blender KN5 exporter is the next required step before KN5 mutations can stage.
