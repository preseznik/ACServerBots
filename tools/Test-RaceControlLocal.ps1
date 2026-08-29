[CmdletBinding()]
param(
    [string] $AssettoCorsaRoot = 'C:\Program Files (x86)\Steam\steamapps\common\assettocorsa',
    [string] $RaceControlBuild,
    [string] $Track = 'magione',
    [string] $TrackLayout = '',
    [string] $Car = 'bmw_m3_e30',
    [string[]] $CarModels,
    [ValidateRange(2, 32)]
    [int] $Slots = 2,
    [ValidateRange(3, 180)]
    [int] $SmokeSeconds = 8,
    [ValidateSet('Efficient', 'Balanced', 'High')]
    [string] $PhysicsFidelity = 'Efficient',
    [ValidateRange(0, 1)]
    [double] $BotAggression = 0.5,
    [ValidateRange(0, 1)]
    [double] $BotDifficulty = 0.75,
    [switch] $VerifyMovingBots,
    [switch] $VerifyPassing,
    [switch] $VerifyLiveControl,
    [switch] $VerifyStoppedObstaclePassing,
    [switch] $FpsGate,
    [switch] $SimulateRace,
    [ValidateRange(1, 100)]
    [double] $SimulationTimeScale = 10
)

$ErrorActionPreference = 'Stop'
if ($VerifyMovingBots -and $SmokeSeconds -lt 30) {
    throw '-VerifyMovingBots requires -SmokeSeconds 30 or greater so the race countdown can finish.'
}
if ($VerifyPassing -and -not $VerifyMovingBots) {
    throw '-VerifyPassing requires -VerifyMovingBots.'
}
if ($VerifyStoppedObstaclePassing -and -not $SimulateRace) {
    throw '-VerifyStoppedObstaclePassing requires -SimulateRace.'
}
if ($FpsGate -and ($VerifyMovingBots -or $VerifyPassing -or $VerifyLiveControl -or
        $VerifyStoppedObstaclePassing -or $SimulateRace)) {
    throw '-FpsGate is an isolated compatibility smoke test and cannot be combined with race simulation or live race controls.'
}
if ($VerifyStoppedObstaclePassing -and $Slots -lt 2) {
    throw '-VerifyStoppedObstaclePassing requires at least two slots.'
}
if ($SimulateRace) { $VerifyLiveControl = $true }
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($RaceControlBuild)) {
    $RaceControlBuild = Join-Path $repositoryRoot 'out-race-control'
}
$coreAssembly = Join-Path $RaceControlBuild 'lib\AssettoServer.RaceControl.Core.dll'
$serverPayload = Join-Path $RaceControlBuild 'lib\Server'
if (-not (Test-Path -LiteralPath $coreAssembly -PathType Leaf)) { throw "Race Control core not found: $coreAssembly" }
if (-not (Test-Path -LiteralPath (Join-Path $serverPayload 'AssettoServer.exe') -PathType Leaf)) { throw "Bundled server not found: $serverPayload" }

Add-Type -Path $coreAssembly
$scanner = [AssettoServer.RaceControl.Core.Content.AcContentScanner]::new()
$catalog = $scanner.Scan($AssettoCorsaRoot)
Write-Host "Scanned $($catalog.Cars.Count) cars, $($catalog.Tracks.Count) track layouts, and $($catalog.Weather.Count) weather sets"

$selectedTrack = $catalog.Tracks | Where-Object { $_.TrackId -ieq $Track -and $_.LayoutId -ieq $TrackLayout } | Select-Object -First 1
if ($null -eq $selectedTrack) { throw "Track layout is not installed: $Track/$TrackLayout" }
$requestedCarIds = @($(if ($CarModels.Count -gt 0) { $CarModels } else { $Car }))
$selectedCars = @($requestedCarIds | ForEach-Object {
    $carId = $_
    $selected = $catalog.Cars | Where-Object Id -IEQ $carId | Select-Object -First 1
    if ($null -eq $selected) { throw "Car is not installed: $carId" }
    if ($selected.Skins.Count -eq 0) { throw "Car has no installed skins: $carId" }
    $selected
})

