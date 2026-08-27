[CmdletBinding()]
param(
    [string]$BlenderPath = "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe",
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\AssettoServer.RaceControl.Core\Assets\Fps")
)

$ErrorActionPreference = "Stop"

$sourceScript = Join-Path $PSScriptRoot "build_fps_rifle_assets.py"
$exporterRoot = Join-Path $PSScriptRoot "vendor\blender_assetto_corsa_tools"

foreach ($required in @($BlenderPath, $sourceScript,
        (Join-Path $exporterRoot "__init__.py"), (Join-Path $exporterRoot "LICENSE.txt"))) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Required FPS asset build dependency was not found: $required"
    }
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

& $BlenderPath --background --python $sourceScript -- `
    --output-dir $OutputDirectory --exporter-root $exporterRoot
if ($LASTEXITCODE -ne 0) {
    throw "Blender FPS rifle generation failed with exit code $LASTEXITCODE."
}

foreach ($name in @("asrc_assault_rifle_viewmodel", "asrc_assault_rifle_world")) {
    $kn5 = Join-Path $OutputDirectory "$name.kn5"
    if (-not (Test-Path -LiteralPath $kn5)) { throw "Blender did not generate expected KN5: $kn5" }
    $bytes = [IO.File]::ReadAllBytes($kn5)
    if ($bytes.Length -lt 1024 -or [Text.Encoding]::ASCII.GetString($bytes, 0, 6) -ne "sc6969") {
        throw "Blender generated an invalid KN5 asset: $kn5"
    }
    Write-Host "Built $kn5 ($($bytes.Length) bytes)"
}
