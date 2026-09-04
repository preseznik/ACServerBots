[CmdletBinding()]
param(
    [string]$BlenderPath = "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe",
    [string]$FragSourceDirectory = "F:\Coding\Codex\.resources\AssettoCorsaMods\FPS\Weapons\m67-grenade",
    [string]$StickySourceDirectory = "F:\Coding\Codex\.resources\AssettoCorsaMods\FPS\Weapons\semtex-sticky-grenade",
    [string]$CarbineFbx = "F:\Coding\Codex\.resources\AssettoCorsaMods\FPS\Weapons\fps-animated-carbine\source\arms@carbine.fbx",
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\AssettoServer.RaceControl.Core\Assets\Fps"),
    [string]$PreviewDirectory = (Join-Path $PSScriptRoot "..\.artifacts\fps-grenades")
)

$ErrorActionPreference = "Stop"
$sourceScript = Join-Path $PSScriptRoot "build_fps_grenade_assets.py"
$validationScript = Join-Path $PSScriptRoot "validate_fps_grenade_assets.py"
$exporterRoot = Join-Path $PSScriptRoot "vendor\blender_assetto_corsa_tools"
$required = @(
    $BlenderPath, $sourceScript, $validationScript, $CarbineFbx,
    (Join-Path $FragSourceDirectory "source\granada_sketchfab.fbx"),
    (Join-Path $StickySourceDirectory "source\Grenade_export.fbx"),
    (Join-Path $exporterRoot "__init__.py")
)
foreach ($path in $required) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required grenade asset build dependency was not found: $path"
    }
}

New-Item -ItemType Directory -Path $OutputDirectory, $PreviewDirectory -Force | Out-Null
$buildMarker = Join-Path $OutputDirectory ".grenade-build-ok"
$validationMarker = Join-Path $OutputDirectory ".grenade-validation-ok"
Remove-Item -LiteralPath $buildMarker, $validationMarker -Force -ErrorAction SilentlyContinue

& $BlenderPath --background --python $sourceScript -- `
    --frag-source-dir $FragSourceDirectory --sticky-source-dir $StickySourceDirectory `
    --carbine-fbx $CarbineFbx --output-dir $OutputDirectory `
    --exporter-root $exporterRoot --success-marker $buildMarker `
    --preview-dir $PreviewDirectory
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $buildMarker -PathType Leaf)) {
    throw "Blender grenade asset generation failed with exit code $LASTEXITCODE."
}

& $BlenderPath --background --python $validationScript -- `
    --asset-dir $OutputDirectory --success-marker $validationMarker
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $validationMarker -PathType Leaf)) {
    throw "Grenade asset validation failed with exit code $LASTEXITCODE."
}

Remove-Item -LiteralPath $buildMarker, $validationMarker -Force
