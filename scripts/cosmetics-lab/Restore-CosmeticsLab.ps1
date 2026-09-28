# Restore-CosmeticsLab.ps1 — exact restore of a CosmeticsLab C1 install from its backup dir.
#
# Usage:
#   pwsh -NoProfile -File scripts\cosmetics-lab\Restore-CosmeticsLab.ps1 -BackupDir <dir>          # dry run
#   pwsh -NoProfile -File scripts\cosmetics-lab\Restore-CosmeticsLab.ps1 -BackupDir <dir> -Apply
#
# Removes only files the C1 install created, restores modified files byte-for-byte, verifies
# hashes afterwards. Historical logs, metamod user data and unrelated files are never touched.

param(
    [Parameter(Mandatory = $true)][string]$BackupDir,
    [switch]$Apply,
    # Escape hatch for created files whose content changed since install (e.g. plugin wrote
    # to them). Default is to refuse and report.
    [switch]$Force
)

. (Join-Path $PSScriptRoot 'CosmeticsLab.Common.ps1')

$recordPath = Join-Path $BackupDir 'install-record.json'
if (-not (Test-Path -LiteralPath $recordPath -PathType Leaf)) { throw "install-record.json not found in $BackupDir" }
$record = Read-C1Json -Path $recordPath
$csgoDir = $record.csgoDir
if (-not (Test-Path -LiteralPath $csgoDir)) { throw "Recorded csgoDir no longer exists: $csgoDir" }

Write-C1Step ("Restore plan for {0} ({1} entries, fakeTree={2})" -f $csgoDir, $record.entries.Count, $record.isFakeTree)
$created = @($record.entries | Where-Object { $_.action -eq 'created' })
$modified = @($record.entries | Where-Object { $_.action -eq 'modified' })
Write-Host "   created : $($created.Count) file(s) -> removed"
foreach ($m in $modified) { Write-Host "   modified: $($m.path) -> restored from backup ($($m.originalSha256))" }

if (-not $Apply) {
    Write-Host 'Dry run only. Re-run with -Apply to execute.' -ForegroundColor Yellow
    return
}

if (Test-C1Cs2Running -and -not $record.isFakeTree) {
    throw 'cs2.exe is running. Close CS2 before restoring.'
}

foreach ($e in $created) {
    $target = Join-Path $csgoDir ($e.path -replace '/', '\')
    if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
        Write-C1Fail "$($e.path): already absent (unexpected, continuing)"
        continue
    }
    $actual = Get-C1FileSha256Hex -Path $target
    if ($actual -ne $e.sha256) {
        if (-not $Force) {
            throw "Refusing to remove $($e.path): content changed since install`n     recorded $($e.sha256)`n     actual   $actual`nInspect the file, then re-run with -Force if removal is intended."
        }
        Write-C1Fail "$($e.path): content changed since install; removing anyway (-Force)"
    }
    Remove-Item -LiteralPath $target -Force
    Write-C1Ok "removed $($e.path)"
    # Remove now-empty directories that this install created (walk up, stop at pre-existing dirs).
    $parent = Split-Path $target -Parent
    while ($parent -and $parent.StartsWith($csgoDir, [System.StringComparison]::OrdinalIgnoreCase)) {
        if ((Get-ChildItem -LiteralPath $parent -Force | Measure-Object).Count -ne 0) { break }
        Remove-Item -LiteralPath $parent -Force
        Write-C1Ok "removed empty dir $(($parent.Substring($csgoDir.Length).TrimStart('\')))"
        $parent = Split-Path $parent -Parent
    }
}

foreach ($m in $modified) {
    $target = Join-Path $csgoDir ($m.path -replace '/', '\')
    $backupFile = Join-Path $BackupDir $m.backupPath
    if (-not (Test-Path -LiteralPath $backupFile -PathType Leaf)) { throw "Backup file missing: $backupFile" }
    Copy-Item -LiteralPath $backupFile -Destination $target -Force
    $restoredSha = Get-C1FileSha256Hex -Path $target
    if ($restoredSha -ne $m.originalSha256) {
        throw "Restore verification failed for $($m.path): hash $restoredSha != original $($m.originalSha256)"
    }
    Write-C1Ok "restored $($m.path) byte-for-byte ($restoredSha)"
}

Write-C1Step 'Final verification'
$bad = 0
foreach ($e in $created) {
    $target = Join-Path $csgoDir ($e.path -replace '/', '\')
    if (Test-Path -LiteralPath $target) {
        Write-C1Fail "$($e.path) still present"
        $bad++
    }
}
$giSha = Get-C1FileSha256Hex -Path (Join-Path $csgoDir 'gameinfo.gi')
$giOk = $true
foreach ($m in $modified) { if ($m.path -eq 'gameinfo.gi' -and $giSha -ne $m.originalSha256) { $giOk = $false } }
if (-not $giOk) { Write-C1Fail 'gameinfo.gi hash does not match pre-C1 state'; $bad++ }
if ($bad -gt 0) { throw "Restore finished with $bad problem(s)." }
Write-C1Ok 'all created files absent, gameinfo.gi matches pre-C1 hash'
Write-C1Ok 'restore complete (historical logs / user data / unrelated files untouched)'
