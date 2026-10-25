#requires -Version 7.0
<#
.SYNOPSIS
Run a bounded Last Shift AC/CSP visual inspection with exact race.ini restoration.
.DESCRIPTION
Creates a fresh fixture under the candidate build directory. Only race.ini is
temporarily changed in the user's configuration; its original bytes are restored
in finally. The game stays open until stop.signal is created, it exits, or the
timeout expires. Do not terminate the hosting PowerShell process; use stop.signal.
Screenshots and visual acceptance are performed separately by the operator.
.EXAMPLE
pwsh -NoProfile -File tools/Run-LastShiftClientSmoke.ps1 -PrepareOnly
.EXAMPLE
pwsh -NoProfile -File tools/Run-LastShiftClientSmoke.ps1 -MaximumSeconds 180
#>
[CmdletBinding()]
param(
    [string]$AssettoCorsaRoot = 'C:\Program Files (x86)\Steam\steamapps\common\assettocorsa',
    [string]$BuildDirectory = (Join-Path $PSScriptRoot '..\artifacts\last-shift-v031'),
    [string]$SourceFixture = (Join-Path $PSScriptRoot '..\artifacts\last-shift-smoke\runtime'),
    [ValidateRange(1024, 65535)][int]$GamePort = 19600,
    [ValidateRange(1024, 65535)][int]$HttpPort = 18081,
    [ValidateRange(15, 180)][int]$MaximumSeconds = 180,
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$buildRoot = [IO.Path]::GetFullPath($BuildDirectory)
$sourceRuntime = [IO.Path]::GetFullPath($SourceFixture)
$gameRoot = [IO.Path]::GetFullPath($AssettoCorsaRoot)
$trackSource = Join-Path $buildRoot 'content\tracks\last_shift'
$trackInstalled = Join-Path $gameRoot 'content\tracks\last_shift'
$validationRoot = Join-Path $buildRoot 'validation'
$oldInstance = Join-Path $sourceRuntime 'Data\Instances\Current'
$runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$runRoot = Join-Path $buildRoot "client-smoke\$runId"
$instance = Join-Path $runRoot 'instance'
$preset = Join-Path $instance 'presets\race-control'
$stopSignal = Join-Path $runRoot 'stop.signal'
$serverSignal = Join-Path $runRoot 'server-stop.signal'
$liveRoot = Join-Path $runRoot 'live'
$racePath = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Assetto Corsa\cfg\race.ini'
$server = $null
$game = $null
$originalRaceBytes = $null
$raceChanged = $false
$report = [ordered]@{
    StartedUtc = [DateTimeOffset]::UtcNow.ToString('o'); RunDirectory = $runRoot
    PrepareOnly = [bool]$PrepareOnly; Track = 'last_shift'; GamePort = $GamePort; HttpPort = $HttpPort
    ClientConnected = $false; ActiveHumanObserved = $false
    ClientConnectionLogObserved = $false; FpsClientProtocolReady = $false; FpsClientReadyProtocols = @()
    VisualAcceptanceVerified = $false; RaceIniRestored = $null
    Completed = $false; CleanupErrors = @()
}

function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Copy-Verified([string]$Source, [string]$Destination) {
    $fullDestination = [IO.Path]::GetFullPath($Destination)
    if (-not $fullDestination.StartsWith($runRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Fixture copy destination escaped this run.'
    }
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $fullDestination))
    Copy-Item -LiteralPath $Source -Destination $fullDestination
    if ((Get-Sha256 $Source) -ne (Get-Sha256 $fullDestination)) { throw "Copy hash mismatch: $Source" }
}

