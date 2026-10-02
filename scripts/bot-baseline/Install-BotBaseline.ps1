#Requires -Version 7
<#
.SYNOPSIS
    Installs the current-main Bot candidate into the local CS2 installation
    behind an explicit backup/restore contract.

.DESCRIPTION
    Default behavior is a PREVIEW: nothing is written. Pass -Apply to mutate the
    game install. The installer refuses to run while cs2.exe is active.

    For every payload file that already exists in the target a byte-exact backup
    is taken, plus the pre-modification gameinfo.gi. Foreign CSS plugins /
    MetaMod module declarations that could execute during the test are isolated
    (moved into the backup, never deleted). All decisions are recorded in an
    install-record.json inside the backup directory; Restore-BotBaseline.ps1
    replays that record to return the tree to its exact pre-install state.

    gameinfo.gi is modified by inserting two Game search-path lines (metamod and
    the botprofile override vpk) instead of replacing the file, so Valve's
    current file content is preserved.

.PARAMETER ReleaseDir
    Directory containing payload\ and baseline-manifest.json (the generated
    release directory under E:\CS2MOD\releases\bot-baseline).

.PARAMETER ReleaseZip
    Alternative to -ReleaseDir: the release ZIP; it is extracted to temp first.

.PARAMETER Cs2Root
    Explicit CS2 installation root. Defaults to auto-detection.

.PARAMETER Apply
    Actually perform the installation. Without it the script only previews.

.EXAMPLE
    pwsh -File scripts\bot-baseline\Install-BotBaseline.ps1 -ReleaseDir E:\CS2MOD\releases\bot-baseline\bot-baseline-lane-a-20260928-000000 -Apply
#>
[CmdletBinding()]
param(
    [string]$ReleaseDir,
    [string]$ReleaseZip,
    [string]$Cs2Root,
    [string]$BackupRoot = 'E:\CS2MOD\backups\bot-baseline',
    [switch]$IsolateHumanCosmetics,
    [ValidateSet('Low','Medium','High')][string]$Difficulty = 'Medium',
    [switch]$AllowFakeTree,
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'BotBaseline.Common.ps1')

$repoRoot = Get-RepoRoot

# ---------------------------------------------------------------------------
# Resolve release source
# ---------------------------------------------------------------------------
if ($ReleaseZip -and -not $ReleaseDir) {
    if (-not (Test-Path $ReleaseZip)) { throw "Release zip not found: $ReleaseZip" }
    $extractDir = Join-Path $repoRoot "temp\staging\install-extract-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    Expand-Archive -Path $ReleaseZip -DestinationPath $extractDir -Force
    $ReleaseDir = $extractDir
}
if (-not $ReleaseDir) { throw 'Provide -ReleaseDir or -ReleaseZip.' }
$manifestPath = Join-Path $ReleaseDir 'baseline-manifest.json'
$payloadDir = Join-Path $ReleaseDir 'payload'
if (-not (Test-Path $manifestPath)) { throw "baseline-manifest.json not found in $ReleaseDir" }
if (-not (Test-Path $payloadDir)) { throw "payload directory not found in $ReleaseDir" }
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json

# verify payload integrity against the manifest before touching anything live
$hashMismatches = @()
$payloadFiles = @(Get-ChildFilesRecursive -Dir $payloadDir)
if ($payloadFiles.Count -ne @($manifest.payloadFiles.PSObject.Properties).Count) { throw 'Payload file set differs from manifest.' }
foreach ($rel in $payloadFiles) {
    $null = Resolve-BaselinePath $payloadDir $rel
    $expected = $manifest.payloadFiles.$rel
    if (-not $expected) { throw "Payload file '$rel' missing from manifest." }
    $actual = Get-FileSha256Lower (Join-Path $payloadDir $rel)
    if ($actual -ne $expected) { $hashMismatches += $rel }
}
if ($hashMismatches.Count -gt 0) { throw ("Payload integrity check FAILED for: " + ($hashMismatches -join ', ')) }
$sourcePaths = @{}
foreach ($rel in $payloadFiles) { $sourcePaths[$rel] = $rel }
if ($manifest.lane -eq 'bot-baseline-main') { $sourcePaths['overrides/botprofile.vpk'] = "overrides/$Difficulty/botprofile.vpk" }

