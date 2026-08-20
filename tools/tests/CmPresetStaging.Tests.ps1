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
    $secondCarRoot = Join-Path $gameRoot 'content\cars\second_car'
    $trackRoot = Join-Path $gameRoot 'content\tracks\test_track'
    @($cmPreset, $publishedRoot, (Join-Path $carRoot 'skins\skin_0'), (Join-Path $carRoot 'skins\skin_1'),
        (Join-Path $secondCarRoot 'skins\skin_1'), (Join-Path $carRoot 'skins\skin_2'),
        (Join-Path $carRoot 'ui'), (Join-Path $secondCarRoot 'ui'),
        (Join-Path $trackRoot 'test_layout\ai'), (Join-Path $trackRoot 'ui\test_layout')) |
        ForEach-Object { New-Item -ItemType Directory -Path $_ -Force | Out-Null }

    Set-Content -LiteralPath (Join-Path $publishedRoot 'AssettoServer.exe') -Value 'test executable'
    Set-Content -LiteralPath (Join-Path $carRoot 'data.acd') -Value 'test checksum source'
    Set-Content -LiteralPath (Join-Path $secondCarRoot 'data.acd') -Value 'second checksum source'
    Set-Content -LiteralPath (Join-Path $carRoot 'ui\ui_car.json') -Value '{"specs":{"bhp":"100 bhp","weight":"1,000 kg","topspeed":"180 km/h","acceleration":"10 s"},"powerCurve":[[1000,20],[6500,100]]}'
    Set-Content -LiteralPath (Join-Path $secondCarRoot 'ui\ui_car.json') -Value @'
{"description":"metadata with a literal
newline","specs":{"bhp":"300 bhp","weight":"1400 kg","topspeed":"260 km/h","acceleration":"5.5 s"},"powerCurve":[[1000,30],[8000,300]]}
'@
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
MODEL=second_car
SKIN=skin_1

[CAR_2]
MODEL=test_car
SKIN=skin_2
'@

    & $stageScript -CmServerPresetsRoot $presetsRoot -AssettoCorsaRoot $gameRoot `
        -PublishedServer $publishedRoot -OutputRoot $outputRoot -PresetName test-dynamic `
        -HumanSlots 2 -BotSlots 0 -SlotMode ReservedHumans -BindAddress 192.168.10.20 -PreserveCmEventSettings

    $stagedCfg = Join-Path $outputRoot 'presets\test-dynamic\server_cfg.ini'
    $stagedEntries = Join-Path $outputRoot 'presets\test-dynamic\entry_list.ini'
    Assert-True ((Read-IniValue $stagedCfg SERVER FUEL_RATE) -eq '75') 'CM fuel rate should be preserved'
    Assert-True ((Read-IniValue $stagedCfg QUALIFY TIME) -eq '11') 'CM qualifying session should be preserved'
    Assert-True ((Read-IniValue $stagedCfg RACE LAPS) -eq '5') 'CM race laps should be preserved'
    Assert-True ((Read-IniValue $stagedCfg RACE WAIT_TIME) -eq '31') 'CM race wait time should be preserved'
    Assert-True ((Read-IniValue $stagedCfg RACE IS_OPEN) -eq '1') 'race should be open for mid-race takeover'
    Assert-True ((Read-IniValue $stagedCfg SERVER REGISTER_TO_LOBBY) -eq '0') 'LAN staging should disable lobby registration'
    Assert-True ((Read-IniValue $stagedCfg SERVER CARS) -eq 'second_car;test_car') 'advertised cars should include every selected model'
    Assert-True ((Read-IniValue $stagedEntries CAR_0 AI) -eq 'none') 'first entry should be human-only'
    Assert-True ((Read-IniValue $stagedEntries CAR_1 AI) -eq 'none') 'second entry should be human-only'
    Assert-True ((Read-IniValue $stagedEntries CAR_2 AI) -eq 'auto') 'entries beyond the human allocation should become replaceable bots'
    $stagedExtra = Get-Content -Raw -LiteralPath (Join-Path $outputRoot 'presets\test-dynamic\extra_cfg.yml')
    Assert-True ($stagedExtra.Contains('Model: test_car')) 'mixed grids should include the first car profile'
    Assert-True ($stagedExtra.Contains('Model: second_car')) 'mixed grids should include the second car profile'
    Assert-True ($stagedExtra.Contains('TopSpeedKph: 180')) 'the first profile should preserve its top speed'
    Assert-True ($stagedExtra.Contains('TopSpeedKph: 260')) 'the second profile should preserve its top speed'
    Assert-True ($stagedExtra.Contains('MassKg: 1000')) 'thousands separators should not be parsed as decimal mass'
    Assert-True ($stagedExtra.Contains('PowerKw: 74.57')) 'horsepower should retain decimal precision when converted to kW'
    Assert-True ($stagedExtra.Contains('ZeroToHundredSeconds: 5.5')) 'acceleration metadata should retain decimal precision'
    Assert-True ($stagedExtra.Contains('EngineMaxRpm: 8000')) 'RPM should be recovered from permissive CM metadata'

    $manifest = Get-Content -Raw -LiteralPath (Join-Path $outputRoot 'race-bot-manifest.json') | ConvertFrom-Json
    Assert-True ($manifest.sourceMode -eq 'currentCmPreset') 'manifest should identify automatic CM discovery'
    Assert-True ($manifest.sourcePresetId -eq 'SERVER_00') 'manifest should identify the selected CM preset'
    Assert-True ($manifest.advertisedSlots -eq 3) 'zero BotSlots should use all remaining CM entries'
    Assert-True ($manifest.humanSlots -eq 2) 'automatic staging should reserve two human slots'
    Assert-True ($manifest.botSlots -eq 1) 'the remaining CM entry should become a bot'
    Assert-True ($manifest.models.Count -eq 2) 'mixed car models should be retained'
    Assert-True ($manifest.vehicleProfiles.Count -eq 2) 'the manifest should include one vehicle profile per model'
    Assert-True ($manifest.preservedCmEventSettings -eq $true) 'manifest should record preserved CM settings'

    Copy-Item -LiteralPath $cmPreset -Destination (Join-Path $presetsRoot 'SERVER_01') -Recurse
    $ambiguousMessage = $null
    try {
        & $stageScript -CmServerPresetsRoot $presetsRoot -AssettoCorsaRoot $gameRoot `
            -PublishedServer $publishedRoot -OutputRoot (Join-Path $testRoot 'ambiguous') `
            -HumanSlots 2 -BotSlots 0 -SlotMode ReservedHumans -BindAddress 192.168.10.20 -PreserveCmEventSettings
    } catch {
        $ambiguousMessage = $_.Exception.Message
    }
    Assert-True ($ambiguousMessage -like '*More than one Content Manager server preset exists*') 'multiple presets should require an explicit selection'

    Set-Content -LiteralPath (Join-Path $trackRoot 'ui\test_layout\ui_track.json') -Value '{"pitboxes": 2}'
    & $launcherScript -CmPresetId SERVER_00 -CmServerPresetsRoot $presetsRoot -AssettoCorsaRoot $gameRoot `
        -PublishedServer $publishedRoot -OutputRoot $outputRoot -PresetName test-dynamic `
        -MinimumSlots 2 -BindAddress 192.168.10.20 -NoLaunch

    $trimmedCfg = Join-Path $outputRoot 'presets\test-dynamic\server_cfg.ini'
    $trimmedEntries = Join-Path $outputRoot 'presets\test-dynamic\entry_list.ini'
    $trimmedManifest = Get-Content -Raw -LiteralPath (Join-Path $outputRoot 'race-bot-manifest.json') | ConvertFrom-Json
    Assert-True ((Read-IniValue $trimmedCfg SERVER MAX_CLIENTS) -eq '2') 'advertised slots should be reduced to pit capacity'
    Assert-True ((Read-IniValue $trimmedEntries CAR_0 AI) -eq 'auto') 'the first fitting entry should remain a bot'
    Assert-True ((Read-IniValue $trimmedEntries CAR_1 AI) -eq 'auto') 'the second fitting entry should remain a bot'
    Assert-True ($null -eq (Read-IniValue $trimmedEntries CAR_2 MODEL)) 'the last non-fitting entry should be removed'
    Assert-True ($trimmedManifest.requestedSlots -eq 3) 'manifest should retain the requested slot count'
    Assert-True ($trimmedManifest.trimmedSlots -eq 1) 'manifest should record capacity trimming'
    Assert-True ($trimmedManifest.trimmedEntrySections[0] -eq 'CAR_2') 'manifest should identify the trailing section removed'
    Assert-True ($trimmedManifest.advertisedSlots -eq 2) 'manifest should advertise only fitting slots'
    Assert-True ($trimmedManifest.botSlots -eq 2) 'all fitting slots should remain replaceable bots'

    $oversizedOutput = Join-Path $testRoot 'oversized-request'
    & $stageScript -CmPresetId SERVER_00 -CmServerPresetsRoot $presetsRoot -AssettoCorsaRoot $gameRoot `
        -PublishedServer $publishedRoot -OutputRoot $oversizedOutput -PresetName oversized-request `
        -HumanSlots 2 -BotSlots 6 -SlotMode AllBots -BindAddress 192.168.10.20 -PreserveCmEventSettings
    $oversizedManifest = Get-Content -Raw -LiteralPath (Join-Path $oversizedOutput 'race-bot-manifest.json') | ConvertFrom-Json
    Assert-True ($oversizedManifest.requestedSlots -eq 8) 'explicit oversized requests should retain their requested count'
    Assert-True ($oversizedManifest.advertisedSlots -eq 2) 'pit capacity should be applied before rejecting a short source roster'
    Assert-True ($oversizedManifest.trimmedSlots -eq 6) 'all requested slots beyond pit capacity should be reported as trimmed'

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

    & (Join-Path $compatTools 'Start-CmLanRaceBots.ps1') -CmServerPresetsRoot $singlePresetRoot `
        -AssettoCorsaRoot $gameRoot -MinimumSlots 2 -BindAddress 192.168.10.20 -NoLaunch

    $automaticRoot = Join-Path $compatRoot '.artifacts\lan-race-bots'
    $automaticPreset = Join-Path $automaticRoot 'presets\cm-lan-race-bots'
    $automaticEntries = Join-Path $automaticPreset 'entry_list.ini'
    $automaticExtra = Get-Content -Raw -LiteralPath (Join-Path $automaticPreset 'extra_cfg.yml')
    $automaticManifest = Get-Content -Raw -LiteralPath (Join-Path $automaticRoot 'race-bot-manifest.json') | ConvertFrom-Json
    Assert-True ((Read-IniValue $automaticEntries CAR_0 AI) -eq 'auto') 'the original slot should be replaceable by default'
    Assert-True ((Read-IniValue $automaticEntries CAR_1 MODEL) -eq 'test_car') 'a one-entry CM grid should be cloned to two slots'
    Assert-True ((Read-IniValue $automaticEntries CAR_1 AI) -eq 'auto') 'the generated second slot should be a replaceable bot by default'
    Assert-True ($automaticExtra.Contains('EnableAi: true')) 'AI should be enabled by default'
    Assert-True ($automaticExtra.Contains('Behavior: Race')) 'the default launcher should use race-bot behavior'
    Assert-True ($automaticExtra.Contains('AllowMidRaceBotTakeover: true')) 'takeover should be enabled by default'
    Assert-True ($automaticManifest.slotMode -eq 'AllBots') 'manifest should record all-bot slot mode'
    Assert-True ($automaticManifest.humanSlots -eq 0) 'all slots should begin under bot control'
    Assert-True ($automaticManifest.botSlots -eq 2) 'the two-slot minimum should contain two bots'
    Assert-True ($automaticManifest.trimmedSlots -eq 0) 'a roster that fits should not report trimming'

    Remove-Item -LiteralPath (Join-Path $trackRoot 'test_layout\ai\fast_lane.ai')
    & (Join-Path $compatTools 'Start-CmLanRaceBots.ps1') -CmServerPresetsRoot $singlePresetRoot `
        -AssettoCorsaRoot $gameRoot -MinimumSlots 2 -BindAddress 192.168.10.20 -DisableBots -NoLaunch

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
    Assert-True ($humanOnlyManifest.slotMode -eq 'NoBots') 'manifest should record disabled-bot slot mode'
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
