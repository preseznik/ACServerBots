# Testing and acceptance

## Default redistributable suite

`dotnet test ACEditor.slnx` runs generated fixtures only. It covers:

- KN5 hierarchy/material/mesh import and corrupt/truncated rejection;
- AC v7 AI point, speed, and lane-width import;
- DiRT 2 format probing and opaque CQTC disposition;
- compact `.acedit` round trip without embedded geometry caches;
- project undo/redo;
- SharpGLTF GLB export/reopen and stable-node diff;
- byte-identical staged copy and unchanged source hashes;
- staged-writer isolation and post-edit manifest hashes;
- blocked KN5 rewrite attempts.

No fixture contains copyrighted game content.

## Gated installed-content suite

```powershell
$env:ACEDITOR_LOCAL_INTEGRATION = '1'
dotnet test ACEditor.Tests\ACEditor.Tests.csproj --filter Category=LocalIntegration
```

The AC gate imports `ks_nurburgring`, checks all four layout IDs, material/scene/route inventories,
and confirms a representative source hash is unchanged. The DiRT 2 gate imports
`tracks\baja\baja_iron`, pins EgoEngineLibrary 15.0.0, imports 1,660 render meshes and 736 textures,
uploads every decoded DDS to a D3D11 WARP device, replaces one texture in a temporary staged
`tracksplit.pssg`, reopens it through EgoEngineLibrary, and confirms the installed source hash is
unchanged.

The following acceptance remains manual/pending because the corresponding native writers are not
enabled: controlled metadata/transform/AI edits reopened from a staged AC track, PSSG mesh/JPK/binary
XML edits reopened by EgoEngineLibrary, a complete DiRT 2-to-AC track opened in ksEditor, and an AC
runtime lap. A passing default suite must not be reported as those gates.
