# Blender Assetto Corsa Tools

Vendored from <https://github.com/I-Rinka/blender-assetto-corsa-tools> at commit
`0c75b3d12d8bc2257a73a7f13e4bde81a33861b8`.

The exporter is licensed under GPL-3.0; see `LICENSE.txt`. Only the exporter,
registration UI, and shared utility modules needed by the headless FPS client
asset build are included.

Local rigid-map opt-in: `KN5FileWriter(..., transform_tangent_space=True)` applies
the object transform and AC axis conversion to exported normals and tangents,
including inverse-transpose normals for nonuniform scale. The default remains
false for existing asset builds. This path rejects skinned meshes; callers must
author nonmirrored UV charts and use the shader's normal-map convention.
