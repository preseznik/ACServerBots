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
        (Join-Path $carRoot 'skins\skin_2'), (Join-Path $trackRoot 'ai'), (Join-Path $trackRoot 'ui')) |
        ForEach-Object { New-Item -ItemType Directory -Path $_ -Force | Out-Null }

    Set-Content -LiteralPath (Join-Path $publishedRoot 'AssettoServer.exe') -Value 'test executable'
    Set-Content -LiteralPath (Join-Path $carRoot 'data.acd') -Value 'test checksum source'
    Set-Content -LiteralPath (Join-Path $trackRoot 'ai\fast_lane.ai') -Value 'test closed spline'
    Set-Content -LiteralPath (Join-Path $trackRoot 'ui\ui_track.json') -Value '{"pitboxes": 8}'
    Set-Content -LiteralPath (Join-Path $cmPreset 'server_cfg.ini') -Value @'
[SERVER]
NAME=CM dynamic test
CARS=test_car
CONFIG_TRACK=
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
        -HumanSlots 1 -BotSlots 0 -BindAddress 192.168.10.20 -PreserveCmEventSettings

    $stagedCfg = Join-Path $outputRoot 'presets\test-dynamic\server_cfg.ini'
    $stagedEntries = Join-Path $outputRoot 'presets\test-dynamic\entry_list.ini'
    Assert-True ((Read-IniValue $stagedCfg SERVER FUEL_RATE) -eq '75') 'CM fuel rate should be preserved'
    Assert-True ((Read-IniValue $stagedCfg QUALIFY TIME) -eq '11') 'CM qualifying session should be preserved'
    Assert-True ((Read-IniValue $stagedCfg RACE LAPS) -eq '5') 'CM race laps should be preserved'
    Assert-True ((Read-IniValue $stagedCfg RACE WAIT_TIME) -eq '31') 'CM race wait time should be preserved'
    Assert-True ((Read-IniValue $stagedCfg RACE IS_OPEN) -eq '1') 'race should be open for mid-race takeover'
    Assert-True ((Read-IniValue $stagedCfg SERVER REGISTER_TO_LOBBY) -eq '0') 'LAN staging should disable lobby registration'
    Assert-True ((Read-IniValue $stagedEntries CAR_0 AI) -eq 'none') 'first entry should be human-only'
    Assert-True ((Read-IniValue $stagedEntries CAR_1 AI) -eq 'auto') 'remaining entries should become replaceable bots'
    Assert-True ((Read-IniValue $stagedEntries CAR_2 AI) -eq 'auto') 'all remaining entries should be included'

    $manifest = Get-Content -Raw -LiteralPath (Join-Path $outputRoot 'race-bot-manifest.json') | ConvertFrom-Json
    Assert-True ($manifest.sourceMode -eq 'currentCmPreset') 'manifest should identify automatic CM discovery'
    Assert-True ($manifest.sourcePresetId -eq 'SERVER_00') 'manifest should identify the selected CM preset'
    Assert-True ($manifest.advertisedSlots -eq 3) 'zero BotSlots should use all remaining CM entries'
    Assert-True ($manifest.preservedCmEventSettings -eq $true) 'manifest should record preserved CM settings'

    Copy-Item -LiteralPath $cmPreset -Destination (Join-Path $presetsRoot 'SERVER_01') -Recurse
    $ambiguousMessage = $null
    try {
        & $stageScript -CmServerPresetsRoot $presetsRoot -AssettoCorsaRoot $gameRoot `
            -PublishedServer $publishedRoot -OutputRoot (Join-Path $testRoot 'ambiguous') `
            -HumanSlots 1 -BotSlots 0 -BindAddress 192.168.10.20 -PreserveCmEventSettings
    } catch {
        $ambiguousMessage = $_.Exception.Message
    }
    Assert-True ($ambiguousMessage -like '*More than one Content Manager server preset exists*') 'multiple presets should require an explicit selection'

    & $launcherScript -CmPresetId SERVER_00 -CmServerPresetsRoot $presetsRoot -AssettoCorsaRoot $gameRoot `
        -PublishedServer $publishedRoot -OutputRoot $outputRoot -PresetName test-dynamic `
        -HumanSlots 1 -BindAddress 192.168.10.20 -NoLaunch

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
