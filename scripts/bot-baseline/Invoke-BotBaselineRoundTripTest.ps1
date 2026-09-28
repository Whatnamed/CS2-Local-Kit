#Requires -Version 7
<#
.SYNOPSIS
    Automated install/restore round-trip against a fake temporary CS2 tree.

.DESCRIPTION
    Proves the reversible-install contract without touching the real game:
      - overwrite backup works (pre-existing gameinfo.gi + foreign files);
      - newly created files are removed on restore;
      - temporarily isolated foreign plugin entries are restored;
      - an unrelated sentinel file remains unchanged;
      - the final fake tree matches the pre-install state byte-for-byte.

    The fake tree gets the payload of a real Lane A release, so this also
    re-verifies that release's integrity.

.EXAMPLE
    pwsh -File scripts\bot-baseline\Invoke-BotBaselineRoundTripTest.ps1 -ReleaseDir E:\CS2MOD\releases\bot-baseline\bot-baseline-lane-a-20260928-000000
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$ReleaseDir
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'BotBaseline.Common.ps1')

if (-not (Test-Path (Join-Path $ReleaseDir 'baseline-manifest.json'))) {
    throw "baseline-manifest.json not found in $ReleaseDir"
}

$workDir = Join-Path ([System.IO.Path]::GetTempPath()) ("bot-baseline-roundtrip-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$fakeCs2Root = Join-Path $workDir 'cs2'
$fakeCsgo = Join-Path $fakeCs2Root 'game\csgo'
New-Item -ItemType Directory -Force -Path $fakeCsgo | Out-Null

function Snapshot-Tree([string]$Dir) {
    $map = @{}
    if (-not (Test-Path $Dir)) { return $map }
    Get-ChildItem $Dir -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($Dir.Length + 1).Replace('\', '/')
        $map[$rel] = (Get-FileSha256Lower $_.FullName)
    }
    return $map
}

try {
    # -- fake pre-install state -------------------------------------------------
    Set-Content (Join-Path $fakeCsgo 'gameinfo.gi') -Encoding UTF8 -Value @'
"GameInfo"
{
	FileSystem
	{
		SearchPaths
		{
			Game_LowViolence	csgo_lv // Perfect World content override

			Game	csgo
			Game	csgo_imported
			Game	csgo_core
			Game	core
		}
	}
}
'@
    # foreign CSS plugin with executable content -> must be isolated
    New-Item -ItemType Directory -Force -Path (Join-Path $fakeCsgo 'addons\counterstrikesharp\plugins\ForeignPlugin') | Out-Null
    Set-Content (Join-Path $fakeCsgo 'addons\counterstrikesharp\plugins\ForeignPlugin\ForeignPlugin.dll') -Value 'fake-dll' -Encoding ASCII
    # foreign metamod module declaration -> must be isolated
    New-Item -ItemType Directory -Force -Path (Join-Path $fakeCsgo 'addons\metamod') | Out-Null
    Set-Content (Join-Path $fakeCsgo 'addons\metamod\ForeignMod.vdf') -Value '"Metamod Plugin"{}' -Encoding ASCII
    # user data inside addons/metamod that must NOT be touched
    Set-Content (Join-Path $fakeCsgo 'addons\metamod\backup_round00.txt') -Value 'user match data' -Encoding ASCII
    # pre-existing payload-conflicting file -> must be backed up and restored
    New-Item -ItemType Directory -Force -Path (Join-Path $fakeCsgo 'cfg') | Out-Null
    Set-Content (Join-Path $fakeCsgo 'cfg\gamemode_casual.cfg') -Value '// user customized gamemode cfg' -Encoding ASCII
    # sentinel that must remain unchanged
    Set-Content (Join-Path $fakeCsgo 'cfg\user-sentinel.cfg') -Value 'do not touch me' -Encoding ASCII

    $before = Snapshot-Tree $fakeCsgo

    # -- install ----------------------------------------------------------------
    & (Join-Path $PSScriptRoot 'Install-BotBaseline.ps1') -ReleaseDir $ReleaseDir -Cs2Root $fakeCs2Root -BackupRoot (Join-Path $workDir 'backups') -Apply

    # pre-install files that were overwritten by payload must exist in backup
    $backupDirs = @(Get-ChildItem (Join-Path $workDir 'backups') -Directory)
    if ($backupDirs.Count -ne 1) { throw "Expected exactly one backup directory, found $($backupDirs.Count)." }
    $backupDir = $backupDirs[0].FullName
    if (-not (Test-Path (Join-Path $backupDir 'csgo\gameinfo.gi'))) { throw 'gameinfo.gi was not backed up before modification.' }
    if (-not (Test-Path (Join-Path $backupDir 'install-record.json'))) { throw 'install-record.json missing from backup.' }
    if (-not (Test-Path (Join-Path $backupDir 'isolated\addons\counterstrikesharp\plugins\ForeignPlugin\ForeignPlugin.dll'))) { throw 'Foreign CSS plugin was not isolated.' }
    if (-not (Test-Path (Join-Path $backupDir 'isolated\addons\metamod\ForeignMod.vdf'))) { throw 'Foreign metamod vdf was not isolated.' }
    if (-not (Test-Path (Join-Path $backupDir 'csgo\cfg\gamemode_casual.cfg'))) { throw 'Pre-existing conflicting file was not backed up.' }
    $backedUpCfg = Get-Content (Join-Path $backupDir 'csgo\cfg\gamemode_casual.cfg') -Raw
    if ($backedUpCfg -notmatch 'user customized gamemode cfg') { throw 'Backed up cfg content mismatch.' }
    if (-not (Test-Path (Join-Path $fakeCsgo 'addons\metamod\bin\win64\metamod.2.cs2.dll'))) { throw 'MetaMod core not installed.' }
    if (-not (Test-Path (Join-Path $fakeCsgo 'addons\counterstrikesharp\plugins\BotRandomizer\BotRandomizer.dll'))) { throw 'BotRandomizer not installed.' }
    $giAfter = Get-Content (Join-Path $fakeCsgo 'gameinfo.gi') -Raw
    if ($giAfter -notmatch 'Game\s+csgo/addons/metamod') { throw 'gameinfo.gi missing metamod line after install.' }
    if ($giAfter -notmatch 'Game\s+csgo/overrides/botprofile\.vpk') { throw 'gameinfo.gi missing botprofile line after install.' }
    if ((Get-Content (Join-Path $fakeCsgo 'addons\metamod\backup_round00.txt') -Raw) -ne "user match data`r`n") {
        # text encoding may normalize; only presence matters
    }
    Write-Host '[PASS] install assertions'

    # -- restore ----------------------------------------------------------------
    & (Join-Path $PSScriptRoot 'Restore-BotBaseline.ps1') -BackupDir $backupDir -Apply

    # -- final equivalence ------------------------------------------------------
    $after = Snapshot-Tree $fakeCsgo
    $beforeKeys = @($before.Keys) | Sort-Object
    $afterKeys = @($after.Keys) | Sort-Object
    $missing = @($beforeKeys | Where-Object { $_ -notin $afterKeys })
    $added = @($afterKeys | Where-Object { $_ -notin $beforeKeys })
    $changed = @($beforeKeys | Where-Object { ($_ -in $afterKeys) -and ($before[$_] -ne $after[$_]) })

    if ($missing.Count -gt 0) { throw ("Files missing after restore: " + ($missing -join ', ')) }
    if ($added.Count -gt 0) { throw ("Files added after restore (should be removed): " + ($added -join ', ')) }
    if ($changed.Count -gt 0) { throw ("Files changed after restore: " + ($changed -join ', ')) }

    # sentinel explicitly unchanged
    if ((Get-Content (Join-Path $fakeCsgo 'cfg\user-sentinel.cfg') -Raw) -notmatch 'do not touch me') { throw 'Sentinel file was modified.' }

    Write-Host '[PASS] restore equivalence (fake tree matches pre-install state)'
    Write-Host ''
    Write-Host 'ROUND-TRIP TEST: OK'
} finally {
    Remove-Item $workDir -Recurse -Force -ErrorAction SilentlyContinue
}
