[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CoreAssembly,
    [Parameter(Mandatory)][string]$ServerPayload,
    [string]$ProfileDirectory = (Join-Path $env:LOCALAPPDATA 'AssettoServer Race Control'),
    [string]$AssettoCorsaRoot,
    [string]$OutputDirectory = 'Release/Content'
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath((Join-Path $repo $OutputDirectory))
if (Test-Path -LiteralPath $output) { throw "Snapshot already exists: $output" }
Add-Type -Path ([IO.Path]::GetFullPath($CoreAssembly))
$options = [AssettoServer.RaceControl.Core.Storage.ReleaseDefaults]::JsonOptions
function Read-Typed($Path, [Type]$Type) { [Text.Json.JsonSerializer]::Deserialize([IO.File]::ReadAllText($Path),$Type,$options) }
function Save-Json($Path,$Value) { [IO.File]::WriteAllText($Path,($Value | ConvertTo-Json -Depth 40)+[Environment]::NewLine) }
function Safe-PresetJson($Preset) {
    $safe=[AssettoServer.RaceControl.Core.Storage.ReleaseDefaults]::SanitizePreset($Preset)
    $value=[Text.Json.JsonSerializer]::Serialize($safe,$safe.GetType(),$options) | ConvertFrom-Json -AsHashtable
    foreach($key in @('Id','AssettoCorsaRoot','ServerPayloadPath')) { $value.Remove($key) }
    $value.Fps.Remove('Arena')
    return $value
}
$settings=Read-Typed (Join-Path $ProfileDirectory 'settings.json') ([AssettoServer.RaceControl.Core.Storage.ApplicationSettings])
if (-not $AssettoCorsaRoot) { $AssettoCorsaRoot=$settings.AssettoCorsaRoot }
if (-not (Test-Path -LiteralPath (Join-Path $AssettoCorsaRoot 'content/tracks'))) { throw 'Assetto Corsa content was not found.' }
$defaults=Join-Path $output 'Defaults'
$maps=Join-Path $output 'FpsMaps'
New-Item -ItemType Directory -Path $defaults,$maps -Force | Out-Null
$safeSettings=[AssettoServer.RaceControl.Core.Storage.ReleaseDefaults]::SanitizeSettings($settings)
$value=[Text.Json.JsonSerializer]::Serialize($safeSettings,$safeSettings.GetType(),$options) | ConvertFrom-Json -AsHashtable
foreach($key in @('AssettoCorsaRoot','ServerPayloadPath','LastPageIndex')) { $value.Remove($key) }
Save-Json (Join-Path $defaults 'settings.json') $value
$presets=@(Get-ChildItem -LiteralPath (Join-Path $ProfileDirectory 'Presets') -Filter '*.json' | Sort-Object LastWriteTime -Descending | ForEach-Object {
    Read-Typed $_.FullName ([AssettoServer.RaceControl.Core.Models.RaceControlPreset])
})
foreach($mode in @('Racing','Fps')) {
    $preset=$presets | Where-Object { $_.Mode.ToString() -eq $mode } | Select-Object -First 1
    if ($preset) { Save-Json (Join-Path $defaults ($mode.ToLowerInvariant()+'.json')) (Safe-PresetJson $preset) }
}
Save-Json (Join-Path $defaults 'startup.json') @{ Mode=$presets[0].Mode.ToString() }
$catalog=[AssettoServer.RaceControl.Core.Content.AcContentScanner]::new().Scan($AssettoCorsaRoot)
$profilePaths=[AssettoServer.RaceControl.Core.Infrastructure.RaceControlPaths]::new($ProfileDirectory)
$cacheType=[AssettoServer.RaceControl.Core.Infrastructure.RaceControlPaths].Assembly.GetType('AssettoServer.RaceControl.Core.Staging.PreparedPhysicsAssetCache')
$getCache=$cacheType.GetMethod('GetFpsPaths',[Reflection.BindingFlags]'Public,Static')
$work=Join-Path $repo ('.artifacts/capture-map-inputs-'+[Guid]::NewGuid().ToString('N'))
$workServer=Join-Path $work 'lib/Server'
New-Item -ItemType Directory -Path $workServer -Force | Out-Null
Copy-Item -Path (Join-Path $ServerPayload '*') -Destination $workServer -Recurse
$serverMarker=Join-Path $workServer 'portable.json'
if(Test-Path -LiteralPath $serverMarker) { Remove-Item -LiteralPath $serverMarker }
Save-Json (Join-Path $work 'portable.json') @{schemaVersion=1}
$workPaths=[AssettoServer.RaceControl.Core.Infrastructure.RaceControlPaths]::new((Join-Path $work 'Data'),$work)
$workPaths.ConfigureProcessStorage()
$preparer=[AssettoServer.RaceControl.Core.Staging.FpsArenaPreparationService]::new(
    [AssettoServer.RaceControl.Core.Storage.FpsArenaStore]::new($workPaths),$workPaths)
$entries=@(foreach($id in @('bo2_nuketown_2020','fire_pit','krvava_rotunda','shipment_1519')) {
    $source=Join-Path $AssettoCorsaRoot ('content/tracks/'+$id)
    $target=Join-Path $maps ('content/tracks/'+$id)
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    foreach($file in Get-ChildItem -LiteralPath $source -Recurse -File) {
        $relative=[IO.Path]::GetRelativePath($source,$file.FullName)
        if ($file.Name -eq 'conversion-report.json' -or $file.Name -match '\.(bak|backup|tmp)$') { continue }
        if($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked map files are not supported.' }
        $destination=Join-Path $target $relative
        New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }
    $arena=Read-Typed (Join-Path $ProfileDirectory ('FpsArenas/'+$id+'.json')) ([AssettoServer.RaceControl.Core.Models.FpsArenaDefinition])
    $preset=[AssettoServer.RaceControl.Core.Models.RaceControlPreset]::CreateDefault($AssettoCorsaRoot,$workServer)
    $preset.Mode=[AssettoServer.RaceControl.Core.Models.EventMode]::Fps
    $preset.TrackId=$id
    $preset.Fps.Arena=$arena
    $preset.Fps.ArenaBoundsPaddingMeters=$arena.BoundsPaddingMeters
    $track=$catalog.Tracks | Where-Object { $_.TrackId -eq $id -and $_.LayoutId -eq '' } | Select-Object -First 1
    $cache=$getCache.Invoke($null,@($profilePaths.PSObject.BaseObject,$preset.PSObject.BaseObject,$track.PSObject.BaseObject))
    $refreshed=$false
    if ($arena.PreparationVersion -ne [AssettoServer.RaceControl.Core.Models.FpsArenaDefinition]::CurrentPreparationVersion -or -not $cache.IsComplete) {
        Write-Host "Refreshing obsolete/missing preparation for $id in isolated staging..."
        $arena=$preparer.PrepareAsync($preset,$track,$null,[Threading.CancellationToken]::None).GetAwaiter().GetResult()
        $preset.Fps.Arena=$arena
        $cache=$getCache.Invoke($null,@($workPaths.PSObject.BaseObject,$preset.PSObject.BaseObject,$track.PSObject.BaseObject))
        $refreshed=$true
    }
    if (-not $cache.IsComplete) { throw "No complete prepared map files for $id" }
    $prepared=Join-Path $target 'race-control'
    New-Item -ItemType Directory -Path $prepared -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $prepared 'arena.json'),[Text.Json.JsonSerializer]::Serialize($arena,$arena.GetType(),$options))
    Copy-Item -LiteralPath $cache.GeometryPath -Destination (Join-Path $prepared 'geometry.bin')
    Copy-Item -LiteralPath $cache.NavigationPath -Destination (Join-Path $prepared 'navigation.bin')
    $inputs=[ordered]@{}
    foreach($name in @('models.ini')+@(Get-ChildItem -LiteralPath $target -Filter '*.kn5' -File | Sort-Object Name | ForEach-Object Name)) {
        $inputs[$name]=(Get-FileHash -LiteralPath (Join-Path $target $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    Save-Json (Join-Path $prepared 'prepared.json') ([ordered]@{
        PreparationVersion=$arena.PreparationVersion; BoundsPaddingMeters=$arena.BoundsPaddingMeters
        CollisionIncludeMeshes=@($arena.CollisionIncludeMeshes); CollisionExcludeMeshes=@($arena.CollisionExcludeMeshes)
        Inputs=$inputs
        GeometrySha256=(Get-FileHash (Join-Path $prepared 'geometry.bin') -Algorithm SHA256).Hash.ToLowerInvariant()
        NavigationSha256=(Get-FileHash (Join-Path $prepared 'navigation.bin') -Algorithm SHA256).Hash.ToLowerInvariant()
    })
    $ui=Get-Content (Join-Path $target 'ui/ui_track.json') -Raw | ConvertFrom-Json
    Write-Host "Captured $id, version $($ui.version), $($arena.SpawnPoints.Count) spawn points."
    [ordered]@{trackId=$id;name=$ui.name;version=$ui.version;preparationRefreshed=$refreshed;spawnPoints=$arena.SpawnPoints.Count}
})
Save-Json (Join-Path $maps 'asrc-fps-maps.json') ([ordered]@{schemaVersion=1;packVersion=1;preparationVersion=4;maps=$entries})
$notice=@'
FPS maps for AssettoServer Race Control
======================================
Includes Nuketown, Fire Pit, Krvava Rotunda and Shipment 1519, plus their
arena definitions and prepared geometry/navigation. All settings are portable;
no installation paths, original preset IDs, history or local catalog is included.

Players: extract content into Assetto Corsa, or use the FPS maps client installer
component. Hosts: use Settings > Local Installations > Import maps ZIP. Full
portable packages already include the maps under Packs/FpsMaps. Hosting reads
these maps without installing them into the external game directory.

Existing map README files and artwork/provenance restrictions are retained.
This local review package does not grant additional third-party artwork rights.
Diagnostic conversion reports and historical preparation backups are omitted.
'@
[IO.File]::WriteAllText((Join-Path $maps 'README.txt'),$notice)
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE'),(Join-Path $repo 'THIRD_PARTY_NOTICES.md') -Destination $maps
# Fail closed on source-PC paths/identities in the newly captured text defaults/content.
$private=@('[A-Za-z]:[\\/]','\\\\[^\\\s]+\\','7656119\d{10}')
foreach($token in @($env:USERNAME,$env:COMPUTERNAME)) {
    if($token.Length -ge 4) { $private += '\b'+[Regex]::Escape($token)+'\b' }
}
foreach($file in Get-ChildItem -LiteralPath $output -File -Recurse | Where-Object Extension -in '.json','.txt','.ini','.lua') {
    if([IO.File]::ReadAllText($file.FullName) -match ($private -join '|')) {
        throw "Private path or identifier found in captured file: $([IO.Path]::GetRelativePath($output,$file.FullName))"
    }
}
Write-Host "Sanitized release inputs captured at $output"
