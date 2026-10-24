#Requires -Version 7
[CmdletBinding()]
param(
    [ValidateSet('Preview','Install','Restore')][string]$Action = 'Preview',
    [Parameter(Mandatory)][string]$ReleaseDir,
    [Parameter(Mandatory)][string]$ManifestSha256,
    [Parameter(Mandatory)][string]$Cs2Root,
    [string]$BackupRoot = 'E:\CS2MOD\backups\nades-14192',
    [string]$InstallId,
    [switch]$Apply,
    [switch]$AllowFakeTree,
    [string]$PythonExe = 'python'
)
$ErrorActionPreference = 'Stop'
$arguments = @('-B', (Join-Path $PSScriptRoot 'nades_14192.py'), $Action.ToLowerInvariant(),
    '--release', $ReleaseDir, '--manifest-sha256', $ManifestSha256,
    '--root', $Cs2Root, '--backup-root', $BackupRoot)
if ($InstallId) { $arguments += @('--install-id', $InstallId) }
if ($Apply) { $arguments += '--apply' }
if ($AllowFakeTree) { $arguments += '--fake' }
& $PythonExe @arguments
if ($LASTEXITCODE -ne 0) { throw 'Nade recovery refused/failed. Preserve both parent transactions and this exact new layer.' }
