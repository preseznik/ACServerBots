[CmdletBinding()]
param(
    [string]$BlenderPath = "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe",
    [string]$SevenZipPath = "C:\Program Files\7-Zip\7z.exe",
    [string]$SourceDirectory = (Join-Path $PSScriptRoot "..\..\..\.resources\AssettoCorsaMods\FPS\Weapons\mp5-submachine-gun"),
    [string]$CarbineFbx = "F:\Coding\Codex\.resources\AssettoCorsaMods\FPS\Weapons\fps-animated-carbine\source\arms@carbine.fbx",
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\AssettoServer.RaceControl.Core\Assets\Fps"),
    [string]$PreviewPath = (Join-Path $PSScriptRoot "..\.artifacts\fps-mp5-viewmodel-preview.png")
)

$ErrorActionPreference = "Stop"

$sourceScript = Join-Path $PSScriptRoot "build_fps_mp5_assets.py"
$validationScript = Join-Path $PSScriptRoot "validate_fps_mp5_assets.py"
$exporterRoot = Join-Path $PSScriptRoot "vendor\blender_assetto_corsa_tools"
$sourceArchive = Join-Path $SourceDirectory "source\MP5.rar"

foreach ($required in @($BlenderPath, $SevenZipPath, $sourceScript, $validationScript,
        $sourceArchive, $CarbineFbx, (Join-Path $exporterRoot "__init__.py"),
        (Join-Path $exporterRoot "LICENSE.txt"))) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Required MP5 asset build dependency was not found: $required"
    }
}

$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$extractDirectory = Join-Path $temporaryRoot ("asrc-mp5-" + [Guid]::NewGuid().ToString("N"))
if (-not ([IO.Path]::GetFullPath($extractDirectory).StartsWith($temporaryRoot,
        [StringComparison]::OrdinalIgnoreCase))) {
    throw "Refusing to use an MP5 extraction path outside the temporary directory."
}

try {
    New-Item -ItemType Directory -Path $extractDirectory | Out-Null
    & $SevenZipPath x -y "-o$extractDirectory" $sourceArchive
    if ($LASTEXITCODE -ne 0) {
        throw "MP5 source extraction failed with exit code $LASTEXITCODE."
    }

    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    $buildMarker = Join-Path $OutputDirectory ".mp5-build-ok"
    $validationMarker = Join-Path $OutputDirectory ".mp5-validation-ok"
    Remove-Item -LiteralPath $buildMarker, $validationMarker -Force -ErrorAction SilentlyContinue

    & $BlenderPath --background --python $sourceScript -- `
        --source-dir $extractDirectory --carbine-fbx $CarbineFbx `
        --output-dir $OutputDirectory --exporter-root $exporterRoot `
        --success-marker $buildMarker --preview-path $PreviewPath
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $buildMarker -PathType Leaf)) {
        throw "Blender MP5 asset generation failed with exit code $LASTEXITCODE."
    }

    & $BlenderPath --background --python $validationScript -- `
        --asset-dir $OutputDirectory --success-marker $validationMarker
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $validationMarker -PathType Leaf)) {
        throw "MP5 asset validation failed with exit code $LASTEXITCODE."
    }

    Remove-Item -LiteralPath $buildMarker, $validationMarker -Force
}
finally {
    if (Test-Path -LiteralPath $extractDirectory) {
        $resolvedExtraction = [IO.Path]::GetFullPath($extractDirectory)
        $extractionLeaf = Split-Path -Leaf $resolvedExtraction
        if ($resolvedExtraction.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -and
            $extractionLeaf.StartsWith("asrc-mp5-")) {
            Remove-Item -LiteralPath $resolvedExtraction -Recurse -Force
        }
    }
}
