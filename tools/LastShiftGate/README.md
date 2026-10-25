# Last Shift offline gate

This small .NET 10 executable links the current server KN5 preparation, collision,
and navigation source directly. It does not build or launch Race Control or
Assetto Corsa, and has no NuGet package dependencies.

From the repository root:

```powershell
dotnet build tools/LastShiftGate/LastShiftGate.csproj -c Release --configfile tools/LastShiftGate/NuGet.Config
dotnet tools/LastShiftGate/bin/Release/net10.0/LastShiftGate.dll --ac-root artifacts/last-shift --output artifacts/last-shift/validation --probes artifacts/last-shift/last-shift-layout.json
```

The AC root must contain `content/tracks/last_shift/models.ini` and its KN5 files.
The gate writes prepared `fps_arena.json`, `fps_geometry.bin`,
`fps_navigation.bin`, and `validation.json` only into the output directory.
Exit code 1 means a preparation or probe check failed. The report records input
KN5 and manifest SHA-256 hashes; regenerate it after changing either map or
linked server source.

The generated layout manifest must supply top-level `spawns` (12 entries of
`[x,y,z,yawDegrees]`) and `movementVolumes` (entries with `center`, `size`, and
`yaw`). These are checked against the exported KN5 marker transforms. Its probe
section contains these fields directly or under `probes`:

```json
{
  "trackId": "last_shift",
  "expectedSpawnCount": 12,
  "standingPoints": [
    { "name": "dispatch upper landing", "position": [0, 3, 0] }
  ],
  "walkRoutes": [
    { "name": "office doorway", "points": [[0, 0, -1], [0, 0, 1]], "bidirectional": true }
  ],
  "shotProbes": [
    { "name": "solid workshop wall", "from": [0, 1.5, -2], "to": [0, 1.5, 2], "blocked": true }
  ]
}
```

Coordinates are AC world `[x,y,z]`, in metres, with Y up. Standing and walking
points describe feet on the expected floor. The example is a schema example,
not the actual map's coordinates. Ray points describe the shot height directly.

Package checks require unique contiguous `AC_START_0..11`, `AC_PIT_0..11`, and
`AC_CREW_0..11`, 12 UI carrier slots, starts matching authored positions/yaw, and
at least one metre between starts. PIT/CREW markers must be at least 150 m outside
the playable polygon, with physical support beneath every pit. All authored
roof/bus movement volumes must survive export accurately and contain no
navigation nodes or crossing edges.

Movement checks cover every prepared spawn, the authored playable boundary, primary
navigation membership, directed paths and walk-only connectivity between every
spawn and spawn 0. Named route segments run the real standing movement resolver
in both directions by default with a 1.8 m actor and the server's capsule radius;
they must end within 0.12 m of the requested floor position. Include short routes
through each doorway, the stair run, and critical cross-map connections. Route
endpoints require nearby navigation nodes and walk-only paths in both directions.
Named shot rays verify expected solid cover and open sightlines.

Passing is offline evidence only. It does not establish in-game rendering,
input, performance, match balance, or human/bot playtest acceptance.
