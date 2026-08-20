[CmdletBinding()]
param(
    [string] $CmServerPack,
    [string] $CmPresetId,
    [string] $CmServerPresetsRoot,
    [string] $AssettoCorsaRoot = 'C:\Program Files (x86)\Steam\steamapps\common\assettocorsa',
    [string] $PublishedServer,
    [string] $OutputRoot,
    [string] $PresetName = 'magione-lan-race-bots',
    [ValidateRange(1, 254)] [int] $HumanSlots = 2,
    [ValidateRange(0, 254)] [int] $BotSlots = 6,
    [ValidateSet('ReservedHumans', 'AllBots', 'NoBots')] [string] $SlotMode = 'AllBots',
    [ValidateRange(10, 120)] [int] $UpdateHz = 60,
    [ValidateSet('Efficient', 'Balanced', 'High')] [string] $PhysicsFidelity = 'Balanced',
    [ValidateRange(1, 120)] [int] $PracticeMinutes = 5,
    [ValidateRange(0.0, 1.0)] [double] $Difficulty = 0.75,
    [ValidateRange(0.0, 1.0)] [double] $Aggression = 0.50,
    [string] $BindAddress,
    [switch] $PreserveCmEventSettings,
    [switch] $Force,
    [Parameter(DontShow)] [switch] $SkipPhysicsPreparation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($PublishedServer)) { $PublishedServer = Join-Path $PSScriptRoot '..\out-win-x64' }
if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path $PSScriptRoot '..\.artifacts\lan-race-bots' }

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

function Add-IniCarClone([string] $Path, [string] $SourceSection, [string] $DestinationSection) {
    $ini = Read-IniFile $Path
    if (-not $ini.Contains($SourceSection)) { throw "Cannot clone missing entry-list section: $SourceSection" }
    if ($ini.Contains($DestinationSection)) { throw "Cannot overwrite existing entry-list section: $DestinationSection" }
    foreach ($key in $ini[$SourceSection].Keys) {
        $value = [string]$ini[$SourceSection][$key]
        if ($key -ieq 'GUID' -or $key -ieq 'DRIVERNAME' -or $key -ieq 'TEAM') { $value = '' }
        if ($key -ieq 'AI') { $value = 'none' }
        Set-IniValue $Path $DestinationSection $key $value
    }
    Set-IniValue $Path $DestinationSection 'AI' 'none'
}

function Get-FirstNumber([object] $Value, [double] $Fallback) {
    if ($null -eq $Value) { return $Fallback }
    $match = [regex]::Match(([string]$Value), '[-+]?\d+(?:[\.,]\d+)?')
    if (-not $match.Success) { return $Fallback }
    $numberText = $match.Value
    if ($numberText -match '^[-+]?\d{1,3},\d{3}$') {
        $numberText = $numberText.Replace(',', '')
    } else {
        $numberText = $numberText.Replace(',', '.')
    }
    $parsed = 0.0
    if (-not [double]::TryParse($numberText, [Globalization.NumberStyles]::Float,
        [Globalization.CultureInfo]::InvariantCulture, [ref]$parsed)) { return $Fallback }
    return $parsed
}

function Get-JsonPropertyText([string] $Json, [string] $Property) {
    $pattern = '"' + [regex]::Escape($Property) + '"\s*:\s*(?:"([^"]*)"|([^,\r\n\}]+))'
    $match = [regex]::Match($Json, $pattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $match.Success) { return $null }
    return $(if ($match.Groups[1].Success) { $match.Groups[1].Value } else { $match.Groups[2].Value.Trim() })
}

