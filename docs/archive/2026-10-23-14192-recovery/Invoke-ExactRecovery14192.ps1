#Requires -Version 7
[CmdletBinding()]
param(
    [ValidateSet('Preview','Install','Restore')][string]$Action = 'Preview',
    [string]$InstallId,
    [switch]$Apply
)
$ErrorActionPreference = 'Stop'
if ($Action -eq 'Restore' -and $InstallId -notmatch '^[0-9a-f]{32}$') {
    throw 'Restore requires the exact NEW recovery install ID returned by this candidate.'
}
$release = 'E:\CS2MOD\releases\compatibility\cs2-14192-medium-human-20261023-425a2c1'
$digest = 'efaba2fadecb7c016cd843e7f35f691b70847946c6c5d35bb97f4f0c7d9fb073'
& (Join-Path $release 'tools/Invoke-Recovery14192.ps1') -Action $Action `
    -ReleaseDir $release -ManifestSha256 $digest `
    -Cs2Root 'E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive' `
    -BackupRoot 'E:\CS2MOD\backups\compat-14192' -InstallId $InstallId -Apply:$Apply
