# Invoke-CosmeticsLabRoundTripTest.ps1 — fake-tree install/restore round trip + fail-closed
# conflict tests, all against a synthetic CS2 tree. The real CS2 install is never touched.

param(
    [string]$ManifestPath = ''
)

. (Join-Path $PSScriptRoot 'CosmeticsLab.Common.ps1')

$wtRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $ManifestPath) {
    $candidates = Get-ChildItem -LiteralPath (Join-Path $script:Cs2ModRoot 'releases\cosmetics-lab') -Directory -Filter 'inventory-simulator-c1-*' -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending
    if (-not $candidates) { throw 'No release found. Run Prepare-CosmeticsLab.ps1 first.' }
    $ManifestPath = Join-Path $candidates[0].FullName 'manifest.json'
}
Write-C1Step "Using release manifest: $ManifestPath"

$workDir = Join-Path $wtRoot 'temp\cosmetics-lab\round-trip'
if (Test-Path -LiteralPath $workDir) { Remove-Item -LiteralPath $workDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $workDir | Out-Null

Write-C1Step 'Building fake CS2 tree (stock gameinfo.gi + user data + historical logs + empty shells)'
$fakeCsgo = Join-Path $workDir 'fake-tree\game\csgo'
New-Item -ItemType Directory -Force -Path (Join-Path $fakeCsgo 'addons\metamod') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $fakeCsgo 'addons\counterstrikesharp\logs') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $fakeCsgo 'addons\BotController') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $fakeCsgo 'cfg') | Out-Null

$realCsRoot = Find-C1Cs2Root
Copy-Item -LiteralPath (Join-Path $realCsRoot 'game\csgo\gameinfo.gi') -Destination (Join-Path $fakeCsgo 'gameinfo.gi')
Copy-Item -LiteralPath (Join-Path $realCsRoot 'game\csgo\steam.inf') -Destination (Join-Path $fakeCsgo 'steam.inf')
Set-Content -LiteralPath (Join-Path $fakeCsgo 'addons\metamod\ServerConfig.vdf') -Value "bot_quota 10`r`nbot_difficulty 3`r`n"
Set-Content -LiteralPath (Join-Path $fakeCsgo 'addons\metamod\backup_round00.txt') -Value 'fake user round backup'
Set-Content -LiteralPath (Join-Path $fakeCsgo 'addons\counterstrikesharp\logs\log-cssharp20260101.txt') -Value 'historical css log'
Set-Content -LiteralPath (Join-Path $fakeCsgo 'cfg\existing_user.cfg') -Value '// unrelated user cfg'
Write-C1Ok 'fake tree ready'

Write-C1Step 'Synthetic private fixture (dummy SteamID64)'
$fixturePath = Join-Path $workDir 'fixture-inventories.json'
$preset = Read-C1Json -Path (Join-Path $wtRoot 'experiments\cosmetics-lab\inventory-simulator\test-preset.json')
$fixture = [ordered]@{}
$fixture['76561190000000000'] = $preset.player
$fixture | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $fixturePath -Encoding utf8NoBOM
Write-C1Ok $fixturePath

$before = Get-C1TreeSnapshot -Root $fakeCsgo
Write-C1Ok ("snapshot before: {0} file(s)" -f $before.Count)

