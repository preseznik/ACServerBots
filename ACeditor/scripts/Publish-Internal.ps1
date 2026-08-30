[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\win-x64')
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectFile = Join-Path $projectRoot 'ACEditor.App\ACEditor.App.csproj'
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)

dotnet publish $projectFile `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    --output $resolvedOutput `
    --nologo

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Write-Host "Published framework-dependent AC Editor to $resolvedOutput"