# Lane A active CSS plugin set (from the payload itself)
$lanePluginSet = @(Get-ChildItem (Join-Path $payloadDir 'addons\counterstrikesharp\plugins') -Directory | ForEach-Object Name)
$laneMetamodVdfs = @('counterstrikesharp.vdf') + @(Get-ChildItem (Join-Path $payloadDir 'addons\metamod') -Filter '*.vdf' -File | ForEach-Object Name)

# ---------------------------------------------------------------------------
# Target validation
# ---------------------------------------------------------------------------
$cs2 = Find-Cs2Root -ExplicitRoot $Cs2Root
if (-not $cs2) { throw 'CS2 installation root not found. Pass -Cs2Root explicitly.' }
$csgo = Get-CsgoDir -Cs2Root $cs2
Write-Host "CS2 root : $cs2"
if ($AllowFakeTree -and (-not $Cs2Root -or (Test-Path -LiteralPath (Join-Path $cs2 'game/bin/win64/cs2.exe')))) {
    throw '-AllowFakeTree requires an explicit fake root without a CS2 executable.'
}
if ($manifest.lane -eq 'bot-baseline-main' -and -not $AllowFakeTree) {
    foreach ($component in @($manifest.sources.framework.metamod, $manifest.sources.framework.counterstrikesharp)) {
        foreach ($p in $component.installedFiles.PSObject.Properties) {
            if ((Get-FileSha256Lower (Resolve-BaselinePath $csgo $p.Name)) -ne $p.Value) {
                throw "Shared framework prerequisite mismatch: $($p.Name). Recover Human framework separately."
            }
        }
    }
}

if (Test-Cs2Running) {
    throw 'cs2.exe is currently RUNNING. Installation is intentionally skipped; close the game first.'
}

$gameinfoPath = Join-Path $csgo 'gameinfo.gi'
$gameinfoContent = Get-Content $gameinfoPath -Raw
if ($gameinfoContent -match 'Game\s+csgo/overrides') { throw 'Existing Bot overrides entry: restore the previous Bot install first.' }
if ($gameinfoContent -notmatch '(?m)^\s*SearchPaths\s*\r?\n\s*\{') { throw 'No SearchPaths insertion point.' }

# ---------------------------------------------------------------------------
# Plan the operations (always; applied only with -Apply)
# ---------------------------------------------------------------------------
$entries = [System.Collections.Generic.List[object]]::new()

foreach ($rel in $payloadFiles) {
    # gameinfo.gi ships in the payload only for manifest completeness; the live
    # file is NEVER replaced wholesale - only the insertion below touches it
    if ($rel -eq 'gameinfo.gi') { continue }
    $targetPath = Resolve-BaselinePath $csgo $rel
    $installedSha = $manifest.payloadFiles.($sourcePaths[$rel])
    if ($rel -eq 'addons/counterstrikesharp/configs/core.json' -and (Test-Path -LiteralPath $targetPath -PathType Leaf)) {
        $originalSha = Get-FileSha256Lower $targetPath
        $entries.Add([pscustomobject]@{ relPath = $rel; action = 'retained'; originalSha256 = $originalSha; installedSha256 = $originalSha })
        continue
    }
    $sharedFramework = $rel -match '^addons/(metamod/bin/|counterstrikesharp/(api|bin|dotnet|gamedata|lang)/)' -or $rel -eq 'addons/metamod/counterstrikesharp.vdf'
    if ((Test-Path -LiteralPath $targetPath) -and (Get-FileSha256Lower $targetPath) -eq $installedSha) {
        $entries.Add([pscustomobject]@{ relPath = $rel; action = 'retained'; originalSha256 = $installedSha; installedSha256 = $installedSha })
        continue
    }
    if ($sharedFramework -and (Test-Path -LiteralPath $targetPath)) { throw "Shared framework drift: $rel. Recover framework separately; Bot install cannot replace it." }
    if (Test-Path $targetPath) {
        $entries.Add([pscustomobject]@{
            relPath = $rel; action = 'overwritten'
            originalSha256 = (Get-FileSha256Lower $targetPath)
            installedSha256 = $installedSha
        })
    } else {
        $entries.Add([pscustomobject]@{ relPath = $rel; action = 'created'; originalSha256 = $null; installedSha256 = $installedSha })
    }
}
$entries.Add([pscustomobject]@{
    relPath = 'gameinfo.gi'; action = 'modified'
    originalSha256 = (Get-FileSha256Lower $gameinfoPath)
    installedSha256 = $null
})

