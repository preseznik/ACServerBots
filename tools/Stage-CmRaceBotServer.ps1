[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $CmServerPack,
    [string] $AssettoCorsaRoot = 'C:\Program Files (x86)\Steam\steamapps\common\assettocorsa',
    [string] $PublishedServer = (Join-Path $PSScriptRoot '..\out-win-x64'),
    [string] $OutputRoot = (Join-Path $PSScriptRoot '..\.artifacts\lan-race-bots'),
    [string] $PresetName = 'magione-lan-race-bots',
    [ValidateRange(1, 254)] [int] $HumanSlots = 2,
    [ValidateRange(1, 254)] [int] $BotSlots = 6,
    [ValidateRange(10, 120)] [int] $UpdateHz = 60,
    [ValidateRange(1, 120)] [int] $PracticeMinutes = 5,
    [ValidateRange(0.0, 1.0)] [double] $Difficulty = 0.75,
    [ValidateRange(0.0, 1.0)] [double] $Aggression = 0.50,
    [string] $BindAddress,
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-IniFile([string] $Path) {
    $result = [ordered]@{}
    $section = $null
    foreach ($rawLine in [IO.File]::ReadAllLines($Path)) {
        $line = $rawLine.Trim()
        if ($line.Length -eq 0 -or $line.StartsWith(';') -or $line.StartsWith('#')) { continue }
        if ($line -match '^\[(.+)\]$') {
            $section = $Matches[1]
            if (-not $result.Contains($section)) { $result[$section] = [ordered]@{} }
            continue
        }
        if ($null -ne $section -and $line.Contains('=')) {
            $parts = $line.Split('=', 2)
            $result[$section][$parts[0].Trim()] = $parts[1].Trim()
        }
    }
    return $result
}

function Set-IniValue([string] $Path, [string] $Section, [string] $Key, [string] $Value) {
    $lines = [Collections.Generic.List[string]]::new([IO.File]::ReadAllLines($Path))
    $sectionLine = -1
    $nextSectionLine = $lines.Count
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i].Trim() -ieq "[$Section]") { $sectionLine = $i; continue }
        if ($sectionLine -ge 0 -and $i -gt $sectionLine -and $lines[$i].Trim() -match '^\[.+\]$') { $nextSectionLine = $i; break }
    }
    if ($sectionLine -lt 0) {
        $lines.Add('')
        $lines.Add("[$Section]")
        $lines.Add("$Key=$Value")
    } else {
        $updated = $false
        for ($i = $sectionLine + 1; $i -lt $nextSectionLine; $i++) {
            if ($lines[$i] -match "^\s*$([regex]::Escape($Key))\s*=") {
                $lines[$i] = "$Key=$Value"
                $updated = $true
                break
            }
        }
        if (-not $updated) { $lines.Insert($nextSectionLine, "$Key=$Value") }
    }
    [IO.File]::WriteAllLines($Path, $lines)
}

function Remove-IniSection([string] $Path, [string] $Section) {
    $source = [IO.File]::ReadAllLines($Path)
    $output = [Collections.Generic.List[string]]::new()
    $skip = $false
    foreach ($line in $source) {
        if ($line.Trim() -match '^\[(.+)\]$') { $skip = $Matches[1] -ieq $Section }
        if (-not $skip) { $output.Add($line) }
    }
    [IO.File]::WriteAllLines($Path, $output)
}

function Test-PrivateIpv4([string] $Address) {
    $parsed = $null
    if (-not [Net.IPAddress]::TryParse($Address, [ref]$parsed)) { return $false }
    $bytes = $parsed.GetAddressBytes()
    return $bytes.Length -eq 4 -and ($bytes[0] -eq 10 -or $bytes[0] -eq 127 -or
        ($bytes[0] -eq 172 -and $bytes[1] -ge 16 -and $bytes[1] -le 31) -or
        ($bytes[0] -eq 192 -and $bytes[1] -eq 168))
}

if (-not (Test-Path -LiteralPath $AssettoCorsaRoot -PathType Container)) { throw "Assetto Corsa root not found: $AssettoCorsaRoot" }
if (-not (Test-Path -LiteralPath $PublishedServer -PathType Container)) { throw "Published server not found: $PublishedServer. Run dotnet publish first." }
if (-not (Test-Path -LiteralPath (Join-Path $PublishedServer 'AssettoServer.exe') -PathType Leaf)) { throw 'Published server is missing AssettoServer.exe' }

$resolvedOutput = [IO.Path]::GetFullPath($OutputRoot)
$resolvedGame = [IO.Path]::GetFullPath($AssettoCorsaRoot)
if ($resolvedOutput -eq [IO.Path]::GetPathRoot($resolvedOutput) -or $resolvedOutput -eq $resolvedGame) { throw "Unsafe output path: $resolvedOutput" }
if (Test-Path -LiteralPath $resolvedOutput) {
    if (-not $Force) { throw "Output already exists: $resolvedOutput. Pass -Force to replace this exact staging directory." }
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}