$preset = [AssettoServer.RaceControl.Core.Models.RaceControlPreset]::CreateDefault($AssettoCorsaRoot, $serverPayload)
$preset.Name = 'Race Control Local Acceptance'
$preset.ServerName = 'Race Control Local Acceptance'
$preset.TrackId = $Track
$preset.TrackLayoutId = $TrackLayout
$preset.Network.BindAddress = '127.0.0.1'
$preset.Network.TcpPort = 19600
$preset.Network.UdpPort = 19600
$preset.Network.HttpPort = 18081
$preset.Sessions.PracticeMinutes = 2
$preset.Sessions.RaceLaps = if ($SimulateRace) { 99 } else { 3 }
$preset.Sessions.PracticeEnabled = -not $VerifyMovingBots
$preset.Bots.Enabled = $true
$preset.Bots.Aggression = $BotAggression
$preset.Bots.Difficulty = $BotDifficulty
$preset.Bots.PhysicsFidelity = [Enum]::Parse(
    [AssettoServer.RaceControl.Core.Models.PhysicsFidelity], $PhysicsFidelity)
if ($FpsGate) {
    $preset.Mode = [AssettoServer.RaceControl.Core.Models.EventMode]::Fps
    $preset.Name = 'Race Control FPS Compatibility Gate'
    $preset.ServerName = 'Race Control FPS Compatibility Gate'
    $preset.Fps.CarrierCarId = $selectedCars[0].Id
}

$grid = [Collections.Generic.List[AssettoServer.RaceControl.Core.Models.GridSlotPreset]]::new()
for ($index = 0; $index -lt $Slots; $index++) {
    $selectedCar = $selectedCars[$index % $selectedCars.Count]
    $slot = [AssettoServer.RaceControl.Core.Models.GridSlotPreset]::new()
    $slot.CarId = $selectedCar.Id
    $slot.SkinId = $selectedCar.Skins[$index % $selectedCar.Skins.Count].Id
    $slot.DriverName = "Smoke Bot $($index + 1)"
    $slot.Mode = [AssettoServer.RaceControl.Core.Models.SlotMode]::Auto
    $grid.Add($slot)
}
$preset.Grid = $grid

$acceptanceRoot = Join-Path $repositoryRoot '.artifacts\race-control-local-acceptance'
$paths = [AssettoServer.RaceControl.Core.Infrastructure.RaceControlPaths]::new($acceptanceRoot)
if ($FpsGate) {
    $arenaStore = [AssettoServer.RaceControl.Core.Storage.FpsArenaStore]::new($paths)
    $preparer = [AssettoServer.RaceControl.Core.Staging.FpsArenaPreparationService]::new($arenaStore)
    Write-Host 'Preparing bounded FPS arena sidecar and safe prototype spawns...'
    $preset.Fps.Arena = $preparer.PrepareAsync(
        $preset, $null, [Threading.CancellationToken]::None).GetAwaiter().GetResult()
}
$validator = [AssettoServer.RaceControl.Core.Validation.RaceControlValidator]::new()
$renderer = [AssettoServer.RaceControl.Core.Configuration.ServerConfigurationRenderer]::new()
$validation = $validator.Validate($preset, $catalog)
if (-not $validation.IsValid) {
    $errors = $validation.Messages | Where-Object Severity -EQ ([AssettoServer.RaceControl.Core.Validation.ValidationSeverity]::Error)
    throw (($errors | ForEach-Object Message) -join [Environment]::NewLine)
}

