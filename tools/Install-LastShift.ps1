[CmdletBinding()]
param(
    [string]$AssettoCorsaRoot = 'C:\Program Files (x86)\Steam\steamapps\common\assettocorsa',
    [string]$BuildDirectory = (Join-Path $PSScriptRoot '..\artifacts\last-shift-v031'),
    [switch]$ReplaceExisting,
    [string]$PreviousInstallReceipt
)
$ErrorActionPreference = 'Stop'
$buildRoot = [IO.Path]::GetFullPath($BuildDirectory)
$sourceRoot = Join-Path $buildRoot 'content\tracks\last_shift'
$targetRoot = [IO.Path]::GetFullPath((Join-Path $AssettoCorsaRoot 'content\tracks\last_shift'))
if (-not (Test-Path -LiteralPath (Join-Path $AssettoCorsaRoot 'acs.exe') -PathType Leaf)) {
    throw "Assetto Corsa installation not found: $AssettoCorsaRoot"
}
$backupRoot = $null
$oldFiles = @()
if (Test-Path -LiteralPath $targetRoot) {
    if (-not $ReplaceExisting -or -not $PreviousInstallReceipt) {
        throw 'Replacing Last Shift requires -ReplaceExisting and its previous install receipt.'
    }
    $previous = Get-Content -LiteralPath $PreviousInstallReceipt -Raw | ConvertFrom-Json
    if ([IO.Path]::GetFullPath($previous.Target) -ine $targetRoot) { throw 'Previous receipt target differs.' }
    $oldFiles = @(Get-ChildItem -LiteralPath $targetRoot -Recurse -File)
    if ($oldFiles.Count -ne $previous.Files.Count) { throw 'Installed map has extra or missing files; refusing replacement.' }
    foreach ($file in $previous.Files) {
        $installedPath = [IO.Path]::GetFullPath((Join-Path $targetRoot $file.File))
        if (-not $installedPath.StartsWith($targetRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Previous receipt path escapes the track folder.'
        }
        if ((Get-FileHash -LiteralPath $installedPath -Algorithm SHA256).Hash -ine $file.Sha256) {
            throw "Installed map was edited since the previous install: $($file.File)"
        }
    }
    # Race Control can keep preview images open. Detect that before replacing
    # any files so a locked thumbnail cannot leave a partially copied update.
    foreach ($file in $oldFiles) {
        try {
            $probe = [IO.File]::Open($file.FullName, [IO.FileMode]::Open,
                [IO.FileAccess]::ReadWrite, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
            $probe.Dispose()
        } catch {
            throw "Installed file is locked or not writable; no files were changed: $($file.FullName). Close the application using this map before retrying."
        }
    }
    $backupRoot = Join-Path $buildRoot ('backups\last_shift-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
    if (Test-Path -LiteralPath $backupRoot) { throw 'Backup already exists.' }
    New-Item -ItemType Directory -Path (Split-Path $backupRoot -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $targetRoot -Destination $backupRoot -Recurse
    foreach ($file in $previous.Files) {
        if ((Get-FileHash -LiteralPath (Join-Path $backupRoot $file.File) -Algorithm SHA256).Hash -ine $file.Sha256) {
            throw "Backup verification failed: $($file.File)"
        }
    }
}
$validation = Get-Content -LiteralPath (Join-Path $buildRoot 'validation\validation.json') -Raw | ConvertFrom-Json
if (-not $validation.passed) { throw 'Last Shift offline validation has not passed.' }
foreach ($model in $validation.models) {
    $actual = (Get-FileHash -LiteralPath (Join-Path $sourceRoot $model.file) -Algorithm SHA256).Hash
    if ($actual -ine $model.sha256) { throw "Validated model changed: $($model.file)" }
}
$materialReport = Get-Content -LiteralPath (Join-Path $buildRoot 'validation\materials.json') -Raw | ConvertFrom-Json
if (-not $materialReport.passed) { throw 'Last Shift material validation has not passed.' }
foreach ($pair in @(@($materialReport.visual, 'last_shift.kn5'), @($materialReport.collision, 'last_shift_collision.kn5'))) {
    if ($pair[0].file -cne $pair[1]) { throw 'Material receipt has an unexpected model identity.' }
    if ((Get-FileHash -LiteralPath (Join-Path $sourceRoot $pair[1]) -Algorithm SHA256).Hash -ine $pair[0].sha256) {
        throw "Material-validated model changed: $($pair[1])"
    }
}
$conversionReport = Get-Content -LiteralPath (Join-Path $sourceRoot 'conversion-report.json') -Raw | ConvertFrom-Json
if ($conversionReport.detailSourceSha256) {
    $detailReport = Get-Content -LiteralPath (Join-Path $buildRoot 'validation\details.json') -Raw | ConvertFrom-Json
    if (-not $detailReport.passed) { throw 'Last Shift detail validation has not passed.' }
    foreach ($model in $detailReport.models) {
        if ($model.file -notin @('last_shift.kn5','last_shift_collision.kn5')) { throw 'Unexpected detail model identity.' }
        if ((Get-FileHash -LiteralPath (Join-Path $sourceRoot $model.file)).Hash -ine $model.sha256) {
            throw "Detail-validated model changed: $($model.file)"
        }
    }
}
New-Item -ItemType Directory -Path $targetRoot -Force | Out-Null
$sourceFiles = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -File)
$createdFiles = [Collections.Generic.List[string]]::new()
try {
    foreach ($file in $sourceFiles) {
        $relative = [IO.Path]::GetRelativePath($sourceRoot, $file.FullName)
        $destination = [IO.Path]::GetFullPath((Join-Path $targetRoot $relative))
        if (-not $destination.StartsWith($targetRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Source file path escapes the track folder.'
        }
        if (Test-Path -LiteralPath $destination) {
            if (-not (Test-Path -LiteralPath $destination -PathType Leaf)) {
                throw "File destination is an existing directory: $relative"
            }
        } else {
            # Record before copying so a partially written new file is also removed.
            $createdFiles.Add($destination)
        }
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    }
    $records = foreach ($file in $sourceFiles) {
        $relative = [IO.Path]::GetRelativePath($sourceRoot, $file.FullName)
        $expected = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        $actual = (Get-FileHash -LiteralPath (Join-Path $targetRoot $relative) -Algorithm SHA256).Hash
        if ($expected -ne $actual) { throw "Installed file differs: $relative" }
        [PSCustomObject]@{ File = $relative; Sha256 = $actual.ToLowerInvariant() }
    }
} catch {
    $installError = $_
    $rollbackErrors = [Collections.Generic.List[string]]::new()
    foreach ($path in $createdFiles) {
        try {
            if (Test-Path -LiteralPath $path -PathType Leaf) {
                Remove-Item -LiteralPath $path -Force
            }
        } catch {
            $rollbackErrors.Add("Could not remove new file '$path': $($_.Exception.Message)")
        }
    }
    if ($backupRoot) {
        foreach ($file in $previous.Files) {
            try {
                $destination = Join-Path $targetRoot $file.File
                Copy-Item -LiteralPath (Join-Path $backupRoot $file.File) -Destination $destination -Force
                if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ine $file.Sha256) {
                    throw 'Restored file hash differs from the previous receipt.'
                }
            } catch {
                $rollbackErrors.Add("Could not restore '$($file.File)': $($_.Exception.Message)")
            }
        }
    }
    if ($rollbackErrors.Count) {
        throw "Install failed: $($installError.Exception.Message). Rollback incomplete; backup: '$backupRoot'. $($rollbackErrors -join ' ')"
    }
    Write-Warning 'Installation failed; copied changes were rolled back. Existing directories were preserved.'
    throw $installError
}
[PSCustomObject]@{
    InstalledUtc = [DateTimeOffset]::UtcNow
    Target = $targetRoot
    Source = $sourceRoot
    PreviousVersionBackup = $backupRoot
    FileCount = @($records).Count
    Files = @($records)
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $buildRoot 'install-receipt.json') -Encoding utf8
Write-Host "Installed and hash-verified $(@($records).Count) files: $targetRoot"
