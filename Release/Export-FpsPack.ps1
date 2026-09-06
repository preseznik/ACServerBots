param([Parameter(Mandatory)][string]$Assembly, [Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference = 'Stop'
Add-Type -Path $Assembly
$stream = [IO.File]::Create($Destination)
try {
    [AssettoServer.RaceControl.Core.Staging.FpsClientPackBuilder]::WriteAsync(
        $stream, '', [Threading.CancellationToken]::None).GetAwaiter().GetResult()
} finally { $stream.Dispose() }
