[CmdletBinding()]
param(
    [string]$BlenderPath = "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe",
    [string]$SourceDirectory = (Join-Path $PSScriptRoot "..\..\..\.resources\AssettoCorsaMods\FPS\Weapons\desert-eagle"),
    [string]$CarbineFbx = "F:\Coding\Codex\.resources\AssettoCorsaMods\FPS\Weapons\fps-animated-carbine\source\arms@carbine.fbx",
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\AssettoServer.RaceControl.Core\Assets\Fps"),
    [string]$PreviewPath = (Join-Path $PSScriptRoot "..\.artifacts\fps-desert-eagle-viewmodel-preview.png")
)

$ErrorActionPreference = "Stop"

$sourceScript = Join-Path $PSScriptRoot "build_fps_desert_eagle_assets.py"
$validationScript = Join-Path $PSScriptRoot "validate_fps_desert_eagle_assets.py"
$exporterRoot = Join-Path $PSScriptRoot "vendor\blender_assetto_corsa_tools"
$sourceFbx = Join-Path $SourceDirectory "source\Deagle_full.fbx"

foreach ($required in @($BlenderPath, $sourceScript, $validationScript, $sourceFbx, $CarbineFbx,
        (Join-Path $exporterRoot "__init__.py"), (Join-Path $exporterRoot "LICENSE.txt"))) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Required Desert Eagle asset build dependency was not found: $required"
    }
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$buildMarker = Join-Path $OutputDirectory ".desert-eagle-build-ok"
$validationMarker = Join-Path $OutputDirectory ".desert-eagle-validation-ok"
Remove-Item -LiteralPath $buildMarker, $validationMarker -Force -ErrorAction SilentlyContinue

& $BlenderPath --background --python $sourceScript -- `
    --source-dir $SourceDirectory --carbine-fbx $CarbineFbx `
    --output-dir $OutputDirectory --exporter-root $exporterRoot `
    --success-marker $buildMarker --preview-path $PreviewPath
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $buildMarker -PathType Leaf)) {
    throw "Blender Desert Eagle asset generation failed with exit code $LASTEXITCODE."
}

& $BlenderPath --background --python $validationScript -- `
    --asset-dir $OutputDirectory --success-marker $validationMarker
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $validationMarker -PathType Leaf)) {
    throw "Desert Eagle asset validation failed with exit code $LASTEXITCODE."
}

Remove-Item -LiteralPath $buildMarker, $validationMarker -Force
