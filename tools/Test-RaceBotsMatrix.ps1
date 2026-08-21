[CmdletBinding()]
param(
    [string] $AssettoCorsaRoot = 'C:\Program Files (x86)\Steam\steamapps\common\assettocorsa',
    [string] $RaceControlBuild,
    [string[]] $TrackKeys,
    [string[]] $CarModels = @('bmw_m3_e30'),
    [ValidateRange(2, 32)]
    [int] $Slots = 8,
    [ValidateRange(1, 999)]
    [int] $RaceLaps = 2,
    [int[]] $Seeds = @(1, 2, 3),
    [ValidateSet('Efficient', 'Balanced', 'High')]
    [string] $PhysicsFidelity = 'Efficient',
    [ValidateRange(0, 1)]
    [double] $BotAggression = 0.5,
    [ValidateRange(1, 1440)]
    [int] $MaximumSimulatedMinutes = 45,
    [ValidateRange(10, 86400)]
    [int] $MaximumWallSeconds = 300,
    [ValidateRange(1, 12)]
    [int] $MaximumTracks = 4,
    [string] $OutputRoot,
    [switch] $FailOnAnomaly
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($RaceControlBuild)) {
    $RaceControlBuild = Join-Path $repositoryRoot 'out-race-control'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot '.artifacts\race-bot-matrix'
}
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$coreAssembly = Join-Path $RaceControlBuild 'lib\AssettoServer.RaceControl.Core.dll'
$serverPayload = Join-Path $RaceControlBuild 'lib\Server'
if (-not (Test-Path -LiteralPath $coreAssembly -PathType Leaf)) {
    throw "Race Control core not found: $coreAssembly"
}
$serverExecutable = Join-Path $serverPayload 'AssettoServer.exe'
if (-not (Test-Path -LiteralPath $serverExecutable -PathType Leaf)) {
    throw "Bundled server not found: $serverExecutable"
}

[IO.Directory]::CreateDirectory($OutputRoot) | Out-Null
Add-Type -Path $coreAssembly
$scanner = [AssettoServer.RaceControl.Core.Content.AcContentScanner]::new()
$catalog = $scanner.Scan($AssettoCorsaRoot)
Write-Host "Scanned $($catalog.Cars.Count) cars and $($catalog.Tracks.Count) track layouts"

$selectedCars = @($CarModels | ForEach-Object {
    $requested = $_
    $car = $catalog.Cars | Where-Object Id -IEQ $requested | Select-Object -First 1
    if ($null -eq $car) { throw "Car is not installed: $requested" }
    if (-not $car.HasCollider) { throw "Car has no collider.kn5: $requested" }
    if ($car.Skins.Count -eq 0) { throw "Car has no installed skins: $requested" }
    $car
})
if ($selectedCars.Count -eq 0) { throw 'At least one car model is required.' }
$Seeds = @($Seeds | Select-Object -Unique)
if ($Seeds.Count -eq 0) { throw 'At least one simulation seed is required.' }

function Resolve-TrackKey([string] $key) {
    $parts = @($key -split '/', 2)
    $trackId = $parts[0]
    $layoutId = if ($parts.Count -gt 1) { $parts[1] } else { '' }
    return $catalog.Tracks | Where-Object {
        $_.TrackId -ieq $trackId -and $_.LayoutId -ieq $layoutId
    } | Select-Object -First 1
}

$tracks = [Collections.Generic.List[object]]::new()
if ($TrackKeys.Count -gt 0) {
    foreach ($key in $TrackKeys) {
        $track = Resolve-TrackKey $key
        if ($null -eq $track) { throw "Track layout is not installed: $key" }
        $tracks.Add($track)
    }
} else {
    foreach ($key in @('magione', 'ks_red_bull_ring/layout_gp', 'ks_nordschleife/nordschleife')) {
        $track = Resolve-TrackKey $key
        if ($null -ne $track -and -not $tracks.Contains($track)) { $tracks.Add($track) }
    }
    $fallbacks = $catalog.Tracks | Where-Object {
        $_.HasModels -and $_.HasFastLane -and $_.PitBoxes -ge 2 -and -not $tracks.Contains($_)
    } | Sort-Object @{ Expression = { [Math]::Abs($_.PitBoxes - $Slots) } }, TrackId, LayoutId
    foreach ($track in $fallbacks) {
        if ($tracks.Count -ge $MaximumTracks) { break }
        $tracks.Add($track)
    }
}
$tracks = @($tracks | Select-Object -First $MaximumTracks)
if ($tracks.Count -eq 0) { throw 'No usable track layouts were selected.' }

