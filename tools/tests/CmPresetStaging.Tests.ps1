[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw "Assertion failed: $Message" }
}

function Read-IniValue([string] $Path, [string] $Section, [string] $Key) {
    $currentSection = $null
    foreach ($line in [IO.File]::ReadAllLines($Path)) {
        if ($line.Trim() -match '^\[(.+)\]$') { $currentSection = $Matches[1]; continue }
        if ($currentSection -ieq $Section -and $line -match "^\s*$([regex]::Escape($Key))\s*=(.*)$") {
            return $Matches[1].Trim()
        }
    }
    return $null
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("assettoserver-cm-stage-tests-" + [guid]::NewGuid().ToString('N'))
$stageScript = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Stage-CmRaceBotServer.ps1'))
$launcherScript = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Start-CmLanRaceBots.ps1'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    $gameRoot = Join-Path $testRoot 'assettocorsa'
    $presetsRoot = Join-Path $gameRoot 'server\presets'
    $cmPreset = Join-Path $presetsRoot 'SERVER_00'
    $publishedRoot = Join-Path $testRoot 'published'
    $outputRoot = Join-Path $testRoot 'staged'
    $carRoot = Join-Path $gameRoot 'content\cars\test_car'
    $trackRoot = Join-Path $gameRoot 'content\tracks\test_track'
    @($cmPreset, $publishedRoot, (Join-Path $carRoot 'skins\skin_0'), (Join-Path $carRoot 'skins\skin_1'),
        (Join-Path $carRoot 'skins\skin_2'), (Join-Path $trackRoot 'test_layout\ai'), (Join-Path $trackRoot 'ui\test_layout')) |
        ForEach-Object { New-Item -ItemType Directory -Path $_ -Force | Out-Null }

    Set-Content -LiteralPath (Join-Path $publishedRoot 'AssettoServer.exe') -Value 'test executable'
    Set-Content -LiteralPath (Join-Path $carRoot 'data.acd') -Value 'test checksum source'
    Set-Content -LiteralPath (Join-Path $trackRoot 'test_layout\ai\fast_lane.ai') -Value 'test closed spline'
    Set-Content -LiteralPath (Join-Path $trackRoot 'ui\test_layout\ui_track.json') -Value '{"pitboxes": 8}'
    Set-Content -LiteralPath (Join-Path $cmPreset 'server_cfg.ini') -Value @'
[SERVER]
NAME=CM dynamic test
CARS=test_car
CONFIG_TRACK=test_layout
TRACK=test_track
REGISTER_TO_LOBBY=1
MAX_CLIENTS=3
CLIENT_SEND_INTERVAL_HZ=20
FUEL_RATE=75
DAMAGE_MULTIPLIER=60
TYRE_WEAR_RATE=90

[PRACTICE]
NAME=CM practice
TIME=17
IS_OPEN=1
INFINITE=0

[QUALIFY]
NAME=CM qualifying
TIME=11
IS_OPEN=1

[RACE]
NAME=CM race
TIME=0
LAPS=5
WAIT_TIME=31
IS_OPEN=2
'@
    Set-Content -LiteralPath (Join-Path $cmPreset 'entry_list.ini') -Value @'
[CAR_0]
MODEL=test_car
SKIN=skin_0

[CAR_1]
MODEL=test_car
SKIN=skin_1

[CAR_2]
MODEL=test_car
SKIN=skin_2
'@

    & $stageScript -CmServerPresetsRoot $presetsRoot -AssettoCorsaRoot $gameRoot `
        -PublishedServer $publishedRoot -OutputRoot $outputRoot -PresetName test-dynamic `
        -HumanSlots 2 -BotSlots 0 -BindAddress 192.168.10.20 -PreserveCmEventSettings

    $stagedCfg = Join-Path $outputRoot 'presets\test-dynamic\server_cfg.ini'
    $stagedEntries = Join-Path $outputRoot 'presets\test-dynamic\entry_list.ini'
    Assert-True ((Read-IniValue $stagedCfg SERVER FUEL_RATE) -eq '75') 'CM fuel rate should be preserved'
    Assert-True ((Read-IniValue $stagedCfg QUALIFY TIME) -eq '11') 'CM qualifying session should be preserved'
    Assert-True ((Read-IniValue $stagedCfg RACE LAPS) -eq '5') 'CM race laps should be preserved'
    Assert-True ((Read-IniValue $stagedCfg RACE WAIT_TIME) -eq '31') 'CM race wait time should be preserved'
    Assert-True ((Read-IniValue $stagedCfg RACE IS_OPEN) -eq '1') 'race should be open for mid-race takeover'
    Assert-True ((Read-IniValue $stagedCfg SERVER REGISTER_TO_LOBBY) -eq '0') 'LAN staging should disable lobby registration'
    Assert-True ((Read-IniValue $stagedEntries CAR_0 AI) -eq 'none') 'first entry should be human-only'
    Assert-True ((Read-IniValue $stagedEntries CAR_1 AI) -eq 'none') 'second entry should be human-only'
    Assert-True ((Read-IniValue $stagedEntries CAR_2 AI) -eq 'auto') 'entries beyond the human allocation should become replaceable bots'

    $manifest = Get-Content -Raw -LiteralPath (Join-Path $outputRoot 'race-bot-manifest.json') | ConvertFrom-Json
    Assert-True ($manifest.sourceMode -eq 'currentCmPreset') 'manifest should identify automatic CM discovery'
    Assert-True ($manifest.sourcePresetId -eq 'SERVER_00') 'manifest should identify the selected CM preset'
    Assert-True ($manifest.advertisedSlots -eq 3) 'zero BotSlots should use all remaining CM entries'
    Assert-True ($manifest.humanSlots -eq 2) 'automatic staging should reserve two human slots'
    Assert-True ($manifest.botSlots -eq 1) 'the remaining CM entry should become a bot'
    Assert-True ($manifest.preservedCmEventSettings -eq $true) 'manifest should record preserved CM settings'

    Copy-Item -LiteralPath $cmPreset -Destination (Join-Path $presetsRoot 'SERVER_01') -Recurse
    $ambiguousMessage = $null
    try {
        & $stageScript -CmServerPresetsRoot $presetsRoot -AssettoCorsaRoot $gameRoot `
            -PublishedServer $publishedRoot -OutputRoot (Join-Path $testRoot 'ambiguous') `
            -HumanSlots 2 -BotSlots 0 -BindAddress 192.168.10.20 -PreserveCmEventSettings
    } catch {
        $ambiguousMessage = $_.Exception.Message
    }
    Assert-True ($ambiguousMessage -like '*More than one Content Manager server preset exists*') 'multiple presets should require an explicit selection'

    & $launcherScript -CmPresetId SERVER_00 -CmServerPresetsRoot $presetsRoot -AssettoCorsaRoot $gameRoot `
        -PublishedServer $publishedRoot -OutputRoot $outputRoot -PresetName test-dynamic `
        -HumanSlots 2 -BindAddress 192.168.10.20 -NoLaunch

    $compatRoot = Join-Path $testRoot 'windows-powershell-compat'
    $compatTools = Join-Path $compatRoot 'tools'
    $compatPublished = Join-Path $compatRoot 'out-win-x64'
    $singlePresetRoot = Join-Path $gameRoot 'server\single-preset'
    $singlePreset = Join-Path $singlePresetRoot 'SERVER_SINGLE'
    @($compatTools, $compatPublished, $singlePreset) |
        ForEach-Object { New-Item -ItemType Directory -Path $_ -Force | Out-Null }
    Copy-Item -LiteralPath $launcherScript -Destination $compatTools
    Copy-Item -LiteralPath $stageScript -Destination $compatTools
    Copy-Item -LiteralPath ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Start-LanRaceBots.ps1'))) -Destination $compatTools
    Set-Content -LiteralPath (Join-Path $compatPublished 'AssettoServer.exe') -Value 'test executable'
    Copy-Item -LiteralPath (Join-Path $cmPreset 'server_cfg.ini') -Destination $singlePreset
    Set-Content -LiteralPath (Join-Path $singlePreset 'entry_list.ini') -Value @'
[CAR_0]
MODEL=test_car
SKIN=skin_0
'@

    Remove-Item -LiteralPath (Join-Path $trackRoot 'test_layout\ai\fast_lane.ai')
    & (Join-Path $compatTools 'Start-CmLanRaceBots.ps1') -CmServerPresetsRoot $singlePresetRoot `
        -AssettoCorsaRoot $gameRoot -HumanSlots 2 -BindAddress 192.168.10.20 -NoLaunch

    $humanOnlyRoot = Join-Path $compatRoot '.artifacts\lan-race-bots'
    $humanOnlyPreset = Join-Path $humanOnlyRoot 'presets\cm-lan-race-bots'
    $humanOnlyEntries = Join-Path $humanOnlyPreset 'entry_list.ini'
    $humanOnlyExtra = Get-Content -Raw -LiteralPath (Join-Path $humanOnlyPreset 'extra_cfg.yml')
    $humanOnlyManifest = Get-Content -Raw -LiteralPath (Join-Path $humanOnlyRoot 'race-bot-manifest.json') | ConvertFrom-Json
    Assert-True ((Read-IniValue $humanOnlyEntries CAR_0 AI) -eq 'none') 'the original slot should remain human-only'
    Assert-True ((Read-IniValue $humanOnlyEntries CAR_1 MODEL) -eq 'test_car') 'a one-entry CM grid should be cloned to two slots'
    Assert-True ((Read-IniValue $humanOnlyEntries CAR_1 AI) -eq 'none') 'the generated second slot should be human-only'
    Assert-True ($humanOnlyExtra.Contains('EnableAi: false')) 'AI should be disabled when the CM grid has no bot entries'
    Assert-True ($humanOnlyExtra.Contains('Behavior: Traffic')) 'human-only staging should avoid race-bot session behavior'
    Assert-True ($humanOnlyExtra.Contains('AllowMidRaceBotTakeover: false')) 'takeover should be disabled without bots'
    Assert-True ($humanOnlyManifest.sourceCarSlots -eq 1) 'manifest should retain the CM source slot count'
    Assert-True ($humanOnlyManifest.autoExpandedSlots -eq 1) 'manifest should record the generated human slot'
    Assert-True ($humanOnlyManifest.humanSlots -eq 2) 'human-only staging should advertise two human slots'
    Assert-True ($humanOnlyManifest.botSlots -eq 0) 'human-only staging should not require a bot'
    Assert-True ($humanOnlyManifest.midRaceBotTakeover -eq $false) 'manifest should disable takeover without bots'

    Write-Host 'CM preset staging tests passed.'
}
finally {
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    $resolvedTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($resolvedTestRoot.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolvedTestRoot).StartsWith('assettoserver-cm-stage-tests-')) {
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
}
