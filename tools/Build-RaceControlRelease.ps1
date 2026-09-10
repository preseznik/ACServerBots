[CmdletBinding()]
param(
    [string]$InnoCompiler = (Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 7/ISCC.exe'),
    [string]$OutputRoot = 'dist'
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$modernManifest = Get-Content -LiteralPath (Join-Path $repo 'AssettoServer.RaceControl.Core/Assets/Fps/Modern/asrc-modern-assets.json') -Raw | ConvertFrom-Json
if ($modernManifest.operators.ghost -and -not $modernManifest.operators.ghost.redistributionRightsConfirmedByUser) {
    throw 'Public release packaging requires a recorded Ghost-specific redistribution permission. Local development builds remain available with Publish-RaceControl.ps1.'
}
[xml]$versions = Get-Content (Join-Path $repo 'Release\Release.props')
$version = [string]$versions.Project.PropertyGroup[0].RaceControlReleaseVersion
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[a-zA-Z0-9.-]+)?$') { throw 'Invalid release version.' }
$destination = [IO.Path]::GetFullPath((Join-Path $repo "$OutputRoot\v$version"))
if (Test-Path -LiteralPath $destination) { throw "Release already exists; use a new version or a separate OutputRoot: $destination" }
if (-not (Test-Path -LiteralPath $InnoCompiler)) { throw "Inno compiler not found: $InnoCompiler" }
$work = Join-Path $repo ('.artifacts\release-' + $version + '-' + [Guid]::NewGuid().ToString('N'))
$stage = Join-Path $work 'stage'
$assets = Join-Path $work 'assets'
$launcher = Join-Path $stage 'launcher'
$server = Join-Path $stage 'server'
$fps = Join-Path $stage 'fps'
$full = Join-Path $stage 'full'
$maps = Join-Path $stage 'maps'
$content = Join-Path $repo 'Release/Content'
foreach ($required in @('Defaults/settings.json','Defaults/fps.json','Defaults/racing.json','Defaults/startup.json','FpsMaps/asrc-fps-maps.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $content $required))) { throw "Release content snapshot missing: $required. Run tools/Capture-RaceControlReleaseContent.ps1 first." }
}
New-Item -ItemType Directory -Path $stage,$assets,$launcher,$server,$fps,$full,$maps -Force | Out-Null
$stamp = [DateTimeOffset]::new(2026,9,6,0,0,0,[TimeSpan]::Zero)
function Save-Json($Path, $Value) { [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 12) + [Environment]::NewLine) }
function Copy-Tree($From, $To) {
    New-Item -ItemType Directory -Path $To -Force | Out-Null
    Get-ChildItem -LiteralPath $From -Force | Copy-Item -Destination $To -Recurse -Force
}
function Write-FileManifest($Root) {
    $hashes = [ordered]@{}
    foreach ($file in (Get-ChildItem -LiteralPath $Root -Recurse -File | Sort-Object FullName)) {
        $name = [IO.Path]::GetRelativePath($Root, $file.FullName).Replace('\','/')
        if ($name -in @('payload-sha256.json','portable.json')) { continue }
        $hashes[$name] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    Save-Json (Join-Path $Root 'payload-sha256.json') $hashes
}
function Write-Zip($Root, $Zip, [string[]]$Names) {
    if (-not $Names) {
        $Names = @(Get-ChildItem -LiteralPath $Root -Recurse -File | ForEach-Object {
            [IO.Path]::GetRelativePath($Root, $_.FullName).Replace('\','/')
        })
    }
    [Array]::Sort($Names, [StringComparer]::Ordinal)
    $stream = [IO.File]::Open($Zip, [IO.FileMode]::CreateNew)
    $archive = [IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in $Names) {
            $entry = $archive.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $stamp
            $input = [IO.File]::OpenRead((Join-Path $Root $name))
            $output = $entry.Open()
            try { $input.CopyTo($output) } finally { $output.Dispose(); $input.Dispose() }
        }
    } finally { $archive.Dispose(); $stream.Dispose() }
}
Write-Host "Building Race Control $version in $work"
& dotnet build (Join-Path $repo 'AssettoServer.RaceControl.Core\AssettoServer.RaceControl.Core.csproj') -c Release -v quiet -p:ExternalFpsAssets=false
if ($LASTEXITCODE) { throw 'Core asset build failed.' }
$embedded = Join-Path $repo 'AssettoServer.RaceControl.Core\bin\Release\net10.0\AssettoServer.RaceControl.Core.dll'
& pwsh -NoProfile -File (Join-Path $repo 'Release\Export-FpsPack.ps1') -Assembly $embedded -Destination (Join-Path $work 'raw-fps.zip')
if ($LASTEXITCODE) { throw 'FPS export failed.' }
[IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $work 'raw-fps.zip'), $fps)
$packVersion = (Get-Content (Join-Path $fps 'asrc-fps-client.json') -Raw | ConvertFrom-Json).clientPackVersion
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE'),(Join-Path $repo 'THIRD_PARTY_NOTICES.md') -Destination $fps
Write-FileManifest $fps
Copy-Tree (Join-Path $content 'FpsMaps') $maps
$mapManifest = Get-Content (Join-Path $maps 'asrc-fps-maps.json') -Raw | ConvertFrom-Json
$mapPackVersion = $mapManifest.packVersion
Write-FileManifest $maps
foreach ($item in @(
    @{ Project = 'AssettoServer\AssettoServer.csproj'; Output = $server },
    @{ Project = 'AssettoServer.RaceControl\AssettoServer.RaceControl.csproj'; Output = $launcher }
)) {
    & dotnet publish (Join-Path $repo $item.Project) -c Release -r win-x64 --self-contained true -o $item.Output -v quiet -p:PublishSingleFile=false -p:ExternalFpsAssets=true -p:DebugSymbols=false -p:DebugType=None
    if ($LASTEXITCODE) { throw "Publish failed: $($item.Project)" }
}
Copy-Tree (Join-Path $content 'Defaults') (Join-Path $launcher 'Defaults')
$capabilities = & (Join-Path $server 'AssettoServer.exe') --capabilities-json | ConvertFrom-Json
if ($LASTEXITCODE -or $capabilities.version -ne $version -or $capabilities.fpsPackVersion -ne $packVersion) { throw 'Release version mismatch.' }
Save-Json (Join-Path $server 'race-control-server.json') $capabilities
$hasGit = Test-Path -LiteralPath (Join-Path $repo '.git')
$commit = if ($hasGit) { (& git -C $repo rev-parse HEAD).Trim() } else { $null }
$modified = if ($hasGit) { [bool](& git -C $repo status --porcelain) } else { $true }
$release = [ordered]@{ product='preseznik/ACServerBots'; version=$version; runtime='win-x64'; sourceCommit=$commit; workingTreeModified=$modified; fpsPackVersion=$packVersion; mapsPackVersion=$mapPackVersion; maps=$mapManifest.maps; controlProtocol=1 }
Save-Json (Join-Path $launcher 'release-build.json') $release
$readme = @"
AssettoServer Race Control $version - Windows x64, LAN-focused preview
==================================================================
This is the ACServerBots fork, not an upstream AssettoServer release.

Portable ZIPs: extract to a writable folder and run AssettoServer Race Control.exe.
Keep portable.json. All launcher settings, servers, logs, caches, downloads,
exports and temporary files live under this extracted folder. Move the whole
folder to relocate it. Assetto Corsa is read as an external content source; choose
its new location if moving to another PC. No automatic game installation occurs.
Do not extract over an installed edition or merge different release versions.

Full ZIP: launcher + matching server + FPS hosting assets + all four FPS maps.
Launcher ZIP: launcher only. Settings > Local Installations > Import server ZIP
adds this release's server. Import FPS ZIP adds optional hosting assets.
Import maps ZIP adds optional FPS maps, arena definitions and prepared navigation.
Server ZIP: matching standalone server; portable.json keeps relative writes local.
FPS client ZIP: extract into your Assetto Corsa folder. It is a game-content
overlay, with no executable or AppData storage. It includes both visual themes.
For a standalone server, extract the same FPS ZIP into Packs\Fps below the server.
FPS maps ZIP: Nuketown, Fire Pit, Krvava Rotunda and Shipment, with arena definitions
and prepared collision/navigation. Clients extract content into Assetto Corsa.
Hosts import the ZIP or extract it into Packs\FpsMaps below the launcher.
Bundled maps are read directly from the package; hosting never installs game files.
Defaults supplies sanitized UI and per-mode gameplay preferences on first run.
Existing saved preferences and arena customizations take precedence.

The installer offers Racing host, Full host, FPS client only and Custom.
FPS hosting maps and client map installation are independently selectable.
Installed launcher data is in %LOCALAPPDATA%\AssettoServer Race Control and survives
upgrade/uninstall. Optional FPS files are copied into the selected game folder and
remain after uninstall. Assetto Corsa and CSP are never bundled. For a protected Steam
folder select an administrator installation when prompted by Setup.

FPS clients need CSP 0.3.0-preview520 or newer, the host's carrier car and arena.
Cars, other tracks, Assetto Corsa, Content Manager and CSP are supplied separately.
Retain each map README and its artwork/provenance restrictions. Some map notices
restrict public distribution pending artwork review; this is a local review build.
The server delivers its versioned online Lua/model archives when an FPS session
runs; the local client ZIP supplies the HUD, audio, and shared visual assets.
Racing works without the optional FPS pack.

Download buttons fetch this exact compatible fork release, not upstream/latest.
For this first local release use Import ZIP until the assets are published at:
https://github.com/preseznik/ACServerBots/releases/tag/race-control-v$version

Source: RaceControl-$version-Source.zip accompanies this release and contains
the matching modified source, assets and build scripts. Build with .NET 10 SDK
(the server targets .NET 9), PowerShell 7 and Inno Setup 7:
pwsh -NoProfile -File tools\Build-RaceControlRelease.ps1
Retain LICENSE, THIRD_PARTY_NOTICES.md and individual asset attributions.
Before public distribution, publish the matching source and binaries together.

This is an unsigned preview. Clean-PC and two-client in-game acceptance are
recorded separately in VALIDATION.md; a local smoke test is not gameplay approval.
"@
foreach ($root in @($launcher,$server)) {
    Copy-Item -LiteralPath (Join-Path $repo 'LICENSE'),(Join-Path $repo 'THIRD_PARTY_NOTICES.md') -Destination $root
    [IO.File]::WriteAllText((Join-Path $root 'README.txt'), $readme)
    Save-Json (Join-Path $root 'portable.json') @{ schemaVersion=1; dataDirectory='Data' }
}
# Bare bundled server inherits the containing launcher root; standalone ZIP gets its marker.
Write-FileManifest $server
Copy-Tree $launcher $full
Copy-Tree $server (Join-Path $full 'lib\Server')
Remove-Item -LiteralPath (Join-Path $full 'lib\Server\portable.json')
Copy-Tree $fps (Join-Path $full 'Packs\Fps')
Copy-Tree $maps (Join-Path $full 'Packs\FpsMaps')
$names = [ordered]@{
    full = "RaceControl-$version-Full-win-x64.zip"
    launcher = "RaceControl-$version-Launcher-win-x64.zip"
    server = "RaceControl-$version-Server-win-x64.zip"
    fps = "RaceControl-$version-FPS-client-v$packVersion.zip"
    maps = "RaceControl-$version-FPS-maps-v$mapPackVersion.zip"
    source = "RaceControl-$version-Source.zip"
}
Write-Host 'Creating portable and client archives...'
Write-Zip $full (Join-Path $assets $names.full)
Write-Zip $launcher (Join-Path $assets $names.launcher)
Write-Zip $server (Join-Path $assets $names.server)
Write-Zip $fps (Join-Path $assets $names.fps)
Write-Zip $maps (Join-Path $assets $names.maps)
# Snapshot the working tree, including uncommitted release changes, excluding ignored outputs.
$sourceNames = if ($hasGit) {
    @(& git -C $repo -c core.quotepath=false ls-files --cached --others --exclude-standard | Where-Object { Test-Path -LiteralPath (Join-Path $repo $_) -PathType Leaf } | Sort-Object -Unique)
} else {
    # Source ZIPs have no .git database. Repackage their source files while
    # excluding outputs produced during this build.
    @(Get-ChildItem -LiteralPath $repo -Recurse -File | ForEach-Object {
        [IO.Path]::GetRelativePath($repo, $_.FullName).Replace('\','/')
    } | Where-Object { $_ -notmatch '(^|/)(bin|obj|dist|\.artifacts|\.git|\.vs|node_modules)/' })
}
# Map binaries are local release inputs, deliberately excluded from normal Git adds.
$sourceNames = @($sourceNames + @(Get-ChildItem -LiteralPath (Join-Path $content 'FpsMaps') -File -Recurse | ForEach-Object {
    [IO.Path]::GetRelativePath($repo, $_.FullName).Replace('\','/')
}) | Sort-Object -Unique)
Write-Zip $repo (Join-Path $assets $names.source) $sourceNames
Write-Host 'Compiling selectable Inno Setup installer...'
& $InnoCompiler /Q "/DReleaseVersion=$version" "/DRepoRoot=$repo" "/DStageRoot=$stage" "/DOutputRoot=$assets" (Join-Path $repo 'Release\RaceControl.iss')
if ($LASTEXITCODE) { throw 'Inno Setup compilation failed.' }
$names.setup = "RaceControl-$version-Setup.exe"
$release.assets = @(
    foreach ($kind in $names.Keys) {
        $file = Get-Item -LiteralPath (Join-Path $assets $names[$kind])
        [ordered]@{ kind=$kind; name=$file.Name; bytes=$file.Length; sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
)
Save-Json (Join-Path $assets 'release.json') $release
$checksums = @(Get-ChildItem -LiteralPath $assets -File | Sort-Object Name | ForEach-Object {
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name
})
[IO.File]::WriteAllLines((Join-Path $assets 'SHA256SUMS.txt'), $checksums)
New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
# Only promote a completed build. Never overwrite an earlier release.
Move-Item -LiteralPath $assets -Destination $destination
Write-Host "Release ready: $destination"
Write-Host "Staging retained for validation: $stage"
