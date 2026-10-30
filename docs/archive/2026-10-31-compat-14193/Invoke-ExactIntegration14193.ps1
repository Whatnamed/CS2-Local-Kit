# Default: read-only recovery preflight of the currently installed integration.
# Actual Restore/Install require subsequent user authorization before -Apply.
#Requires -Version 7
[CmdletBinding()]
param(
    [ValidateSet('Preview','Install','Restore','Diagnostics')][string]$Action='Restore',
    [string]$InstallId='cf73e2b17dde4b98808bb42780ea59c6',
    [switch]$Apply
)
$ErrorActionPreference='Stop'
$candidate='E:\CS2MOD\releases\bot-experiment\bot-integrated-14193-medium-bd0d333'
$backup='E:\CS2MOD\backups\bot-experiment'
$manifest='74e414d887e70685bfd10bde5154513bb8af622cff69d4191869b8e7e5b27f42'
if((Get-FileHash -LiteralPath (Join-Path $candidate 'experiment-manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest){throw 'Candidate manifest drift'}
$arguments=@('-NoProfile','-File',(Join-Path $candidate 'tools/Invoke-BotExperiment.ps1'),'-Action',$Action,'-BackupRoot',$backup)
if($Action -in @('Restore','Diagnostics')) {
    if($InstallId -notmatch '^[0-9a-f]{32}$'){throw 'Exact integration install ID required'}
    $record=Get-Content -LiteralPath (Join-Path $backup "$InstallId/install-record.json") -Raw | ConvertFrom-Json
    if($record.manifestSha256 -notin @(
        'a0bd599514f551e03efb817052acb57b1f0021d21c9d7b78ab025dac32fe2865',
        '3542f17be9d9a7e04a3408d7816cf1a68124ffc7c0b2cadfa5b252cfa68341d1',
        '20967841f00e8625fd713ff99790660fdd0d7948df9b6224d969de17c8fca687',
        'c722e017dd6e7e39350d1397b2fa5b9140c68906757df7cdefee492608bc513d',
        $manifest,
        '4521a828405e8fe9cb207caff2ea7333392c3f230c13b1f23bde1237378b4453'
    )){throw 'Record is not a fixed integration candidate'}
    $arguments+=@('-InstallId',$InstallId)
} else {
    $arguments+=@('-ReleaseDir',$candidate,'-ManifestSha256',$manifest,'-Cs2Root','E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive','-PreserveHumanCosmetics')
}
if($Apply){$arguments+='-Apply'}
& pwsh @arguments
if($LASTEXITCODE -ne 0){throw 'Integration refused/failed; preserve its record and backup'}