Write-C1Step 'Round trip: install'
$backupDir = Join-Path $workDir 'backup-rt'
pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Install-CosmeticsLab.ps1') `
    -ManifestPath $ManifestPath -FixturePath $fixturePath `
    -CsgoDir $fakeCsgo -BackupDir $backupDir -AllowFakeTree
if ($LASTEXITCODE -ne 0) { throw "Round-trip install exited with $LASTEXITCODE" }

$record = Read-C1Json -Path (Join-Path $backupDir 'install-record.json')
if ($record.entries.Count -lt 10) { throw "Suspiciously few install entries: $($record.entries.Count)" }
$installedPaths = @($record.entries | Where-Object { $_.action -eq 'created' } | ForEach-Object { $_.path })
foreach ($rel in $installedPaths) {
    if (-not (Test-Path -LiteralPath (Join-Path $fakeCsgo ($rel -replace '/', '\')) -PathType Leaf)) {
        throw "Round-trip: recorded created file missing: $rel"
    }
}
$giContent = Get-Content -LiteralPath (Join-Path $fakeCsgo 'gameinfo.gi') -Raw
$metamodRefs = ([regex]::Matches($giContent, [regex]::Escape('csgo/addons/metamod'))).Count
if ($metamodRefs -ne 1) { throw "Round-trip: gameinfo.gi metamod reference count = $metamodRefs, expected 1" }
Write-C1Ok "install verified: $($installedPaths.Count) created files + gameinfo.gi +1 line"

Write-C1Step 'Round trip: restore'
pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Restore-CosmeticsLab.ps1') -BackupDir $backupDir -Apply
if ($LASTEXITCODE -ne 0) { throw "Round-trip restore exited with $LASTEXITCODE" }

$after = Get-C1TreeSnapshot -Root $fakeCsgo
$mismatch = @()
foreach ($k in $before.Keys) {
    if (-not $after.ContainsKey($k)) { $mismatch += "missing after restore: $k"; continue }
    if ($after[$k] -ne $before[$k]) { $mismatch += "hash changed: $k" }
}
foreach ($k in $after.Keys) {
    if (-not $before.ContainsKey($k)) { $mismatch += "extra after restore: $k" }
}
if ($mismatch.Count -gt 0) {
    throw "Fake tree differs after restore:`n  $($mismatch -join "`n  ")"
}
Write-C1Ok ("fake tree byte-identical after restore ({0} file(s))" -f $before.Count)
if (-not (Test-Path -LiteralPath (Join-Path $fakeCsgo 'addons\BotController'))) {
    throw 'Pre-existing empty dir shell (BotController) was removed by restore.'
}
Write-C1Ok 'pre-existing empty dir shell preserved'

Write-C1Step 'Fail-closed: existing target file must abort install'
Set-Content -LiteralPath (Join-Path $fakeCsgo 'cfg\cosmeticslab_c1.cfg') -Value '// pre-existing junk'
$conflictOutput = pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Install-CosmeticsLab.ps1') `
    -ManifestPath $ManifestPath -FixturePath $fixturePath `
    -CsgoDir $fakeCsgo -BackupDir (Join-Path $workDir 'backup-conflict') -AllowFakeTree 2>&1
$conflictExit = $LASTEXITCODE
if ($conflictExit -eq 0) { throw 'Conflict test failed: install succeeded despite existing cfg file.' }
if (-not (($conflictOutput | Out-String)).Contains('cosmeticslab_c1.cfg')) {
    throw 'Conflict test failed: abort message does not name the conflicting file.'
}
Write-C1Ok 'install refused with exact conflicting path'

Write-C1Step 'Fail-closed: foreign active plugin payload must abort install'
Remove-Item -LiteralPath (Join-Path $fakeCsgo 'cfg\cosmeticslab_c1.cfg') -Force
$foreignDir = Join-Path $fakeCsgo 'addons\counterstrikesharp\plugins\EvilPlugin'
New-Item -ItemType Directory -Force -Path $foreignDir | Out-Null
Set-Content -LiteralPath (Join-Path $foreignDir 'EvilPlugin.dll') -Value 'not really a dll'
$foreignOutput = pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Install-CosmeticsLab.ps1') `
    -ManifestPath $ManifestPath -FixturePath $fixturePath `
    -CsgoDir $fakeCsgo -BackupDir (Join-Path $workDir 'backup-foreign') -AllowFakeTree 2>&1
if ($LASTEXITCODE -eq 0) { throw 'Foreign plugin test failed: install succeeded despite foreign payload.' }
if (-not (($foreignOutput | Out-String)).Contains('EvilPlugin')) {
    throw 'Foreign plugin test failed: abort message does not name the foreign plugin dir.'
}
Write-C1Ok 'install refused with exact foreign plugin path'

Write-Host ''
Write-C1Ok 'ROUND-TRIP + FAIL-CLOSED VALIDATION: ALL PASS'
