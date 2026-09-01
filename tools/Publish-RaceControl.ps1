[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [ValidateSet('win-x64', 'win-arm64')]
    [string] $Runtime = 'win-x64',
    [string] $OutputDirectory = 'out-race-control'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$appOutput = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
$publishRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot ".artifacts\race-control-publish-$PID"))
$appStaging = Join-Path $publishRoot 'app'
$serverStaging = Join-Path $publishRoot 'server'
$packageRoot = Join-Path $publishRoot 'package'

try {
    New-Item -ItemType Directory -Path $appStaging, $serverStaging, $packageRoot -Force | Out-Null

    Write-Host "Publishing AssettoServer ($Runtime, $Configuration)..."
    & dotnet publish (Join-Path $repositoryRoot 'AssettoServer\AssettoServer.csproj') `
        -c $Configuration -r $Runtime --self-contained true -o $serverStaging
    if ($LASTEXITCODE -ne 0) { throw "AssettoServer publish failed with exit code $LASTEXITCODE" }

    Write-Host "Publishing single-file Race Control ($Runtime, $Configuration)..."
    & dotnet publish (Join-Path $repositoryRoot 'AssettoServer.RaceControl\AssettoServer.RaceControl.csproj') `
        -c $Configuration -r $Runtime --self-contained true -o $appStaging `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugSymbols=false `
        -p:DebugType=None
    if ($LASTEXITCODE -ne 0) { throw "Race Control publish failed with exit code $LASTEXITCODE" }

    $appExecutable = Join-Path $appStaging 'AssettoServer Race Control.exe'
    if (-not (Test-Path -LiteralPath $appExecutable -PathType Leaf)) {
        throw "Single-file Race Control executable was not produced: $appExecutable"
    }
    $unexpectedAppFiles = @(Get-ChildItem -LiteralPath $appStaging -File | Where-Object { $_.FullName -ne $appExecutable })
    if ($unexpectedAppFiles.Count -gt 0) {
        throw "Unexpected loose Race Control files were produced: $($unexpectedAppFiles.Name -join ', ')"
    }

    $libraryRoot = Join-Path $packageRoot 'lib'
    $bundledServer = Join-Path $libraryRoot 'Server'
    $languageRoot = Join-Path $packageRoot 'lang'
    $documentationRoot = Join-Path $packageRoot 'docs'
    New-Item -ItemType Directory -Path $libraryRoot, $bundledServer, $languageRoot, $documentationRoot -Force | Out-Null

    Copy-Item -LiteralPath $appExecutable -Destination $packageRoot -Force
    Copy-Item -Path (Join-Path $serverStaging '*') -Destination $bundledServer -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $repositoryRoot "AssettoServer.RaceControl.Core\bin\$Configuration\net10.0\AssettoServer.RaceControl.Core.dll") `
        -Destination $libraryRoot -Force

    $languageDirectories = @(Get-ChildItem -LiteralPath $appStaging -Directory | Where-Object {
        @(Get-ChildItem -LiteralPath $_.FullName -File -Filter '*.resources.dll').Count -gt 0
    })
    if ($languageDirectories.Count -eq 0) {
        throw 'No external localization resources were produced for the lang folder.'
    }
    $unexpectedAppDirectories = @(Get-ChildItem -LiteralPath $appStaging -Directory | Where-Object {
        $_.FullName -notin $languageDirectories.FullName
    })
    if ($unexpectedAppDirectories.Count -gt 0) {
        throw "Unexpected loose Race Control directories were produced: $($unexpectedAppDirectories.Name -join ', ')"
    }
    foreach ($languageDirectory in $languageDirectories) {
        Copy-Item -LiteralPath $languageDirectory.FullName -Destination $languageRoot -Recurse -Force
    }

    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination $packageRoot -Force
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD_PARTY_NOTICES.md') -Destination $packageRoot -Force
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\race-control.md') -Destination $documentationRoot -Force
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\race-bots.md') -Destination $documentationRoot -Force
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\fps-client-rendering.md') -Destination $documentationRoot -Force
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\fps-modern-theme.md') -Destination $documentationRoot -Force

    $runningFromOutput = @(Get-Process -Name 'AssettoServer Race Control' -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -and [IO.Path]::GetFullPath($_.Path).StartsWith($appOutput, [StringComparison]::OrdinalIgnoreCase) }
        catch { $false }
    })
    if ($runningFromOutput.Count -gt 0) {
        throw "Close Race Control from $appOutput before publishing. Running PID(s): $($runningFromOutput.Id -join ', ')"
    }

    if (Test-Path -LiteralPath $appOutput) {
        Remove-Item -LiteralPath $appOutput -Recurse -Force
    }
    Move-Item -LiteralPath $packageRoot -Destination $appOutput

    Write-Host "Portable Race Control build ready: $appOutput"
    Write-Host "Run: $(Join-Path $appOutput 'AssettoServer Race Control.exe')"
}
finally {
    if (Test-Path -LiteralPath $publishRoot) {
        Remove-Item -LiteralPath $publishRoot -Recurse -Force
    }
}
