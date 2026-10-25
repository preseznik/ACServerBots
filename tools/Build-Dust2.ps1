[CmdletBinding()]
param(
    [string]$GameRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Source',
    [string]$BlenderPath = 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe',
    [string]$PythonPath = (Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'),
    [string]$SourceIoPin = (Join-Path $PSScriptRoot '..\.artifacts\de-dust2-conversion\tools\sourceio-pin.json'),
    [string]$BuildRoot = (Join-Path $PSScriptRoot '..\.artifacts\de-dust2-conversion\stage'),
    [string]$SourceRoot = (Join-Path $PSScriptRoot '..\.artifacts\de-dust2-conversion\css-source'),
    [switch]$ReuseImportedSource,
    [switch]$SkipRenders
)
$ErrorActionPreference = 'Stop'
$build = [IO.Path]::GetFullPath($BuildRoot)
$source = [IO.Path]::GetFullPath($SourceRoot)
$layout = Join-Path $PSScriptRoot '..\maps\css_dust2\layout.json'
foreach ($path in @($BlenderPath, $PythonPath, $SourceIoPin, $layout)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required input missing: $path" }
}
if (-not $ReuseImportedSource) {
    & $BlenderPath --background --factory-startup --python-exit-code 1 --python (Join-Path $PSScriptRoot 'import_dust2_source.py') -- `
        --game-root $GameRoot --sourceio-pin $SourceIoPin --output $source
    if ($LASTEXITCODE -ne 0) { throw 'CS:S source import failed.' }
}
$arguments = @('--background', '--factory-startup', '--python-exit-code', '1', '--python',
    (Join-Path $PSScriptRoot 'build_dust2.py'), '--', '--source', $source, '--output', $build, '--layout', $layout)
if ($SkipRenders) { $arguments += '--skip-renders' }
& $BlenderPath @arguments
if ($LASTEXITCODE -ne 0) { throw 'Dust II KN5 export failed.' }
& dotnet build (Join-Path $PSScriptRoot 'Dust2Gate\Dust2Gate.csproj') -c Release `
    --configfile (Join-Path $PSScriptRoot 'Dust2Gate\NuGet.Config') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Dust II gate build failed.' }
& dotnet (Join-Path $PSScriptRoot 'Dust2Gate\bin\Release\net10.0\Dust2Gate.dll') $build (Join-Path $build 'validation') $layout
if ($LASTEXITCODE -ne 0) { throw 'Dust II geometry/route checks failed.' }
& $PythonPath (Join-Path $PSScriptRoot 'validate_dust2.py') (Join-Path $build 'content\tracks\css_dust2') (Join-Path $build 'validation\materials.json')
if ($LASTEXITCODE -ne 0) { throw 'Dust II material checks failed.' }
if (-not (Test-Path -LiteralPath (Join-Path $build 'content\tracks\css_dust2\ui\preview.png'))) {
    throw 'A first build needs renders; rerun without -SkipRenders.'
}
& $PythonPath (Join-Path $PSScriptRoot 'package_dust2.py') $build
if ($LASTEXITCODE -ne 0) { throw 'Dust II packaging failed.' }
