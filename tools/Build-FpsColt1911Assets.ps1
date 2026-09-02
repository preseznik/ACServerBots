[CmdletBinding()]
param(
    [string]$BlenderPath = "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe",
    [string]$SourceDirectory = (Join-Path $PSScriptRoot "..\..\..\.resources\AssettoCorsaMods\FPS\Weapons\m1911-pistol-with-magazine-and-bullet"),
    [string]$CarbineFbx = "F:\Coding\Codex\.resources\AssettoCorsaMods\FPS\Weapons\fps-animated-carbine\source\arms@carbine.fbx",
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\AssettoServer.RaceControl.Core\Assets\Fps"),
    [string]$PreviewPath = (Join-Path $PSScriptRoot "..\.artifacts\fps-colt-1911-viewmodel-preview.png")
)

$ErrorActionPreference = "Stop"

$sourceScript = Join-Path $PSScriptRoot "build_fps_colt_1911_assets.py"
$validationScript = Join-Path $PSScriptRoot "validate_fps_colt_1911_assets.py"
$exporterRoot = Join-Path $PSScriptRoot "vendor\blender_assetto_corsa_tools"
$sourceFbx = Join-Path $SourceDirectory "source\m1911+mag+bullets_final_low.fbx"

foreach ($required in @($BlenderPath, $sourceScript, $validationScript, $sourceFbx, $CarbineFbx,
        (Join-Path $exporterRoot "__init__.py"), (Join-Path $exporterRoot "LICENSE.txt"))) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Required Colt 1911 asset build dependency was not found: $required"
    }
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$buildMarker = Join-Path $OutputDirectory ".colt-1911-build-ok"
$validationMarker = Join-Path $OutputDirectory ".colt-1911-validation-ok"
Remove-Item -LiteralPath $buildMarker, $validationMarker -Force -ErrorAction SilentlyContinue

& $BlenderPath --background --python $sourceScript -- `
    --source-dir $SourceDirectory --carbine-fbx $CarbineFbx `
    --output-dir $OutputDirectory --exporter-root $exporterRoot `
    --success-marker $buildMarker --preview-path $PreviewPath
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $buildMarker -PathType Leaf)) {
    throw "Blender Colt 1911 asset generation failed with exit code $LASTEXITCODE."
}

& $BlenderPath --background --python $validationScript -- `
    --asset-dir $OutputDirectory --success-marker $validationMarker
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $validationMarker -PathType Leaf)) {
    throw "Colt 1911 asset validation failed with exit code $LASTEXITCODE."
}

Remove-Item -LiteralPath $buildMarker, $validationMarker -Force
