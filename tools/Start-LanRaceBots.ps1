[CmdletBinding()]
param(
    [string] $ServerRoot,
    [string] $PresetName = 'magione-lan-race-bots',
    [switch] $VerboseLog
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ServerRoot)) { $ServerRoot = Join-Path $PSScriptRoot '..\.artifacts\lan-race-bots' }
$serverRootPath = [IO.Path]::GetFullPath($ServerRoot)
$executable = Join-Path $serverRootPath 'AssettoServer.exe'
$preset = Join-Path $serverRootPath "presets\$PresetName\server_cfg.ini"
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "Server executable not found: $executable" }
if (-not (Test-Path -LiteralPath $preset -PathType Leaf)) { throw "Preset not found: $preset" }

$arguments = @('--preset', $PresetName)
if ($VerboseLog) { $arguments += '--verbose' }
Push-Location $serverRootPath
try {
    & $executable @arguments
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
