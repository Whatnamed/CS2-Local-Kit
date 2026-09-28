# Collect-CosmeticsLabDiagnostics.ps1 — read-only post-test evidence collection for C1.
# Output: E:\CS2MOD\diagnostics\cosmetics-lab\<timestamp>-posttest\

param(
    [int]$LogHours = 6
)

. (Join-Path $PSScriptRoot 'CosmeticsLab.Common.ps1')

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$outDir = Join-Path $script:Cs2ModRoot "diagnostics\cosmetics-lab\$stamp-posttest"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = New-Object System.Collections.Generic.List[string]
function Add-Report { param([string]$Line) $report.Add($Line); Write-Host "   $Line" }

Write-C1Step "Collecting diagnostics -> $outDir"

Write-C1Step 'CS2 build'
$csRoot = Find-C1Cs2Root
$csgoDir = Join-Path $csRoot 'game\csgo'
foreach ($line in (Get-Content -LiteralPath (Join-Path $csgoDir 'steam.inf'))) {
    if ($line -match '^(PatchVersion|ClientVersion|VersionDate|VersionTime)=') { Add-Report $line }
}
$acf = Get-Content -LiteralPath (Join-Path $csRoot '..\..\appmanifest_730.acf') -Raw
Add-Report ("buildid = " + [regex]::Match($acf, '"buildid"\s+"(\d+)"').Groups[1].Value)
Add-Report ("cs2.exe running = " + ((Test-C1Cs2Running) ? 'YES' : 'no'))

Write-C1Step 'gameinfo.gi'
$giPath = Join-Path $csgoDir 'gameinfo.gi'
Add-Report ("gameinfo.gi sha256 = " + (Get-C1FileSha256Hex -Path $giPath))
Add-Report ("metamod search path present = " + ((Select-String -LiteralPath $giPath -Pattern 'csgo/addons/metamod' -SimpleMatch) ? 'yes' : 'NO'))

Write-C1Step 'Installed runtime files (hashes)'
$backupCandidates = Get-ChildItem -LiteralPath (Join-Path $script:Cs2ModRoot 'backups\cosmetics-lab') -Directory -Filter '*-inventory-simulator-c1' -ErrorAction SilentlyContinue | Sort-Object Name -Descending
if ($backupCandidates) {
    $record = Read-C1Json -Path (Join-Path $backupCandidates[0].FullName 'install-record.json')
    Add-Report ("install record: " + $backupCandidates[0].Name + " (" + $record.entries.Count + " entries, fakeTree=" + $record.isFakeTree + ")")
    foreach ($e in $record.entries) {
        $target = Join-Path $csgoDir ($e.path -replace '/', '\')
        if (Test-Path -LiteralPath $target -PathType Leaf) {
            $actual = Get-C1FileSha256Hex -Path $target
            $state = if ($e.action -eq 'modified') { if ($actual -eq $e.modifiedSha256) { 'modified-as-recorded' } else { 'MODIFIED-DEVIATES' } }
                    else { if ($actual -eq $e.sha256) { 'present-as-recorded' } else { 'DEVIATES' } }
            Add-Report ("{0} {1} [{2}] {3}" -f $e.action, $e.path, $e.role, $state)
        } else {
            Add-Report ("MISSING {0} [{1}]" -f $e.path, $e.role)
        }
    }
} else {
    Add-Report 'no cosmetics-lab C1 backup record found'
}

Write-C1Step 'CounterStrikeSharp state'
$coreJson = Join-Path $csgoDir 'addons\counterstrikesharp\configs\core.json'
if (Test-Path -LiteralPath $coreJson) {
    Add-Report ('core.json = ' + ((Get-Content -LiteralPath $coreJson -Raw) -replace '\s+', ' ').Trim())
    Copy-Item -LiteralPath $coreJson -Destination $outDir
}
$pluginsRoot = Join-Path $csgoDir 'addons\counterstrikesharp\plugins'
if (Test-Path -LiteralPath $pluginsRoot) {
    foreach ($dir in (Get-ChildItem -LiteralPath $pluginsRoot -Directory)) {
        $n = (Get-ChildItem -LiteralPath $dir.FullName -Recurse -File -Force | Measure-Object).Count
        Add-Report ("plugin dir: {0} ({1} file(s))" -f $dir.Name, $n)
    }
}

Write-C1Step 'CSS logs (tight window)'
$logsDir = Join-Path $csgoDir 'addons\counterstrikesharp\logs'
$cutoff = (Get-Date).AddHours(-$LogHours)
$copiedLogs = @()
if (Test-Path -LiteralPath $logsDir) {
    $copiedLogs = Get-ChildItem -LiteralPath $logsDir -File | Where-Object { $_.LastWriteTime -ge $cutoff }
    foreach ($log in $copiedLogs) { Copy-Item -LiteralPath $log.FullName -Destination $outDir }
    Add-Report ("copied {0} log file(s) newer than {1}" -f $copiedLogs.Count, $cutoff.ToString('s'))
} else {
    Add-Report 'no CSS logs dir'
}

Write-C1Step 'Network observation (from captured logs)'
$patterns = @('inventory.cstrike.app', '127.0.0.1:9', 'GET http', 'POST http')
$findings = @{}
foreach ($log in $copiedLogs) {
    foreach ($m in (Select-String -LiteralPath $log.FullName -Pattern $patterns -SimpleMatch)) {
        $key = [regex]::Match($m.Line, 'https?://[^ "''\]]+').Value
        if (-not $key) { $key = $m.Pattern ?? 'unknown' }
        if (-not $findings.ContainsKey($key)) { $findings[$key] = 0 }
        $findings[$key]++
    }
}
if ($findings.Count -eq 0) {
    Add-Report 'no HTTP attempt recorded in CSS logs within the window'
} else {
    foreach ($k in $findings.Keys) { Add-Report ("attempt: {0} (x{1})" -f $k, $findings[$k]) }
    $publicAttempts = @($findings.Keys | Where-Object { $_ -like '*cstrike.app*' })
    if ($publicAttempts.Count -gt 0) { Add-Report 'WARNING: public Inventory Simulator endpoint was contacted - investigate fixture!' }
}

Write-C1Step 'Private fixture summary (SteamID redacted)'
$fixturePath = Join-Path $script:Cs2ModRoot 'app-data\cosmetics-lab\inventory-simulator\inventories.json'
if (Test-Path -LiteralPath $fixturePath) {
    $fixture = Read-C1Json -Path $fixturePath
    $steamId = @($fixture.PSObject.Properties)[0].Name
    $summaryPath = Join-Path $outDir 'fixture-summary-redacted.json'
    $fixture.PSObject.Properties[$steamId].Value | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $summaryPath -Encoding utf8NoBOM
    Add-Report ("fixture present, top-level key <redacted:{0}...{1}> ({2} chars), items summary -> fixture-summary-redacted.json" -f $steamId.Substring(0, 4), $steamId.Substring($steamId.Length - 4), $steamId.Length)
} else {
    Add-Report 'no private fixture found in app-data'
}

$report | Set-Content -LiteralPath (Join-Path $outDir 'diagnostics-summary.txt') -Encoding utf8NoBOM
Write-Host ''
Write-C1Ok "diagnostics complete: $outDir"
