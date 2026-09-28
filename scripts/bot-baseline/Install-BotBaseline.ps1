#Requires -Version 7
<#
.SYNOPSIS
    Installs the bot-baseline Lane A payload into the local CS2 installation
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
foreach ($rel in $payloadFiles) {
    $expected = $manifest.payloadFiles.$rel
    if (-not $expected) { throw "Payload file '$rel' missing from manifest." }
    $actual = Get-FileSha256Lower (Join-Path $payloadDir $rel)
    if ($actual -ne $expected) { $hashMismatches += $rel }
}
if ($hashMismatches.Count -gt 0) { throw ("Payload integrity check FAILED for: " + ($hashMismatches -join ', ')) }

# Lane A active CSS plugin set (from the payload itself)
$lanePluginSet = @(Get-ChildItem (Join-Path $payloadDir 'addons\counterstrikesharp\plugins') -Directory).Name
$laneMetamodVdfs = @('BotController.vdf', 'BotHider.vdf', 'BotVision.vdf', 'counterstrikesharp.vdf', 'RayTrace.vdf')

# ---------------------------------------------------------------------------
# Target validation
# ---------------------------------------------------------------------------
$cs2 = Find-Cs2Root -ExplicitRoot $Cs2Root
if (-not $cs2) { throw 'CS2 installation root not found. Pass -Cs2Root explicitly.' }
$csgo = Get-CsgoDir -Cs2Root $cs2
Write-Host "CS2 root : $cs2"

if (Test-Cs2Running) {
    throw 'cs2.exe is currently RUNNING. Installation is intentionally skipped; close the game first.'
}

$gameinfoPath = Join-Path $csgo 'gameinfo.gi'
$gameinfoContent = Get-Content $gameinfoPath -Raw
if ($gameinfoContent -match 'Game\s+csgo/addons/metamod') {
    throw "gameinfo.gi already references MetaMod. A baseline appears to be (or have been) installed; refusing to double-install."
}

$targetMetamodCore = Join-Path $csgo 'addons\metamod\bin\win64\metamod.2.cs2.dll'
if (Test-Path $targetMetamodCore) {
    throw "Existing MetaMod core found at $targetMetamodCore with no gameinfo.gi reference. Manual review required before overwriting."
}

# ---------------------------------------------------------------------------
# Plan the operations (always; applied only with -Apply)
# ---------------------------------------------------------------------------
$entries = [System.Collections.Generic.List[object]]::new()

foreach ($rel in $payloadFiles) {
    # gameinfo.gi ships in the payload only for manifest completeness; the live
    # file is NEVER replaced wholesale - only the insertion below touches it
    if ($rel -eq 'gameinfo.gi') { continue }
    $targetPath = Join-Path $csgo $rel
    if (Test-Path $targetPath) {
        $entries.Add([pscustomobject]@{
            relPath = $rel; action = 'overwritten'
            originalSha256 = (Get-FileSha256Lower $targetPath)
        })
    } else {
        $entries.Add([pscustomobject]@{ relPath = $rel; action = 'created'; originalSha256 = $null })
    }
}
$entries.Add([pscustomobject]@{
    relPath = 'gameinfo.gi'; action = 'modified'
    originalSha256 = (Get-FileSha256Lower $gameinfoPath)
})

# isolation scan: executable content outside the Lane A set on the plugin/module surfaces
$cssPluginsDir = Join-Path $csgo 'addons\counterstrikesharp\plugins'
if (Test-Path $cssPluginsDir) {
    foreach ($dir in (Get-ChildItem $cssPluginsDir -Directory)) {
        if ($dir.Name -in $lanePluginSet) { continue }
        $dlls = @(Get-ChildItem $dir.FullName -Filter '*.dll' -File -Recurse)
        if ($dlls.Count -gt 0) {
            $entries.Add([pscustomobject]@{
                relPath = "addons/counterstrikesharp/plugins/$($dir.Name)"; action = 'isolated'
                originalSha256 = $null; kind = 'foreign-css-plugin'
            })
        }
    }
    foreach ($f in (Get-ChildItem $cssPluginsDir -Filter '*.dll' -File)) {
        $entries.Add([pscustomobject]@{
            relPath = "addons/counterstrikesharp/plugins/$($f.Name)"; action = 'isolated'
            originalSha256 = $null; kind = 'foreign-css-plugin'
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
            originalSha256 = $null; kind = 'foreign-metamod-module'
        })
    }
}