function Set-IniValue([string]$Text, [string]$Section, [string]$Key, [string]$Value) {
    $sectionPattern = '(?ms)^\[' + [regex]::Escape($Section) + '\][^\r\n]*\r?\n(?:(?!^\[).)*'
    $sectionMatches = [regex]::Matches($Text, $sectionPattern)
    if ($sectionMatches.Count -ne 1) { throw "Expected one [$Section] section." }
    $sectionText = $sectionMatches[0].Value
    $keyPattern = '(?m)^' + [regex]::Escape($Key) + '=[^\r\n]*'
    if ([regex]::Matches($sectionText, $keyPattern).Count -ne 1) { throw "Expected one [$Section] $Key." }
    $updated = [regex]::Replace($sectionText, $keyPattern, [Text.RegularExpressions.MatchEvaluator]{
        param($match) "$Key=$Value"
    })
    $Text.Remove($sectionMatches[0].Index, $sectionMatches[0].Length).Insert($sectionMatches[0].Index, $updated)
}

function Assert-NoGameSession {
    $running = @(Get-Process | Where-Object { $_.ProcessName -match '^(acs|acs_x86|AssettoServer Race Control|Content Manager)$' })
    if ($running.Count -gt 0) { throw 'An existing game or launcher session is running; close it before this isolated smoke.' }
}

function Assert-FreePorts {
    $network = [Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties()
    $tcp = @($network.GetActiveTcpListeners().Port)
    $udp = @($network.GetActiveUdpListeners().Port)
    if ($GamePort -eq $HttpPort -or $tcp -contains $GamePort -or $udp -contains $GamePort -or $tcp -contains $HttpPort) {
        throw 'Smoke ports are in use or overlap; choose unused -GamePort and -HttpPort values.'
    }
}

function Assert-InstalledRuntime {
    Add-Type -Path (Join-Path $sourceRuntime 'lib\AssettoServer.RaceControl.Core.dll')
    $assetType = [AssettoServer.RaceControl.Core.Staging.FpsClientPackAssets]
    $assets = [Collections.Generic.List[object]]::new()
    foreach ($methodName in @('GetAbilityAssets','GetModernAssets','GetAudioAssets','GetLoadoutImages',
                              'GetCompactSmgAnimations','GetDesertEagleAnimations','GetColt1911Animations')) {
        foreach ($asset in $assetType.GetMethod($methodName).Invoke($null, @())) {
            $assets.Add([pscustomobject]@{ Path = $asset.Item1; Bytes = $asset.Item2 })
        }
    }
    foreach ($method in $assetType.GetMethods([Reflection.BindingFlags]'Public,Static') |
        Where-Object { $_.ReturnType -eq [byte[]] -and $_.GetParameters().Count -eq 0 }) {
        $pathField = $assetType.GetField($method.Name.Substring(3) + 'Path')
        if ($null -eq $pathField) { throw "Missing embedded-asset path: $($method.Name)" }
        $assets.Add([pscustomobject]@{ Path = $pathField.GetValue($null); Bytes = $method.Invoke($null, @()) })
    }
    foreach ($asset in $assets) {
        $expected = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($asset.Bytes)).ToLowerInvariant()
        if ((Get-Sha256 (Join-Path $gameRoot $asset.Path)) -ne $expected) {
            throw "Installed FPS asset differs from this fixture runtime: $($asset.Path)"
        }
    }
    $report.InstalledRuntimeAssetsVerified = $assets.Count
}