$temporary = Join-Path ([IO.Path]::GetTempPath()) ("assettoserver-racebots-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $packRoot = $CmServerPack
    if (Test-Path -LiteralPath $CmServerPack -PathType Leaf) {
        if ([IO.Path]::GetExtension($CmServerPack) -ine '.zip') { throw 'CM server pack must be a directory or .zip file' }
        [IO.Compression.ZipFile]::ExtractToDirectory([IO.Path]::GetFullPath($CmServerPack), $temporary)
        $packRoot = $temporary
    }
    if (-not (Test-Path -LiteralPath $packRoot -PathType Container)) { throw "CM server pack not found: $CmServerPack" }

    $serverCfg = Get-ChildItem -LiteralPath $packRoot -Filter server_cfg.ini -File -Recurse | Select-Object -First 1
    $entryList = Get-ChildItem -LiteralPath $packRoot -Filter entry_list.ini -File -Recurse | Select-Object -First 1
    if ($null -eq $serverCfg -or $null -eq $entryList) { throw 'CM server pack must contain server_cfg.ini and entry_list.ini' }

    $serverIni = Read-IniFile $serverCfg.FullName
    $entryIni = Read-IniFile $entryList.FullName
    if (-not $serverIni.Contains('SERVER')) { throw 'server_cfg.ini is missing [SERVER]' }
    $track = [string]$serverIni['SERVER']['TRACK']
    $trackConfig = [string]$serverIni['SERVER']['CONFIG_TRACK']
    if ([string]::IsNullOrWhiteSpace($track)) { throw 'TRACK is empty in server_cfg.ini' }

    $slotCount = $HumanSlots + $BotSlots
    $carSections = @($entryIni.Keys | Where-Object { $_ -match '^CAR_\d+$' } | Sort-Object { [int]($_ -replace '^CAR_', '') })
    if ($carSections.Count -lt $slotCount) { throw "CM pack has $($carSections.Count) car slots; $slotCount are required" }
    $selectedSections = $carSections | Select-Object -First $slotCount

    $models = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($section in $selectedSections) {
        $model = [string]$entryIni[$section]['MODEL']
        $skin = [string]$entryIni[$section]['SKIN']
        $carRoot = Join-Path $AssettoCorsaRoot "content\cars\$model"
        if (-not (Test-Path -LiteralPath $carRoot -PathType Container)) { throw "Car is not installed: $model" }
        if (-not (Test-Path -LiteralPath (Join-Path $carRoot "skins\$skin") -PathType Container)) { throw "Skin is not installed: $model/$skin" }
        if (-not (Test-Path -LiteralPath (Join-Path $carRoot 'data.acd') -PathType Leaf)) { throw "Car has no data.acd checksum source: $model" }
        [void]$models.Add($model)
    }
    if ($models.Count -ne 1) { throw 'Race bot V1 requires one homogeneous car model across all selected slots' }

    $trackRoot = Join-Path $AssettoCorsaRoot "content\tracks\$track"
    $layoutRoot = if ([string]::IsNullOrWhiteSpace($trackConfig)) { $trackRoot } else { Join-Path $trackRoot $trackConfig }
    $fastLane = Join-Path $layoutRoot 'ai\fast_lane.ai'
    if (-not (Test-Path -LiteralPath $fastLane -PathType Leaf)) { throw "Closed-line source not found: $fastLane" }
    $uiTrack = Join-Path $layoutRoot 'ui\ui_track.json'
    if (-not (Test-Path -LiteralPath $uiTrack -PathType Leaf)) { $uiTrack = Join-Path $trackRoot 'ui\ui_track.json' }
    if (-not (Test-Path -LiteralPath $uiTrack -PathType Leaf)) { throw "Track UI metadata not found for pit capacity: $track" }
    $pitBoxes = [int]((Get-Content -Raw -LiteralPath $uiTrack | ConvertFrom-Json).pitboxes)
    if ($pitBoxes -lt $slotCount) { throw "Track exposes $pitBoxes pit boxes; $slotCount slots were requested" }

    if ([string]::IsNullOrWhiteSpace($BindAddress)) {
        $BindAddress = [Net.Dns]::GetHostAddresses([Net.Dns]::GetHostName()) |
            Where-Object { Test-PrivateIpv4 $_.ToString() -and -not $_.Equals([Net.IPAddress]::Loopback) } |
            Select-Object -First 1 |
            ForEach-Object ToString
    }
    if ([string]::IsNullOrWhiteSpace($BindAddress) -or -not (Test-PrivateIpv4 $BindAddress) -or $BindAddress -eq '127.0.0.1') {
        throw 'Could not select a private LAN IPv4 address. Pass -BindAddress explicitly.'
    }

    New-Item -ItemType Directory -Path $resolvedOutput | Out-Null
    Copy-Item -Path (Join-Path $PublishedServer '*') -Destination $resolvedOutput -Recurse -Force
    New-Item -ItemType Directory -Path (Join-Path $resolvedOutput 'plugins') -Force | Out-Null
    $presetRoot = Join-Path $resolvedOutput "presets\$PresetName"
    New-Item -ItemType Directory -Path $presetRoot -Force | Out-Null
    Copy-Item -LiteralPath $serverCfg.FullName -Destination (Join-Path $presetRoot 'server_cfg.ini')
    Copy-Item -LiteralPath $entryList.FullName -Destination (Join-Path $presetRoot 'entry_list.ini')

    $stagedServerCfg = Join-Path $presetRoot 'server_cfg.ini'
    Set-IniValue $stagedServerCfg 'SERVER' 'REGISTER_TO_LOBBY' '0'
    Set-IniValue $stagedServerCfg 'SERVER' 'MAX_CLIENTS' $slotCount
    Set-IniValue $stagedServerCfg 'SERVER' 'CLIENT_SEND_INTERVAL_HZ' $UpdateHz
    Set-IniValue $stagedServerCfg 'SERVER' 'FUEL_RATE' '0'
    Set-IniValue $stagedServerCfg 'SERVER' 'DAMAGE_MULTIPLIER' '0'
    Set-IniValue $stagedServerCfg 'SERVER' 'TYRE_WEAR_RATE' '0'
    Set-IniValue $stagedServerCfg 'SERVER' 'LOOP_MODE' '1'
    Set-IniValue $stagedServerCfg 'PRACTICE' 'INFINITE' '0'
    Set-IniValue $stagedServerCfg 'PRACTICE' 'TIME' $PracticeMinutes
    Set-IniValue $stagedServerCfg 'PRACTICE' 'IS_OPEN' '1'
    Remove-IniSection $stagedServerCfg 'QUALIFY'
    Set-IniValue $stagedServerCfg 'RACE' 'TIME' '0'
    Set-IniValue $stagedServerCfg 'RACE' 'LAPS' '3'
    Set-IniValue $stagedServerCfg 'RACE' 'WAIT_TIME' '20'
    Set-IniValue $stagedServerCfg 'RACE' 'IS_OPEN' '0'

    $stagedEntryList = Join-Path $presetRoot 'entry_list.ini'
    for ($i = 0; $i -lt $slotCount; $i++) {
        Set-IniValue $stagedEntryList "CAR_$i" 'AI' $(if ($i -lt $HumanSlots) { 'none' } else { 'fixed' })
    }

    $extraCfg = @(
        "NetworkBindAddress: $BindAddress",
        'UseSteamAuth: false',
        'EnableUPnP: false',
        'IgnoreConfigurationErrors:',
        '  MissingTrackParams: true',
        'EnableAi: true',
        'AiParams:',
        '  Behavior: Race',
        '  AutoAssignTrafficCars: false',
        '  HideAiCars: false',
        '  NamePrefix: Bot',
        '  MaxSpeedKph: 160',
        '  Race:',
        ('    Difficulty: ' + $Difficulty.ToString('0.00', [Globalization.CultureInfo]::InvariantCulture)),
        ('    Aggression: ' + $Aggression.ToString('0.00', [Globalization.CultureInfo]::InvariantCulture)),
        '    StartSplinePointId: 0',
        '    GridSpacingMeters: 9',
        "    UpdateHz: $UpdateHz"
    )
    [IO.File]::WriteAllLines((Join-Path $presetRoot 'extra_cfg.yml'), $extraCfg)
    [IO.File]::WriteAllText((Join-Path $presetRoot 'welcome.txt'), 'LAN race bots experimental server')
    $cfgRoot = Join-Path $resolvedOutput 'cfg'
    New-Item -ItemType Directory -Path $cfgRoot -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $cfgRoot 'data_track_params.ini'), '; Offline LAN staging intentionally uses the configured UTC fallback.')

    $trackAiDestination = if ([string]::IsNullOrWhiteSpace($trackConfig)) {
        Join-Path $resolvedOutput "content\tracks\$track\ai"
    } else {
        Join-Path $resolvedOutput "content\tracks\$track\$trackConfig\ai"
    }
    New-Item -ItemType Directory -Path $trackAiDestination -Force | Out-Null
    Copy-Item -LiteralPath $fastLane -Destination (Join-Path $trackAiDestination 'fast_lane.ai')

    foreach ($model in $models) {
        $carDestination = Join-Path $resolvedOutput "content\cars\$model"
        New-Item -ItemType Directory -Path $carDestination -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $AssettoCorsaRoot "content\cars\$model\data.acd") -Destination (Join-Path $carDestination 'data.acd')
    }

    $manifest = [ordered]@{
        preset = $PresetName
        bindAddress = $BindAddress
        track = $track
        trackConfig = $trackConfig
        model = @($models)[0]
        humanSlots = $HumanSlots
        botSlots = $BotSlots
        advertisedSlots = $slotCount
        pitBoxes = $pitBoxes
        sourcePack = [IO.Path]::GetFullPath($CmServerPack)
        assettoCorsaRoot = $resolvedGame
    }
    $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $resolvedOutput 'race-bot-manifest.json') -Encoding utf8
    Write-Host "Staged $slotCount slots ($HumanSlots human, $BotSlots bot) at $resolvedOutput"
    Write-Host "LAN endpoint: $BindAddress"
    Write-Host "Launch: .\AssettoServer.exe --preset $PresetName"
}
finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}
