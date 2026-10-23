#Requires -Version 7
[CmdletBinding()]
param(
    [ValidateSet('Preview','Install','Restore')][string]$Action = 'Preview',
    [Parameter(Mandatory)][string]$ReleaseDir,
    [Parameter(Mandatory)][string]$ManifestSha256,
    [Parameter(Mandatory)][string]$Cs2Root,
    [string]$BackupRoot = 'E:\CS2MOD\backups\compat-14192',
    [string]$InstallId,
    [switch]$Apply,
    [switch]$AllowFakeTree,
    [string]$PythonExe = 'python'
)
$ErrorActionPreference = 'Stop'
$arguments = @((Join-Path $PSScriptRoot 'compat_14192.py'), $Action.ToLowerInvariant(),
    '--release', $ReleaseDir, '--manifest-sha256', $ManifestSha256,
    '--root', $Cs2Root, '--backup-root', $BackupRoot)
if ($InstallId) { $arguments += @('--install-id', $InstallId) }
if ($Apply) { $arguments += '--apply' }
if ($AllowFakeTree) { $arguments += '--fake' }
& $PythonExe @arguments
if ($LASTEXITCODE -ne 0) { throw 'Compatibility recovery refused/failed. Preserve the exact original and recovery transactions.' }