$stager = [AssettoServer.RaceControl.Core.Staging.ServerInstanceStager]::new($paths, $validator, $renderer)
Write-Host $(if ($FpsGate) { 'Staging FPS compatibility server...' } else { 'Staging and preparing rigid-body inputs...' })
$instance = $stager.StageAsync($preset, $catalog, $null, [Threading.CancellationToken]::None).GetAwaiter().GetResult()
Write-Host "Staged $($instance.SlotCount) slots ($($instance.BotSlotCount) bot-capable) at $($instance.RootPath)"
if ($FpsGate) {
    $fpsGeometry = Join-Path $instance.RootPath 'presets\race-control\fps-arena-geometry.bin'
    $fpsNavigation = Join-Path $instance.RootPath 'presets\race-control\fps-arena-navigation.bin'
    if (-not (Test-Path -LiteralPath $fpsGeometry -PathType Leaf) -or
        (Get-Item -LiteralPath $fpsGeometry).Length -le 12) {
        throw 'FPS staging did not produce a non-empty physical arena geometry asset.'
    }
    if (-not (Test-Path -LiteralPath $fpsNavigation -PathType Leaf) -or
        (Get-Item -LiteralPath $fpsNavigation).Length -le 24) {
        throw 'FPS staging did not produce a non-empty arena navigation asset.'
    }
}

