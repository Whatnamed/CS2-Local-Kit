#Requires -Version 7
<#
Read-only preview by default. Upgrade only the two CS2 win64 MetaMod binaries and
insert the loader search path; preserve CSS, InventorySimulator and private state.
The schema-2 backup is restored with Restore-BotBaseline.ps1 -BackupDir <printed path>.
No runtime acceptance is inferred from a successful file installation.
#>
[CmdletBinding()]
param(
    [string]$Cs2Root,
    [string]$ArchivePath,
    [string]$BackupRoot = 'E:\CS2MOD\backups\cosmetics-lab',
    [switch]$Apply
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../bot-baseline/BotBaseline.Common.ps1')
$repo = Get-RepoRoot
$pin = Get-Content (Join-Path $repo 'runtime/inventory-simulator.lock.json') -Raw | ConvertFrom-Json
$candidate = $pin.compatibilityCandidate
if ($candidate.status -ne 'candidate') { throw 'Expected an explicitly unaccepted compatibility candidate.' }
if (Test-Cs2Running) { throw 'Close CS2 before framework recovery.' }
$cs2 = Find-Cs2Root -ExplicitRoot $Cs2Root
if (-not $cs2) { throw 'CS2 installation not found.' }
$csgo = Get-CsgoDir $cs2
$mm = $candidate.framework.metamod
if (-not $ArchivePath) {
    $ArchivePath = Join-Path $repo "temp/downloads/metamod-$($mm.version).zip"
    New-Item -ItemType Directory -Force -Path (Split-Path $ArchivePath -Parent) | Out-Null
    if (-not (Test-Path -LiteralPath $ArchivePath)) { Invoke-WebRequest $mm.url -OutFile $ArchivePath }
}
if ((Get-FileSha256Lower $ArchivePath) -ne $mm.sha256) { throw 'MetaMod archive hash mismatch.' }
$stage = Join-Path $repo ('temp/staging/mm-recovery-' + [Guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $ArchivePath -DestinationPath $stage
$gameinfo = Resolve-BaselinePath $csgo 'gameinfo.gi'
$stagedGameinfo = Join-Path $stage 'gameinfo.gi'
New-BaselineGameinfo $gameinfo $stagedGameinfo @('csgo/addons/metamod')
$entries = [Collections.Generic.List[object]]::new()
foreach ($p in $mm.installedFiles.PSObject.Properties) {
    $source = Resolve-BaselinePath $stage $p.Name
    if ((Get-FileSha256Lower $source) -ne $p.Value) { throw "Candidate payload hash mismatch: $($p.Name)" }
    $target = Resolve-BaselinePath $csgo $p.Name
    $entries.Add([pscustomobject]@{ relPath = $p.Name; action = $(if (Test-Path -LiteralPath $target) { 'overwritten' } else { 'created' }); originalSha256 = (Get-FileSha256Lower $target); installedSha256 = $p.Value })
}
$entries.Add([pscustomobject]@{ relPath = 'gameinfo.gi'; action = 'modified'; originalSha256 = (Get-FileSha256Lower $gameinfo); installedSha256 = (Get-FileSha256Lower $stagedGameinfo) })
$cssPath = Resolve-BaselinePath $csgo 'addons/counterstrikesharp'
$dllPath = Resolve-BaselinePath $csgo 'addons/counterstrikesharp/plugins/InventorySimulator/InventorySimulator.dll'
if ((Get-FileSha256Lower $dllPath) -ne $pin.acceptedRuntime.patchedDllSha256) { throw 'Installed Human runtime differs from accepted patched DLL.' }
foreach ($p in $candidate.framework.counterstrikesharp.installedFiles.PSObject.Properties) {
    if ((Get-FileSha256Lower (Resolve-BaselinePath $csgo $p.Name)) -ne $p.Value) { throw "CSS framework mismatch; MetaMod-only recovery blocked: $($p.Name)" }
}
$protectedBefore = Get-BaselineIdentity $cssPath
Write-Host "Target: $csgo; candidate: $($mm.version); status: manual-game-pending"
$entries | Format-Table relPath,action
if (-not $Apply) { Write-Host 'PREVIEW ONLY; use -Apply to install this compatibility candidate.'; return }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmssfff'
$backup = Join-Path $BackupRoot "$stamp-metamod-compatibility"
if (Test-Path -LiteralPath $backup) { throw "Backup already exists: $backup" }
New-Item -ItemType Directory -Path (Join-Path $backup 'csgo') -Force | Out-Null
foreach ($e in $entries) {
    if ($e.action -eq 'created') { continue }
    $target = Resolve-BaselinePath $csgo $e.relPath
    if ((Get-FileSha256Lower $target) -ne $e.originalSha256) { throw "Live file changed during preflight: $($e.relPath)" }
    $dest = Resolve-BaselinePath (Join-Path $backup 'csgo') $e.relPath
    New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null
    Copy-Item -LiteralPath $target -Destination $dest
    if ((Get-FileSha256Lower $dest) -ne $e.originalSha256) { throw 'Backup verification failed.' }
}
$record = [ordered]@{ schemaVersion = 2; lane = 'human-metamod-compatibility'; csgoDir = $csgo; cs2Root = $cs2; createdAtUtc = [DateTime]::UtcNow.ToString('o'); entries = $entries }
$record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $backup 'install-record.json') -Encoding utf8
foreach ($e in $entries) {
    $target = Resolve-BaselinePath $csgo $e.relPath
    New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
    Copy-Item -LiteralPath (Resolve-BaselinePath $stage $e.relPath) -Destination $target -Force
    if ((Get-FileSha256Lower $target) -ne $e.installedSha256) { throw "Post-install verification failed: $($e.relPath). Backup: $backup" }
}
if ((Get-BaselineIdentity $cssPath) -ne $protectedBefore) { throw "Protected CSS/Human tree changed. Backup: $backup" }
Write-Host "STATIC INSTALL PASS; CSS/Human tree unchanged. BACKUP: $backup"
Write-Host "Restore: pwsh -File scripts/bot-baseline/Restore-BotBaseline.ps1 -BackupDir `"$backup`" -Apply"
Write-Host 'Real startup / cosmetic compatibility is pending user testing. Do not promote the candidate.'