function Get-RaceBotVehicleProfile([string] $Model, [string] $CarRoot) {
    $source = 'fallback'
    $massKg = 1200.0
    $powerKw = 110.0
    $topSpeedKph = 200.0
    $zeroToHundredSeconds = 8.0
    $engineMaxRpm = 7000
    $uiCarPath = Join-Path $CarRoot 'ui\ui_car.json'

    if (Test-Path -LiteralPath $uiCarPath -PathType Leaf) {
        $rawMetadata = Get-Content -Raw -LiteralPath $uiCarPath
        try {
            $metadata = $rawMetadata | ConvertFrom-Json
            $source = 'ui_car.json'
            if ($null -ne $metadata.specs) {
                $weightText = [string]$metadata.specs.weight
                $massKg = Get-FirstNumber $weightText $massKg
                if ($weightText -match '(?i)\blb') { $massKg *= 0.45359237 }

                $powerText = [string]$metadata.specs.bhp
                $powerValue = Get-FirstNumber $powerText ($powerKw / 0.745699872)
                $powerKw = if ($powerText -match '(?i)\bkw\b') { $powerValue } else { $powerValue * 0.745699872 }

                $topSpeedText = [string]$metadata.specs.topspeed
                $topSpeedKph = Get-FirstNumber $topSpeedText $topSpeedKph
                if ($topSpeedText -match '(?i)\bmph\b') { $topSpeedKph *= 1.609344 }

                $zeroToHundredSeconds = Get-FirstNumber $metadata.specs.acceleration 0
            }

            $curveRpms = @(
                @($metadata.powerCurve) + @($metadata.torqueCurve) |
                    Where-Object { $null -ne $_ -and $_.Count -ge 2 } |
                    ForEach-Object { Get-FirstNumber $_[0] 0 } |
                    Where-Object { $_ -ge 2000 -and $_ -le 25000 }
            )
            if ($curveRpms.Count -gt 0) { $engineMaxRpm = [int](($curveRpms | Measure-Object -Maximum).Maximum) }
        } catch {
            # Some original and mod cars contain literal newlines in descriptions. CM accepts them,
            # but strict JSON parsers do not, so recover the simple specs without rewriting the car.
            $source = 'ui_car.json'
            $weightText = Get-JsonPropertyText $rawMetadata 'weight'
            $massKg = Get-FirstNumber $weightText $massKg
            if ($weightText -match '(?i)\blb') { $massKg *= 0.45359237 }
            $powerText = Get-JsonPropertyText $rawMetadata 'bhp'
            $powerValue = Get-FirstNumber $powerText ($powerKw / 0.745699872)
            $powerKw = if ($powerText -match '(?i)\bkw\b') { $powerValue } else { $powerValue * 0.745699872 }
            $topSpeedText = Get-JsonPropertyText $rawMetadata 'topspeed'
            $topSpeedKph = Get-FirstNumber $topSpeedText $topSpeedKph
            if ($topSpeedText -match '(?i)\bmph\b') { $topSpeedKph *= 1.609344 }
            $zeroToHundredSeconds = Get-FirstNumber (Get-JsonPropertyText $rawMetadata 'acceleration') 0
            $curveRpms = @(
                [regex]::Matches($rawMetadata, '\[\s*"?(\d{3,5}(?:\.\d+)?)"?\s*,\s*"?[-+]?\d') |
                    ForEach-Object { Get-FirstNumber $_.Groups[1].Value 0 } |
                    Where-Object { $_ -ge 2000 -and $_ -le 25000 }
            )
            if ($curveRpms.Count -gt 0) { $engineMaxRpm = [int](($curveRpms | Measure-Object -Maximum).Maximum) }
        }
    } else {
        Write-Warning "Car UI metadata is missing; using a bounded fallback profile for $Model"
    }

    $massKg = [Math]::Min(5000.0, [Math]::Max(300.0, $massKg))
    $powerKw = [Math]::Min(2000.0, [Math]::Max(5.0, $powerKw))
    $topSpeedKph = [Math]::Min(600.0, [Math]::Max(40.0, $topSpeedKph))
    if ($zeroToHundredSeconds -le 0) {
        $zeroToHundredSeconds = 8 * ($massKg / 1200) * [Math]::Sqrt(110 / $powerKw)
    }
    $zeroToHundredSeconds = [Math]::Min(60.0, [Math]::Max(1.5, $zeroToHundredSeconds))
    $engineMaxRpm = [Math]::Min(25000, [Math]::Max(2000, $engineMaxRpm))

    return [pscustomobject][ordered]@{
        Model = $Model
        Source = $source
        MassKg = $massKg
        PowerKw = $powerKw
        TopSpeedKph = $topSpeedKph
        ZeroToHundredSeconds = $zeroToHundredSeconds
        MaxBrakeDeceleration = 8.5
        LateralGripG = 1.0
        TyreDiameterMeters = 0.65
        EngineIdleRpm = 900
        EngineMaxRpm = $engineMaxRpm
        GearCount = 6
    }
}

