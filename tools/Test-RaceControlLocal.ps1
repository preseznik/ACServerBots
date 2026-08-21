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
    [switch] $VerifyMovingBots,
    [switch] $VerifyPassing
)

$ErrorActionPreference = 'Stop'
if ($VerifyMovingBots -and $SmokeSeconds -lt 30) {
    throw '-VerifyMovingBots requires -SmokeSeconds 30 or greater so the race countdown can finish.'
}
if ($VerifyPassing -and -not $VerifyMovingBots) {
    throw '-VerifyPassing requires -VerifyMovingBots.'
}
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
$preset.Sessions.RaceLaps = 3
$preset.Sessions.PracticeEnabled = -not $VerifyMovingBots
$preset.Bots.Enabled = $true
$preset.Bots.Aggression = $BotAggression
$preset.Bots.PhysicsFidelity = [Enum]::Parse(
    [AssettoServer.RaceControl.Core.Models.PhysicsFidelity], $PhysicsFidelity)

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
$validator = [AssettoServer.RaceControl.Core.Validation.RaceControlValidator]::new()
$renderer = [AssettoServer.RaceControl.Core.Configuration.ServerConfigurationRenderer]::new()
$validation = $validator.Validate($preset, $catalog)
if (-not $validation.IsValid) {
    $errors = $validation.Messages | Where-Object Severity -EQ ([AssettoServer.RaceControl.Core.Validation.ValidationSeverity]::Error)
    throw (($errors | ForEach-Object Message) -join [Environment]::NewLine)
}

$stager = [AssettoServer.RaceControl.Core.Staging.ServerInstanceStager]::new($paths, $validator, $renderer)
Write-Host 'Staging and preparing rigid-body inputs...'
$instance = $stager.StageAsync($preset, $catalog, $null, [Threading.CancellationToken]::None).GetAwaiter().GetResult()
Write-Host "Staged $($instance.SlotCount) slots ($($instance.BotSlotCount) bot-capable) at $($instance.RootPath)"

$stdout = Join-Path $instance.RootPath 'acceptance-stdout.log'
$stderr = Join-Path $instance.RootPath 'acceptance-stderr.log'
$arguments = @('--preset', $instance.PresetName, '--shutdown-file', $instance.ShutdownFilePath)
$serverProcess = Start-Process -FilePath $instance.ExecutablePath -WorkingDirectory $instance.RootPath `
    -ArgumentList $arguments -RedirectStandardOutput $stdout -RedirectStandardError $stderr -WindowStyle Hidden -PassThru
try {
    Start-Sleep -Seconds $SmokeSeconds
    if ($serverProcess.HasExited) {
        throw "Server exited early with code $($serverProcess.ExitCode): $((Get-Content -Raw -LiteralPath $stderr -ErrorAction SilentlyContinue))"
    }
    $startupLog = Get-Content -Raw -LiteralPath $stdout -ErrorAction SilentlyContinue
    if ($startupLog -match 'Fatal exception occurred|\sFTL\]') {
        throw "Server reported a fatal startup error: $startupLog"
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

$combinedLog = (Get-Content -Raw -LiteralPath $stdout -ErrorAction SilentlyContinue) + [Environment]::NewLine + `
    (Get-Content -Raw -LiteralPath $stderr -ErrorAction SilentlyContinue)
if ($combinedLog -notmatch 'Using preset race-control') { throw 'Server log did not confirm the generated preset' }
if ($combinedLog -notmatch 'Shutdown requested by control file') { throw 'Server log did not confirm graceful control-file shutdown' }
if ($VerifyMovingBots) {
    $samples = @([regex]::Matches($combinedLog,
        'Race physics: \d+ bots, Y (?<min>-?\d+(?:\.\d+)?)\.\.(?<max>-?\d+(?:\.\d+)?) m, max speed (?<speed>\d+(?:\.\d+)?) m/s, max rise (?<rise>\d+(?:\.\d+)?) m/s, height error (?<height>\d+(?:\.\d+)?) m, suspension (?<suspension>\d+(?:\.\d+)?) m'))
    if ($samples.Count -lt 2) { throw 'Server log did not contain enough rigid-body diagnostics.' }
    $finalSpeed = [double]::Parse($samples[-1].Groups['speed'].Value, [Globalization.CultureInfo]::InvariantCulture)
    $maximumRise = ($samples | ForEach-Object {
        [double]::Parse($_.Groups['rise'].Value, [Globalization.CultureInfo]::InvariantCulture)
    } | Measure-Object -Maximum).Maximum
    $finalHeightError = [double]::Parse($samples[-1].Groups['height'].Value, [Globalization.CultureInfo]::InvariantCulture)
    $maximumSuspensionCompression = ($samples | ForEach-Object {
        [double]::Parse($_.Groups['suspension'].Value, [Globalization.CultureInfo]::InvariantCulture)
    } | Measure-Object -Maximum).Maximum
    if ($finalHeightError -gt 2.5) { throw "Bots left the road surface: final spline height error was $finalHeightError m." }
    if ($maximumRise -gt 12) { throw "Bots were launched from the road: maximum upward speed was $maximumRise m/s." }
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
Write-Host 'PASS: installed content scan, exact physics preparation, headless startup, and graceful shutdown succeeded.'
