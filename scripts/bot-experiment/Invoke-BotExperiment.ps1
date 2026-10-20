#Requires -Version 7
[CmdletBinding()]
param(
    [ValidateSet('Preview','Install','Restore','Diagnostics')][string]$Action = 'Preview',
    [string]$ReleaseDir,
    [string]$ManifestSha256,
    [string]$Cs2Root,
    [string]$BackupRoot = 'E:\CS2MOD\backups\bot-experiment',
    [string]$InstallId,
    [switch]$IsolateHumanCosmetics,
    [switch]$AllowFakeTree,
    [switch]$Apply,
    [string]$PythonExe = 'python'
)
$ErrorActionPreference = 'Stop'
$arguments = @((Join-Path $PSScriptRoot 'bot_experiment.py'), $Action.ToLowerInvariant(), '--backup-root', $BackupRoot)
if ($ReleaseDir) { $arguments += @('--release', $ReleaseDir) }
if ($ManifestSha256) { $arguments += @('--manifest-sha256', $ManifestSha256) }
if ($Cs2Root) { $arguments += @('--root', $Cs2Root) }
if ($InstallId) { $arguments += @('--install-id', $InstallId) }
if ($IsolateHumanCosmetics) { $arguments += '--isolate-human' }
if ($AllowFakeTree) { $arguments += '--fake' }
if ($Apply) { $arguments += '--apply' }
& $PythonExe @arguments
if ($LASTEXITCODE -ne 0) { throw "Bot experiment $Action refused/failed (exit $LASTEXITCODE). Preserve the exact backup/record; do not use the formal Bot restore for a framework overlay." }
