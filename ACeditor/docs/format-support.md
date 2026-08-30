# Format support and safety gates

| Format | Read/inspect | Stage behavior | Mutation status |
|---|---|---|---|
| AC `models.ini` / `models_*.ini` | Layout ownership and KN5 references | Byte-verified copy | Text rewrite infrastructure pending |
| AC KN5 v5 | Nodes, transforms, static/skinned mesh buffers, materials, shader properties, texture mappings, and embedded DDS preview data | Copy only | Blocked until Blender exporter + reopen test |
| AC `fast_lane.ai` / `pit_lane.ai` v7 and -1 | Points, speed and lane widths | Copy only | Authoring/writer pending |
| AC INI/JSON/UI/map/image files | Hashed/inventoried | Byte-verified copy | Per-format editors pending |
| DiRT 2 PSSG | Hierarchy, accumulated transforms, triangle render buffers, normals, UV0, source shaders/properties, texture bindings, and DDS preview data | Byte-verified copy; selected DDS replacements are written with EgoEngineLibrary and reopened before the stage is accepted | Texture export/replacement supported; track mesh topology replacement remains blocked because the pinned glTF writer is car-specific |
| DiRT 2 binary XML | Racing-line route gates through EgoEngineLibrary 15.0.0 | Byte-verified copy | Writer pending |
| DiRT 2 JPK | Hashed/inventoried | Byte-verified copy | Repack connection pending |
| DiRT 2 CQTC/CLM/GRS/CNS/VIS/GSSP/HTF/BIN | Hashed and visible as artifacts | Byte-preserved | Blocked |

The source shader name is retained. The D3D viewport can show wireframe, neutral filled, imported
diffuse texture, approximated lighting, or texture plus lighting. It never writes the preview
approximation back as a generic PBR replacement.

## Conversion gate

DiRT 2-to-AC export cannot be labelled complete until the selected route resolves PSSG render
geometry, route placements, textures and material semantics, collision responsibilities, AI/progress
gates, start grids, cameras, and all required AC output files. This milestone deliberately stops
before conversion because route placements, collision semantics, native output writers, and runtime
acceptance are not complete. Unresolved references must remain a blocking conversion report, not a
partial success.
