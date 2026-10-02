#Requires -Version 7
<#
.SYNOPSIS
    Collects the current CS2 / MetaMod / CounterStrikeSharp state for the
    bot-baseline experiment into E:\CS2MOD\diagnostics\bot-baseline.

.DESCRIPTION
    Read-only. Never launches the game. Run this AFTER the user's manual
    in-game test so the runtime state (plugin inventory, logs, hashes) is
    preserved next to the preflight snapshot.

.EXAMPLE
    pwsh -File scripts\bot-baseline\Collect-BotBaselineDiagnostics.ps1
#>
[CmdletBinding()]
param(
    [string]$OutDir,
    [string]$BackupDir,
    [int]$LogDaysToInclude = 7
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'BotBaseline.Common.ps1')

$cs2 = Find-Cs2Root
if (-not $cs2) { throw 'CS2 installation root not found.' }
$csgo = Get-CsgoDir -Cs2Root $cs2

if (-not $OutDir) {
    $stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')
    $OutDir = "E:\CS2MOD\diagnostics\bot-baseline\$stamp-posttest"
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function Get-FileSha256OrNull([string]$Path) {
    if (Test-Path $Path -PathType Leaf) { return (Get-FileHash $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
    return $null
}

$snapshot = [ordered]@{
    schemaVersion  = 2
    kind           = 'bot-baseline-posttest'
    collectedAtUtc = [DateTime]::UtcNow.ToString('o')
    cs2Running     = (Test-Cs2Running)
    cs2 = [ordered]@{
        root = $cs2
        gameinfoGiSha256 = (Get-FileSha256OrNull (Join-Path $csgo 'gameinfo.gi'))
        gameinfoMetamodLines = @(Get-Content (Join-Path $csgo 'gameinfo.gi') | Where-Object { $_ -match 'metamod|botprofile\.vpk' })
    }
    metamod = [ordered]@{}
    counterstrikesharp = [ordered]@{}
}
$pin = Read-BotImproverLock (Get-RepoRoot)
$snapshot.candidate = [ordered]@{ ref = $pin.ref; status = $pin.status; submodules = $pin.submodules; evidenceLevel = 'disk observation only; no inferred game acceptance' }
$manifestPath = Join-Path $cs2 '../../appmanifest_730.acf'
if (Test-Path -LiteralPath $manifestPath) {
    $snapshot.cs2.buildId = [regex]::Match((Get-Content -LiteralPath $manifestPath -Raw), '"buildid"\s+"(\d+)"').Groups[1].Value
}
if ($BackupDir) {
    $recordPath = Join-Path $BackupDir 'install-record.json'
    $record = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
    $ownership = [Collections.Generic.List[object]]::new()
    if ($record.schemaVersion -eq 2 -and $record.csgoDir -eq $csgo) {
        foreach ($e in $record.entries) {
            $target = Resolve-BaselinePath $csgo $e.relPath
            $live = Get-BaselineIdentity $target
            $state = switch ($e.action) {
                'retained' { 'externally-owned; no restore write' }
                'isolated' {
                    if ($live -or (Get-BaselineIdentity (Resolve-BaselinePath $BackupDir "isolated/$($e.relPath)")) -ne $e.originalSha256) { 'blocked' } else { 'restorable' }
                }
                'created' { if ($null -eq $live -or $live -eq $e.installedSha256) { 'restorable' } else { 'blocked' } }
                default {
                    if ($live -eq $e.installedSha256 -and (Get-BaselineIdentity (Resolve-BaselinePath (Join-Path $BackupDir 'csgo') $e.relPath)) -eq $e.originalSha256) { 'restorable' } else { 'blocked' }
                }
            }
            $ownership.Add([ordered]@{ path=$e.relPath; action=$e.action; liveIdentity=$live; state=$state })
        }
    }
    $snapshot.ownership = [ordered]@{ record=$recordPath; lane=$record.lane; schemaVersion=$record.schemaVersion; safeRestoreAvailable=($record.schemaVersion -eq 2 -and $record.csgoDir -eq $csgo -and @($ownership | Where-Object state -eq 'blocked').Count -eq 0); entries=$ownership }
}

# steam.inf
$steamInf = Join-Path $csgo 'steam.inf'
if (Test-Path $steamInf) {
    $snapshot.cs2.steamInf = @{}
    foreach ($line in Get-Content $steamInf) {
        if ($line -match '^(\w+)=(.*)$') { $snapshot.cs2.steamInf[$Matches[1]] = $Matches[2] }
    }
}

# MetaMod
$metamodDir = Join-Path $csgo 'addons\metamod'
$snapshot.metamod.dirExists = (Test-Path $metamodDir)
if (Test-Path $metamodDir) {
    $coreDll = Join-Path $metamodDir 'bin\win64\metamod.2.cs2.dll'
    $snapshot.metamod.coreDllSha256 = (Get-FileSha256OrNull $coreDll)
    $snapshot.metamod.moduleVdfs = @((Get-ChildItem $metamodDir -Filter '*.vdf' -File -ErrorAction SilentlyContinue).Name)
}

# CounterStrikeSharp
$cssDir = Join-Path $csgo 'addons\counterstrikesharp'
$snapshot.counterstrikesharp.dirExists = (Test-Path $cssDir)
if (Test-Path $cssDir) {
    $apiDll = Join-Path $cssDir 'api\CounterStrikeSharp.API.dll'
    $snapshot.counterstrikesharp.apiDllSha256 = (Get-FileSha256OrNull $apiDll)
    $snapshot.counterstrikesharp.apiDllVersion = (Get-Item $apiDll -ErrorAction SilentlyContinue).VersionInfo.FileVersion
    $pluginsDir = Join-Path $cssDir 'plugins'
    $snapshot.counterstrikesharp.plugins = @(Get-ChildItem $pluginsDir -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        [ordered]@{
            name = $_.Name
            dlls = @(Get-ChildItem $_.FullName -Filter '*.dll' -File -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
                [ordered]@{ name = $_.Name; sha256 = (Get-FileSha256OrNull $_.FullName) }
            })
        }
    })
    # copy recent CSS logs
    $logsDir = Join-Path $cssDir 'logs'
    if (Test-Path $logsDir) {
        $cutoff = (Get-Date).AddDays(-$LogDaysToInclude)
        $logs = @(Get-ChildItem $logsDir -Filter 'log-*.txt' -File | Where-Object { $_.LastWriteTime -ge $cutoff })
        if ($logs.Count -gt 0) {
            $logDest = Join-Path $OutDir 'cssharp-logs'
            New-Item -ItemType Directory -Force -Path $logDest | Out-Null
            $logs | ForEach-Object { Copy-Item $_.FullName $logDest -Force }
            $snapshot.counterstrikesharp.copiedLogs = @($logs | ForEach-Object Name)
        }
    }
}

$snapshot | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutDir 'posttest.json') -Encoding UTF8
Write-Host "DIAGNOSTICS_SNAPSHOT: $OutDir"
Write-Host "CS2_RUNNING: $($snapshot.cs2Running)"
Write-Host "CSS_API_VERSION: $($snapshot.counterstrikesharp.apiDllVersion)"
Write-Host "ACTIVE_PLUGIN_DIRS: $(@($snapshot.counterstrikesharp.plugins).Count)"