# isolation scan: executable content outside the Lane A set on the plugin/module surfaces
$cssPluginsDir = Join-Path $csgo 'addons\counterstrikesharp\plugins'
if (Test-Path $cssPluginsDir) {
    foreach ($dir in (Get-ChildItem $cssPluginsDir -Directory)) {
        if ($dir.Name -in $lanePluginSet) { continue }
        if ($dir.Name -eq 'InventorySimulator' -and -not $IsolateHumanCosmetics) { continue }
        $dlls = @(Get-ChildItem $dir.FullName -Filter '*.dll' -File -Recurse)
        if ($dlls.Count -gt 0) {
            $entries.Add([pscustomobject]@{
                relPath = "addons/counterstrikesharp/plugins/$($dir.Name)"; action = 'isolated'
                originalSha256 = (Get-BaselineIdentity $dir.FullName); kind = 'foreign-css-plugin'
            })
        }
    }
    foreach ($f in (Get-ChildItem $cssPluginsDir -Filter '*.dll' -File)) {
        $entries.Add([pscustomobject]@{
            relPath = "addons/counterstrikesharp/plugins/$($f.Name)"; action = 'isolated'
            originalSha256 = (Get-BaselineIdentity $f.FullName); kind = 'foreign-css-plugin'
        })
    }
}
$metamodDir = Join-Path $csgo 'addons\metamod'
if (Test-Path $metamodDir) {
    foreach ($f in (Get-ChildItem $metamodDir -Filter '*.vdf' -File)) {
        if ($f.Name -in $laneMetamodVdfs) { continue }
        # only actual MetaMod module declarations are executable content; game
        # data files that merely live in this directory (e.g. ServerConfig.vdf)
        # are user data and must stay untouched
        $content = Get-Content $f.FullName -Raw
        if ($content -notmatch '"Metamod Plugin"' -and $content -notmatch 'addons/.*\.dll') { continue }
        $entries.Add([pscustomobject]@{
            relPath = "addons/metamod/$($f.Name)"; action = 'isolated'
            originalSha256 = (Get-BaselineIdentity $f.FullName); kind = 'foreign-metamod-module'
        })
    }
}