$paths = [AssettoServer.RaceControl.Core.Infrastructure.RaceControlPaths]::new(
    (Join-Path $OutputRoot 'staging'))
$validator = [AssettoServer.RaceControl.Core.Validation.RaceControlValidator]::new()
$renderer = [AssettoServer.RaceControl.Core.Configuration.ServerConfigurationRenderer]::new()
$stager = [AssettoServer.RaceControl.Core.Staging.ServerInstanceStager]::new(
    $paths, $validator, $renderer)
$fidelity = [Enum]::Parse(
    [AssettoServer.RaceControl.Core.Models.PhysicsFidelity], $PhysicsFidelity)
$runs = [Collections.Generic.List[object]]::new()
$hardAnomalies = @('stuck', 'surface_height', 'suspension_compression', 'overturned', 'vertical_launch')

foreach ($track in $tracks) {
    $effectiveSlots = [Math]::Min($Slots, $track.PitBoxes)
    if ($effectiveSlots -lt $Slots) {
        Write-Warning "$($track.Key) exposes $($track.PitBoxes) pits; reducing this run to $effectiveSlots bots."
    }
    foreach ($seed in $Seeds) {
        $safeKey = ($track.Key -replace '[^a-zA-Z0-9_.-]', '-')
        $runName = "$safeKey-seed-$seed"
        $runOutput = Join-Path (Join-Path $OutputRoot 'runs') $runName
        [IO.Directory]::CreateDirectory($runOutput) | Out-Null
        foreach ($oldOutput in @('summary.json', 'events.jsonl', 'samples.jsonl',
            'server-stdout.log', 'server-stderr.log')) {
            $oldPath = Join-Path $runOutput $oldOutput
            if ([IO.File]::Exists($oldPath)) { [IO.File]::Delete($oldPath) }
        }

        $preset = [AssettoServer.RaceControl.Core.Models.RaceControlPreset]::CreateDefault(
            $AssettoCorsaRoot, $serverPayload)
        $preset.Name = "Race simulation $runName"
        $preset.ServerName = "Race simulation $runName"
        $preset.TrackId = $track.TrackId
        $preset.TrackLayoutId = $track.LayoutId
        $preset.Sessions.PracticeEnabled = $false
        $preset.Sessions.RaceLaps = $RaceLaps
        $preset.Bots.Enabled = $true
        $preset.Bots.Aggression = $BotAggression
        $preset.Bots.PhysicsFidelity = $fidelity
        $preset.Bots.AllowMidRaceTakeover = $false
        $preset.Bots.RestartWhenFirstHumanConnects = $false
        $preset.Network.BindAddress = '127.0.0.1'

        $grid = [Collections.Generic.List[AssettoServer.RaceControl.Core.Models.GridSlotPreset]]::new()
        for ($index = 0; $index -lt $effectiveSlots; $index++) {
            $car = $selectedCars[$index % $selectedCars.Count]
            $slot = [AssettoServer.RaceControl.Core.Models.GridSlotPreset]::new()
            $slot.CarId = $car.Id
            $slot.SkinId = $car.Skins[$index % $car.Skins.Count].Id
            $slot.DriverName = "Simulation Bot $($index + 1)"
            $slot.Mode = [AssettoServer.RaceControl.Core.Models.SlotMode]::Fixed
            $grid.Add($slot)
        }
        $preset.Grid = $grid

        $validation = $validator.Validate($preset, $catalog)
        if (-not $validation.IsValid) {
            $errors = $validation.Messages | Where-Object Severity -EQ (
                [AssettoServer.RaceControl.Core.Validation.ValidationSeverity]::Error)
            throw (($errors | ForEach-Object Message) -join [Environment]::NewLine)
        }

        Write-Host "[$runName] staging $effectiveSlots bots..."
        $instance = $stager.StageAsync($preset, $catalog, $null,
            [Threading.CancellationToken]::None).GetAwaiter().GetResult()
        $stdoutPath = Join-Path $runOutput 'server-stdout.log'
        $stderrPath = Join-Path $runOutput 'server-stderr.log'
        $processInfo = [Diagnostics.ProcessStartInfo]::new()
        $processInfo.FileName = $instance.ExecutablePath
        $processInfo.WorkingDirectory = $instance.RootPath
        $processInfo.UseShellExecute = $false
        $processInfo.CreateNoWindow = $true
        $processInfo.RedirectStandardOutput = $true
        $processInfo.RedirectStandardError = $true
        foreach ($argument in @(
            '--preset', $instance.PresetName,
            '--simulate-race',
            '--simulation-output', $runOutput,
            '--simulation-seed', "$seed",
            '--simulation-max-minutes', "$MaximumSimulatedMinutes",
            '--simulation-max-wall-seconds', "$MaximumWallSeconds",
            '--simulation-sample-ms', '500'
        )) { $processInfo.ArgumentList.Add($argument) }

        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $processInfo
        $startedAt = [DateTimeOffset]::UtcNow
        if (-not $process.Start()) { throw "Failed to start simulation $runName" }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(($MaximumWallSeconds + 30) * 1000)) {
            $process.Kill($true)
            $process.WaitForExit()
        }
        [IO.File]::WriteAllText($stdoutPath, $stdoutTask.GetAwaiter().GetResult())
        [IO.File]::WriteAllText($stderrPath, $stderrTask.GetAwaiter().GetResult())

        $summaryPath = Join-Path $runOutput 'summary.json'
        if (Test-Path -LiteralPath $summaryPath -PathType Leaf) {
            $summary = Get-Content -Raw -LiteralPath $summaryPath | ConvertFrom-Json
            $anomalyNames = @($summary.anomalies.PSObject.Properties | Where-Object Value -GT 0 |
                ForEach-Object Name)
            $hardFailureNames = @($anomalyNames | Where-Object { $_ -in $hardAnomalies })
            $completeResults = @($summary.results | Where-Object {
                $_.hasCompletedLastLap -or $_.isDnf
            }).Count
            $rejectedAnomalies = if ($FailOnAnomaly) { $anomalyNames } else { $hardFailureNames }
            $passed = $process.ExitCode -eq 0 -and $summary.status -eq 'completed' -and
                $completeResults -eq $summary.botCount -and $rejectedAnomalies.Count -eq 0
            $runs.Add([pscustomobject]@{
                Track = $track.Key
                Seed = $seed
                Bots = $summary.botCount
                Status = $summary.status
                SimulatedSeconds = [Math]::Round($summary.simulatedMilliseconds / 1000, 1)
                WallSeconds = [Math]::Round($summary.wallMilliseconds / 1000, 1)
                Factor = [Math]::Round($summary.realTimeFactor, 1)
                Anomalies = ($anomalyNames -join ', ')
                Passes = "$($summary.passCommits)/$($summary.separatedPasses)/$($summary.completedPasses)"
                ExitCode = $process.ExitCode
                Passed = $passed
                Output = $runOutput
                Instance = $instance.RootPath
            })
        } else {
            $runs.Add([pscustomobject]@{
                Track = $track.Key
                Seed = $seed
                Bots = $effectiveSlots
                Status = 'no_summary'
                SimulatedSeconds = 0
                WallSeconds = [Math]::Round(([DateTimeOffset]::UtcNow - $startedAt).TotalSeconds, 1)
                Factor = 0
                Anomalies = 'server failed before producing a summary'
                Passes = '0/0/0'
                ExitCode = $process.ExitCode
                Passed = $false
                Output = $runOutput
                Instance = $instance.RootPath
            })
        }
        $last = $runs[$runs.Count - 1]
        Write-Host "[$runName] $($last.Status), $($last.Factor)x, anomalies: $($last.Anomalies)"
    }
}

