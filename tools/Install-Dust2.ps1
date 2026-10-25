[CmdletBinding()]
param(
    [string]$BuildRoot = (Join-Path $PSScriptRoot '..\.artifacts\de-dust2-conversion\stage'),
    [string]$AssettoCorsaRoot = 'C:\Program Files (x86)\Steam\steamapps\common\assettocorsa'
)
$ErrorActionPreference = 'Stop'
$build = [IO.Path]::GetFullPath($BuildRoot)
$source = Join-Path $build 'package\content\tracks\css_dust2'
$tracks = [IO.Path]::GetFullPath((Join-Path $AssettoCorsaRoot 'content\tracks'))
$destination = Join-Path $tracks 'css_dust2'
$temporary = Join-Path $tracks ('.css_dust2-install-' + [Guid]::NewGuid().ToString('N'))
$manifest = Get-Content -LiteralPath (Join-Path $build 'package-files.json') -Raw | ConvertFrom-Json
$entries = @($manifest | Where-Object path -Like 'content/tracks/css_dust2/*')
if ($entries.Count -lt 15) { throw 'Incomplete candidate manifest.' }
if (-not (Test-Path -LiteralPath $tracks -PathType Container)) { throw 'Assetto Corsa track directory is missing.' }
if (Test-Path -LiteralPath $destination) { throw 'css_dust2 is already installed; refusing an unreviewed overwrite.' }
foreach ($entry in $entries) {
    $relative = $entry.path.Substring('content/tracks/css_dust2/'.Length)
    $candidate = [IO.Path]::GetFullPath((Join-Path $source $relative))
    if (-not $candidate.StartsWith($source + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid manifest path.' }
    if ((Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash -ne $entry.sha256) { throw "Candidate hash mismatch: $relative" }
}
New-Item -ItemType Directory -Path $temporary | Out-Null
foreach ($entry in $entries) {
    $relative = $entry.path.Substring('content/tracks/css_dust2/'.Length)
    $target = [IO.Path]::GetFullPath((Join-Path $temporary $relative))
    if (-not $target.StartsWith($temporary + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid install path.' }
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $source $relative) -Destination $target
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $entry.sha256) { throw "Installed hash mismatch: $relative" }
}
# Both resolved paths must remain inside the explicitly selected track directory.
foreach ($path in @($temporary, $destination)) {
    if (-not [IO.Path]::GetFullPath($path).StartsWith($tracks + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Install destination escaped track directory.' }
}
Move-Item -LiteralPath $temporary -Destination $destination
[pscustomobject]@{ Track = 'css_dust2'; Destination = $destination; VerifiedFiles = $entries.Count; RuntimeValidated = $false } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $build 'install-report.json') -Encoding utf8
Write-Output "Installed Dust II for AC/CSP playtesting: $destination"
