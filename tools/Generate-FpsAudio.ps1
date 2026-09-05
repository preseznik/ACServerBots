[CmdletBinding()]
param(
    [string] $PromptManifest = (Join-Path $PSScriptRoot '..\AssettoServer.RaceControl.Core\Assets\Fps\Audio\audio-prompts.json'),
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\AssettoServer.RaceControl.Core\Assets\Fps\Audio'),
    [string] $ArtifactDirectory = (Join-Path $PSScriptRoot '..\.artifacts\fps-audio'),
    [string[]] $ClipId = @(),
    [ValidateRange(1, 3)]
    [int] $GenerationAttempts = 3,
    [switch] $Force,
    [switch] $NormalizeOnly,
    [switch] $ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$promptManifestPath = [IO.Path]::GetFullPath($PromptManifest)
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$artifactRoot = [IO.Path]::GetFullPath($ArtifactDirectory)
$rawRoot = Join-Path $artifactRoot 'raw'
$normalizedRoot = Join-Path $artifactRoot 'normalized'
$publishedManifestPath = Join-Path $outputRoot 'audio-manifest.json'

if ($NormalizeOnly -and $ValidateOnly) {
    throw 'NormalizeOnly and ValidateOnly cannot be used together'
}

if (-not (Test-Path -LiteralPath $promptManifestPath -PathType Leaf)) {
    throw "FPS audio prompt manifest was not found: $promptManifestPath"
}

$manifest = Get-Content -LiteralPath $promptManifestPath -Raw | ConvertFrom-Json
$clips = @($manifest.clips)
if ($manifest.schemaVersion -ne 1 -or $manifest.catalogVersion -ne 1) {
    throw 'FPS audio prompt manifest schema/catalog version is unsupported'
}
if ($clips.Count -ne 54) {
    throw "FPS audio catalog must contain exactly 54 clips; found $($clips.Count)"
}

$duplicateIds = $clips | Group-Object id | Where-Object Count -gt 1
$duplicateFiles = $clips | Group-Object file | Where-Object Count -gt 1
if ($duplicateIds -or $duplicateFiles) {
    throw 'FPS audio clip IDs and filenames must be unique'
}
$requestedClipIds = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
foreach ($requestedClipId in @($ClipId)) {
    if ([string]::IsNullOrWhiteSpace($requestedClipId)) {
        throw 'ClipId cannot be empty'
    }
    if (-not $requestedClipIds.Add($requestedClipId)) {
        throw "ClipId was specified more than once: $requestedClipId"
    }
}
$knownClipIds = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
foreach ($clip in $clips) {
    [void]$knownClipIds.Add([string]$clip.id)
    if ($clip.file -notmatch '^[a-z0-9_]+\.wav$') {
        throw "Unsafe FPS audio filename: $($clip.file)"
    }
    if ([double]$clip.durationSeconds -lt 0.5 -or [double]$clip.durationSeconds -gt 2.5) {
        throw "FPS audio duration is outside the supported one-shot range: $($clip.id)"
    }
}
$unknownClipIds = @($requestedClipIds | Where-Object { -not $knownClipIds.Contains($_) })
if ($unknownClipIds.Count -gt 0) {
    throw "Unknown FPS audio ClipId: $($unknownClipIds -join ', ')"
}
$hasClipFilter = $requestedClipIds.Count -gt 0
if ($ValidateOnly -and $hasClipFilter) {
    throw 'ClipId cannot be combined with ValidateOnly; validation always covers all 54 clips'
}

$ffmpeg = (Get-Command ffmpeg -ErrorAction Stop).Source
$ffprobe = (Get-Command ffprobe -ErrorAction Stop).Source
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
New-Item -ItemType Directory -Path $rawRoot -Force | Out-Null
New-Item -ItemType Directory -Path $normalizedRoot -Force | Out-Null

$apiKey = [Environment]::GetEnvironmentVariable('ELEVENLABS_API_KEY')

function Get-PeakDb([string] $Path) {
    $volumeReport = & $ffmpeg -hide_banner -nostats -i $Path -af volumedetect `
        -f null NUL 2>&1 | Out-String
    if ($volumeReport -notmatch 'max_volume:\s+(-?[0-9.]+) dB') {
        throw "Could not measure FPS audio peak: $Path"
    }
    [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture)
}

function Get-RawPcmPeakDb([string] $Path) {
    $volumeReport = & $ffmpeg -hide_banner -nostats -f s16le -ar 48000 -ac 2 `
        -i $Path -af volumedetect -f null NUL 2>&1 | Out-String
    if ($volumeReport -notmatch 'max_volume:\s+(-?[0-9.]+) dB') {
        throw "Could not measure raw FPS audio peak: $Path"
    }
    [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture)
}

function Get-WaveDetails([string] $Path) {
    $probeJson = & $ffprobe -v error -select_streams a:0 `
        -show_entries stream=codec_name,sample_rate,channels,sample_fmt,duration `
        -of json -- $Path | Out-String
    if ($LASTEXITCODE -ne 0) { throw "ffprobe failed for $Path" }
    $stream = (ConvertFrom-Json $probeJson).streams[0]
    if ($stream.codec_name -ne 'pcm_s16le' -or [int]$stream.sample_rate -ne 44100 `
            -or [int]$stream.channels -ne 1 -or $stream.sample_fmt -ne 's16') {
        throw "FPS audio is not mono 44.1 kHz PCM S16LE: $Path"
    }
    $peakDb = Get-PeakDb $Path
    if ($peakDb -gt -0.8) { throw "FPS audio peak exceeds the -1 dBFS target: $Path ($peakDb dB)" }
    [pscustomobject]@{
        DurationSeconds = [Math]::Round([double]$stream.duration, 3)
        SampleRate = [int]$stream.sample_rate
        Channels = [int]$stream.channels
        Codec = [string]$stream.codec_name
        PeakDb = $peakDb
    }
}

function ConvertTo-UtcTimestamp($Value) {
    $date = if ($Value -is [datetime]) {
        [DateTimeOffset]$Value
    } else {
        [DateTimeOffset]::Parse([string]$Value,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal)
    }
    $date.UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ssZ')
}

$existingPublishedManifest = if (Test-Path -LiteralPath $publishedManifestPath -PathType Leaf) {
    Get-Content -LiteralPath $publishedManifestPath -Raw | ConvertFrom-Json
} else { $null }
$existingClipById = @{}
if ($null -ne $existingPublishedManifest) {
    foreach ($existingClip in @($existingPublishedManifest.clips)) {
        $existingClipById[[string]$existingClip.id] = $existingClip
    }
}
$catalogGeneratedAt = if ($ValidateOnly -and $null -ne $existingPublishedManifest) {
    ConvertTo-UtcTimestamp $existingPublishedManifest.generatedAtUtc
} else {
    [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
}
$publishedClips = [Collections.Generic.List[object]]::new()
$headers = if ($ValidateOnly -or $NormalizeOnly) {
    $null
} else {
    @{ 'xi-api-key' = $apiKey }
}
$index = 0
foreach ($clip in $clips) {
    $index++
    $finalPath = Join-Path $outputRoot $clip.file
    $selected = -not $hasClipFilter -or $requestedClipIds.Contains([string]$clip.id)
    if ($null -ne $clip.PSObject.Properties['copyOf']) {
        # Reuse an already validated clip, including its true generation provenance.
        # Copies must follow their source in the recipe; no API call or raw candidate is used.
        $sourceEntry = $publishedClips | Where-Object { $_.id -eq $clip.copyOf } | Select-Object -First 1
        if ($null -eq $sourceEntry -or $sourceEntry.category -ne $clip.category `
                -or $sourceEntry.durationSeconds -gt [double]$clip.durationSeconds + 0.15) {
            throw "FPS audio copy requires an earlier compatible source: $($clip.id) -> $($clip.copyOf)"
        }
        $sourcePath = Join-Path $outputRoot ([IO.Path]::GetFileName($sourceEntry.path))
        $copyExists = Test-Path -LiteralPath $finalPath -PathType Leaf
        $copyMatches = $copyExists -and (Get-FileHash -LiteralPath $finalPath -Algorithm SHA256).Hash `
            -ieq $sourceEntry.sha256
        if (-not $copyMatches) {
            if ($ValidateOnly -or -not $selected -or ($copyExists -and -not $Force)) {
                throw "FPS audio copy differs from its source: $($clip.id); select it with -Force to restore it"
            }
            Copy-Item -LiteralPath $sourcePath -Destination $finalPath -Force
        }
        $copyEntry = $sourceEntry | ConvertTo-Json -Depth 8 | ConvertFrom-Json
        $copyEntry.id = [string]$clip.id
        $copyEntry.path = "extension/audio/asrc_fps/$($clip.file)"
        $copyEntry | Add-Member -NotePropertyName copyOf -NotePropertyValue ([string]$clip.copyOf) -Force
        $publishedClips.Add($copyEntry)
        Write-Host ("[{0:d2}/54] Validating {1} (copy of {2})" -f $index, $clip.id, $clip.copyOf)
        continue
    }
    $existingClip = $existingClipById[[string]$clip.id]
    $recorded = $null -ne $clip.PSObject.Properties['recorded'] -and $clip.recorded
    if ($recorded -and ($null -eq $existingClip -or $null -eq $existingClip.PSObject.Properties['source'])) {
        throw "Restore the accepted recording and audio-manifest.json for $($clip.id); it cannot be regenerated with ElevenLabs"
    }
    if ($recorded -and $hasClipFilter -and $selected) {
        throw "Clip $($clip.id) is an accepted source recording, not an ElevenLabs prompt; import a replacement explicitly"
    }
    $shouldProcess = -not $ValidateOnly -and -not $recorded -and $selected `
        -and ((-not (Test-Path -LiteralPath $finalPath)) -or $Force)
    if ($shouldProcess) {
        $rawPath = Join-Path $rawRoot ($clip.id + '.pcm')
        $normalizedPath = Join-Path $normalizedRoot $clip.file
        $preNormalizedPath = Join-Path $normalizedRoot ($clip.id + '.pre.wav')
        if ($NormalizeOnly) {
            if (-not (Test-Path -LiteralPath $rawPath -PathType Leaf)) {
                throw "Raw FPS audio asset is missing: $rawPath"
            }
            Write-Host ("[{0:d2}/54] Normalizing {1}" -f $index, $clip.id)
        } else {
            if ([string]::IsNullOrWhiteSpace($apiKey)) {
                throw 'Set ELEVENLABS_API_KEY in the process environment before generating FPS audio'
            }
            $body = @{
                text = [string]$clip.prompt
                loop = $false
                duration_seconds = [double]$clip.durationSeconds
                prompt_influence = [double]$manifest.promptInfluence
                model_id = [string]$manifest.model
            } | ConvertTo-Json -Compress
            for ($attempt = 1; $attempt -le $GenerationAttempts; $attempt++) {
                Write-Host ("[{0:d2}/54] Generating {1} (attempt {2}/{3})" -f `
                    $index, $clip.id, $attempt, $GenerationAttempts)
                Invoke-WebRequest -Method Post `
                    -Uri 'https://api.elevenlabs.io/v1/sound-generation?output_format=pcm_48000' `
                    -Headers $headers -ContentType 'application/json' -Body $body -OutFile $rawPath | Out-Null
                $rawPeakDb = Get-RawPcmPeakDb $rawPath
                if ($rawPeakDb -gt -45) { break }
                if ($attempt -eq $GenerationAttempts) {
                    throw "ElevenLabs returned an effectively silent FPS audio candidate after $GenerationAttempts attempts: $($clip.id) ($rawPeakDb dB)"
                }
                Write-Warning "ElevenLabs returned an effectively silent candidate for $($clip.id) ($rawPeakDb dB); retrying"
            }
        }
        # ElevenLabs returns headerless stereo PCM for pcm_48000.
        & $ffmpeg -hide_banner -loglevel error -y -f s16le -ar 48000 -ac 2 -i $rawPath `
            -af 'silenceremove=start_periods=1:start_duration=0.01:start_threshold=-55dB:stop_periods=-1:stop_duration=0.04:stop_threshold=-55dB,loudnorm=I=-18:TP=-1:LRA=7,afade=t=in:st=0:d=0.003,areverse,afade=t=in:st=0:d=0.01,areverse' `
            -ac 1 -ar 44100 -c:a pcm_s16le $preNormalizedPath
        if ($LASTEXITCODE -ne 0) { throw "ffmpeg normalization failed for $($clip.id)" }
        $peakGainDb = -1 - (Get-PeakDb $preNormalizedPath)
        $gainText = $peakGainDb.ToString('0.0', [Globalization.CultureInfo]::InvariantCulture)
        & $ffmpeg -hide_banner -loglevel error -y -i $preNormalizedPath `
            -af "volume=${gainText}dB,alimiter=limit=0.891251:level=false" `
            -ac 1 -ar 44100 -c:a pcm_s16le $normalizedPath
        if ($LASTEXITCODE -ne 0) { throw "ffmpeg peak normalization failed for $($clip.id)" }
        Remove-Item -LiteralPath $preNormalizedPath -Force
        Move-Item -LiteralPath $normalizedPath -Destination $finalPath -Force
    }
    elseif (-not (Test-Path -LiteralPath $finalPath -PathType Leaf)) {
        throw "FPS audio asset is missing: $finalPath"
    }
    else {
        Write-Host ("[{0:d2}/54] Validating {1}" -f $index, $clip.id)
    }

    $details = Get-WaveDetails $finalPath
    if ($details.DurationSeconds -lt 0.08 `
            -or $details.DurationSeconds -gt [double]$clip.durationSeconds + 0.15) {
        throw "FPS audio duration is invalid after normalization: $($clip.id) ($($details.DurationSeconds)s)"
    }
    if ($recorded) {
        # Preserve the accepted audio and its real provenance, including during -Force runs.
        $targetPeakDb = if ($clip.category -eq 'locomotion') { -6 } else { -1 }
        if ($existingClip.sha256 -ne (Get-FileHash -LiteralPath $finalPath -Algorithm SHA256).Hash.ToLowerInvariant() `
                -or [Math]::Abs([double]$existingClip.durationSeconds - $details.DurationSeconds) -gt 0.002 `
                -or $existingClip.sampleRate -ne $details.SampleRate `
                -or $existingClip.channels -ne $details.Channels -or $existingClip.codec -ne $details.Codec `
                -or [Math]::Abs([double]$existingClip.peakDb - $details.PeakDb) -gt 0.05 `
                -or [Math]::Abs($details.PeakDb - $targetPeakDb) -gt 0.1 `
                -or $existingClip.source.license -ne 'CC0-1.0' `
                -or [string]::IsNullOrWhiteSpace($existingClip.source.author) `
                -or [string]::IsNullOrWhiteSpace($existingClip.source.url) `
                -or [string]::IsNullOrWhiteSpace($existingClip.source.downloadUrl) `
                -or $existingClip.source.sha256 -notmatch '^[a-f0-9]{64}$' `
                -or [string]::IsNullOrWhiteSpace($existingClip.source.processing)) {
            throw "Accepted FPS recording or provenance does not match its manifest: $($clip.id)"
        }
        $existingClip.importedAtUtc = ConvertTo-UtcTimestamp $existingClip.importedAtUtc
        $publishedClips.Add($existingClip)
        continue
    }
    $clipGeneratedAt = $catalogGeneratedAt
    if (-not $shouldProcess -and $existingClipById.ContainsKey([string]$clip.id)) {
        $existingGeneratedAt = $existingClipById[[string]$clip.id].generatedAtUtc
        if ($null -ne $existingGeneratedAt) {
            $clipGeneratedAt = ConvertTo-UtcTimestamp $existingGeneratedAt
        }
    }
    $publishedClips.Add([ordered]@{
        id = [string]$clip.id
        category = [string]$clip.category
        path = "extension/audio/asrc_fps/$($clip.file)"
        prompt = [string]$clip.prompt
        model = [string]$manifest.model
        generatedAtUtc = $clipGeneratedAt
        durationSeconds = $details.DurationSeconds
        sampleRate = $details.SampleRate
        channels = $details.Channels
        codec = $details.Codec
        peakDb = $details.PeakDb
        sha256 = (Get-FileHash -LiteralPath $finalPath -Algorithm SHA256).Hash.ToLowerInvariant()
    })
}

$published = [ordered]@{
    schemaVersion = 1
    catalogVersion = 1
    generatedAtUtc = $catalogGeneratedAt
    generator = [ordered]@{
        provider = [string]$manifest.provider
        model = [string]$manifest.model
        requestedFormat = 'pcm_48000'
        normalizedFormat = 'mono pcm_s16le 44100 Hz'
        commercialLicenseConfirmedByUser = $true
    }
    clips = $publishedClips
}
$json = $published | ConvertTo-Json -Depth 8
if ($ValidateOnly) {
    if (-not (Test-Path -LiteralPath $publishedManifestPath -PathType Leaf)) {
        throw "Published FPS audio manifest is missing: $publishedManifestPath"
    }
    $existing = Get-Content -LiteralPath $publishedManifestPath -Raw
    if (($existing | ConvertFrom-Json).clips.Count -ne 54) {
        throw 'Published FPS audio manifest does not contain 54 clips'
    }
    if ($existing.Replace("`r`n", "`n").Trim() -ne $json.Replace("`r`n", "`n").Trim()) {
        throw 'Published FPS audio manifest does not match the validated WAV files'
    }
} else {
    [IO.File]::WriteAllText($publishedManifestPath, $json + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false))
}

Write-Host "FPS audio catalog validated: 54 mono PCM WAV files"
