# Install-CosmeticsLab.ps1 — backup + install the CosmeticsLab C1 runtime into the real CS2
# (or an explicitly passed fake tree for round-trip validation).
#
# Fail-closed: any pre-existing target file, foreign active plugin payload, unexpected
# gameinfo.gi content, or invalid private fixture aborts the install with exact paths.

param(
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    # Resolved after dot-sourcing Common.ps1 (param defaults run before it loads).
    [string]$FixturePath = '',
    [string]$ExpectedSteamId64 = '',
    [string]$CsgoDir = '',
    [string]$BackupDir = '',
    # Only Invoke-CosmeticsLabRoundTripTest.ps1 may set this; it permits installing into a
    # non-real CS2 tree and skips the cs2.exe process check.
    [switch]$AllowFakeTree
)

. (Join-Path $PSScriptRoot 'CosmeticsLab.Common.ps1')

if (-not $FixturePath) {
    $FixturePath = Join-Path $script:Cs2ModRoot 'app-data\cosmetics-lab\inventory-simulator\inventories.json'
}

Write-C1Step 'Resolving target CS2 install'
$isFakeTree = $false
if ($CsgoDir) {
    if (-not $AllowFakeTree) {
        throw 'Passing -CsgoDir requires -AllowFakeTree (round-trip testing only).'
    }
    $isFakeTree = $true
    $csRoot = (Get-Item -LiteralPath $CsgoDir).Parent.Parent.FullName
    Write-Host "   fake tree: $CsgoDir"
} else {
    $csRoot = Find-C1Cs2Root
    $CsgoDir = Join-Path $csRoot 'game\csgo'
    Write-Host "   real install: $CsgoDir"
}

Write-C1Step 'Preflight checks'
if (-not $isFakeTree -and (Test-C1Cs2Running)) {
    throw 'cs2.exe is running. Close CS2 before installing.'
}
$gameinfoPath = Join-Path $CsgoDir 'gameinfo.gi'
if (-not (Test-Path -LiteralPath $gameinfoPath -PathType Leaf)) { throw "gameinfo.gi not found at $gameinfoPath" }
$gameinfoOriginalSha = Get-C1FileSha256Hex -Path $gameinfoPath
Write-C1Ok "cs2.exe not running / gameinfo.gi present ($($gameinfoOriginalSha.Substring(0,16))...)"