Write-Host ''
Write-Host ('Payload files: {0}  (created: {1}, overwritten: {2})' -f `
    $payloadFiles.Count, `
    @($entries | Where-Object { $_.action -eq 'created' }).Count, `
    @($entries | Where-Object { $_.action -eq 'overwritten' }).Count)
$isolated = @($entries | Where-Object { $_.action -eq 'isolated' })
if ($isolated.Count -gt 0) {
    Write-Host 'To isolate during the test:'
    $isolated | ForEach-Object { Write-Host ("  - {0} ({1})" -f $_.relPath, $_.kind) }
} else {
    Write-Host 'Foreign plugin/module isolation: nothing to isolate.'
}

if (-not $Apply) {
    Write-Host ''
    Write-Host 'PREVIEW ONLY. Re-run with -Apply to install.'
    return
}

# ---------------------------------------------------------------------------
# Apply
# ---------------------------------------------------------------------------
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
$backupDir = Join-Path $BackupRoot $stamp
$backupCsgo = Join-Path $backupDir 'csgo'
New-Item -ItemType Directory -Force -Path $backupCsgo | Out-Null

function Save-BackupFile([string]$SourcePath, [string]$RelPath) {
    $dest = Join-Path $backupCsgo $RelPath
    New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null
    Copy-Item $SourcePath $dest -Force
}

$stagedGameinfo = Join-Path $repoRoot ('temp/staging/gameinfo-' + [Guid]::NewGuid().ToString('N') + '.gi')
New-Item -ItemType Directory -Force -Path (Split-Path $stagedGameinfo -Parent) | Out-Null
New-BaselineGameinfo $gameinfoPath $stagedGameinfo @('csgo/overrides/botprofile.vpk', 'csgo/addons/metamod')
($entries | Where-Object relPath -eq 'gameinfo.gi').installedSha256 = Get-FileSha256Lower $stagedGameinfo

$record = [ordered]@{
    schemaVersion = 2
    createdAtUtc  = $stamp
    cs2Root       = $cs2
    csgoDir       = $csgo
    releaseDir    = $ReleaseDir
    # the manifest inside a release directory does not carry its own zip hash;
    # only the standalone <lane>.manifest.json next to the zip does
    releaseSha256 = if ($manifest.PSObject.Properties['releaseArtifact']) { $manifest.releaseArtifact.sha256 } else { $null }
    lane          = $manifest.lane
    difficulty    = $Difficulty
    entries       = $entries
}
$record | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $backupDir 'install-record.json') -Encoding UTF8

foreach ($e in $entries) {
    $targetPath = Resolve-BaselinePath $csgo $e.relPath
    switch ($e.action) {
        'overwritten' { Save-BackupFile $targetPath $e.relPath }
        'modified'    { Save-BackupFile $targetPath $e.relPath }
        'isolated' {
            if ((Get-BaselineIdentity $targetPath) -ne $e.originalSha256) { throw "Isolation target changed during preflight: $($e.relPath). Backup: $backupDir" }
            $dest = Join-Path $backupDir "isolated\$($e.relPath)"
            New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null
            $null = Resolve-BaselinePath $BackupDir "isolated/$($e.relPath)"
            Move-Item -LiteralPath $targetPath -Destination $dest
        }
        'created' { }
    }
}

# copy payload (gameinfo.gi excluded - handled by insertion below)
foreach ($rel in $payloadFiles) {
    if ($rel -eq 'gameinfo.gi') { continue }
    if (@($entries | Where-Object { $_.relPath -eq $rel -and $_.action -eq 'retained' }).Count) { continue }
    $targetPath = Resolve-BaselinePath $csgo $rel
    $planned = $entries | Where-Object relPath -eq $rel
    if ((Get-BaselineIdentity $targetPath) -ne $planned.originalSha256) { throw "Install target changed during preflight: $rel. Backup: $backupDir" }
    New-Item -ItemType Directory -Force -Path (Split-Path $targetPath -Parent) | Out-Null
    Copy-Item -LiteralPath (Join-Path $payloadDir $sourcePaths[$rel]) -Destination $targetPath -Force
}

if ((Get-FileSha256Lower $gameinfoPath) -ne ($entries | Where-Object relPath -eq 'gameinfo.gi').originalSha256) { throw "gameinfo.gi changed during installation. Backup: $backupDir" }
Copy-Item -LiteralPath $stagedGameinfo -Destination $gameinfoPath -Force
foreach ($e in $entries | Where-Object { $_.action -in @('created','overwritten','modified') }) {
    if ((Get-FileSha256Lower (Join-Path $csgo $e.relPath)) -ne $e.installedSha256) { throw "Installed identity mismatch: $($e.relPath)" }
}

Write-Host ''
Write-Host "INSTALLED : $($manifest.lane) ($Difficulty) -> $csgo"
Write-Host "BACKUP    : $backupDir"
Write-Host "RESTORE   : pwsh -File scripts\bot-baseline\Restore-BotBaseline.ps1 -BackupDir `"$backupDir`" -Apply"
