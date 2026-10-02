#Requires -Version 7
<# Build the current-main candidate from exact gitlinks, never from the old v1.4.4 plugin set.
Native/managed BotController assets are an inseparable pair at the upstream-selected tag.
No game startup or acceptance is performed. Downloads/source/builds stay under this worktree's temp/.
#>
[CmdletBinding()]
param([string]$Ref, [string]$DotNetExe, [string]$ReleasesRoot = 'E:\CS2MOD\releases\bot-baseline')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'BotBaseline.Common.ps1')
$repo = Get-RepoRoot
$lock = Read-BotImproverLock $repo
if (-not $Ref) { $Ref = $lock.ref }
if ($Ref -ne $lock.ref) { throw 'Build ref must equal the candidate lock.' }
$human = Get-Content (Join-Path $repo 'runtime/inventory-simulator.lock.json') -Raw | ConvertFrom-Json
$framework = if ($human.PSObject.Properties['compatibilityCandidate'] -and $human.compatibilityCandidate.status -eq 'candidate') {
    $human.compatibilityCandidate.framework
} else { $human.framework }
$upstream = Join-Path $repo 'temp/upstream/CS2-Bot-Improver'
$downloads = Join-Path $repo 'temp/downloads'
$stage = Join-Path $repo ('temp/staging/bot-main-' + [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $stage 'payload'
New-Item -ItemType Directory -Force -Path $downloads,$payload | Out-Null
if (-not (Test-Path (Join-Path $upstream '.git'))) {
    git clone "https://github.com/$($lock.repository).git" $upstream
    if ($LASTEXITCODE) { throw 'Clone failed.' }
}
git -C $upstream checkout --detach $Ref
if ($LASTEXITCODE) { throw 'Pinned upstream checkout failed.' }
git -C $upstream submodule update --init --recursive
if ($LASTEXITCODE) { throw 'Submodule checkout failed.' }
if ((git -C $upstream rev-parse HEAD).Trim() -ne $Ref) { throw 'Upstream ref mismatch.' }
$actualGitlinks = @(git -C $upstream ls-tree -r HEAD | Where-Object { $_ -match '^160000 ' })
if ($actualGitlinks.Count -ne @($lock.submodules.PSObject.Properties).Count) { throw 'Submodule set differs from the lock.' }
foreach ($p in $lock.submodules.PSObject.Properties) {
    $actual = (git -C (Join-Path $upstream $p.Name) rev-parse HEAD).Trim()
    if ($actual -ne $p.Value) { throw "Submodule mismatch: $($p.Name)" }
    if (@(git -C $upstream ls-tree HEAD $p.Name | Where-Object { $_ -match [regex]::Escape($p.Value) }).Count -ne 1) { throw 'Lock is not an upstream gitlink.' }
}
if (-not $DotNetExe) { $DotNetExe = Join-Path $repo 'temp/tooling/dotnet/dotnet.exe' }
if (-not (Test-Path -LiteralPath $DotNetExe)) { throw 'Provide a .NET 10 SDK using -DotNetExe (no global installation needed).' }
$env:DOTNET_ROOT = Split-Path $DotNetExe -Parent
function Extract-Verified([string]$Url, [string]$Sha, [string]$Name) {
    $zip = Join-Path $downloads $Name
    if (-not (Test-Path -LiteralPath $zip)) { Invoke-WebRequest $Url -OutFile $zip }
    if ((Get-FileSha256Lower $zip) -ne $Sha) { throw "Archive hash mismatch: $Name" }
    $out = Join-Path $stage ($Name -replace '\.zip$','')
    Expand-Archive -LiteralPath $zip -DestinationPath $out
    return $out
}
# Bot package depends on the separately recovered shared host. It never owns or
# redistributes CSS/MetaMod core files/configs/gamedata or the Human fixture.
$plugins = Join-Path $payload 'addons/counterstrikesharp/plugins'
New-Item -ItemType Directory -Force -Path $plugins | Out-Null
foreach ($a in $lock.nativeAssets) {
    # Never substitute a newer component tag for the gitlink ref selected by Bot-Improver.
    $sourceDir = switch ($a.repository) {
        'XBribo/CS2-Bot-Controller' { Join-Path $upstream 'addons/BotController' }
        'XBribo/CS2-Bot-Hider' { Join-Path $upstream 'addons/BotHider' }
        'XBribo/CS2-Bot-Vision' { Join-Path $upstream 'addons/BotVision' }
        default { throw 'Unexpected native component.' }
    }
    $tagRef = (git -C $sourceDir rev-parse "$($a.tag)^{commit}").Trim()
    if ($tagRef -ne $a.ref -or (git -C $sourceDir rev-parse HEAD).Trim() -ne $a.ref) { throw "Native release tag does not match gitlink: $($a.name)" }
    $asset = Extract-Verified $a.url $a.sha256 $a.name
    Copy-Item -Path (Join-Path $asset 'addons/*') -Destination (Join-Path $payload 'addons') -Recurse -Force
}
# Supply the exact contract packaged with the selected native bridge to BotState.
$contract = Join-Path $payload 'addons/counterstrikesharp/shared/BotControllerApi/BotControllerApi.dll'
$libs = Join-Path $upstream 'addons/counterstrikesharp/plugins/BotState/libs'
New-Item -ItemType Directory -Force -Path $libs | Out-Null
Copy-Item -LiteralPath $contract -Destination (Join-Path $libs 'BotControllerApi.dll') -Force
$managedBuilds = [ordered]@{}
foreach ($name in @('BotAI','BotAimImprover','BotBuy','BotRandomizer','BotState','NadeSystem','RoundDamageRecap')) {
    $source = Join-Path $upstream "addons/counterstrikesharp/plugins/$name"
    $out = Join-Path $stage "build/$name"
    & $DotNetExe build (Join-Path $source "$name.csproj") -c Release -o $out --nologo -v quiet
    if ($LASTEXITCODE) { throw "Pinned $name build failed." }
    $target = Join-Path $plugins $name
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $out -Recurse -File) {
        $rel = $file.FullName.Substring($out.Length + 1)
        # The host owns its API and Microsoft/System assemblies. Never deploy older copies alongside plugins.
        if ($file.Name -match '^(CounterStrikeSharp\.API|Microsoft\.|System\.)' -or $file.Extension -eq '.pdb') { continue }
        $dest = Join-Path $target $rel
        New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $dest
    }
    if (-not (Test-Path (Join-Path $target "$name.dll"))) { throw "Missing plugin output: $name" }
    $project = [xml](Get-Content (Join-Path $source "$name.csproj") -Raw)
    $api = @($project.SelectNodes('//PackageReference[@Include="CounterStrikeSharp.API"]') | ForEach-Object Version)
    $managedBuilds[$name] = @{apiReference=$api; dllSha256=(Get-FileSha256Lower (Join-Path $target "$name.dll"))}
}
# Bot datasets/configuration come from the current main tree, not the stale release.
$data = Join-Path $payload 'addons/counterstrikesharp/data'
Copy-Item -LiteralPath (Join-Path $upstream 'addons/counterstrikesharp/data') -Destination $data -Recurse -Force
Copy-Item -LiteralPath (Join-Path $upstream 'cfg') -Destination (Join-Path $payload 'cfg') -Recurse
$removedBindings = @()
foreach ($cfg in Get-ChildItem (Join-Path $payload 'cfg') -Filter '*.cfg') {
    $text = [IO.File]::ReadAllText($cfg.FullName)
    if ($text -match '(?m)^bind .*subclass_create') {
        $text = [regex]::Replace($text, '(?m)^bind .*subclass_create[^\r\n]*(\r?\n)?', '')
        [IO.File]::WriteAllText($cfg.FullName, $text, [Text.UTF8Encoding]::new($false))
        $removedBindings += $cfg.Name
    }
}
$profiles = Join-Path $payload 'overrides'
New-Item -ItemType Directory -Force -Path $profiles | Out-Null
foreach ($level in @('High','Low','Medium')) {
    $dir = Join-Path $profiles $level
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $db = Join-Path $upstream "overrides/$level/botprofile.db"
    $vpk = Join-Path $dir 'botprofile.vpk'
    New-BotProfileVpk -DbPath $db -OutPath $vpk
    if (-not (Test-BotProfileVpkEmbedsDb $vpk $db)) { throw "Profile VPK mismatch: $level" }
}
Copy-Item -LiteralPath (Join-Path $profiles 'Medium/botprofile.vpk') -Destination (Join-Path $profiles 'botprofile.vpk')
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$release = Join-Path $ReleasesRoot "bot-baseline-main-$stamp"
if (Test-Path -LiteralPath $release) { throw 'Release already exists.' }
New-Item -ItemType Directory -Force -Path $release | Out-Null
Copy-Item -LiteralPath $payload -Destination (Join-Path $release 'payload') -Recurse
# Preserve new Rush behavior-tree sources separately until its second-layer game gate.
Copy-Item -LiteralPath (Join-Path $upstream 'overrides/scripts') -Destination (Join-Path $release 'optional-rush-tree-sources') -Recurse
$hashes = [ordered]@{}
foreach ($rel in Get-ChildFilesRecursive (Join-Path $release 'payload') | Sort-Object) { $hashes[$rel] = Get-FileSha256Lower (Join-Path $release "payload/$rel") }
if (@($hashes.Keys | Where-Object { $_ -match 'InventorySimulator|HumanCosmetics|PlayerKnifeCustomizer|LocalArena|Panel.*\.exe' }).Count) { throw 'Unexpected Human/legacy/Panel payload.' }
$manifest = [ordered]@{
    schemaVersion=2; lane='bot-baseline-main'; status='candidate'; createdAtUtc=[DateTime]::UtcNow.ToString('o')
    builtFromRepoHead=(git -C $repo rev-parse HEAD).Trim()
    sources=@{botImprover=@{repository=$lock.repository;ref=$Ref;submodules=$lock.submodules};nativeAssets=$lock.nativeAssets;framework=$framework;managedBuilds=$managedBuilds}
    configChanges=@{excludedSubclassHotkeys=$removedBindings; sharedFramework='prerequisite-only, never owned by Bot'; defaultDifficulty='Medium'; rushBehaviorTrees='source-only, not active in minimum baseline; mapping and game gate pending'}
    targetCs2=$(if ($human.PSObject.Properties['compatibilityCandidate']) { $human.compatibilityCandidate.targetCs2Build } else { $human.testedCs2Build })
    payloadFiles=$hashes
}
$manifest | ConvertTo-Json -Depth 14 | Set-Content (Join-Path $release 'baseline-manifest.json') -Encoding utf8
$licenses = Join-Path $release 'licenses'
$sources = Join-Path $release 'sources'
New-Item -ItemType Directory -Force -Path $licenses,$sources | Out-Null
Copy-Item -LiteralPath (Join-Path $upstream 'LICENSE') -Destination (Join-Path $licenses 'CS2-Bot-Improver-LICENSE')
git -C $upstream archive --format=zip -o (Join-Path $sources 'CS2-Bot-Improver.zip') HEAD
if ($LASTEXITCODE) { throw 'Source archive failed.' }
foreach ($p in $lock.submodules.PSObject.Properties) {
    $dir = Join-Path $upstream $p.Name
    $name = Split-Path $p.Name -Leaf
    Get-ChildItem -LiteralPath $dir -Filter 'LICENSE*' -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $licenses "$name-$($_.Name)") }
    git -C $dir archive --format=zip -o (Join-Path $sources "$name.zip") HEAD
    if ($LASTEXITCODE) { throw "Source archive failed: $name" }
}
@"
Local testing candidate. Corresponding unmodified pinned source archives and licenses are in sources/ and licenses/.
All upstream refs, release asset URLs and SHA256 hashes are recorded in baseline-manifest.json.
Bot runtime: AGPL-3.0; MetaMod / CounterStrikeSharp: their applicable GPL licenses and exceptions.
Framework identity expectations refer to official archives; shared host files are not in this Bot package.
CSS API package versions remain upstream-selected;
older reference versions are not evidence of a game compatibility PASS. The prerequisite host stays v1.0.376.
Only package configuration removes upstream subclass_create shortcut bindings (outside this task).
No InventorySimulator / private presets / private fixture / Panel is distributed.
"@ | Set-Content (Join-Path $release 'THIRD_PARTY-NOTICES.txt') -Encoding utf8
@"
Bot baseline 实机验收（candidate；Agent 不自动启动 CS2）
先完成 Human 单独验收，再关闭 CS2，安装 Bot 候选。
预览：pwsh -File scripts/bot-baseline/Install-BotBaseline.ps1 -ReleaseDir "$release" -IsolateHumanCosmetics
安装：同命令加 -Apply；明确 -IsolateHumanCosmetics 将 Human plugin 移入 backup（fixture/preset 不变）。
启动 CS2 (-insecure) → Windows 本地 listen server → Bot 生成 → 至少正常一局。
检查 TAB / score / round / side change、difficulty、Bot identity/cosmetics、FPS。
难度 Medium 默认；先 restore，再通过 installer -Difficulty Low 或 High 重装；不直接改 live VPK。
刀/drop crash probe 在普通局稳定后单独进行；不绑定快捷造刀键。
测试后关闭游戏并运行 Collect-BotBaselineDiagnostics.ps1。
恢复：Restore-BotBaseline.ps1 -BackupDir <安装时打印的确切 backup> -Apply；先跑不带 -Apply 的 preview。
restore 发生 hash 漂移会拒绝全部写入，旧 schema-1 record 也拒绝，禁止改用旧 restore 强删。
恢复后重测 Human cosmetics；实机未 PASS 不升级任何 candidate 为 accepted。
Rush 作为第二层；behavior-tree source 随包留存，未部署且未宣称 Rush ready。
"@ | Set-Content (Join-Path $release 'MANUAL-TEST.txt') -Encoding utf8
$zip = "$release.zip"
Compress-Archive -Path (Join-Path $release '*') -DestinationPath $zip
$manifest.releaseArtifact=@{path=$zip;sha256=(Get-FileSha256Lower $zip)}
$manifest | ConvertTo-Json -Depth 14 | Set-Content "$release.manifest.json" -Encoding utf8
Write-Host "RELEASE_DIR: $release"
Write-Host "RELEASE_ZIP: $zip"
Write-Host "RELEASE_SHA: $($manifest.releaseArtifact.sha256)"
Write-Host "PAYLOAD_FILES: $($hashes.Count); real-game gate PENDING"
