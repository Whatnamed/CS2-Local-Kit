#Requires -Version 7
<#
.SYNOPSIS
    Restores the CS2 installation to the exact pre-install state recorded by
    Install-BotBaseline.ps1.

.DESCRIPTION
    Replays install-record.json from a backup directory:
      - 'overwritten'/'modified' entries are restored byte-for-byte from the backup;
      - 'created' entries are deleted (they did not exist before installation);
      - 'isolated' entries are moved back to their original paths;
      - everything else is left untouched; no broad cleaning of the CS2 tree.

    After restoring, every entry is re-verified against the recorded original
    hashes and the result is reported. Default is a preview; pass -Apply to
    execute.

.EXAMPLE
    pwsh -File scripts\bot-baseline\Restore-BotBaseline.ps1 -BackupDir E:\CS2MOD\backups\bot-baseline\20260928-000000 -Apply
#>
[CmdletBinding()]
param(
    [string]$BackupDir,
    [string]$Cs2Root,
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'BotBaseline.Common.ps1')

if (-not $BackupDir) {
    $latest = Get-ChildItem 'E:\CS2MOD\backups\bot-baseline' -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | Select-Object -First 1
    if (-not $latest) { throw 'No backup directories found under E:\CS2MOD\backups\bot-baseline. Pass -BackupDir.' }
    $BackupDir = $latest.FullName
    Write-Host "Using most recent backup: $BackupDir"
}
$recordPath = Join-Path $BackupDir 'install-record.json'
if (-not (Test-Path $recordPath)) { throw "install-record.json not found in $BackupDir" }
$record = Get-Content $recordPath -Raw | ConvertFrom-Json

$csgo = $record.csgoDir
if (-not (Test-Path $csgo)) { throw "Recorded csgoDir no longer exists: $csgo" }
if ($Cs2Root -and ((Get-CsgoDir -Cs2Root $Cs2Root) -ne $csgo)) {
    throw "Recorded csgoDir ($csgo) does not match -Cs2Root."
}
Write-Host "Target CS2 install: $csgo"

if (Test-Cs2Running) {
    throw 'cs2.exe is currently RUNNING. Restore is intentionally skipped; close the game first.'
}

$backupCsgo = Join-Path $BackupDir 'csgo'

# plan the restore first
$plan = [System.Collections.Generic.List[object]]::new()
foreach ($e in $record.entries) {
    $targetPath = Join-Path $csgo $e.relPath
    switch ($e.action) {
        'overwritten' { $plan.Add([pscustomobject]@{ entry = $e; verb = 'restore-file' }) }
        'modified'    { $plan.Add([pscustomobject]@{ entry = $e; verb = 'restore-file' }) }
        'created'     { $plan.Add([pscustomobject]@{ entry = $e; verb = 'remove' }) }
        'isolated'    { $plan.Add([pscustomobject]@{ entry = $e; verb = 'move-back' }) }
        default       { throw "Unknown action '$($e.action)' in install record." }
    }
}

$verbCount = @{}
foreach ($p in $plan) { $verbCount[$p.verb] = 1 + ($verbCount[$p.verb] ?? 0) }
Write-Host ('Restore plan: ' + (($verbCount.Keys | Sort-Object | ForEach-Object { "$_=$($verbCount[$_])" }) -join ', '))

if (-not $Apply) {
    Write-Host ''
    Write-Host 'PREVIEW ONLY. Re-run with -Apply to restore.'
    return
}

$results = [System.Collections.Generic.List[object]]::new()
foreach ($p in $plan) {
    $e = $p.entry
    $targetPath = Join-Path $csgo $e.relPath
    switch ($p.verb) {
        'restore-file' {
            Copy-Item (Join-Path $backupCsgo $e.relPath) $targetPath -Force
            $ok = (Get-FileSha256Lower $targetPath) -eq $e.originalSha256
            $results.Add([pscustomobject]@{ relPath = $e.relPath; verb = 'restore-file'; verified = $ok })
        }
        'remove' {
            if (Test-Path $targetPath) { Remove-Item $targetPath -Recurse -Force }
            $ok = -not (Test-Path $targetPath)
            $results.Add([pscustomobject]@{ relPath = $e.relPath; verb = 'remove'; verified = $ok })
        }
        'move-back' {
            $isolatedPath = Join-Path $BackupDir "isolated\$($e.relPath)"
            if (Test-Path $isolatedPath) {
                $parent = Split-Path $targetPath -Parent
                if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
                if (Test-Path $targetPath) { throw "Isolation restore conflict: $targetPath already exists." }
                Move-Item $isolatedPath $targetPath -Force
            }
            $ok = Test-Path $targetPath
            $results.Add([pscustomobject]@{ relPath = $e.relPath; verb = 'move-back'; verified = $ok })
        }
    }
}

$failed = @($results | Where-Object { -not $_.verified })
$failed | ForEach-Object { Write-Host ("[FAIL] {0} ({1})" -f $_.relPath, $_.verb) }
Write-Host ''
Write-Host ("Restore summary: {0} entries, {1} failed verification" -f $results.Count, $failed.Count)

if ($failed.Count -eq 0) {
    Write-Host 'RESTORE: OK (all entries verified against pre-install hashes)'
} else {
    Write-Host 'RESTORE: INCOMPLETE - review failures above'
    exit 1
}
