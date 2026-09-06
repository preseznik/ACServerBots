[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReleaseDirectory,
    [Parameter(Mandatory)][string]$EvidenceDirectory
)
$ErrorActionPreference = 'Stop'
$releaseRoot = [IO.Path]::GetFullPath($ReleaseDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidence) { throw 'Choose a new evidence directory.' }
New-Item -ItemType Directory -Path $evidence | Out-Null
$manifest = Get-Content (Join-Path $releaseRoot 'release.json') -Raw | ConvertFrom-Json
$passed = [Collections.Generic.List[string]]::new()
function Require($Condition, $Message) { if (-not $Condition) { throw $Message } }
foreach ($asset in $manifest.assets) {
    $file = Join-Path $releaseRoot $asset.name
    Require ((Get-Item -LiteralPath $file).Length -eq $asset.bytes) "Size mismatch: $($asset.name)"
    Require ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -eq $asset.sha256) "Checksum mismatch: $($asset.name)"
}
$passed.Add("All $($manifest.assets.Count) release artifact sizes and SHA-256 checksums match release.json.")
$launcher = Join-Path $evidence 'Launcher Original'
$full = Join-Path $evidence 'Full'
foreach ($item in @(@{Kind='launcher';Path=$launcher},@{Kind='full';Path=$full})) {
    $name = ($manifest.assets | Where-Object kind -EQ $item.Kind).name
    [IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $releaseRoot $name),$item.Path)
    Require (Test-Path (Join-Path $item.Path 'portable.json')) 'Portable marker missing.'
    Require (Test-Path (Join-Path $item.Path 'coreclr.dll')) 'Self-contained runtime missing.'
    Require (-not (Test-Path (Join-Path $item.Path 'Data'))) 'A release contains user data.'
}
Require (-not (Test-Path (Join-Path $launcher 'lib/Server'))) 'Launcher-only ZIP contains the server.'
Require (-not (Test-Path (Join-Path $launcher 'Packs'))) 'Launcher-only ZIP contains FPS assets.'
Require (Test-Path (Join-Path $full 'Packs/Fps/asrc-fps-client.json')) 'Full pack missing.'
Require (-not (Test-Path (Join-Path $full 'lib/Server/portable.json'))) 'Bundled server must inherit the outer portable root.'
$assembly = Join-Path $launcher 'AssettoServer.RaceControl.Core.dll'
Add-Type -Path $assembly
Require ([AssettoServer.Release.ReleaseIdentity]::Version -eq $manifest.version) 'Launcher version mismatch.'
Require ([AssettoServer.Release.ReleaseIdentity]::IsReleaseBuild) 'Release compatibility gate is disabled.'
Require (([Reflection.Assembly]::LoadFrom($assembly).GetManifestResourceNames() | Where-Object { $_ -like '*.Assets.Fps.*' }).Count -eq 0) 'FPS assets are still embedded.'
$paths = [AssettoServer.RaceControl.Core.Infrastructure.RaceControlPaths]::new([NullString]::Value, $launcher)
$paths.ConfigureProcessStorage()
Require ($env:TEMP -eq $paths.TempDirectory) 'Temporary files escape the package.'
Require (-not [AssettoServer.Release.FpsAssetResources]::IsAvailable([Reflection.Assembly]::LoadFrom($assembly))) 'Missing FPS pack was not detected.'
$passed.Add('Launcher is self-contained, portable, has release compatibility checks, and contains no server/FPS payload.')
$installer = [AssettoServer.RaceControl.Core.Staging.ComponentInstaller]::new($paths)
$serverZip = Join-Path $releaseRoot ($manifest.assets | Where-Object kind -EQ 'server').name
$server = $installer.ImportAsync($serverZip,'server',$null,[Threading.CancellationToken]::None).GetAwaiter().GetResult()
Require ($server.StartsWith($launcher,[StringComparison]::OrdinalIgnoreCase)) 'Imported server escaped package.'
Require (-not (Test-Path (Join-Path $server 'portable.json'))) 'Imported server does not inherit host root.'
$settings = [AssettoServer.RaceControl.Core.Storage.ApplicationSettings]::new()
$settings.ServerPayloadPath = $server
$store = [AssettoServer.RaceControl.Core.Storage.ApplicationSettingsStore]::new($paths)
$freshSettings = $store.Load()
Require ($freshSettings.Theme.ToString() -eq 'Dark' -and $freshSettings.CompactGridRows) 'Captured UI defaults were not loaded.'
Require ($freshSettings.AssettoCorsaRoot -eq '' -and $freshSettings.ServerPayloadPath -eq '') 'Defaults contain installation paths.'
Require ($freshSettings.LastPageIndex -eq 0 -and $freshSettings.WebUiBindAddress -eq '127.0.0.1') 'Defaults contain remembered state or a source network address.'
$store.Save($settings)
$fpsZip = Join-Path $releaseRoot ($manifest.assets | Where-Object kind -EQ 'fps').name
$pack = $installer.ImportAsync($fpsZip,'fps',$null,[Threading.CancellationToken]::None).GetAwaiter().GetResult()
Require ([AssettoServer.Release.FpsAssetResources]::IsAvailable([Reflection.Assembly]::LoadFrom($assembly))) 'Imported FPS pack not detected.'
$export = Join-Path $paths.ExportsDirectory 'fps-roundtrip.zip'
$stream = [IO.File]::Create($export)
try { [AssettoServer.RaceControl.Core.Staging.FpsClientPackBuilder]::WriteAsync($stream,'',[Threading.CancellationToken]::None).GetAwaiter().GetResult() | Out-Null }
finally { $stream.Dispose() }
$archive = [IO.Compression.ZipFile]::OpenRead($export)
try {
    foreach ($entry in $archive.Entries) {
        $input = $entry.Open()
        try { $actual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($input)) }
        finally { $input.Dispose() }
        $original = Get-FileHash -LiteralPath (Join-Path $pack $entry.FullName) -Algorithm SHA256
        Require ($actual -eq $original.Hash) "FPS round-trip changed $($entry.FullName)"
    }
    $passed.Add("FPS external pack export matches all $($archive.Entries.Count) canonical client entries byte-for-byte.")
} finally { $archive.Dispose() }
$mapsZip = Join-Path $releaseRoot ($manifest.assets | Where-Object kind -EQ 'maps').name
$maps = $installer.ImportAsync($mapsZip,'maps',$null,[Threading.CancellationToken]::None).GetAwaiter().GetResult()
$mapManifest = Get-Content (Join-Path $maps 'asrc-fps-maps.json') -Raw | ConvertFrom-Json
Require ($mapManifest.maps.Count -eq 4 -and $mapManifest.packVersion -eq $manifest.mapsPackVersion) 'Map pack metadata mismatch.'
$emptyAc = Join-Path $evidence 'Game Without Maps'
New-Item -ItemType Directory -Path (Join-Path $emptyAc 'content') | Out-Null
$defaults = [AssettoServer.RaceControl.Core.Storage.ReleaseDefaults]::new($paths)
$fresh = $defaults.CreateStartupPreset($emptyAc, $server)
Require ($fresh.Mode.ToString() -eq 'Fps' -and $fresh.Fps.Theme.ToString() -eq 'Modern' -and $fresh.Fps.MatchType.ToString() -eq 'TeamDeathmatch') 'Captured FPS startup defaults were not loaded.'
Require ($fresh.Fps.Bots.Difficulty -eq 0.13 -and $fresh.Conditions.SunAngleDegrees -eq 32) 'Captured gameplay defaults changed.'
$cacheType = $paths.GetType().Assembly.GetType('AssettoServer.RaceControl.Core.Staging.PreparedPhysicsAssetCache')
$getCache = $cacheType.GetMethod('GetFpsPaths',[Reflection.BindingFlags]'Public,Static')
function Check-Maps($CurrentPaths) {
    $catalog = [AssettoServer.RaceControl.Core.Content.AcContentScanner]::new($CurrentPaths.PackageRoot).Scan($emptyAc)
    Require ($catalog.Tracks.Count -eq 4) 'Bundled maps are not visible without installed game maps.'
    $arenas = [AssettoServer.RaceControl.Core.Storage.FpsArenaStore]::new($CurrentPaths)
    foreach ($track in $catalog.Tracks) {
        $preset = [AssettoServer.RaceControl.Core.Models.RaceControlPreset]::CreateDefault($emptyAc, '')
        $preset.Mode = [AssettoServer.RaceControl.Core.Models.EventMode]::Fps
        $preset.TrackId = $track.TrackId
        $preset.Fps.Arena = $arenas.Load($track.TrackId, $track.LayoutId)
        Require ($preset.Fps.Arena.PreparationVersion -eq 4 -and $preset.Fps.Arena.SpawnPoints.Count -ge 4) "Arena missing or outdated: $($track.TrackId)"
        $preset.Fps.ArenaBoundsPaddingMeters = $preset.Fps.Arena.BoundsPaddingMeters
        $cache = $getCache.Invoke($null,@($CurrentPaths.PSObject.BaseObject,$preset.PSObject.BaseObject,$track.PSObject.BaseObject))
        Require ($cache.IsComplete) "Prepared map cache did not rebind: $($track.TrackId)"
        foreach ($pair in @(@{Local=$cache.GeometryPath;Seed='geometry.bin'},@{Local=$cache.NavigationPath;Seed='navigation.bin'})) {
            $seed = Join-Path $track.RootPath ('race-control/'+$pair.Seed)
            Require ((Get-FileHash -LiteralPath $pair.Local).Hash -eq (Get-FileHash -LiteralPath $seed).Hash) "Prepared map cache changed: $($track.TrackId)"
            Require ($pair.Local.StartsWith($CurrentPaths.PackageRoot,[StringComparison]::OrdinalIgnoreCase)) 'Map cache escaped the portable root.'
        }
    }
}
Check-Maps $paths
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $launcher 'Defaults'),$maps -Recurse -File | Where-Object Extension -in '.json','.txt','.ini','.lua') {
    $text = [IO.File]::ReadAllText($file.FullName)
    Require (-not ($text -match '[A-Za-z]:[\\/] |7656119\d{10}'.Replace(' ',''))) "Private path or Steam identifier in release content: $($file.Name)"
    foreach ($token in @($env:USERNAME,$env:COMPUTERNAME)) {
        if ($token.Length -ge 4) { Require (-not ($text -match ('\b'+[Regex]::Escape($token)+'\b'))) "Machine identifier in release content: $($file.Name)" }
    }
    Require ($file.Name -ne 'conversion-report.json') 'Diagnostic source paths were packaged.'
}
$passed.Add('Offline server, FPS and map ZIP imports passed checksums and local installation.')
$passed.Add('All four maps are discoverable without installed tracks; their prepared geometry/navigation seeds match exactly.')
$passed.Add('Sanitized UI/FPS startup defaults load successfully; captured text contains no source PC paths or identifiers.')
# Move only after releasing file streams. Core remains loaded from its old name; Windows permits directory relocation.
$relocated = Join-Path $evidence 'Launcher Relocated'
$expectedRoot = [IO.Path]::GetFullPath($evidence) + [IO.Path]::DirectorySeparatorChar
Require ($launcher.StartsWith($expectedRoot,[StringComparison]::OrdinalIgnoreCase) -and $relocated.StartsWith($expectedRoot,[StringComparison]::OrdinalIgnoreCase)) 'Relocation target escaped evidence directory.'
Move-Item -LiteralPath $launcher -Destination $relocated
$relocatedPaths = [AssettoServer.RaceControl.Core.Infrastructure.RaceControlPaths]::new([NullString]::Value,$relocated)
$relocatedPaths.ConfigureProcessStorage()
[AppContext]::SetData('ASRC.FpsAssetRoot',$relocatedPaths.FpsAssetsDirectory)
[AppContext]::SetData('ASRC.FpsMapsRoot',$relocatedPaths.FpsMapsDirectory)
Check-Maps $relocatedPaths
$passed.Add('All four imported maps and prepared cache seeds resolve after moving the entire package.')
$reloaded = [AssettoServer.RaceControl.Core.Storage.ApplicationSettingsStore]::new($relocatedPaths).Load()
Require ($reloaded.ServerPayloadPath.StartsWith($relocated,[StringComparison]::OrdinalIgnoreCase)) 'Saved server path did not relocate.'
[AssettoServer.RaceControl.Core.Staging.ComponentInstaller]::ValidateServerBinaryAsync($reloaded.ServerPayloadPath,[Threading.CancellationToken]::None).GetAwaiter().GetResult() | Out-Null
$passed.Add('Relocated package resolves saved relative server paths and executes its self-contained server.')
$passed | ForEach-Object { Write-Host "PASS: $_" }
[IO.File]::WriteAllLines((Join-Path $evidence 'package-checks.txt'),$passed)
