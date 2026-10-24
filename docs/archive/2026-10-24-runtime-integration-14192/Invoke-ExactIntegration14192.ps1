# Requires separate user authorization before -Apply. Default is read-only Preview.
#Requires -Version 7
[CmdletBinding()]
param([ValidateSet('Preview','Install','Restore','Diagnostics')][string]$Action='Preview',[string]$InstallId,[switch]$Apply)
$ErrorActionPreference='Stop'
$candidate='E:\CS2MOD\releases\bot-experiment\bot-integrated-14192-medium-eda1676'
$backup='E:\CS2MOD\backups\bot-experiment'
$arguments=@('-NoProfile','-File',(Join-Path $candidate 'tools/Invoke-BotExperiment.ps1'),'-Action',$Action,'-BackupRoot',$backup)
if($Action -in @('Restore','Diagnostics')) {
    if($InstallId -notmatch '^[0-9a-f]{32}$'){throw 'Exact new integration install ID required'}
    $record=Get-Content -LiteralPath (Join-Path $backup "$InstallId/install-record.json") -Raw | ConvertFrom-Json
    if($record.manifestSha256 -notin @('a0bd599514f551e03efb817052acb57b1f0021d21c9d7b78ab025dac32fe2865','3542f17be9d9a7e04a3408d7816cf1a68124ffc7c0b2cadfa5b252cfa68341d1','20967841f00e8625fd713ff99790660fdd0d7948df9b6224d969de17c8fca687')){throw 'This ID is not one of the three fixed integrated candidates'}
    $arguments+=@('-InstallId',$InstallId)
} else {
    $arguments+=@('-ReleaseDir',$candidate,'-ManifestSha256','3542f17be9d9a7e04a3408d7816cf1a68124ffc7c0b2cadfa5b252cfa68341d1','-Cs2Root','E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive','-PreserveHumanCosmetics')
}
if($Apply){$arguments+='-Apply'}
& pwsh @arguments
if($LASTEXITCODE -ne 0){throw 'Integration refused/failed; preserve the exact record and backup'}
