# AC Editor

AC Editor is a Windows x64 track-assembly editor for Assetto Corsa and DiRT 2. It is a separate
.NET 10 WPF solution so it does not change AssettoServer's solution, runtime, or release path.

The current implementation is the safe foundation milestone: it opens native track folders,
renders AC KN5 and DiRT 2 PSSG meshes/materials/textures through a Vortice Direct3D 11 surface,
decodes DiRT 2 binary XML routes, saves compact `.acedit` projects, validates source hashes,
exports selected geometry to a SharpGLTF GLB Blender workspace, and builds byte-verified staged
copies. Unsupported native writes fail closed.

## Build and run

Prerequisites on this workstation:

- Visual Studio 2026 Community 18.9.1 at
  `C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE`
- .NET 10 SDK with the Managed Desktop workload
- Ego PSSG Editor 12.1.1 / EgoEngineLibrary 15.0.0 at
  `F:\Tools 3rd Party\Ego PSSG Editor`
- SharpGLTF Core/Toolkit 1.0.0-alpha0023 from that pinned editor installation
- Assetto Corsa and DiRT 2 installations for local integration tests
- Blender, `texconv`, and `ksEditor` for their respective external-tool workflows

```powershell
dotnet build ACEditor.slnx
dotnet test ACEditor.slnx
dotnet run --project ACEditor.App\ACEditor.App.csproj
```

If Ego PSSG Editor lives elsewhere, set `EGO_PSSG_EDITOR_ROOT` or pass
`-p:EgoPssgEditorRoot="..."`. No game asset or third-party binary is committed.

Open **Tools → Settings…** to select **Follow Windows**, **Dark**, or **Light** appearance and to
configure local tool paths. Appearance changes apply immediately; **Save settings** persists the
choice and refreshes tool discovery. Settings live at `%LocalAppData%\AC Editor\settings.json`.
Existing flat `ToolchainPaths` settings files are migrated in memory without losing overrides.

## Implemented feature inventory

| Area | Current behavior | Implementation |
|---|---|---|
| Tool settings | Discovers local AC, DiRT 2, Ego PSSG Editor, Blender, `texconv`, and `ksEditor`; shows the effective paths in Settings and accepts persisted overrides. | `ToolchainDiscovery` and `%LocalAppData%\AC Editor\settings.json`. |
| Appearance settings | Complete light/dark control templates, a Windows-following option, live preview, themed native title bars, and persisted selection. | `Themes/Colors.*.xaml`, `Themes/Controls.xaml`, and `ThemeManager`. |
| Dual-format probe | Scores AC from `models*.ini`/KN5/UI and DiRT 2 from PSSG/JPK/binary XML evidence. | `TrackFormatRegistry`, `AssettoCorsaTrackAdapter`, `Dirt2TrackAdapter`. |
| AC import | Reads every layout's model list, KN5 v5 hierarchy/materials/embedded DDS textures/mesh buffers, and v7/-1 AI splines. | Bounded binary readers reject bad counts, indices, versions, and truncation. |
| DiRT 2 import | Reads shared/route PSSG transforms, render buffers, hierarchy, source shaders, texture bindings, and DDS preview data; decodes route racing-line gates. | `EgoPssgTrackReader` uses the pinned EgoEngineLibrary 15.0.0 APIs and preserves unsupported records. PSSGs with no supported render or texture records remain locked. |
| Canonical scene | Uses right-handed, Y-up, metres with one explicit adapter conversion contract and retained source IDs/files. | `CoordinateContract`, stable source IDs, raw artifact hashes. |
| Native viewport | Chunked D3D11 mesh buffers, active-layout filtering, unclamped perspective orbit/pan/zoom, distinct wireframe/filled/textured/lit/textured+lit modes, scene picking, selection framing/gizmo, route-only ribbons, and collision-role colors. | WPF `DrawingSurface` with Vortice 3.8.3; vertex and pixel stages share explicit frame constants, while a scale-aware clip range preserves depth precision for layered road/decal geometry. |
| Scene/inspector | The left dock opens directly to a nested scene outliner, routes, and source-file safety inventory; the inspector shows ownership, visibility, lock state, rename, and project-only undo/redo. | `IEditCommand`, `UndoRedoStack`, stable-ID deltas. |
| Route workspace | Displays imported lanes with widths and a dedicated route/elevation workspace. | Route authoring handles and native AI writing are not enabled yet. |
| Materials/collision | Shows source material/shader inventories and semantic collision colors; KN5 and PSSG diffuse DDS textures render without changing raw shader identity. PSSG textures can be exported or queued for DDS replacement. | The replacement is previewed immediately, stored as a hash-bound `.acedit` delta, and written only during staging. Dedicated channel/UV editing remains pending. |
| Blender round trip | Exports selected meshes, normals, UV0, material names, and stable node IDs to GLB; writes a Blender bootstrap and compares reimported node inventories/hashes. | SharpGLTF from the pinned Ego editor installation. |
| Project persistence | Writes only compact, schema-versioned `.acedit` JSON containing hashes, layouts, provenance, tool versions, and edit deltas. | Scene caches rehydrate from read-only sources when a project opens. |
| Validation | Detects missing/changed source files, path escape, unsafe stage paths, missing route/scene data, and edits requiring opaque rewrites. | Errors block staging. Warnings remain visible in Problems. |
| Staging | Copies every imported artifact to a temporary sibling, verifies each copied SHA-256, applies supported PSSG DDS replacements, reopens each changed PSSG, writes post-edit hashes to the manifest, and atomically renames the stage. | Existing non-editor directories and installed game sources are never overwritten. |
| Publishing | Requires an explicit staged copy and target; moves the target to a timestamped backup before atomic install, restoring it if install fails. | Invoked only by the `Publish with Backup` action and confirmation dialog. |
| Reliability | Async jobs/progress, fail-closed parsing, source-change detection, and D3D resource recreation. | Cancellation UI, autosave/recovery, cache eviction, and structured log files are pending. |

## Deliberate blockers

This build does **not** claim a complete native writer or DiRT 2-to-AC conversion. KN5 topology,
PSSG mesh topology replacement, JPK/CQTC rewrite, native route editing, texture-channel preview, and
automatic conversion remain blocked until their writers are connected and outputs reopen in the
authoritative tools. CQTC/CLM/GRS/CNS/VIS/GSSP/HTF/BIN variants are byte-preserved. An edit that
names one of those artifacts as its required output fails validation rather than silently losing
data.

Installed game data is never a normal save target. **Save** writes `.acedit`; **Build Staged Copy**
writes elsewhere; **Publish with Backup** is the only install mutation.

See [architecture](docs/architecture.md), [format support](docs/format-support.md), and
[testing](docs/testing.md).
