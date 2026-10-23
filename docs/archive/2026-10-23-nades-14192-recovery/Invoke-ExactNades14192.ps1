#Requires -Version 7
[CmdletBinding()]
param(
    [ValidateSet('Preview','Install','Restore')][string]$Action = 'Preview',
    [string]$InstallId,
    [switch]$Apply
)
$ErrorActionPreference = 'Stop'
if ($Action -eq 'Restore' -and $InstallId -notmatch '^[0-9a-f]{32}$') {
    throw 'Restore requires the exact NEW NadeSystem recovery install ID.'
}
& 'E:\CS2MOD\releases\compatibility\nades-14192-medium-human-20261023-8d359cb\tools\Invoke-Nades14192.ps1' `
    -Action $Action `
    -ReleaseDir 'E:\CS2MOD\releases\compatibility\nades-14192-medium-human-20261023-8d359cb' `
    -ManifestSha256 'a3a9414b11f507604380abc4abfc9d0ddefd64d6a56ac364cb837cc7910cad33' `
    -Cs2Root 'E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive' `
    -BackupRoot 'E:\CS2MOD\backups\nades-14192' -InstallId $InstallId -Apply:$Apply