Write-Host ''
Write-Host ('Payload files: {0}  (created: {1}, overwritten: {2})' -f `
    @($entries | Where-Object { $_.action -eq 'created' }).Count, `
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
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')
$backupDir = Join-Path $BackupRoot $stamp
$backupCsgo = Join-Path $backupDir 'csgo'
New-Item -ItemType Directory -Force -Path $backupCsgo | Out-Null

function Save-BackupFile([string]$SourcePath, [string]$RelPath) {
    $dest = Join-Path $backupCsgo $RelPath
    New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null
    Copy-Item $SourcePath $dest -Force
}

foreach ($e in $entries) {
    $targetPath = Join-Path $csgo $e.relPath
    switch ($e.action) {
        'overwritten' { Save-BackupFile $targetPath $e.relPath }
        'modified'    { Save-BackupFile $targetPath $e.relPath }
        'isolated' {
            $dest = Join-Path $backupDir "isolated\$($e.relPath)"
            New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null
            Move-Item $targetPath $dest -Force
        }
        'created' { }
    }
}

# copy payload (gameinfo.gi excluded - handled by insertion below)
foreach ($rel in $payloadFiles) {
    if ($rel -eq 'gameinfo.gi') { continue }
    $targetPath = Join-Path $csgo $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $targetPath -Parent) | Out-Null
    Copy-Item (Join-Path $payloadDir $rel) $targetPath -Force
}

# gameinfo.gi insertion (preserve the file's own indentation style)
$lines = [System.Collections.Generic.List[string]](Get-Content $gameinfoPath)
$insertAt = -1
for ($i = 0; $i -lt $lines.Count - 1; $i++) {
    if ($lines[$i] -match '^\s*SearchPaths\s*$' -and $lines[$i + 1] -match '^\s*\{\s*$') { $insertAt = $i + 1; break }
}
if ($insertAt -lt 0) { throw 'SearchPaths { block not found in gameinfo.gi; no insertion point.' }
$indent = if ($lines[$insertAt + 1] -match '^(\s*)Game') { $Matches[1] } else { "`t`t`t" }
$newLines = @(
    "$($indent)Game`tcsgo/overrides/botprofile.vpk",
    '',
    "$($indent)Game`tcsgo/addons/metamod"
)
for ($i = $lines.Count - 1; $i -ge 0; $i--) {
    if ($lines[$i] -match 'Game\s+csgo/overrides/botprofile\.vpk' -or $lines[$i] -match 'Game\s+csgo/addons/metamod') {
        $lines.RemoveAt($i)
    }
}
$lines.InsertRange($insertAt + 1, [System.Collections.Generic.List[string]]$newLines)
Set-Content -Path $gameinfoPath -Value $lines -Encoding UTF8

$record = [ordered]@{
    schemaVersion = 1
    createdAtUtc  = $stamp
    cs2Root       = $cs2
    csgoDir       = $csgo
    releaseDir    = $ReleaseDir
    # the manifest inside a release directory does not carry its own zip hash;
    # only the standalone <lane>.manifest.json next to the zip does
    releaseSha256 = if ($manifest.PSObject.Properties['releaseArtifact']) { $manifest.releaseArtifact.sha256 } else { $null }
    lane          = $manifest.lane
    gameinfoInsertAfterLine = $insertAt + 1
    entries       = $entries
}
$record | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $backupDir 'install-record.json') -Encoding UTF8

Write-Host ''
Write-Host "INSTALLED : Lane A -> $csgo"
Write-Host "BACKUP    : $backupDir"
Write-Host "RESTORE   : pwsh -File scripts\bot-baseline\Restore-BotBaseline.ps1 -BackupDir `"$backupDir`" -Apply"
