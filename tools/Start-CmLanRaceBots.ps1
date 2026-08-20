[CmdletBinding()]
param(
    [string] $CmPresetId,
    [string] $CmServerPresetsRoot,
    [string] $AssettoCorsaRoot = 'C:\Program Files (x86)\Steam\steamapps\common\assettocorsa',
    [string] $PublishedServer,
    [string] $OutputRoot,
    [string] $PresetName = 'cm-lan-race-bots',
    [ValidateRange(2, 254)] [int] $MinimumSlots = 2,
    [ValidateRange(10, 120)] [int] $UpdateHz = 60,
    [ValidateSet('Efficient', 'Balanced', 'High')] [string] $PhysicsFidelity = 'Balanced',
    [ValidateRange(0.0, 1.0)] [double] $Difficulty = 0.75,
    [ValidateRange(0.0, 1.0)] [double] $Aggression = 0.50,
    [string] $BindAddress,
    [switch] $DisableBots,
    [switch] $VerboseLog,
    [switch] $NoLaunch,
    [Parameter(DontShow)] [switch] $SkipPhysicsPreparation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($PublishedServer)) { $PublishedServer = Join-Path $PSScriptRoot '..\out-win-x64' }
if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path $PSScriptRoot '..\.artifacts\lan-race-bots' }

$targetExecutable = [IO.Path]::GetFullPath((Join-Path $OutputRoot 'AssettoServer.exe'))
$runningTarget = Get-Process -Name AssettoServer -ErrorAction SilentlyContinue | Where-Object {
    try { [string]::Equals([IO.Path]::GetFullPath($_.Path), $targetExecutable, [StringComparison]::OrdinalIgnoreCase) }
    catch { $false }
} | Select-Object -First 1
if ($null -ne $runningTarget) {
    throw "The staged LAN server is already running (PID $($runningTarget.Id)). Close its server window before rebuilding it from Content Manager."
}

if ([string]::IsNullOrWhiteSpace($CmPresetId)) {
    $discoveryRoot = if ([string]::IsNullOrWhiteSpace($CmServerPresetsRoot)) {
        Join-Path $AssettoCorsaRoot 'server\presets'
    } else {
        $CmServerPresetsRoot
    }
    if (Test-Path -LiteralPath $discoveryRoot -PathType Container) {
        $availablePresets = @(
            Get-ChildItem -LiteralPath $discoveryRoot -Directory |
                Where-Object {
                    (Test-Path -LiteralPath (Join-Path $_.FullName 'server_cfg.ini') -PathType Leaf) -and
                    (Test-Path -LiteralPath (Join-Path $_.FullName 'entry_list.ini') -PathType Leaf)
                } |
                Sort-Object Name
        )
        if ($availablePresets.Count -gt 1) {
            Write-Host 'Choose the Content Manager server preset to run:'
            for ($i = 0; $i -lt $availablePresets.Count; $i++) {
                Write-Host "  $($i + 1). $($availablePresets[$i].Name)"
            }
            $selection = Read-Host 'Preset number'
            $selectedIndex = 0
            if (-not [int]::TryParse($selection, [ref]$selectedIndex) -or
                $selectedIndex -lt 1 -or $selectedIndex -gt $availablePresets.Count) {
                throw 'Invalid Content Manager preset selection.'
            }
            $CmPresetId = $availablePresets[$selectedIndex - 1].Name
        }
    }
}

$stageArguments = @{
    AssettoCorsaRoot = $AssettoCorsaRoot
    PublishedServer = $PublishedServer
    OutputRoot = $OutputRoot
    PresetName = $PresetName
    HumanSlots = $MinimumSlots
    BotSlots = 0
    SlotMode = $(if ($DisableBots) { 'NoBots' } else { 'AllBots' })
    UpdateHz = $UpdateHz
    PhysicsFidelity = $PhysicsFidelity
    Difficulty = $Difficulty
    Aggression = $Aggression
    PreserveCmEventSettings = $true
    Force = $true
}
if ($SkipPhysicsPreparation) { $stageArguments.SkipPhysicsPreparation = $true }
if (-not [string]::IsNullOrWhiteSpace($CmPresetId)) { $stageArguments.CmPresetId = $CmPresetId }
if (-not [string]::IsNullOrWhiteSpace($CmServerPresetsRoot)) { $stageArguments.CmServerPresetsRoot = $CmServerPresetsRoot }
if (-not [string]::IsNullOrWhiteSpace($BindAddress)) { $stageArguments.BindAddress = $BindAddress }

Write-Host 'Reading the current Content Manager server preset...'
& (Join-Path $PSScriptRoot 'Stage-CmRaceBotServer.ps1') @stageArguments

if ($NoLaunch) {
    Write-Host 'Staging complete; launch was skipped.'
    return
}

Write-Host 'Starting the LAN race-bot server. Close this window to stop it.'
$startArguments = @{
    ServerRoot = $OutputRoot
    PresetName = $PresetName
}
if ($VerboseLog) { $startArguments.VerboseLog = $true }
& (Join-Path $PSScriptRoot 'Start-LanRaceBots.ps1') @startArguments