[void][IO.Directory]::CreateDirectory($runRoot)
try {
    $validation = Get-Content -LiteralPath (Join-Path $validationRoot 'validation.json') -Raw | ConvertFrom-Json
    $materials = Get-Content -LiteralPath (Join-Path $validationRoot 'materials.json') -Raw | ConvertFrom-Json
    if (-not $validation.passed -or -not $materials.passed) { throw 'Candidate offline and material gates must pass first.' }
    foreach ($model in $validation.models) {
        if ((Get-Sha256 (Join-Path $trackSource $model.file)) -ne $model.sha256) { throw "Candidate changed after validation: $($model.file)" }
    }
    $report.CandidateModels = $validation.models
    # PortableServer changes CWD to its executable directory: copy the runtime,
    # never launch the old fixture executable with only a new WorkingDirectory.
    [IO.File]::WriteAllText((Join-Path $runRoot 'portable.json'), '{"purpose":"isolated Last Shift client smoke"}')
    # The plugin loader enumerates this directory without an existence guard.
    # Match the ordinary stager's directory skeleton, without loading plugins.
    foreach ($directory in @('plugins','cfg','cfg\reference','cfg\schemas','wwwroot','logs','data-protection-keys')) {
        [void][IO.Directory]::CreateDirectory((Join-Path $instance $directory))
    }
    foreach ($name in @('AssettoServer.exe','aspnetcorev2_inprocess.dll','csp_xxhash3.dll','steam_api64.dll')) {
        Copy-Verified (Join-Path $sourceRuntime "lib\Server\$name") (Join-Path $instance $name)
    }
    $trackParameters = Join-Path $oldInstance 'cfg\data_track_params.ini'
    if (Test-Path -LiteralPath $trackParameters) {
        Copy-Verified $trackParameters (Join-Path $instance 'cfg\data_track_params.ini')
    }
    foreach ($name in @('server_cfg.ini','entry_list.ini','extra_cfg.yml','welcome.txt')) {
        Copy-Verified (Join-Path $oldInstance "presets\race-control\$name") (Join-Path $preset $name)
    }
    foreach ($name in @('admins.txt','blacklist.txt','whitelist.txt')) {
        [IO.File]::WriteAllText((Join-Path $instance $name), '')
    }
    foreach ($relative in @('data.acd','collider.kn5')) {
        Copy-Verified (Join-Path $gameRoot "content\cars\bmw_m3_e30\$relative") (Join-Path $instance "content\cars\bmw_m3_e30\$relative")
    }
    $trackFiles = @(Get-ChildItem -LiteralPath $trackSource -Recurse -File)
    $mapHashes = foreach ($file in $trackFiles) {
        if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Candidate map contains a file link.' }
        $relative = [IO.Path]::GetRelativePath($trackSource, $file.FullName)
        Copy-Verified $file.FullName (Join-Path $instance "content\tracks\last_shift\$relative")
        [pscustomobject]@{ Path = $relative; Sha256 = Get-Sha256 $file.FullName }
    }
    $report.ServerMapFiles = @($mapHashes)
    Copy-Verified (Join-Path $validationRoot 'fps_geometry.bin') (Join-Path $preset 'fps-arena-geometry.bin')
    Copy-Verified (Join-Path $validationRoot 'fps_navigation.bin') (Join-Path $preset 'fps-arena-navigation.bin')
    $arena = Get-Content -LiteralPath (Join-Path $validationRoot 'fps_arena.json') -Raw | ConvertFrom-Json
    if ($arena.TrackId -ne 'last_shift' -or $arena.PreparationVersion -ne 5) { throw 'Candidate arena sidecar has unexpected identity/version.' }
    # JSON flow mappings are valid YAML and avoid a second vector/float serializer.
    $extraPath = Join-Path $preset 'extra_cfg.yml'
    $extra = [IO.File]::ReadAllText($extraPath)
    $arenaMatch = [regex]::Match($extra, '(?ms)^  Arena:\r?\n.*\z')
    if (-not $arenaMatch.Success) { throw 'Fixture must end with its FPS Arena configuration.' }
    $arenaConfig = [ordered]@{
        GeometryPath = 'fps-arena-geometry.bin'; NavigationPath = 'fps-arena-navigation.bin'
        BoundsMin = $arena.BoundsMin; BoundsMax = $arena.BoundsMax
        OutOfBoundsSeconds = $arena.OutOfBoundsSeconds; PlayableBoundary = $arena.PlayableBoundary
        SpawnPoints = $arena.SpawnPoints
    }
    $extra = $extra.Substring(0, $arenaMatch.Index) + '  Arena: ' + ($arenaConfig | ConvertTo-Json -Depth 8 -Compress) + "`r`n"
    if ($extra -notmatch '(?m)^NetworkBindAddress: "127\.0\.0\.1"\r?$') { throw 'Fixture is not restricted to localhost.' }
    [IO.File]::WriteAllText($extraPath, $extra)
    $serverCfgPath = Join-Path $preset 'server_cfg.ini'
    $serverCfg = [IO.File]::ReadAllText($serverCfgPath)
    foreach ($setting in @{ TRACK='last_shift'; CARS='bmw_m3_e30'; CONFIG_TRACK=''; UDP_PORT="$GamePort";
                           TCP_PORT="$GamePort"; HTTP_PORT="$HttpPort"; REGISTER_TO_LOBBY='0' }.GetEnumerator()) {
        $serverCfg = Set-IniValue $serverCfg 'SERVER' $setting.Key $setting.Value
    }
    [IO.File]::WriteAllText($serverCfgPath, $serverCfg)
    $report.GeometrySha256 = Get-Sha256 (Join-Path $preset 'fps-arena-geometry.bin')
    $report.NavigationSha256 = Get-Sha256 (Join-Path $preset 'fps-arena-navigation.bin')
    if ($PrepareOnly) {
        $report.Completed = $true
        Write-Host "Prepared only; no processes or installed settings changed: $runRoot"
        return
    }
    Assert-NoGameSession
    Assert-FreePorts
    foreach ($file in $mapHashes) {
        if ((Get-Sha256 (Join-Path $trackInstalled $file.Path)) -ne $file.Sha256) {
            throw "Install the validated textured candidate before launching: $($file.Path)"
        }
    }
    Assert-InstalledRuntime
    $server = Start-Process -FilePath (Join-Path $instance 'AssettoServer.exe') -WorkingDirectory $instance `
        -ArgumentList @('--preset','race-control','--shutdown-file',('"' + $serverSignal + '"'),
            '--race-control-directory',('"' + $liveRoot + '"')) -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $runRoot 'server.stdout.log') -RedirectStandardError (Join-Path $runRoot 'server.stderr.log')
    $report.ServerPid = $server.Id
    $statePath = Join-Path $liveRoot 'state.json'
    $readyDeadline = [DateTime]::UtcNow.AddSeconds(30)
    $ready = $false
    do {
        if ($server.HasExited) { throw 'Fixture server exited before readiness; inspect server logs.' }
        if (Test-Path -LiteralPath $statePath) {
            try { $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json; $ready = $state.serverRunning -and $state.isFps } catch { }
        }
        if (-not $ready) { Start-Sleep -Milliseconds 250 }
    } while (-not $ready -and [DateTime]::UtcNow -lt $readyDeadline)
    if (-not $ready) { throw 'Fixture server did not become ready within 30 seconds.' }
    Assert-NoGameSession
    $originalRaceBytes = [IO.File]::ReadAllBytes($racePath)
    $report.OriginalRaceIniSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($originalRaceBytes)).ToLowerInvariant()
    [IO.File]::WriteAllBytes((Join-Path $runRoot 'race.ini.original'), $originalRaceBytes)
    # Latin1 is a byte-preserving mapping. It leaves BOMs/non-ASCII values untouched.
    $raceText = [Text.Encoding]::Latin1.GetString($originalRaceBytes)
    foreach ($setting in @{ TRACK='last_shift'; MODEL='bmw_m3_e30'; SKIN='0_jet_black'; CONFIG_TRACK='' }.GetEnumerator()) {
        $raceText = Set-IniValue $raceText 'RACE' $setting.Key $setting.Value
    }
    foreach ($setting in @{ ACTIVE='1'; SERVER_IP='127.0.0.1'; SERVER_PORT="$GamePort"; SERVER_HTTP_PORT="$HttpPort" }.GetEnumerator()) {
        $raceText = Set-IniValue $raceText 'REMOTE' $setting.Key $setting.Value
    }
    $raceChanged = $true
    [IO.File]::WriteAllBytes($racePath, [Text.Encoding]::Latin1.GetBytes($raceText))
    $game = Start-Process -FilePath (Join-Path $gameRoot 'acs.exe') -WorkingDirectory $gameRoot -PassThru
    $report.GamePid = $game.Id
    $deadline = [DateTime]::UtcNow.AddSeconds($MaximumSeconds)
    $report.InspectionDeadlineUtc = $deadline.ToString('o')
    Write-Host "GAME OPEN: PID $($game.Id); inspection deadline $($deadline.ToString('o'))"
    Write-Host "End inspection safely by creating: $stopSignal"
    Write-Host "Evidence: $runRoot"
    while (-not $game.HasExited -and [DateTime]::UtcNow -lt $deadline -and -not (Test-Path -LiteralPath $stopSignal)) {
        if ($server.HasExited) { throw 'Fixture server exited during the client inspection.' }
        if (Test-Path -LiteralPath $statePath) {
            try {
                $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
                # FPS snapshots use isConnected for active/deployed humans; a
                # connected player still in the loadout menu remains inactive.
                if (@($state.cars | Where-Object { $_.isConnected -and -not $_.isBot }).Count -gt 0) {
                    $report.ActiveHumanObserved = $true
                }
            } catch { }
        }
        Start-Sleep -Milliseconds 500
    }
    $report.EndReason = if ($game.HasExited) { 'GameExited' } elseif (Test-Path -LiteralPath $stopSignal) { 'StopSignal' } else { 'Timeout' }
    $report.Completed = $true
} catch {
    $report.Error = $_.Exception.Message
    throw
} finally {
    # Cleanup steps are independent: one failure cannot skip exact config restore.
    try {
        if ($null -ne $game -and -not $game.HasExited) {
            [void]$game.CloseMainWindow()
            if (-not $game.WaitForExit(5000)) { $game.Kill(); [void]$game.WaitForExit(5000) }
        }
    } catch { $report.CleanupErrors += "Owned game cleanup: $($_.Exception.Message)" }
    try {
        if ($null -ne $server -and -not $server.HasExited) {
            [IO.File]::WriteAllText($serverSignal, 'stop')
            if (-not $server.WaitForExit(10000)) { $server.Kill(); [void]$server.WaitForExit(5000) }
        }
    } catch { $report.CleanupErrors += "Owned server cleanup: $($_.Exception.Message)" }
    try {
        if ($raceChanged) {
            [IO.File]::WriteAllBytes($racePath, $originalRaceBytes)
            $report.RaceIniRestored = (Get-Sha256 $racePath) -eq $report.OriginalRaceIniSha256
            if (-not $report.RaceIniRestored) { throw 'Restored race.ini hash mismatch.' }
        }
    } catch { $report.RaceIniRestored = $false; $report.CleanupErrors += "Race configuration restore: $($_.Exception.Message)" }
    # Read only after the owned server exits so buffered protocol messages have
    # reached disk. Record transport/handshake evidence separately from gameplay
    # activation and from screenshots; none of these proves visual acceptance.
    $stdoutPath = Join-Path $runRoot 'server.stdout.log'
    if (Test-Path -LiteralPath $stdoutPath) {
        try {
            $serverLog = [IO.File]::ReadAllText($stdoutPath)
            $report.ClientConnectionLogObserved = [regex]::IsMatch($serverLog,
                '(?m)^\[\d\d:\d\d:\d\d INF\] .+ has connected\r?$')
            $readyMessages = [regex]::Matches($serverLog,
                '(?m)^\[\d\d:\d\d:\d\d INF\] FPS client ready with protocol (?<protocol>\d+)\r?$')
            $report.FpsClientReadyProtocols = @($readyMessages | ForEach-Object {
                [int]$_.Groups['protocol'].Value
            } | Sort-Object -Unique)
            $report.FpsClientProtocolReady = $report.FpsClientReadyProtocols -contains 7
            $report.ClientConnected = $report.ActiveHumanObserved -or $report.ClientConnectionLogObserved -or $report.FpsClientProtocolReady
            $report.ProtocolEvidenceLog = $stdoutPath
            $report.ProtocolEvidenceLogSha256 = Get-Sha256 $stdoutPath
        } catch { $report.ProtocolEvidenceReadError = $_.Exception.Message }
    }
    $report.FinishedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    $report | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath (Join-Path $runRoot 'report.json') -Encoding utf8
    Write-Host "Smoke report: $(Join-Path $runRoot 'report.json')"
    if ($report.CleanupErrors.Count -gt 0) { throw ($report.CleanupErrors -join [Environment]::NewLine) }
}