$stdout = Join-Path $instance.RootPath 'acceptance-stdout.log'
$stderr = Join-Path $instance.RootPath 'acceptance-stderr.log'
$arguments = @('--preset', $instance.PresetName, '--shutdown-file', $instance.ShutdownFilePath)
$liveClient = $null
$expectedSimulationTimeScale = $SimulationTimeScale
if ($VerifyLiveControl) {
    $liveClient = [AssettoServer.RaceControl.Core.Runtime.LiveRaceControlClient]::new($instance.RootPath)
    $arguments += @('--race-control-directory', $liveClient.ControlDirectory)
}
if ($SimulateRace) {
    $simulationOutput = Join-Path $instance.RootPath 'simulation-live-acceptance'
    $arguments += @('--simulate-race', '--simulation-output', $simulationOutput,
        '--simulation-seed', '23', '--simulation-max-minutes', '30',
        '--simulation-max-wall-seconds', '60', '--simulation-time-scale',
        $SimulationTimeScale.ToString([Globalization.CultureInfo]::InvariantCulture))
}
$serverProcess = Start-Process -FilePath $instance.ExecutablePath -WorkingDirectory $instance.RootPath `
    -ArgumentList $arguments -RedirectStandardOutput $stdout -RedirectStandardError $stderr -WindowStyle Hidden -PassThru
try {
    if ($FpsGate) {
        $assetUrl = 'http://127.0.0.1:18081/fps/assets/asrc-fps-assets-v6.zip'
        $assetDeadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
        $assetArchiveBytes = $null
        $httpClient = [Net.Http.HttpClient]::new()
        $httpClient.Timeout = [TimeSpan]::FromSeconds(2)
        try {
            while ([DateTimeOffset]::UtcNow -lt $assetDeadline -and $null -eq $assetArchiveBytes) {
                try {
                    $response = $httpClient.GetAsync($assetUrl).GetAwaiter().GetResult()
                    try {
                        if ($response.IsSuccessStatusCode) {
                            $assetArchiveBytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
                        }
                    } finally {
                        $response.Dispose()
                    }
                } catch {
                    if ($serverProcess.HasExited) { break }
                }
                if ($null -eq $assetArchiveBytes) { Start-Sleep -Milliseconds 100 }
            }
        } finally {
            $httpClient.Dispose()
        }
        if ($null -eq $assetArchiveBytes -or $assetArchiveBytes.Length -le 10KB -or
            $assetArchiveBytes[0] -ne 0x50 -or $assetArchiveBytes[1] -ne 0x4B) {
            throw "FPS server did not expose a valid CSP asset archive at $assetUrl"
        }
        Write-Host "Verified CSP rifle asset archive endpoint ($($assetArchiveBytes.Length) bytes)."
    }

    if ($VerifyLiveControl) {
        function Wait-LiveState([scriptblock] $Condition, [string] $Description) {
            $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
            while ([DateTimeOffset]::UtcNow -lt $deadline) {
                $state = $liveClient.TryReadSnapshot()
                if ($null -ne $state -and (& $Condition $state)) { return $state }
                Start-Sleep -Milliseconds 100
            }
            throw "Timed out waiting for live Race Control state: $Description"
        }

        $initialState = Wait-LiveState { param($state) $state.ServerRunning -and $state.Cars.Count -eq $Slots } 'initial snapshot'
        if ($SimulateRace -and -not $initialState.IsSimulation) {
            throw 'Live snapshot did not identify the accelerated simulation mode.'
        }
        $trackMap = $liveClient.TryReadTrack()
        if ($null -eq $trackMap -or $trackMap.Points.Count -lt 20) {
            throw 'Live Race Control track map is missing or unusable.'
        }

        $startId = $liveClient.SendCommandAsync(
            [AssettoServer.RaceControl.Core.Runtime.LiveRaceCommand]::Start).GetAwaiter().GetResult()
        $startedState = Wait-LiveState { param($state)
            $null -ne $state.LastCommand -and $state.LastCommand.Id -eq $startId -and
            $state.LastCommand.Status -eq 'accepted' -and $state.Session.Type -eq 'Race'
        } 'start-race acknowledgement'

        if ($SimulateRace) {
            $movingState = Wait-LiveState { param($state)
                @($state.Cars | Where-Object {
                    $_.SpeedKmh -gt 1 -and ([Math]::Abs($_.X) -gt 1 -or [Math]::Abs($_.Z) -gt 1)
                }).Count -gt 0
            } 'moving car coordinates'
            if ($movingState.MaximumSimulatedMilliseconds -ne 30 * 60 * 1000 -or
                $movingState.SimulationProgressPercent -le 0 -or
                $movingState.EstimatedRemainingSimulatedMilliseconds -le 0) {
                throw 'Live simulation progress did not expose a usable duration and remaining-time estimate.'
            }

            $expectedSimulationTimeScale = if ($SimulationTimeScale -ge 100) { 50 } else {
                [Math]::Min(100, $SimulationTimeScale * 2)
            }
            $timeScaleId = $liveClient.SendSimulationTimeScaleAsync(
                $expectedSimulationTimeScale).GetAwaiter().GetResult()
            $scaledState = Wait-LiveState { param($state)
                $null -ne $state.LastCommand -and $state.LastCommand.Id -eq $timeScaleId -and
                $state.LastCommand.Status -eq 'accepted' -and
                [Math]::Abs($state.TargetRealTimeFactor - $expectedSimulationTimeScale) -lt 0.001
            } 'live simulation time-scale acknowledgement'

            if ($VerifyStoppedObstaclePassing) {
                $scaledState = Wait-LiveState { param($state)
                    $state.Session.Phase -eq 'racing' -and
                    $state.SimulatedMilliseconds - $state.Session.StartTimeMilliseconds -ge 90000 -and
                    @($state.Cars | Where-Object { $null -ne $_.RacePosition }).Count -eq $Slots
                } 'a settled racing field before the stopped-leader test'
            }

            $controlledBot = $scaledState.Cars | Where-Object IsBot | Sort-Object {
                if ($null -eq $_.RacePosition) { [int]::MaxValue } else { $_.RacePosition }
            } | Select-Object -First 1
            $stoppedPassesBefore = ($scaledState.Cars | Measure-Object `
                -Property StoppedObstaclePassesCompleted -Sum).Sum
            $stopBotId = $liveClient.SendBotStopAsync($controlledBot.SessionId, $true).GetAwaiter().GetResult()
            $stoppedBotState = Wait-LiveState { param($state)
                $bot = $state.Cars | Where-Object SessionId -EQ $controlledBot.SessionId | Select-Object -First 1
                $null -ne $state.LastCommand -and $state.LastCommand.Id -eq $stopBotId -and
                $state.LastCommand.Status -eq 'accepted' -and $bot.ControlMode -eq 'stopped' -and
                $bot.SpeedKmh -lt 0.5
            } 'selected bot hard stop'

            if ($VerifyStoppedObstaclePassing) {
                $passedState = Wait-LiveState { param($state)
                    $passer = $state.Cars | Where-Object {
                        $_.SessionId -ne $controlledBot.SessionId -and
                        $_.StoppedObstaclePassesCompleted -gt 0
                    } | Select-Object -First 1
                    $stoppedBot = $state.Cars | Where-Object SessionId -EQ $controlledBot.SessionId |
                        Select-Object -First 1
                    $completedPasses = ($state.Cars | Measure-Object `
                        -Property StoppedObstaclePassesCompleted -Sum).Sum
                    $null -ne $passer -and $null -ne $stoppedBot -and
                    $completedPasses -gt $stoppedPassesBefore -and
                    $passer.RacePosition -lt $stoppedBot.RacePosition
                } 'a racing bot to navigate around and complete a pass of the stopped leader'
            }

            $goBotId = $liveClient.SendBotStopAsync($controlledBot.SessionId, $false).GetAwaiter().GetResult()
            $goBotState = Wait-LiveState { param($state)
                $bot = $state.Cars | Where-Object SessionId -EQ $controlledBot.SessionId | Select-Object -First 1
                $null -ne $state.LastCommand -and $state.LastCommand.Id -eq $goBotId -and
                $state.LastCommand.Status -eq 'accepted' -and $bot.ControlMode -eq 'automatic'
            } 'selected bot GO command'

            $teleportId = $liveClient.SendBotTeleportToP1Async(
                $controlledBot.SessionId).GetAwaiter().GetResult()
            $teleportedState = Wait-LiveState { param($state)
                $null -ne $state.LastCommand -and $state.LastCommand.Id -eq $teleportId -and
                $state.LastCommand.Status -eq 'accepted'
            } 'selected bot P1 teleport'

            $takeoverId = $liveClient.SendBotTakeoverAsync(
                $controlledBot.SessionId, $true).GetAwaiter().GetResult()
            $manualState = Wait-LiveState { param($state)
                $bot = $state.Cars | Where-Object SessionId -EQ $controlledBot.SessionId | Select-Object -First 1
                $null -ne $state.LastCommand -and $state.LastCommand.Id -eq $takeoverId -and
                $state.LastCommand.Status -eq 'accepted' -and $bot.ControlMode -eq 'manual'
            } 'selected bot manual takeover'
            [void]$liveClient.WriteManualInputAsync(
                $controlledBot.SessionId, 0.35, 1, 0).GetAwaiter().GetResult()
            $manualInputState = Wait-LiveState { param($state)
                $bot = $state.Cars | Where-Object SessionId -EQ $controlledBot.SessionId | Select-Object -First 1
                $bot.ControlMode -eq 'manual' -and [Math]::Abs($bot.ManualSteering - 0.35) -lt 0.01 -and
                [Math]::Abs($bot.ManualThrottle - 1) -lt 0.01 -and $bot.ManualBrake -lt 0.01
            } 'manual steering and throttle input'

            $releaseId = $liveClient.SendBotTakeoverAsync(
                $controlledBot.SessionId, $false).GetAwaiter().GetResult()
            $releasedState = Wait-LiveState { param($state)
                $bot = $state.Cars | Where-Object SessionId -EQ $controlledBot.SessionId | Select-Object -First 1
                $null -ne $state.LastCommand -and $state.LastCommand.Id -eq $releaseId -and
                $state.LastCommand.Status -eq 'accepted' -and $bot.ControlMode -eq 'automatic'
            } 'selected bot manual-control release'
        }

        if (-not $SimulateRace) {
            $stopId = $liveClient.SendCommandAsync(
                [AssettoServer.RaceControl.Core.Runtime.LiveRaceCommand]::Stop).GetAwaiter().GetResult()
            $stoppedState = Wait-LiveState { param($state)
                $null -ne $state.LastCommand -and $state.LastCommand.Id -eq $stopId -and
                $state.LastCommand.Status -eq 'accepted' -and $state.Session.Phase -eq 'stopped'
            } 'stop-race acknowledgement'
            if (@($stoppedState.Cars | Where-Object IsActive | Where-Object { -not $_.IsDnf }).Count -gt 0) {
                throw 'Stopping a race did not classify every unfinished active car as DNF.'
            }

            $restartId = $liveClient.SendCommandAsync(
                [AssettoServer.RaceControl.Core.Runtime.LiveRaceCommand]::Restart).GetAwaiter().GetResult()
            $restartedState = Wait-LiveState { param($state)
                $null -ne $state.LastCommand -and $state.LastCommand.Id -eq $restartId -and
                $state.LastCommand.Status -eq 'accepted' -and $state.Session.Type -eq 'Race' -and
                $state.Session.Phase -in @('countdown', 'racing')
            } 'restart-race acknowledgement'
        }
    }

    Start-Sleep -Seconds $SmokeSeconds
    if ($serverProcess.HasExited) {
        throw "Server exited early with code $($serverProcess.ExitCode): $((Get-Content -Raw -LiteralPath $stderr -ErrorAction SilentlyContinue))"
    }
    $startupLog = Get-Content -Raw -LiteralPath $stdout -ErrorAction SilentlyContinue
    if ($startupLog -match 'Fatal exception occurred|\sFTL\]') {
        throw "Server reported a fatal startup error: $startupLog"
    }
    if ($SimulateRace) {
        $simulationState = $liveClient.TryReadSnapshot()
        if ($null -eq $simulationState -or $simulationState.RealTimeFactor -le 1) {
            throw 'Accelerated live simulation did not advance faster than real time.'
        }
        if ($simulationState.RealTimeFactor -gt ($expectedSimulationTimeScale * 1.25)) {
            throw "Live simulation exceeded its $expectedSimulationTimeScale`x target: $($simulationState.RealTimeFactor)x."
        }
    }

    [IO.File]::WriteAllText($instance.ShutdownFilePath, 'stop')
    Wait-Process -Id $serverProcess.Id -Timeout 15 -ErrorAction SilentlyContinue
    $serverProcess.Refresh()
    if (-not $serverProcess.HasExited) {
        Stop-Process -Id $serverProcess.Id
        throw 'Server did not honor the graceful shutdown signal within 15 seconds'
    }
    if ($serverProcess.ExitCode -ne 0) {
        throw "Server returned exit code $($serverProcess.ExitCode)"
    }
} finally {
    $serverProcess.Refresh()
    if (-not $serverProcess.HasExited) { Stop-Process -Id $serverProcess.Id }
}

