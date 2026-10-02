#Requires -Version 7
# Regression probes for cross-workstream ownership, entirely in a fake CS2 tree.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'BotBaseline.Common.ps1')
$work = Join-Path (Get-RepoRoot) ('temp/ownership-' + [Guid]::NewGuid().ToString('N'))
$cs2 = Join-Path $work 'cs2'
$csgo = Join-Path $cs2 'game/csgo'
$release = Join-Path $work 'release'
$payload = Join-Path $release 'payload'
function Put([string]$Root, [string]$Rel, [string]$Value) {
    $path = Join-Path $Root $Rel
    New-Item -ItemType Directory -Force -Path (Split-Path $path -Parent) | Out-Null
    [IO.File]::WriteAllText($path, $Value)
}
function Expect-Blocked([scriptblock]$Action, [string]$Pattern) {
    $before = Get-BaselineIdentity $csgo
    $blocked = $false
    try { & $Action } catch { if ($_.Exception.Message -notmatch $Pattern) { throw }; $blocked = $true }
    if (-not $blocked) { throw "Expected refusal: $Pattern" }
    if ((Get-BaselineIdentity $csgo) -ne $before) { throw "Refusal mutated live tree: $Pattern" }
    Write-Host "[PASS] refusal without mutation: $Pattern"
}
try {
    Put $csgo 'gameinfo.gi' "SearchPaths`r`n{`r`n`tGame`tcsgo/addons/metamod`r`n`tGame`tcsgo`r`n}`r`n"
    Put $csgo 'cfg/gamemode_casual.cfg' 'original-user-cfg'
    Put $csgo 'addons/metamod/bin/win64/server.dll' 'shared-framework'
    Put $csgo 'addons/counterstrikesharp/plugins/InventorySimulator/InventorySimulator.dll' 'human-runtime'
    Put $csgo 'addons/counterstrikesharp/plugins/Foreign/Foreign.dll' 'foreign-original'
    Put $payload 'cfg/gamemode_casual.cfg' 'bot-cfg'
    Put $payload 'cfg/bot-new.cfg' 'bot-created'
    Put $payload 'addons/metamod/bin/win64/server.dll' 'shared-framework'
    Put $payload 'addons/counterstrikesharp/gamedata/new.json' 'created-shared-framework'
    Put $payload 'addons/counterstrikesharp/plugins/BotRandomizer/BotRandomizer.dll' 'bot-runtime'
    $hashes = @{}
    foreach ($rel in Get-ChildFilesRecursive $payload) { $hashes[$rel] = Get-FileSha256Lower (Join-Path $payload $rel) }
    @{lane='ownership-test';payloadFiles=$hashes} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $release 'baseline-manifest.json')
    $original = Get-BaselineIdentity $csgo
    $originalFiles = @{}
    foreach ($rel in Get-ChildFilesRecursive $csgo) { $originalFiles[$rel] = Get-FileSha256Lower (Join-Path $csgo $rel) }
    & "$PSScriptRoot/Install-BotBaseline.ps1" -ReleaseDir $release -Cs2Root $cs2
    if ((Get-BaselineIdentity $csgo) -ne $original) { throw 'Install preview mutated tree.' }
    & "$PSScriptRoot/Install-BotBaseline.ps1" -ReleaseDir $release -Cs2Root $cs2 -BackupRoot (Join-Path $work 'backups') -Apply
    $backup = (Get-ChildItem (Join-Path $work 'backups') -Directory | Select-Object -First 1).FullName
    $recordPath = Join-Path $backup 'install-record.json'
    $record = Get-Content $recordPath -Raw | ConvertFrom-Json
    if ($record.schemaVersion -ne 2 -or @($record.entries | Where-Object { $_.action -eq 'created' -and -not $_.installedSha256 }).Count) { throw 'Created file lacks installed hash.' }
    if (-not (Test-Path (Join-Path $csgo 'addons/counterstrikesharp/plugins/InventorySimulator/InventorySimulator.dll'))) { throw 'Human runtime was implicitly isolated.' }
    $installed = Get-BaselineIdentity $csgo
    & "$PSScriptRoot/Restore-BotBaseline.ps1" -BackupDir $backup
    if ((Get-BaselineIdentity $csgo) -ne $installed) { throw 'Restore preview mutated tree.' }
    foreach ($rel in @('cfg/bot-new.cfg','cfg/gamemode_casual.cfg','gameinfo.gi','addons/counterstrikesharp/gamedata/new.json')) {
        $path = Join-Path $csgo $rel
        $bytes = [IO.File]::ReadAllBytes($path)
        Put $csgo $rel 'later-legitimate-workstream-update'
        Expect-Blocked { & "$PSScriptRoot/Restore-BotBaseline.ps1" -BackupDir $backup } 'Ownership drift'
        Expect-Blocked { & "$PSScriptRoot/Restore-BotBaseline.ps1" -BackupDir $backup -Apply } 'Ownership drift'
        [IO.File]::WriteAllBytes($path, $bytes)
    }
    $legacy = Get-Content $recordPath -Raw
    $record.schemaVersion = 1
    $record | ConvertTo-Json -Depth 8 | Set-Content $recordPath
    Expect-Blocked { & "$PSScriptRoot/Restore-BotBaseline.ps1" -BackupDir $backup -Apply } 'Legacy install record'
    [IO.File]::WriteAllText($recordPath, $legacy)
    $isolated = Join-Path $backup 'isolated/addons/counterstrikesharp/plugins/Foreign/Foreign.dll'
    $bytes = [IO.File]::ReadAllBytes($isolated)
    [IO.File]::WriteAllText($isolated, 'corrupt-backup')
    Expect-Blocked { & "$PSScriptRoot/Restore-BotBaseline.ps1" -BackupDir $backup -Apply } 'Isolated backup identity'
    [IO.File]::WriteAllBytes($isolated, $bytes)
    Put $csgo 'addons/counterstrikesharp/plugins/Foreign/new.dll' 'new-plugin'
    Expect-Blocked { & "$PSScriptRoot/Restore-BotBaseline.ps1" -BackupDir $backup -Apply } 'Isolation restore conflict'
    $conflict = Resolve-BaselinePath $csgo 'addons/counterstrikesharp/plugins/Foreign'
    Remove-Item -LiteralPath $conflict -Recurse -Force
    # Shared retained files are outside Bot ownership even after another workstream updates them.
    Put $csgo 'addons/metamod/bin/win64/server.dll' 'framework-updated-after-bot'
    & "$PSScriptRoot/Restore-BotBaseline.ps1" -BackupDir $backup -Apply
    if ([IO.File]::ReadAllText((Join-Path $csgo 'addons/metamod/bin/win64/server.dll')) -ne 'framework-updated-after-bot') { throw 'Retained shared file was restored or deleted.' }
    Put $csgo 'addons/metamod/bin/win64/server.dll' 'shared-framework'
    if (@(Get-ChildFilesRecursive $csgo).Count -ne $originalFiles.Count) { throw 'Round trip file set differs.' }
    foreach ($rel in $originalFiles.Keys) {
        if ((Get-FileSha256Lower (Join-Path $csgo $rel)) -ne $originalFiles[$rel]) { throw "Round trip content differs: $rel" }
    }
    Put $csgo 'addons/metamod/bin/win64/server.dll' 'different-framework'
    Expect-Blocked { & "$PSScriptRoot/Install-BotBaseline.ps1" -ReleaseDir $release -Cs2Root $cs2 -Apply } 'Shared framework drift'
    Write-Host 'OWNERSHIP TESTS: PASS'
} finally {
    $safe = Resolve-BaselinePath (Join-Path (Get-RepoRoot) 'temp') (Split-Path $work -Leaf)
    Remove-Item -LiteralPath $safe -Recurse -Force
}
