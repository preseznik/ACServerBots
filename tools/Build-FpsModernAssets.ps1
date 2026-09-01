[CmdletBinding()]
param(
    [string]$BlenderPath = "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe",
    [string]$OfficerZip = "F:\Coding\Codex\.resources\AssettoCorsaMods\FPS\Characters\army-officer\source\army_officer.zip",
    [string]$CarbineFbx = "F:\Coding\Codex\.resources\AssettoCorsaMods\FPS\Weapons\fps-animated-carbine\source\arms@carbine.fbx",
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\AssettoServer.RaceControl.Core\Assets\Fps\Modern")
)

$ErrorActionPreference = "Stop"

$sourceScript = Join-Path $PSScriptRoot "build_fps_modern_assets.py"
$exporterRoot = Join-Path $PSScriptRoot "vendor\blender_assetto_corsa_tools"
$requiredInputs = @(
    $BlenderPath,
    $OfficerZip,
    $CarbineFbx,
    $sourceScript,
    (Join-Path $PSScriptRoot "validate_fps_modern_assets.py"),
    (Join-Path $exporterRoot "__init__.py"),
    (Join-Path $exporterRoot "LICENSE.txt"),
    (Join-Path $exporterRoot "exporter\ksanim_writer.py")
)
foreach ($required in $requiredInputs) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required Modern FPS asset build input was not found: $required"
    }
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

& $BlenderPath --background --python $sourceScript -- `
    --output-dir $OutputDirectory `
    --exporter-root $exporterRoot `
    --officer-zip $OfficerZip `
    --carbine-fbx $CarbineFbx
if ($LASTEXITCODE -ne 0) {
    throw "Blender Modern FPS asset generation failed with exit code $LASTEXITCODE."
}

$expected = @(
    "asrc_modern_operator_carbine.kn5",
    "asrc_modern_carbine_viewmodel.kn5",
    "asrc_modern_carbine_pickup.kn5",
    "asrc-modern-assets.json"
)
$expected += @(
    "aim_idle", "aim_up", "aim_down", "walk_forward", "walk_backward",
    "strafe_left", "strafe_right", "sprint", "crouch_idle", "crouch_move",
    "prone_idle", "prone_crawl", "jump_start", "airborne", "land",
    "mantle", "vault", "fire", "reload", "death"
) | ForEach-Object { "asrc_modern_operator_$_.ksanim" }
$expected += @("idle", "fire", "reload", "reload_empty", "equip", "sprint") |
    ForEach-Object { "asrc_modern_carbine_$_.ksanim" }

foreach ($name in $expected) {
    $path = Join-Path $OutputDirectory $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Blender did not generate expected Modern FPS asset: $path"
    }
    if ((Get-Item -LiteralPath $path).Length -eq 0) {
        throw "Blender generated an empty Modern FPS asset: $path"
    }
}

$manifestPath = Join-Path $OutputDirectory "asrc-modern-assets.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.validation.status -ne "passed") {
    throw "Modern FPS asset validation did not complete successfully."
}

Write-Host "Built Modern FPS theme assets in $OutputDirectory"