function Format-Invariant([double] $Value) {
    return $Value.ToString('0.###', [Globalization.CultureInfo]::InvariantCulture)
}

function Test-PrivateIpv4([string] $Address) {
    $parsed = $null
    if (-not [Net.IPAddress]::TryParse($Address, [ref]$parsed)) { return $false }
    $bytes = $parsed.GetAddressBytes()
    return $bytes.Length -eq 4 -and ($bytes[0] -eq 10 -or $bytes[0] -eq 127 -or
        ($bytes[0] -eq 172 -and $bytes[1] -ge 16 -and $bytes[1] -le 31) -or
        ($bytes[0] -eq 192 -and $bytes[1] -eq 168))
}

function Get-PreferredPrivateIpv4 {
    foreach ($networkInterface in [Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces()) {
        if ($networkInterface.OperationalStatus -ne [Net.NetworkInformation.OperationalStatus]::Up) { continue }
        if ($networkInterface.NetworkInterfaceType -eq [Net.NetworkInformation.NetworkInterfaceType]::Loopback) { continue }
        $properties = $networkInterface.GetIPProperties()
        $hasIpv4Gateway = @($properties.GatewayAddresses | Where-Object {
            $_.Address.AddressFamily -eq [Net.Sockets.AddressFamily]::InterNetwork -and
            $_.Address.ToString() -ne '0.0.0.0'
        }).Count -gt 0
        if (-not $hasIpv4Gateway) { continue }
        foreach ($unicast in $properties.UnicastAddresses) {
            $candidate = $unicast.Address.ToString()
            if ($unicast.Address.AddressFamily -eq [Net.Sockets.AddressFamily]::InterNetwork -and
                (Test-PrivateIpv4 $candidate) -and $candidate -ne '127.0.0.1') {
                return $candidate
            }
        }
    }

    return [Net.Dns]::GetHostAddresses([Net.Dns]::GetHostName()) |
        Where-Object { Test-PrivateIpv4 $_.ToString() -and -not $_.Equals([Net.IPAddress]::Loopback) } |
        Sort-Object { $bytes = $_.GetAddressBytes(); if ($bytes[0] -eq 192) { 0 } elseif ($bytes[0] -eq 172) { 1 } else { 2 } } |
        Select-Object -First 1 |
        ForEach-Object ToString
}

function Get-PresetFilePair([string] $Root) {
    $candidates = @(
        Get-ChildItem -LiteralPath $Root -Filter server_cfg.ini -File -Recurse |
            Where-Object { Test-Path -LiteralPath (Join-Path $_.DirectoryName 'entry_list.ini') -PathType Leaf }
    )
    if ($candidates.Count -eq 0) { throw "No server_cfg.ini and entry_list.ini pair was found under: $Root" }
    if ($candidates.Count -gt 1) { throw "More than one server preset was found under: $Root" }
    return [pscustomobject]@{
        ServerCfg = $candidates[0].FullName
        EntryList = Join-Path $candidates[0].DirectoryName 'entry_list.ini'
    }
}

function Get-FileSignature([string] $Path) {
    $item = Get-Item -LiteralPath $Path
    return "$($item.Length)|$($item.LastWriteTimeUtc.Ticks)|$((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash)"
}

function Copy-StablePresetFiles([string] $ServerCfg, [string] $EntryList, [string] $Destination) {
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    for ($attempt = 0; $attempt -lt 10; $attempt++) {
        try {
            $before = (Get-FileSignature $ServerCfg) + '|' + (Get-FileSignature $EntryList)
            Start-Sleep -Milliseconds 300
            $settled = (Get-FileSignature $ServerCfg) + '|' + (Get-FileSignature $EntryList)
            if ($before -ne $settled) { continue }

            Copy-Item -LiteralPath $ServerCfg -Destination (Join-Path $Destination 'server_cfg.ini') -Force
            Copy-Item -LiteralPath $EntryList -Destination (Join-Path $Destination 'entry_list.ini') -Force
            $after = (Get-FileSignature $ServerCfg) + '|' + (Get-FileSignature $EntryList)
            if ($settled -eq $after) {
                return [pscustomobject]@{
                    ServerCfg = Join-Path $Destination 'server_cfg.ini'
                    EntryList = Join-Path $Destination 'entry_list.ini'
                }
            }
        } catch [IO.IOException] {
            # Content Manager can briefly hold either file while flushing a preset.
        }
        Start-Sleep -Milliseconds 200
    }
    throw 'Content Manager preset did not settle. Finish editing it and try again.'
}

if (-not (Test-Path -LiteralPath $AssettoCorsaRoot -PathType Container)) { throw "Assetto Corsa root not found: $AssettoCorsaRoot" }
if (-not (Test-Path -LiteralPath $PublishedServer -PathType Container)) { throw "Published server not found: $PublishedServer. Run dotnet publish first." }
if (-not (Test-Path -LiteralPath (Join-Path $PublishedServer 'AssettoServer.exe') -PathType Leaf)) { throw 'Published server is missing AssettoServer.exe' }

if (-not [string]::IsNullOrWhiteSpace($CmServerPack) -and -not [string]::IsNullOrWhiteSpace($CmPresetId)) {
    throw 'Use either -CmServerPack or -CmPresetId, not both.'
}

$sourceMode = 'serverPack'
$sourcePresetId = $null
$sourcePath = $CmServerPack
if ([string]::IsNullOrWhiteSpace($CmServerPack)) {
    $sourceMode = 'currentCmPreset'
    if ([string]::IsNullOrWhiteSpace($CmServerPresetsRoot)) {
        $CmServerPresetsRoot = Join-Path $AssettoCorsaRoot 'server\presets'
    }
    if (-not (Test-Path -LiteralPath $CmServerPresetsRoot -PathType Container)) {
        throw "Content Manager server preset directory not found: $CmServerPresetsRoot"
    }

    $presetDirectories = @(
        Get-ChildItem -LiteralPath $CmServerPresetsRoot -Directory |
            Where-Object {
                (Test-Path -LiteralPath (Join-Path $_.FullName 'server_cfg.ini') -PathType Leaf) -and
                (Test-Path -LiteralPath (Join-Path $_.FullName 'entry_list.ini') -PathType Leaf)
            } |
            Sort-Object Name
    )
    if (-not [string]::IsNullOrWhiteSpace($CmPresetId)) {
        if ([IO.Path]::GetFileName($CmPresetId) -ne $CmPresetId) { throw 'CmPresetId must be a preset directory name, not a path.' }
        $presetDirectories = @($presetDirectories | Where-Object Name -CEQ $CmPresetId)
        if ($presetDirectories.Count -eq 0) { throw "Content Manager server preset not found: $CmPresetId" }
    } elseif ($presetDirectories.Count -gt 1) {
        $available = ($presetDirectories | ForEach-Object Name) -join ', '
        throw "More than one Content Manager server preset exists ($available). Pass -CmPresetId with the one to use."
    }
    if ($presetDirectories.Count -eq 0) { throw "No Content Manager server presets were found in: $CmServerPresetsRoot" }
    $sourcePath = $presetDirectories[0].FullName
    $sourcePresetId = $presetDirectories[0].Name
}

$resolvedOutput = [IO.Path]::GetFullPath($OutputRoot)
$resolvedGame = [IO.Path]::GetFullPath($AssettoCorsaRoot)
if ($resolvedOutput -eq [IO.Path]::GetPathRoot($resolvedOutput) -or $resolvedOutput -eq $resolvedGame) { throw "Unsafe output path: $resolvedOutput" }

$temporary = Join-Path ([IO.Path]::GetTempPath()) ("assettoserver-racebots-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $packRoot = $sourcePath
    if (Test-Path -LiteralPath $sourcePath -PathType Leaf) {
        if ([IO.Path]::GetExtension($sourcePath) -ine '.zip') { throw 'CM server pack must be a directory or .zip file' }
        $expandedRoot = Join-Path $temporary 'expanded'
        New-Item -ItemType Directory -Path $expandedRoot | Out-Null
        [IO.Compression.ZipFile]::ExtractToDirectory([IO.Path]::GetFullPath($sourcePath), $expandedRoot)
        $packRoot = $expandedRoot
    }
    if (-not (Test-Path -LiteralPath $packRoot -PathType Container)) { throw "CM server pack not found: $sourcePath" }

    $sourceFiles = Get-PresetFilePair $packRoot
    $snapshot = Copy-StablePresetFiles $sourceFiles.ServerCfg $sourceFiles.EntryList (Join-Path $temporary 'snapshot')

    $serverIni = Read-IniFile $snapshot.ServerCfg
    $entryIni = Read-IniFile $snapshot.EntryList
    if (-not $serverIni.Contains('SERVER')) { throw 'server_cfg.ini is missing [SERVER]' }
    $track = [string]$serverIni['SERVER']['TRACK']
    $trackConfig = [string]$serverIni['SERVER']['CONFIG_TRACK']
    if ([string]::IsNullOrWhiteSpace($track)) { throw 'TRACK is empty in server_cfg.ini' }

    $trackRoot = Join-Path $AssettoCorsaRoot "content\tracks\$track"
    $layoutRoot = if ([string]::IsNullOrWhiteSpace($trackConfig)) { $trackRoot } else { Join-Path $trackRoot $trackConfig }
    $fastLane = Join-Path $layoutRoot 'ai\fast_lane.ai'
    $uiTrack = if ([string]::IsNullOrWhiteSpace($trackConfig)) {
        Join-Path $trackRoot 'ui\ui_track.json'
    } else {
        Join-Path $trackRoot "ui\$trackConfig\ui_track.json"
    }
    if (-not (Test-Path -LiteralPath $uiTrack -PathType Leaf)) { $uiTrack = Join-Path $layoutRoot 'ui\ui_track.json' }
    if (-not (Test-Path -LiteralPath $uiTrack -PathType Leaf)) { $uiTrack = Join-Path $trackRoot 'ui\ui_track.json' }
    if (-not (Test-Path -LiteralPath $uiTrack -PathType Leaf)) { throw "Track UI metadata not found for pit capacity: $track" }
    $pitBoxes = [int]((Get-Content -Raw -LiteralPath $uiTrack | ConvertFrom-Json).pitboxes)
    if ($pitBoxes -lt 1) { throw "Track exposes no usable pit boxes: $track" }

    $carSections = @($entryIni.Keys | Where-Object { $_ -match '^CAR_\d+$' } | Sort-Object { [int]($_ -replace '^CAR_', '') })
    $sourceCarSlotCount = $carSections.Count
    if ($sourceCarSlotCount -eq 0) { throw 'CM preset has no car entries to stage' }

    $effectiveHumanSlots = $HumanSlots
    $effectiveBotSlots = $BotSlots
    if ($BotSlots -eq 0) {
        $effectiveHumanSlots = [Math]::Max(2, $HumanSlots)
        while ($carSections.Count -lt $effectiveHumanSlots) {
            $sourceSection = $carSections[$carSections.Count % $sourceCarSlotCount]
            Add-IniCarClone $snapshot.EntryList $sourceSection "CAR_$($carSections.Count)"
            $entryIni = Read-IniFile $snapshot.EntryList
            $carSections = @($entryIni.Keys | Where-Object { $_ -match '^CAR_\d+$' } | Sort-Object { [int]($_ -replace '^CAR_', '') })
        }
        $effectiveBotSlots = [Math]::Max(0, $carSections.Count - $effectiveHumanSlots)
    }
    $slotCount = $effectiveHumanSlots + $effectiveBotSlots
    if ($slotCount -gt 254) { throw 'The combined human and bot slot count cannot exceed 254' }
    $requestedSlotCount = $slotCount
    if ($slotCount -gt $pitBoxes) { $slotCount = $pitBoxes }
    if ($carSections.Count -lt $slotCount) { throw "CM pack has $($carSections.Count) car slots; $slotCount are required" }
    if ($SlotMode -eq 'AllBots') {
        $effectiveHumanSlots = 0
        $effectiveBotSlots = $slotCount
    } elseif ($SlotMode -eq 'NoBots') {
        $effectiveHumanSlots = $slotCount
        $effectiveBotSlots = 0
    }
    $effectiveHumanSlots = [Math]::Min($effectiveHumanSlots, $slotCount)
    $effectiveBotSlots = $slotCount - $effectiveHumanSlots
    $trimmedSections = @()
    if ($requestedSlotCount -gt $slotCount) {
        $trimmedSections = @($carSections | Select-Object -Skip $slotCount)
        foreach ($section in $trimmedSections) {
            Remove-IniSection $snapshot.EntryList $section
        }
        $entryIni = Read-IniFile $snapshot.EntryList
        $carSections = @($entryIni.Keys | Where-Object { $_ -match '^CAR_\d+$' } | Sort-Object { [int]($_ -replace '^CAR_', '') })
        Write-Warning "Track exposes $pitBoxes pit boxes; reduced the staged roster from $requestedSlotCount to $slotCount slots"
    }
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
    $hasBots = $effectiveBotSlots -gt 0
    $vehicleProfiles = @()
    if ($hasBots) {
        $vehicleProfiles = @($models | Sort-Object | ForEach-Object {
            Get-RaceBotVehicleProfile $_ (Join-Path $AssettoCorsaRoot "content\cars\$_")
        })
    }

    if ($hasBots -and -not (Test-Path -LiteralPath $fastLane -PathType Leaf)) { throw "Closed-line source not found: $fastLane" }

    if ([string]::IsNullOrWhiteSpace($BindAddress)) {
        $BindAddress = Get-PreferredPrivateIpv4
    }
    if ([string]::IsNullOrWhiteSpace($BindAddress) -or -not (Test-PrivateIpv4 $BindAddress) -or $BindAddress -eq '127.0.0.1') {
        throw 'Could not select a private LAN IPv4 address. Pass -BindAddress explicitly.'
    }

    if ($PreserveCmEventSettings -and -not $serverIni.Contains('RACE')) {
        throw 'The current Content Manager preset has no race session. Add a race session in CM and try again.'
    }

    if (Test-Path -LiteralPath $resolvedOutput) {
        if (-not $Force) { throw "Output already exists: $resolvedOutput. Pass -Force to replace this exact staging directory." }
        Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
    }
    New-Item -ItemType Directory -Path $resolvedOutput | Out-Null
    Copy-Item -Path (Join-Path $PublishedServer '*') -Destination $resolvedOutput -Recurse -Force
    New-Item -ItemType Directory -Path (Join-Path $resolvedOutput 'plugins') -Force | Out-Null
    $presetRoot = Join-Path $resolvedOutput "presets\$PresetName"
    New-Item -ItemType Directory -Path $presetRoot -Force | Out-Null
    Copy-Item -LiteralPath $snapshot.ServerCfg -Destination (Join-Path $presetRoot 'server_cfg.ini')
    Copy-Item -LiteralPath $snapshot.EntryList -Destination (Join-Path $presetRoot 'entry_list.ini')

    $stagedServerCfg = Join-Path $presetRoot 'server_cfg.ini'
    Set-IniValue $stagedServerCfg 'SERVER' 'REGISTER_TO_LOBBY' '0'
    Set-IniValue $stagedServerCfg 'SERVER' 'MAX_CLIENTS' $slotCount
    Set-IniValue $stagedServerCfg 'SERVER' 'CARS' ((@($models | Sort-Object)) -join ';')
    Set-IniValue $stagedServerCfg 'SERVER' 'CLIENT_SEND_INTERVAL_HZ' $UpdateHz
    Set-IniValue $stagedServerCfg 'RACE' 'IS_OPEN' '1'
    if (-not $PreserveCmEventSettings) {
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
    }

    $stagedEntryList = Join-Path $presetRoot 'entry_list.ini'
    for ($i = 0; $i -lt $slotCount; $i++) {
        Set-IniValue $stagedEntryList "CAR_$i" 'AI' $(if ($i -lt $effectiveHumanSlots) { 'none' } else { 'auto' })
    }

    $hasBotsText = $hasBots.ToString().ToLowerInvariant()
    $aiBehavior = if ($hasBots) { 'Race' } else { 'Traffic' }
    $extraCfg = @(
        "NetworkBindAddress: $BindAddress",
        'UseSteamAuth: false',
        'EnableUPnP: false',
        'IgnoreConfigurationErrors:',
        '  MissingTrackParams: true',
        "EnableAi: $hasBotsText",
        'AiParams:',
        "  Behavior: $aiBehavior",
        '  AutoAssignTrafficCars: false',
        '  HideAiCars: false',
        '  NamePrefix: Bot',
        '  MaxSpeedKph: 160',
        '  Race:',
        ('    Difficulty: ' + $Difficulty.ToString('0.00', [Globalization.CultureInfo]::InvariantCulture)),
        ('    Aggression: ' + $Aggression.ToString('0.00', [Globalization.CultureInfo]::InvariantCulture)),
        '    StartSplinePointId: 0',
        "    UpdateHz: $UpdateHz",
        '    Physics:',
        "      Fidelity: $PhysicsFidelity",
        '      AssetFile: race-physics.bin',
        '      Friction: 1.15',
        "    AllowMidRaceBotTakeover: $hasBotsText",
        "    RestartSessionOnFirstHumanConnect: $hasBotsText"
    )
    if ($hasBots) {
        $extraCfg += '    VehicleProfiles:'
        foreach ($profile in $vehicleProfiles) {
            $extraCfg += "      - Model: $($profile.Model)"
            $extraCfg += "        Source: $($profile.Source)"
            $extraCfg += "        MassKg: $(Format-Invariant $profile.MassKg)"
            $extraCfg += "        PowerKw: $(Format-Invariant $profile.PowerKw)"
            $extraCfg += "        TopSpeedKph: $(Format-Invariant $profile.TopSpeedKph)"
            $extraCfg += "        ZeroToHundredSeconds: $(Format-Invariant $profile.ZeroToHundredSeconds)"
            $extraCfg += "        MaxBrakeDeceleration: $(Format-Invariant $profile.MaxBrakeDeceleration)"
            $extraCfg += "        LateralGripG: $(Format-Invariant $profile.LateralGripG)"
            $extraCfg += "        TyreDiameterMeters: $(Format-Invariant $profile.TyreDiameterMeters)"
            $extraCfg += "        EngineIdleRpm: $($profile.EngineIdleRpm)"
            $extraCfg += "        EngineMaxRpm: $($profile.EngineMaxRpm)"
            $extraCfg += "        GearCount: $($profile.GearCount)"
        }
    }
    [IO.File]::WriteAllLines((Join-Path $presetRoot 'extra_cfg.yml'), $extraCfg)
    [IO.File]::WriteAllText((Join-Path $presetRoot 'welcome.txt'), 'LAN race bots experimental server')
    $cfgRoot = Join-Path $resolvedOutput 'cfg'
    New-Item -ItemType Directory -Path $cfgRoot -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $cfgRoot 'data_track_params.ini'), '; Offline LAN staging intentionally uses the configured UTC fallback.')

    if ($hasBots) {
        $trackAiDestination = if ([string]::IsNullOrWhiteSpace($trackConfig)) {
            Join-Path $resolvedOutput "content\tracks\$track\ai"
        } else {
            Join-Path $resolvedOutput "content\tracks\$track\$trackConfig\ai"
        }
        New-Item -ItemType Directory -Path $trackAiDestination -Force | Out-Null
        Copy-Item -LiteralPath $fastLane -Destination (Join-Path $trackAiDestination 'fast_lane.ai')
    }

    foreach ($model in $models) {
        $carDestination = Join-Path $resolvedOutput "content\cars\$model"
        New-Item -ItemType Directory -Path $carDestination -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $AssettoCorsaRoot "content\cars\$model\data.acd") -Destination (Join-Path $carDestination 'data.acd')
    }

    if ($hasBots -and -not $SkipPhysicsPreparation) {
        $physicsOutput = Join-Path $presetRoot 'race-physics.bin'
        $physicsArguments = @(
            '--prepare-race-physics',
            '--ac-root', $resolvedGame,
            '--track', $track,
            '--cars', ((@($models | Sort-Object)) -join ';'),
            '--physics-output', $physicsOutput
        )
        if (-not [string]::IsNullOrWhiteSpace($trackConfig)) {
            $physicsArguments += @('--track-config', $trackConfig)
        }
        Write-Host 'Preparing exact track, grid and car collision geometry...'
        & (Join-Path $resolvedOutput 'AssettoServer.exe') @physicsArguments
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $physicsOutput -PathType Leaf)) {
            throw "Rigid-body physics preparation failed with exit code $LASTEXITCODE"
        }
    }

    $manifest = [ordered]@{
        preset = $PresetName
        bindAddress = $BindAddress
        track = $track
        trackConfig = $trackConfig
        model = @($models)[0]
        models = @($models | Sort-Object)
        vehicleProfiles = @($vehicleProfiles)
        physicsFidelity = $PhysicsFidelity
        physicsPrepared = [bool]($hasBots -and -not $SkipPhysicsPreparation)
        sourceCarSlots = $sourceCarSlotCount
        requestedSlots = $requestedSlotCount
        trimmedSlots = $requestedSlotCount - $slotCount
        trimmedEntrySections = @($trimmedSections)
        autoExpandedSlots = [Math]::Max(0, $slotCount - $sourceCarSlotCount)
        slotMode = $SlotMode
        humanSlots = $effectiveHumanSlots
        botSlots = $effectiveBotSlots
        midRaceBotTakeover = $hasBots
        restartSessionOnFirstHumanConnect = $hasBots
        advertisedSlots = $slotCount
        pitBoxes = $pitBoxes
        sourceMode = $sourceMode
        sourcePresetId = $sourcePresetId
        sourcePack = [IO.Path]::GetFullPath($sourcePath)
        preservedCmEventSettings = [bool]$PreserveCmEventSettings
        assettoCorsaRoot = $resolvedGame
    }
    $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $resolvedOutput 'race-bot-manifest.json') -Encoding utf8
    Write-Host "Staged $slotCount slots ($effectiveHumanSlots human, $effectiveBotSlots bot) at $resolvedOutput"
    Write-Host "LAN endpoint: $BindAddress"
    Write-Host "Launch: .\AssettoServer.exe --preset $PresetName"
}
finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}
