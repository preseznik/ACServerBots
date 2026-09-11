[CmdletBinding()]
param(
    [string]$BlenderPath = 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe',
    [string]$SourceBlend = 'F:\Coding\Codex\.resources\AssettoCorsaMods\FPS\Characters\Universal Animation Library[Source]\UAL1.blend',
    [string]$ReferenceDirectory = (Join-Path $PSScriptRoot '..\.artifacts\ghost-work'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\AssettoServer.RaceControl.Core\Assets\Fps\Modern'),
    [string]$CacheDirectory = (Join-Path $PSScriptRoot '..\.artifacts\quaternius-work'),
    [switch]$ProofOnly,
    [switch]$Render
)
$ErrorActionPreference = 'Stop'
$reference = Join-Path $ReferenceDirectory 'officer-reference.blend'
$ghost = Join-Path $ReferenceDirectory 'ghost-production.blend'
foreach ($path in @($BlenderPath, $SourceBlend, $reference, $ghost,
        (Join-Path $OutputDirectory 'asrc-modern-assets.json'))) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing locomotion input: $path. Build the base Modern assets first if a reference is missing."
    }
}
$buildArguments = @('--source', $SourceBlend, '--reference', $reference,
    '--ghost-reference', $ghost, '--output-dir', $OutputDirectory, '--cache-dir', $CacheDirectory)
if ($ProofOnly) { $buildArguments += '--proof-only' }
if ($Render) { $buildArguments += '--render' }
& $BlenderPath --background --factory-startup --disable-autoexec --python-exit-code 1 `
    --python (Join-Path $PSScriptRoot 'build_fps_locomotion.py') -- @buildArguments
if ($LASTEXITCODE -ne 0) { throw "Locomotion build failed: $LASTEXITCODE" }
