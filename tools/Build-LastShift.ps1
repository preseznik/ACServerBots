[CmdletBinding()]
param(
    [string]$BlenderPath = 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\last-shift-v031'),
    [switch]$SkipRenders,
    [switch]$SkipValidation
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $BlenderPath)) { throw "Blender not found: $BlenderPath" }
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$buildArgs = @('--background', '--python-exit-code', '1', '--python', (Join-Path $PSScriptRoot 'build_last_shift.py'), '--',
    '--output-root', $outputRoot, '--layout', (Join-Path $repoRoot 'maps\last_shift\layout.json'),
    '--exporter-root', (Join-Path $PSScriptRoot 'vendor\blender_assetto_corsa_tools'))
if ($SkipRenders) { $buildArgs += '--skip-renders' }
& $BlenderPath @buildArgs
if ($LASTEXITCODE -ne 0) { throw "Last Shift Blender build failed: $LASTEXITCODE" }
if (-not $SkipValidation) {
    & dotnet build (Join-Path $PSScriptRoot 'LastShiftGate\LastShiftGate.csproj') -c Release `
        --configfile (Join-Path $PSScriptRoot 'LastShiftGate\NuGet.Config') --nologo
    if ($LASTEXITCODE -ne 0) { throw "Last Shift gate build failed: $LASTEXITCODE" }
    & dotnet (Join-Path $PSScriptRoot 'LastShiftGate\bin\Release\net10.0\LastShiftGate.dll') `
        --ac-root $outputRoot --output (Join-Path $outputRoot 'validation') `
        --probes (Join-Path $outputRoot 'last-shift-layout.json')
    if ($LASTEXITCODE -ne 0) { throw "Last Shift arena gate failed: $LASTEXITCODE" }
    $blenderDirectory = Split-Path $BlenderPath -Parent
    $pythonRuntimes = @(Get-ChildItem -LiteralPath $blenderDirectory -Directory |
        Where-Object Name -Match '^\d+\.\d+$' |
        ForEach-Object { Join-Path $_.FullName 'python\bin\python.exe' } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
    if ($pythonRuntimes.Count -ne 1) { throw 'Could not select the bundled Blender Python runtime.' }
    & $pythonRuntimes[0] (Join-Path $PSScriptRoot 'validate_last_shift_materials.py') `
        --track (Join-Path $outputRoot 'content\tracks\last_shift') `
        --output (Join-Path $outputRoot 'validation\materials.json')
    if ($LASTEXITCODE -ne 0) { throw "Last Shift material validation failed: $LASTEXITCODE" }
    & $pythonRuntimes[0] (Join-Path $PSScriptRoot 'last_shift_detail_checks.py') `
        --build-root $outputRoot --output (Join-Path $outputRoot 'validation\details.json')
    if ($LASTEXITCODE -ne 0) { throw "Last Shift detail validation failed: $LASTEXITCODE" }
}
$trackRoot = Join-Path $outputRoot 'content\tracks\last_shift'
foreach ($relative in @('last_shift.kn5', 'last_shift_collision.kn5', 'models.ini',
        'data\surfaces.ini', 'data\crew.ini', 'data\lighting.ini', 'data\map.ini',
        'map.png', 'ui\ui_track.json', 'ui\preview.png', 'ui\outline.png', 'conversion-report.json',
        'TEXTURE_CREDITS.txt', 'texture-sources.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $trackRoot $relative) -PathType Leaf)) {
        throw "Package input missing: $relative. Run without -SkipRenders for a complete first build."
    }
}
$identity = Get-Content -LiteralPath (Join-Path $trackRoot 'ui\ui_track.json') -Raw | ConvertFrom-Json
$archive = Join-Path $outputRoot "last_shift_$($identity.version).zip"
Compress-Archive -LiteralPath $trackRoot -DestinationPath $archive -Force
$hash = Get-FileHash -LiteralPath $archive -Algorithm SHA256
"$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($archive))" |
    Set-Content -LiteralPath (Join-Path $outputRoot 'SHA256SUMS.txt') -Encoding ascii
$hash | Format-List
Write-Host "Last Shift staged at $trackRoot"