# Foreign plugin contamination check: any non-empty dir under CSS plugins other than
# InventorySimulator would load once CSS is installed. Empty leftover shells are inert
# and are only observed, never touched.
$pluginsRoot = Join-Path $CsgoDir 'addons\counterstrikesharp\plugins'
$foreignPayload = @()
if (Test-Path -LiteralPath $pluginsRoot) {
    foreach ($dir in (Get-ChildItem -LiteralPath $pluginsRoot -Directory)) {
        if ($dir.Name -eq 'InventorySimulator') {
            throw "Refusing to install: $pluginsRoot\InventorySimulator already exists (restore first)."
        }
        $hasFiles = (Get-ChildItem -LiteralPath $dir.FullName -Recurse -File -Force | Measure-Object).Count -gt 0
        if ($hasFiles) { $foreignPayload += $dir.FullName }
    }
}
if ($foreignPayload.Count -gt 0) {
    throw "Unexpected active CSS plugin payload found (fail-closed):`n  $($foreignPayload -join "`n  ")"
}
Write-C1Ok 'no foreign active CSS plugin payload (leftover empty shells, if any, are left untouched)'

Write-C1Step 'Verifying package manifest and payload'
$payloadDir = [System.IO.Path]::GetFullPath((Join-Path (Split-Path $ManifestPath -Parent) 'payload'))
$manifest = Assert-C1Payload -Root $payloadDir -ManifestPath $ManifestPath
if ($manifest.kind -ne 'cosmetics-lab-c1-release') { throw "Unexpected manifest kind '$($manifest.kind)'." }
$manifestSha = Get-C1FileSha256Hex -Path $ManifestPath

Write-C1Step 'Validating private inventory fixture'
if (-not (Test-Path -LiteralPath $FixturePath -PathType Leaf)) { throw "Private fixture not found: $FixturePath (generate with Prepare-CosmeticsLab.ps1 -GenerateFixture)." }
$fixtureJson = Get-Content -LiteralPath $FixturePath -Raw | ConvertFrom-Json
$keys = @($fixtureJson.PSObject.Properties | ForEach-Object { $_.Name })
if ($keys.Count -ne 1) { throw "Fixture must contain exactly one SteamID64 key (found $($keys.Count))." }
$steamId = $keys[0]
if ($steamId -notmatch '^\d{17}$') { throw "Fixture key is not a 17-digit SteamID64: $steamId" }
if ($ExpectedSteamId64 -and $steamId -ne $ExpectedSteamId64) {
    throw "Fixture SteamID64 does not match -ExpectedSteamId64 (fixture: $steamId, expected: $ExpectedSteamId64)."
}
$inv = $fixtureJson.$steamId
foreach ($section in @('ctWeapons', 'tWeapons', 'knives', 'gloves')) {
    if (-not $inv.PSObject.Properties[$section]) { throw "Fixture missing section '$section'." }
}
if (-not ($inv.knives.PSObject.Properties['2'] -and $inv.knives.PSObject.Properties['3'])) {
    throw 'Fixture must define knives for both teams (keys "2" and "3").'
}
if (-not ($inv.gloves.PSObject.Properties['2'] -and $inv.gloves.PSObject.Properties['3'])) {
    throw 'Fixture must define gloves for both teams (keys "2" and "3").'
}
$hashes = @{}
foreach ($sectionName in @('ctWeapons', 'tWeapons', 'knives', 'gloves')) {
    foreach ($p in $inv.$sectionName.PSObject.Properties) {
        $item = $p.Value
        foreach ($field in @('def', 'hash')) {
            if (-not $item.PSObject.Properties[$field] -or $null -eq $item.$field) {
                throw "Fixture item $($sectionName)[$($p.Name)] missing '$field'."
            }
        }
        if (-not $item.hash) { throw "Fixture item $($sectionName)[$($p.Name)] has an empty hash." }
        if ($hashes.ContainsKey($item.hash)) { throw "Duplicate fixture hash: $($item.hash)" }
        $hashes[$item.hash] = $true
    }
}
if (-not ($inv.PSObject.Properties['musicKit'] -and $inv.musicKit.PSObject.Properties['musicId'])) {
    throw 'Fixture must define musicKit.musicId.'
}
Write-C1Ok "fixture valid (SteamID64 $steamId, $($hashes.Count) hashed items, musicKit $($inv.musicKit.musicId))"

Write-C1Step 'Creating backup'
if (-not $BackupDir) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $BackupDir = Join-Path $script:Cs2ModRoot "backups\cosmetics-lab\$stamp-inventory-simulator-c1"
}
if (Test-Path -LiteralPath $BackupDir) { throw "Backup dir already exists: $BackupDir" }
New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null

$entries = New-Object System.Collections.Generic.List[object]

# Conflict detection: every payload target must not exist yet.
$conflicts = @()
foreach ($e in $manifest.payload) {
    $target = Join-Path $CsgoDir ($e.path -replace '/', '\')
    if (Test-Path -LiteralPath $target) { $conflicts += $target }
}
if ($conflicts.Count -gt 0) {
    throw "Refusing to overwrite existing file(s) (restore first or investigate):`n  $($conflicts -join "`n  ")"
}
$fixtureTarget = Join-Path $CsgoDir $script:C1FixtureInstalledRelPath
if (Test-Path -LiteralPath $fixtureTarget) { throw "Refusing to overwrite existing fixture: $fixtureTarget" }

# The only modified file: gameinfo.gi. Copy the original into the backup.
Copy-Item -LiteralPath $gameinfoPath -Destination (Join-Path $BackupDir 'gameinfo.gi')