if ($SimulateRace) {
    $summaryPath = Join-Path $simulationOutput 'summary.json'
    if (-not (Test-Path -LiteralPath $summaryPath -PathType Leaf)) {
        throw "Simulation did not write a result summary: $summaryPath"
    }
    $summary = Get-Content -Raw -LiteralPath $summaryPath | ConvertFrom-Json
    if ($summary.schemaVersion -lt 2 -or $summary.results.Count -ne $Slots) {
        throw 'Simulation summary is missing its versioned per-car classification.'
    }
    if ([Math]::Abs([double]$summary.targetRealTimeFactor - $expectedSimulationTimeScale) -gt 0.001) {
        throw "Simulation summary reported the wrong time target: $($summary.targetRealTimeFactor)x."
    }
    $requiredResultFields = @('averageSpeedKmh', 'topSpeedKmh', 'crashCount', 'fullStopCount',
        'fullyStoppedMilliseconds')
    foreach ($result in $summary.results) {
        $missingFields = @($requiredResultFields | Where-Object { $_ -notin $result.PSObject.Properties.Name })
        if ($missingFields.Count -gt 0) {
            throw "Simulation result for $($result.name) is missing: $($missingFields -join ', ')."
        }
    }
}

$combinedLog = (Get-Content -Raw -LiteralPath $stdout -ErrorAction SilentlyContinue) + [Environment]::NewLine + `
    (Get-Content -Raw -LiteralPath $stderr -ErrorAction SilentlyContinue)
$presetLogPattern = if ($SimulateRace) {
    'Running network-free race simulation for preset race-control'
} else {
    'Using preset race-control'
}
if ($combinedLog -notmatch $presetLogPattern) { throw 'Server log did not confirm the generated preset' }
if ($combinedLog -notmatch 'Shutdown requested by control file') { throw 'Server log did not confirm graceful control-file shutdown' }
if ($FpsGate -and $combinedLog -notmatch 'FPS deathmatch world started') {
    throw 'Server log did not confirm the authoritative FPS world startup.'
}
if ($FpsGate -and $combinedLog -notmatch '\d+ (?:physical arena|collision) triangles') {
    throw 'Server log did not confirm loading physical FPS arena geometry.'
}
if ($FpsGate -and $combinedLog -notmatch '\d+ navigation nodes in \d+ components') {
    throw 'Server log did not confirm loading FPS arena navigation.'
}
if ($FpsGate) {
    $initialBotSpawns = @([regex]::Matches($combinedLog,
        'FPS actor initial spawn: actor=\d+, role=(?:Auto|Bot), human=False,'))
    if ($initialBotSpawns.Count -ne $Slots) {
        throw "FPS world spawned $($initialBotSpawns.Count) bots; expected $Slots."
    }
    $activeBots = @([regex]::Matches($combinedLog,
        'FPS bot behavior active: actor=\d+,'))
    if ($activeBots.Count -ne $Slots) {
        throw "FPS world activated behavior for $($activeBots.Count) bots; expected $Slots."
    }
    if ($combinedLog -notmatch 'FPS rifle accepted first shot:') {
        throw 'FPS bots did not produce an authoritative rifle shot during the gate.'
    }
}
if ($VerifyLiveControl -and $combinedLog -notmatch 'Race Control live bridge ready') {
    throw 'Server log did not confirm the Race Control live bridge.'
}
if ($VerifyMovingBots) {
    $samples = @([regex]::Matches($combinedLog,
        'Race physics: \d+ bots, Y (?<min>-?\d+(?:\.\d+)?)\.\.(?<max>-?\d+(?:\.\d+)?) m, max speed (?<speed>\d+(?:\.\d+)?) m/s, max rise (?<rise>\d+(?:\.\d+)?) m/s, height error (?<height>\d+(?:\.\d+)?) m, excess rise (?<excess>\d+(?:\.\d+)?) m/s, grounded (?<grounded>\d+)/4, suspension (?<suspension>\d+(?:\.\d+)?) m'))
    if ($samples.Count -lt 2) { throw 'Server log did not contain enough rigid-body diagnostics.' }
    $finalSpeed = [double]::Parse($samples[-1].Groups['speed'].Value, [Globalization.CultureInfo]::InvariantCulture)
    $maximumRise = ($samples | ForEach-Object {
        [double]::Parse($_.Groups['rise'].Value, [Globalization.CultureInfo]::InvariantCulture)
    } | Measure-Object -Maximum).Maximum
    $maximumExcessRise = ($samples | ForEach-Object {
        [double]::Parse($_.Groups['excess'].Value, [Globalization.CultureInfo]::InvariantCulture)
    } | Measure-Object -Maximum).Maximum
    $finalHeightError = [double]::Parse($samples[-1].Groups['height'].Value, [Globalization.CultureInfo]::InvariantCulture)
    $maximumSuspensionCompression = ($samples | ForEach-Object {
        [double]::Parse($_.Groups['suspension'].Value, [Globalization.CultureInfo]::InvariantCulture)
    } | Measure-Object -Maximum).Maximum
    if ($finalHeightError -gt 2.5) { throw "Bots left the road surface: final spline height error was $finalHeightError m." }
    if ($maximumRise -gt 12) { throw "Bots were launched from the road: maximum upward speed was $maximumRise m/s." }
    if ($maximumExcessRise -gt 4) {
        throw "Bots gained unexplained vertical speed: maximum slope-relative excess was $maximumExcessRise m/s."
    }
    if ($maximumSuspensionCompression -gt 0.12) {
        throw "Bot suspension collapsed: maximum chassis compression was $maximumSuspensionCompression m."
    }
    if ($finalSpeed -lt 1) { throw "Bots did not begin moving after the countdown: final maximum speed was $finalSpeed m/s." }

    $launchSamples = @([regex]::Matches($combinedLog,
        'launched (?<moving>\d+)/(?<bots>\d+), launch spread (?<spread>\d+) ticks'))
    $fullFieldLaunch = $launchSamples | Where-Object {
        [int]$_.Groups['moving'].Value -eq [int]$_.Groups['bots'].Value -and [int]$_.Groups['bots'].Value -gt 0
    } | Select-Object -First 1
    if ($null -eq $fullFieldLaunch) { throw 'Not every race bot launched from the grid.' }
    if ([int]$fullFieldLaunch.Groups['spread'].Value -gt 30) {
        throw "Race bots launched sequentially: first-motion spread was $($fullFieldLaunch.Groups['spread'].Value) ticks."
    }

    $stabilitySamples = @([regex]::Matches($combinedLog,
        'upright (?<upright>-?\d+(?:\.\d+)?), overturned (?<overturned>\d+), recoveries (?<recoveries>\d+)'))
    if ($stabilitySamples.Count -lt 2) { throw 'Server log did not contain enough rollover diagnostics.' }
    $finalUpright = [double]::Parse($stabilitySamples[-1].Groups['upright'].Value, [Globalization.CultureInfo]::InvariantCulture)
    $finalOverturned = [int]$stabilitySamples[-1].Groups['overturned'].Value
    if ($finalOverturned -ne 0 -or $finalUpright -lt 0.5) {
        throw "Bots did not finish the smoke interval upright: dot=$finalUpright, overturned=$finalOverturned."
    }

    if ($VerifyPassing) {
        $passingSamples = @([regex]::Matches($combinedLog,
            'lane offset (?<lane>\d+(?:\.\d+)?) m, pass separation (?<separation>\d+(?:\.\d+)?) m, passes (?<commits>\d+)/(?<separated>\d+)/(?<completed>\d+) committed/separated/completed'))
        if ($passingSamples.Count -lt 2) { throw 'Server log did not contain enough passing diagnostics.' }
        $lastPassing = $passingSamples[-1]
        $maximumLaneOffset = [double]::Parse($lastPassing.Groups['lane'].Value,
            [Globalization.CultureInfo]::InvariantCulture)
        $maximumPassSeparation = [double]::Parse($lastPassing.Groups['separation'].Value,
            [Globalization.CultureInfo]::InvariantCulture)
        $passCommits = [int]$lastPassing.Groups['commits'].Value
        $separatedPasses = [int]$lastPassing.Groups['separated'].Value
        $completedPasses = [int]$lastPassing.Groups['completed'].Value
        if ($maximumLaneOffset -lt 0.25) { throw 'Bots converged on the spline instead of using varied lines.' }
        $passingFailed = $passCommits -lt 1 -or $separatedPasses -lt 1 -or `
            $maximumPassSeparation -lt 2.0 -or $completedPasses -lt 1
        if ($passingFailed) {
            throw "Bots did not execute a measured pass: lane=$maximumLaneOffset m, separation=$maximumPassSeparation m, passes=$passCommits/$separatedPasses/$completedPasses."
        }
        $contactSamples = @([regex]::Matches($combinedLog, 'vehicle contacts (?<contacts>\d+)'))
        if ($contactSamples.Count -lt 2) { throw 'Server log did not contain enough vehicle-contact diagnostics.' }
        $vehicleContacts = [int]$contactSamples[-1].Groups['contacts'].Value
        $maximumContactFrames = [Math]::Max(10, $completedPasses * 40)
        if ($vehicleContacts -gt $maximumContactFrames) {
            throw "Passing produced prolonged vehicle contact: $vehicleContacts contact frames for $completedPasses completed passes (maximum $maximumContactFrames)."
        }
    }
}
if ($FpsGate) {
    Write-Host "PASS: FPS arena navigation, $Slots active combat bots, authoritative-world startup, rifle fire, and graceful shutdown succeeded."
} else {
    Write-Host 'PASS: installed content scan, exact physics preparation, headless startup, and graceful shutdown succeeded.'
}