$jsonReport = Join-Path $OutputRoot 'matrix-summary.json'
[IO.File]::WriteAllText($jsonReport, ($runs | ConvertTo-Json -Depth 6))
$markdown = [Text.StringBuilder]::new()
[void]$markdown.AppendLine('# Race bot simulation matrix')
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("Generated: $([DateTimeOffset]::Now.ToString('u'))")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine('| Track | Seed | Bots | Status | Sim s | Wall s | Factor | Pass C/S/F | Anomalies | Result |')
[void]$markdown.AppendLine('|---|---:|---:|---|---:|---:|---:|---|---|---|')
foreach ($run in $runs) {
    $result = if ($run.Passed) { 'PASS' } else { 'FAIL' }
    $anomalies = if ([string]::IsNullOrWhiteSpace($run.Anomalies)) { 'none' } else { $run.Anomalies }
    [void]$markdown.AppendLine("| $($run.Track) | $($run.Seed) | $($run.Bots) | $($run.Status) | $($run.SimulatedSeconds) | $($run.WallSeconds) | $($run.Factor)x | $($run.Passes) | $anomalies | $result |")
}
$failed = @($runs | Where-Object { -not $_.Passed })
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("Passed $($runs.Count - $failed.Count) of $($runs.Count) runs.")
$markdownReport = Join-Path $OutputRoot 'matrix-report.md'
[IO.File]::WriteAllText($markdownReport, $markdown.ToString())
Write-Host "Matrix report: $markdownReport"

if ($failed.Count -gt 0) {
    throw "$($failed.Count) of $($runs.Count) race simulation runs failed acceptance."
}