Write-C1Step 'Installing payload'
foreach ($e in $manifest.payload) {
    $source = Join-Path $payloadDir ($e.path -replace '/', '\')
    $target = Join-Path $CsgoDir ($e.path -replace '/', '\')
    $targetParent = Split-Path $target -Parent
    if (-not (Test-Path -LiteralPath $targetParent)) { New-Item -ItemType Directory -Force -Path $targetParent | Out-Null }
    Copy-Item -LiteralPath $source -Destination $target
    $entries.Add([pscustomobject]@{
        path = $e.path; action = 'created'; role = $e.role
        sha256 = (Get-C1FileSha256Hex -Path $target)
    })
}
Write-C1Ok "$($manifest.payload.Count) payload file(s) installed"

Write-C1Step 'Installing private fixture'
$fixtureTargetParent = Split-Path $fixtureTarget -Parent
if (-not (Test-Path -LiteralPath $fixtureTargetParent)) { New-Item -ItemType Directory -Force -Path $fixtureTargetParent | Out-Null }
Copy-Item -LiteralPath $FixturePath -Destination $fixtureTarget
$entries.Add([pscustomobject]@{
    path   = ($script:C1FixtureInstalledRelPath -replace '\\', '/')
    action = 'created'; role = 'private-fixture'
    source = $FixturePath
    sha256 = (Get-C1FileSha256Hex -Path $fixtureTarget)
})
Write-C1Ok $script:C1FixtureInstalledRelPath

Write-C1Step 'Modifying gameinfo.gi (insert-only)'
$addedLine = Add-C1GameinfoMetamodLine -GameinfoPath $gameinfoPath
$gameinfoNewSha = Get-C1FileSha256Hex -Path $gameinfoPath
# Byte-level proof that the ONLY change is the inserted line: removing that one line
# (with its indentation and trailing newline) must reproduce the original file hash.
$newRaw = [System.IO.File]::ReadAllText($gameinfoPath)
$origRaw = [System.IO.File]::ReadAllText((Join-Path $BackupDir 'gameinfo.gi'))
$linePattern = '[ \t]*' + [regex]::Escape($script:C1GameinfoLine) + '\r?\n'
$insertedMatches = [regex]::Matches($newRaw, $linePattern)
if ($insertedMatches.Count -ne 1) {
    throw "gameinfo.gi round-trip check failed: inserted line found $($insertedMatches.Count) time(s), expected exactly 1. Manual restore required."
}
$reconstructed = $newRaw.Remove($insertedMatches[0].Index, $insertedMatches[0].Length)
$noBomEnc = [System.Text.UTF8Encoding]::new($false)
$reconBytes = $noBomEnc.GetBytes($reconstructed)
$reconSha = ([BitConverter]::ToString([System.Security.Cryptography.SHA256]::HashData($reconBytes))).Replace('-', '').ToLowerInvariant()
if ($reconSha -ne $gameinfoOriginalSha) {
    throw 'gameinfo.gi round-trip check failed: removing the inserted line does not reproduce the original file. Manual restore required.'
}
$entries.Add([pscustomobject]@{
    path = 'gameinfo.gi'; action = 'modified'
    originalSha256 = $gameinfoOriginalSha
    modifiedSha256 = $gameinfoNewSha
    backupPath = 'gameinfo.gi'
    detail = "inserted 1 line after SearchPaths '{': $addedLine"
})
Write-C1Ok "gameinfo.gi: +1 line, byte-exact reconstruction check passed ($gameinfoOriginalSha -> $gameinfoNewSha)"

Write-C1Step 'Writing install record'
$observedBuild = $null
$steamInfPath = Join-Path $CsgoDir 'steam.inf'
if (Test-Path -LiteralPath $steamInfPath) {
    $observedBuild = @{}
    foreach ($line in (Get-Content -LiteralPath $steamInfPath)) {
        if ($line -match '^(PatchVersion|ClientVersion)=(.*)$') { $observedBuild[$matches[1]] = $matches[2] }
    }
}
$record = [pscustomobject]@{
    kind = 'cosmetics-lab-c1-install-record'
    schemaVersion = 1
    createdAt = (Get-Date -Format 'o')
    csgoDir = $CsgoDir
    isFakeTree = $isFakeTree
    package = [pscustomobject]@{
        manifestPath = $ManifestPath
        manifestSha256 = $manifestSha
        releaseComponents = $manifest.components
    }
    fixture = [pscustomobject]@{
        steamId64 = $steamId
        appDataPath = $FixturePath
        installedPath = $script:C1FixtureInstalledRelPath
    }
    observedCs2Build = $observedBuild
    entries = $entries
}
$recordPath = Join-Path $BackupDir 'install-record.json'
$record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $recordPath -Encoding utf8NoBOM

Write-C1Step 'Post-install verification'
foreach ($e in $entries) {
    if ($e.action -eq 'modified') { continue }
    $target = Join-Path $CsgoDir ($e.path -replace '/', '\')
    $actual = Get-C1FileSha256Hex -Path $target
    if ($actual -ne $e.sha256) { throw "Post-install hash mismatch for $($e.path)" }
}
Write-C1Ok "all $($entries.Count) recorded files verified in place"

Write-Host ''
Write-C1Ok "backup / install record: $BackupDir"
Write-C1Ok "restore: pwsh -NoProfile -File scripts\cosmetics-lab\Restore-CosmeticsLab.ps1 -BackupDir `"$BackupDir`" -Apply"
Write-Host ''
Write-Host 'C1 install complete. Do NOT launch CS2 from automation - hand over to the user for MANUAL-TEST.md.' -ForegroundColor Yellow
