#Requires -Version 7
[CmdletBinding()]
param(
    [ValidateRange(10,1800)][int]$Seconds = 600,
    [string]$OutputRoot = 'E:\CS2MOD\diagnostics\match-setup',
    [string]$Cs2Root = 'E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive',
    [string]$PresentMonCsv
)
$ErrorActionPreference = 'Stop'
# Opt-in observation of an already running game. Never launches or attaches a debugger.
$gameProcess = Get-Process cs2 -ErrorAction Stop
if (@($gameProcess).Count -ne 1) { throw 'Expected exactly one existing CS2 process' }
$sampleRoot = Join-Path $OutputRoot ('performance-' + [DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss'))
if (Test-Path -LiteralPath $sampleRoot) { throw 'Output exists' }
New-Item -ItemType Directory -Path $sampleRoot | Out-Null
$rows = [System.Collections.Generic.List[object]]::new()
$watch = [Diagnostics.Stopwatch]::StartNew()
while ($watch.Elapsed.TotalSeconds -lt $Seconds) {
    try { $gameProcess.Refresh(); if ($gameProcess.HasExited) { break } } catch { break }
    $rows.Add([pscustomobject]@{
        utc = [DateTimeOffset]::UtcNow.ToString('o'); elapsedSeconds = $watch.Elapsed.TotalSeconds
        pid = $gameProcess.Id; privateBytes = $gameProcess.PrivateMemorySize64
        workingSetBytes = $gameProcess.WorkingSet64; handles = $gameProcess.HandleCount
        threads = $gameProcess.Threads.Count; cpuSeconds = $gameProcess.TotalProcessorTime.TotalSeconds
    })
    Start-Sleep -Seconds 2
}
$rows | Export-Csv -LiteralPath (Join-Path $sampleRoot 'process.csv') -NoTypeInformation
$logs = Join-Path $Cs2Root 'game\csgo\addons\counterstrikesharp\logs'
Get-ChildItem -LiteralPath $logs -File | Where-Object LastWriteTimeUtc -GE ([DateTime]::UtcNow.AddSeconds(-$Seconds - 120)) | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $sampleRoot }
$status = 'E:\CS2MOD\app-data\match-setup\runtime-status.json'
if (Test-Path -LiteralPath $status) { Copy-Item -LiteralPath $status -Destination $sampleRoot }
if ($PresentMonCsv) { Copy-Item -LiteralPath $PresentMonCsv -Destination (Join-Path $sampleRoot 'presentmon.csv') }
[pscustomobject]@{ output = $sampleRoot; samples = $rows.Count; fps = 'unknown without frame capture'; frametime = 'unknown without frame capture'; longFrames = 'unknown without frame capture'; botCounts = 'round diagnostics from LocalKitMatchSetup; otherwise record manually' } | ConvertTo-Json
# Compare opening/middle/late rounds at the same map/settings/bot count. A PresentMon
# capture is optional user-provided evidence; no installation or ETW service is added.
