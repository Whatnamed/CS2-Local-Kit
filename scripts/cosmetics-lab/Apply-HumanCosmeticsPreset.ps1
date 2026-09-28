# Apply-HumanCosmeticsPreset.ps1 — the single entrypoint that takes a canonical HumanPreset
# v1 from "file on disk" to "InventorySimulator actually reads it".
#
# Responsibilities:
#   - refuse while cs2.exe runs;
#   - validate the preset (schema + pinned catalog);
#   - project the runtime inventories.json into a staging file (pure projection);
#   - back up the currently installed fixture (hash recorded);
#   - atomically replace the file InventorySimulator actually reads
#     (game\csgo\addons\counterstrikesharp\configs\plugins\InventorySimulator\inventories.json);
#   - hash verify; auto-rollback on post-replace verification failure (never leave a
#     half-applied state);
#   - write rollback metadata.
#
# Later Panel/UI work must reuse THIS script instead of writing into the game directory.
#
# Usage:
#   pwsh -NoProfile -File scripts\cosmetics-lab\Apply-HumanCosmeticsPreset.ps1 [-PresetPath <file>] [-SteamId64 <id>]

param(
    [string]$PresetPath = '',
    [string]$SteamId64 = ''
)

. (Join-Path $PSScriptRoot 'CosmeticsLab.Common.ps1')
. (Join-Path $PSScriptRoot 'HumanPreset.Common.ps1')

if (-not $PresetPath) { $PresetPath = Join-Path $script:Cs2ModRoot 'presets\human\personal-default.v1.json' }
if (-not (Test-Path -LiteralPath $PresetPath -PathType Leaf)) { throw "Preset not found: $PresetPath" }

$csRoot = Find-C1Cs2Root
$csgoDir = Join-Path $csRoot 'game\csgo'
$installedPath = Join-Path $csgoDir $script:C1FixtureInstalledRelPath

Write-C1Step 'Preflight'
if (Test-C1Cs2Running) { throw 'cs2.exe is running. Close CS2, then re-run.' }
if (-not (Test-Path -LiteralPath $installedPath -PathType Leaf)) {
    throw "Installed fixture not found at $installedPath - the C1/C1.1 runtime install is missing."
}
$installedShaBefore = Get-C1FileSha256Hex -Path $installedPath
Write-C1Ok "cs2 not running; installed fixture present (sha256 $installedShaBefore)"

Write-C1Step 'Projecting to staging (validates schema + pinned catalog)'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$staging = Join-Path $env:TEMP "cosmetics-lab-apply-$stamp.json"
$projector = Join-Path $PSScriptRoot 'Convert-HumanPresetToInventorySimulator.ps1'
$projArgs = @('-NoProfile', '-File', $projector, '-PresetPath', $PresetPath, '-OutPath', $staging)
if ($SteamId64) { $projArgs += @('-SteamId64', $SteamId64) }
& pwsh @projArgs
if ($LASTEXITCODE -ne 0) { throw "Projection failed with exit code $LASTEXITCODE; installed fixture untouched." }
$stagingSha = Get-C1FileSha256Hex -Path $staging
Write-C1Ok "staging ready (sha256 $stagingSha)"

try {
    Write-C1Step 'Backing up currently installed fixture'
    $backupDir = Join-Path $script:Cs2ModRoot "backups\cosmetics-lab\$stamp-preset-apply"
    New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
    Copy-Item -LiteralPath $installedPath -Destination (Join-Path $backupDir 'inventories.json')
    $backupSha = Get-C1FileSha256Hex -Path (Join-Path $backupDir 'inventories.json')
    if ($backupSha -ne $installedShaBefore) { throw 'Backup verification failed - aborting before any change.' }

    Write-C1Step 'Atomically replacing installed fixture'
    $tempTarget = "$installedPath.new"
    Copy-Item -LiteralPath $staging -Destination $tempTarget -Force
    if ((Get-C1FileSha256Hex -Path $tempTarget) -ne $stagingSha) { throw 'Staging copy to target volume failed.' }
    Move-Item -LiteralPath $tempTarget -Destination $installedPath -Force

    Write-C1Step 'Hash verification'
    $installedShaAfter = Get-C1FileSha256Hex -Path $installedPath
    if ($installedShaAfter -ne $stagingSha) {
        Copy-Item -LiteralPath (Join-Path $backupDir 'inventories.json') -Destination $installedPath -Force
        $restored = Get-C1FileSha256Hex -Path $installedPath
        if ($restored -ne $installedShaBefore) { throw 'Auto-rollback failed - manual restore required from backup dir.' }
        throw "Post-replace verification failed; installed fixture auto-rolled back to previous state ($installedShaBefore)."
    }
    Write-C1Ok "installed fixture verified (sha256 $installedShaAfter)"

    $record = [pscustomobject]@{
        kind = 'cosmetics-lab-preset-apply-record'
        createdAt = (Get-Date -Format 'o')
        presetPath = $PresetPath
        projectedSha256 = $stagingSha
        installedPath = $installedPath
        installed = [pscustomobject]@{ previousSha256 = $installedShaBefore; newSha256 = $installedShaAfter }
        backupPath = (Join-Path $backupDir 'inventories.json')
        backupSha256 = $backupSha
        rollback = "Copy-Item -LiteralPath '$(Join-Path $backupDir 'inventories.json')' -Destination '$installedPath' -Force"
    }
    $record | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $backupDir 'apply-record.json') -Encoding utf8NoBOM
    Write-Host ''
    Write-C1Ok "apply complete. rollback record: $(Join-Path $backupDir 'apply-record.json')"
    Write-C1Ok "rollback: $($record.rollback)"
}
finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Force }
}
