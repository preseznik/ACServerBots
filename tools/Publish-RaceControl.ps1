[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [ValidateSet('win-x64', 'win-arm64')]
    [string] $Runtime = 'win-x64',
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot "out-race-control-$Runtime"
}
$serverOutput = Join-Path $repositoryRoot "out-$Runtime"
$appOutput = [IO.Path]::GetFullPath($OutputDirectory)

Write-Host "Publishing AssettoServer ($Runtime, $Configuration)..."
& dotnet publish (Join-Path $repositoryRoot 'AssettoServer\AssettoServer.csproj') `
    -c $Configuration -r $Runtime --self-contained true -o $serverOutput
if ($LASTEXITCODE -ne 0) { throw "AssettoServer publish failed with exit code $LASTEXITCODE" }

Write-Host "Publishing Race Control ($Runtime, $Configuration)..."
& dotnet publish (Join-Path $repositoryRoot 'AssettoServer.RaceControl\AssettoServer.RaceControl.csproj') `
    -c $Configuration -r $Runtime --self-contained true -o $appOutput
if ($LASTEXITCODE -ne 0) { throw "Race Control publish failed with exit code $LASTEXITCODE" }

$bundledServer = Join-Path $appOutput 'Server'
New-Item -ItemType Directory -Path $bundledServer -Force | Out-Null
Copy-Item -Path (Join-Path $serverOutput '*') -Destination $bundledServer -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination $appOutput -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD_PARTY_NOTICES.md') -Destination $appOutput -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\race-control.md') -Destination $appOutput -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\race-bots.md') -Destination $appOutput -Force

Write-Host "Portable Race Control build ready: $appOutput"
Write-Host "Run: $(Join-Path $appOutput 'AssettoServer Race Control.exe')"
