[CmdletBinding()]
param(
    [string]$BuildRoot = (Join-Path $PSScriptRoot '..\.artifacts\de-dust2-conversion\stage'),
    [string]$RaceControlRoot = (Join-Path $PSScriptRoot '..\out-race-control'),
    [string]$AssettoCorsaRoot = 'C:\Program Files (x86)\Steam\steamapps\common\assettocorsa'
)
$ErrorActionPreference = 'Stop'
$build = [IO.Path]::GetFullPath($BuildRoot)
$runtime = [IO.Path]::GetFullPath($RaceControlRoot)
$trackRoot = Join-Path $AssettoCorsaRoot 'content\tracks\css_dust2'
$prepared = Join-Path $build 'package\content\tracks\css_dust2\race-control'
$manifest = Get-Content -LiteralPath (Join-Path $prepared 'prepared.json') -Raw | ConvertFrom-Json
foreach ($entry in $manifest.Inputs.PSObject.Properties) {
    if ((Get-FileHash -LiteralPath (Join-Path $trackRoot $entry.Name) -Algorithm SHA256).Hash -ne $entry.Value) {
        throw "Installed map differs from prepared input: $($entry.Name)"
    }
}
foreach ($name in @('geometry', 'navigation')) {
    $expected = $manifest.($name + 'Sha256')
    if ((Get-FileHash -LiteralPath (Join-Path $prepared "$name.bin") -Algorithm SHA256).Hash -ne $expected) {
        throw "Prepared $name hash mismatch."
    }
}
Add-Type -Path (Join-Path $runtime 'lib\AssettoServer.RaceControl.Core.dll')
$dustDataRoot = if (Test-Path -LiteralPath (Join-Path $runtime 'portable.json')) {
    Join-Path $runtime 'Data'
} else {
    Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'AssettoServer Race Control'
}
$paths = [AssettoServer.RaceControl.Core.Infrastructure.RaceControlPaths]::new($dustDataRoot, $runtime)
$metadataPath = Join-Path $paths.FpsArenasDirectory 'css_dust2.json'
if (Test-Path -LiteralPath $metadataPath) { throw 'Dust II is already registered; refusing to replace existing arena settings.' }
$scanner = [AssettoServer.RaceControl.Core.Content.AcContentScanner]::new($runtime)
$catalog = $scanner.Scan($AssettoCorsaRoot)
$track = $catalog.Tracks | Where-Object { $_.TrackId -eq 'css_dust2' -and $_.LayoutId -eq '' } | Select-Object -First 1
if ($null -eq $track) { throw 'Installed Dust II was not found by the actual Race Control scanner.' }
$preset = [AssettoServer.RaceControl.Core.Models.RaceControlPreset]::CreateDefault($AssettoCorsaRoot, (Join-Path $runtime 'lib\Server'))
$preset.TrackId = 'css_dust2'
$preset.Mode = [AssettoServer.RaceControl.Core.Models.EventMode]::Fps
$arena = [Text.Json.JsonSerializer]::Deserialize((Get-Content -LiteralPath (Join-Path $prepared 'arena.json') -Raw),
    [AssettoServer.RaceControl.Core.Models.FpsArenaDefinition], [Text.Json.JsonSerializerOptions]::new())
$preset.Fps.Arena = $arena
$preset.Fps.ArenaBoundsPaddingMeters = $arena.BoundsPaddingMeters
$store = [AssettoServer.RaceControl.Core.Storage.FpsArenaStore]::new($paths)
$service = [AssettoServer.RaceControl.Core.Staging.FpsArenaPreparationService]::new($store, $paths)
$cacheType = $service.GetType().Assembly.GetType('AssettoServer.RaceControl.Core.Staging.PreparedPhysicsAssetCache', $true)
$cacheArguments = @($paths.PSObject.BaseObject, $preset.PSObject.BaseObject, $track.PSObject.BaseObject)
$cache = $cacheType.GetMethod('GetFpsPaths', [Reflection.BindingFlags]'Public,Static').Invoke($null, $cacheArguments)
foreach ($path in @($cache.GeometryPath, $cache.NavigationPath)) {
    if (-not $path.StartsWith($paths.CacheDirectory + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Cache escaped Race Control storage.' }
    if (Test-Path -LiteralPath $path) { throw 'A cache entry already exists; refusing to overwrite it.' }
}
# Use the application's own cache keys and atomic persistence; no preset is saved.
$method = $service.GetType().GetMethod('PersistPreparedArena', [Reflection.BindingFlags]'Instance,NonPublic')
if ($null -eq $method) { throw 'This Race Control build does not expose the expected preparation persistence method.' }
$geometrySource = Join-Path $prepared 'geometry.bin'
$navigationSource = Join-Path $prepared 'navigation.bin'
$persistArguments = @($preset.PSObject.BaseObject, $track.PSObject.BaseObject, $arena.PSObject.BaseObject,
    $geometrySource.PSObject.BaseObject, $navigationSource.PSObject.BaseObject)
[void]$method.Invoke($service, $persistArguments)
if (-not $store.IsPrepared('css_dust2', '')) { throw 'Race Control did not recognize the registered arena.' }
foreach ($pair in @(@($cache.GeometryPath, $manifest.GeometrySha256), @($cache.NavigationPath, $manifest.NavigationSha256))) {
    if ((Get-FileHash -LiteralPath $pair[0] -Algorithm SHA256).Hash -ne $pair[1]) { throw 'Registered cache hash mismatch.' }
}
[pscustomobject]@{ Track='css_dust2'; Arena=$metadataPath; Geometry=$cache.GeometryPath; Navigation=$cache.NavigationPath;
    CellSize=$arena.Navigation.CellSize; SpawnCount=$arena.SpawnPoints.Count; PresetChanged=$false } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $build 'registration-report.json') -Encoding utf8
Write-Output "Registered Dust II: $metadataPath; 16 starts, $($arena.Navigation.CellSize) m navigation."
