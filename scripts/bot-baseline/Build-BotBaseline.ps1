#Requires -Version 7
<#
.SYNOPSIS
    Builds the CS2 Local Kit bot-baseline Lane A release from immutable upstream sources.

.DESCRIPTION
    Lane A = CS2-Bot-Improver v1.4.4 Windows release
           + CounterStrikeSharp 1.0.375 framework runtime (only if base is not already 1.0.375)
           + BotRandomizer rebuilt from the ref pinned in runtime/bot-improver.lock.json
           + candidate High/Low/Medium botprofile.db files packaged as botprofile.vpk
             (root overrides/botprofile.vpk = candidate Medium, the upstream default).

    Everything is verified by SHA-256 before use. The upstream Panel executable and
    Panel-owned gameinfo.gi toggle backups are excluded. No HumanCosmetics and no
    legacy Local-Arena runtime modules are included.

    Task-local downloads/sources/staging live under <repo>\temp\. The final release
    artifact goes to E:\CS2MOD\releases\bot-baseline\ (never into Git).

.PARAMETER Ref
    Upstream CS2-Bot-Improver ref to build BotRandomizer and botprofile.dbs from.
    Defaults to the ref in runtime\bot-improver.lock.json.

.EXAMPLE
    pwsh -File scripts\bot-baseline\Build-BotBaseline.ps1
#>
[CmdletBinding()]
param(
    [string]$Ref,
    [string]$ReleaseTag = 'v1.4.4',
    [string]$CssVersion = '1.0.375',
    [string]$ReleaseAssetUrl = 'https://github.com/ed0ard/CS2-Bot-Improver/releases/download/v1.4.4/CS2BotImprover.zip',
    [string]$ReleaseAssetSha256 = 'cba05fdc239bf0e3e95dff7d8670863fe69de9fb03ade8512b6d4f79f88664db',
    [string]$CssAssetUrl = 'https://github.com/roflmuffin/CounterStrikeSharp/releases/download/v1.0.375/counterstrikesharp-with-runtime-windows-1.0.375.zip',
    [string]$CssAssetSha256 = '1e21a4d0ea8abf27b6dabdcc66b1bbaeafab37b7b593da752489c1becd0c41e5',
    [string]$ReleasesRoot = 'E:\CS2MOD\releases\bot-baseline',
    [switch]$SkipSdkBootstrap
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'BotBaseline.Common.ps1')

$repoRoot = Get-RepoRoot
$laneId = 'bot-baseline-lane-a'
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')

$tempDir = Join-Path $repoRoot 'temp'
$downloadsDir = Join-Path $tempDir 'downloads'
$upstreamDir = Join-Path $tempDir 'upstream\CS2-Bot-Improver'
$stagingDir = Join-Path $tempDir 'staging'
$toolingDir = Join-Path $tempDir 'tooling'
foreach ($d in @($downloadsDir, $stagingDir, $toolingDir)) {
    New-Item -ItemType Directory -Force -Path $d | Out-Null
}

$validation = [System.Collections.Generic.List[object]]::new()
function Add-Validation([string]$Name, [bool]$Ok, [string]$Detail = '') {
    $script:validation.Add([pscustomobject]@{ name = $Name; ok = $Ok; detail = $Detail })
    $status = if ($Ok) { 'PASS' } else { 'FAIL' }
    Write-Host ("[{0}] {1} {2}" -f $status, $Name, $Detail)
    if (-not $Ok) { throw "Validation failed: $Name $Detail" }
}

# ---------------------------------------------------------------------------
# 1. Resolve the pinned upstream ref
# ---------------------------------------------------------------------------
$lock = Read-BotImproverLock -RepoRoot $repoRoot
if (-not $Ref) { $Ref = $lock.ref }
Add-Validation 'lock-ref-resolved' ([bool]$Ref) "repository=$($lock.repository) ref=$Ref status=$($lock.status)"

# ---------------------------------------------------------------------------
# 2. Upstream source tree (task-local temp/ only)
# ---------------------------------------------------------------------------
if (Test-Path (Join-Path $upstreamDir '.git')) {
    git -C $upstreamDir fetch origin 2>&1 | Out-Null
} else {
    git clone --filter=blob:none "https://github.com/$($lock.repository).git" $upstreamDir 2>&1 | Select-Object -Last 1
}
$upstreamHead = (git -C $upstreamDir rev-parse HEAD).Trim()
if ($upstreamHead -ne $Ref) {
    git -C $upstreamDir checkout --detach $Ref 2>&1 | Out-Null
    $upstreamHead = (git -C $upstreamDir rev-parse HEAD).Trim()
}
Add-Validation 'upstream-ref-checked-out' ($upstreamHead -eq $Ref) "HEAD=$upstreamHead"

# ---------------------------------------------------------------------------
# 3. Download + verify release assets
# ---------------------------------------------------------------------------
function Get-VerifiedAsset {
    param([string]$Url, [string]$ExpectedSha256, [string]$OutFile, [string]$Name)
    $outPath = Join-Path $downloadsDir $OutFile
    if (-not (Test-Path $outPath)) {
        Write-Host "Downloading $Name ..."
        Invoke-WebRequest -Uri $Url -OutFile $outPath -MaximumRetryCount 3
    }
    $actual = Get-FileSha256Lower $outPath
    Add-Validation "$Name-sha256" ($actual -eq $ExpectedSha256.ToLowerInvariant()) "sha256=$actual"
    return $outPath
}

$baseZip = Get-VerifiedAsset -Url $ReleaseAssetUrl -ExpectedSha256 $ReleaseAssetSha256 -OutFile 'CS2BotImprover-v1.4.4.zip' -Name 'bot-improver-release'
$cssZip = Get-VerifiedAsset -Url $CssAssetUrl -ExpectedSha256 $CssAssetSha256 -OutFile "counterstrikesharp-with-runtime-windows-$CssVersion.zip" -Name 'css-runtime'

# ---------------------------------------------------------------------------
# 4. .NET SDK (task-local preferred, no machine-level install)
# ---------------------------------------------------------------------------
$dotnetExe = $null
$candidateSdks = @()
foreach ($dotnet in @('dotnet', (Join-Path $toolingDir 'dotnet\dotnet.exe'))) {
    try {
        $listed = & $dotnet --list-sdks 2>$null
        if ($LASTEXITCODE -eq 0 -and $listed) { $candidateSdks += $listed }
        if ($candidateSdks) { $dotnetExe = $dotnet; break }
    } catch { }
}
$hasNet10 = @($candidateSdks | Where-Object { $_ -match '^10\.\d+' }).Count -gt 0
if (-not $hasNet10) {
    if ($SkipSdkBootstrap) { throw 'No .NET 10 SDK available and -SkipSdkBootstrap was set.' }
    Write-Host 'Bootstrapping .NET 10 SDK into temp/tooling/dotnet (task-local)...'
    $installScript = Join-Path $toolingDir 'dotnet-install.ps1'
    if (-not (Test-Path $installScript)) {
        Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installScript
    }
    & $installScript -Channel 10.0 -InstallDir (Join-Path $toolingDir 'dotnet') -NoPath | Out-Null
    $dotnetExe = Join-Path $toolingDir 'dotnet\dotnet.exe'
    $candidateSdks = & $dotnetExe --list-sdks
}
$hasNet10 = @($candidateSdks | Where-Object { $_ -match '^10\.\d+' }).Count -gt 0
Add-Validation 'dotnet-10-sdk' $hasNet10 "sdk=$($dotnetExe ?? 'unknown'); versions=$($candidateSdks -join ', ')"

# ---------------------------------------------------------------------------
# 5. Build candidate BotRandomizer (Release)
# ---------------------------------------------------------------------------
$csproj = Join-Path $upstreamDir 'addons\counterstrikesharp\plugins\BotRandomizer\BotRandomizer.csproj'
& $dotnetExe build $csproj -c Release 2>&1 | Select-Object -Last 4
if ($LASTEXITCODE -ne 0) { throw 'BotRandomizer build failed.' }
$buildOut = Join-Path $upstreamDir 'addons\counterstrikesharp\plugins\BotRandomizer\bin\Release\net10.0'
$requiredOutputs = @('BotRandomizer.dll', 'BotRandomizer.deps.json', 'charm_placements.json', 'cosmetic_catalog.json')
$missing = @($requiredOutputs | Where-Object { -not (Test-Path (Join-Path $buildOut $_)) })
Add-Validation 'botrandomizer-build' ($missing.Count -eq 0) "missing=$($missing -join ',')"
$depsJson = Get-Content (Join-Path $buildOut 'BotRandomizer.deps.json') -Raw | ConvertFrom-Json
$apiVersion = $depsJson.targets.PSObject.Properties |
    ForEach-Object { $_.Value.PSObject.Properties } |
    Where-Object { $_.Name -like 'CounterStrikeSharp.API/*' } |
    Select-Object -First 1 -ExpandProperty Name
Add-Validation 'botrandomizer-api-version' ($apiVersion -eq "CounterStrikeSharp.API/$CssVersion") "reference=$apiVersion"

# ---------------------------------------------------------------------------
# 6. Extract base package + CSS runtime into staging
# ---------------------------------------------------------------------------
$extractBase = Join-Path $stagingDir 'extract-base'
$extractCss = Join-Path $stagingDir 'extract-css'
if (Test-Path $extractBase) { Remove-Item $extractBase -Recurse -Force }
if (Test-Path $extractCss) { Remove-Item $extractCss -Recurse -Force }
Expand-Archive -Path $baseZip -DestinationPath $extractBase -Force
Expand-Archive -Path $cssZip -DestinationPath $extractCss -Force

# Confirm the bundled CSS runtime version in the base package
$baseApiDll = Join-Path $extractBase 'addons\counterstrikesharp\api\CounterStrikeSharp.API.dll'
$baseApiVersion = (Get-Item $baseApiDll).VersionInfo.FileVersion
Add-Validation 'base-css-version-inspected' ([bool]$baseApiVersion) "bundled=$baseApiVersion (official=$CssVersion)"

# ---------------------------------------------------------------------------
# 7. Assemble the Lane A payload (maps 1:1 onto game/csgo)
# ---------------------------------------------------------------------------
$payload = Join-Path $stagingDir "payload-$laneId"
if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
New-Item -ItemType Directory -Force -Path $payload | Out-Null

# 7.1 base package, minus the Panel executable and Panel-owned gameinfo toggles
Copy-Item -Path (Join-Path $extractBase '*') -Destination $payload -Recurse -Force
Remove-Item (Join-Path $payload 'Panel v1.4.4.exe') -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $payload 'backup') -Recurse -Force -ErrorAction SilentlyContinue

# 7.2 replace framework-owned CounterStrikeSharp runtime with the official one
$cssPayload = Join-Path $payload 'addons\counterstrikesharp'
$cssOfficial = Join-Path $extractCss 'addons\counterstrikesharp'
$frameworkDirs = @('api', 'bin', 'dotnet', 'gamedata', 'lang')
foreach ($dir in $frameworkDirs) {
    Remove-Item (Join-Path $cssPayload $dir) -Recurse -Force -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $cssOfficial $dir) (Join-Path $cssPayload $dir) -Recurse -Force
}
# official example configs; keep the base package's core.json (Bot-Improver's intentional config)
Get-ChildItem (Join-Path $cssOfficial 'configs') -File | ForEach-Object {
    if ($_.Name -ne 'core.json') {
        Copy-Item $_.FullName (Join-Path $cssPayload "configs\$($_.Name)") -Force
    }
}
if (-not (Test-Path (Join-Path $cssPayload 'configs\core.json'))) {
    throw 'core.json missing from assembled payload; Bot-Improver config would be lost.'
}
# Bot-Improver's plugins/ and shared/ stay from the base package; only the official
# source/README.txt layout difference is not relevant at runtime.

$newApiVersion = (Get-Item (Join-Path $cssPayload 'api\CounterStrikeSharp.API.dll')).VersionInfo.FileVersion
$alignedOk = ($newApiVersion -match ('^' + [regex]::Escape($CssVersion) + '(\.|$)'))
Add-Validation 'payload-css-aligned' $alignedOk "payload API=$newApiVersion"

# 7.3 overlay the candidate BotRandomizer build output
$brTarget = Join-Path $cssPayload 'plugins\BotRandomizer'
Remove-Item $brTarget -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $brTarget | Out-Null
Get-ChildItem $buildOut -File | ForEach-Object { Copy-Item $_.FullName $brTarget -Force }

# 7.4 candidate bot profiles as botprofile.vpk (root default = Medium)
$overridesPayload = Join-Path $payload 'overrides'
foreach ($level in @('High', 'Low', 'Medium')) {
    $db = Join-Path $upstreamDir "overrides\$level\botprofile.db"
    if (-not (Test-Path $db)) { throw "Candidate botprofile.db missing: $db" }
    New-BotProfileVpk -DbPath $db -OutPath (Join-Path $overridesPayload "$level\botprofile.vpk")
    Copy-Item (Join-Path $overridesPayload "$level\botprofile.vpk") (Join-Path $overridesPayload 'botprofile.vpk') -Force
}
# verify each generated vpk embeds the exact candidate db
foreach ($level in @('High', 'Low', 'Medium')) {
    $ok = Test-BotProfileVpkEmbedsDb -VpkPath (Join-Path $overridesPayload "$level\botprofile.vpk") -DbPath (Join-Path $upstreamDir "overrides\$level\botprofile.db")
    Add-Validation "profile-vpk-$level" $ok "db=$level embedded bytes verified"
}

# ---------------------------------------------------------------------------
# 8. Isolation assertions: no legacy modules, no Panel, no HumanCosmetics
# ---------------------------------------------------------------------------
$forbidden = @('PlayerKnifeCustomizer', 'TeamLineupInjector', 'PlusMatchCoordinator', 'HumanCosmetics', 'LocalArena', 'Local-Arena')
$violations = @()
foreach ($rel in (Get-ChildFilesRecursive -Dir $payload)) {
    foreach ($name in $forbidden) {
        if ($rel -like "*$name*") { $violations += "$rel (matched $name)" }
    }
}
Add-Validation 'no-legacy-or-cosmetics-modules' ($violations.Count -eq 0) "violations=$($violations -join '; ')"
$panelFiles = @(Get-ChildFilesRecursive -Dir $payload | Where-Object { $_ -match '(^|/)(Panel|panel).*\.exe$' })
Add-Validation 'no-upstream-panel' ($panelFiles.Count -eq 0) "found=$($panelFiles -join '; ')"
$pluginDirs = @(Get-ChildItem (Join-Path $cssPayload 'plugins') -Directory).Name
Write-Host ("Active Lane A CSS plugin set: {0}" -f ($pluginDirs -join ', '))

# ---------------------------------------------------------------------------
# 9. Manifest + provenance
# ---------------------------------------------------------------------------
$targetCs2Build = $null
$cs2Root = Find-Cs2Root
if ($cs2Root) {
    $steamInf = Join-Path (Get-CsgoDir -Cs2Root $cs2Root) 'steam.inf'
    if (Test-Path $steamInf) {
        foreach ($line in Get-Content $steamInf) {
            if ($line -match '^(PatchVersion|ClientVersion|VersionDate)=(.*)$') {
                $targetCs2Build += "$($Matches[1])=$($Matches[2]) "
            }
        }
    }
}

$fileManifest = @{}
foreach ($rel in (Get-ChildFilesRecursive -Dir $payload)) {
    $fileManifest[$rel] = Get-FileSha256Lower (Join-Path $payload $rel)
}

$manifest = [ordered]@{
    schemaVersion = 1
    lane          = $laneId
    createdAtUtc  = $stamp
    builtFromRepoHead = (git -C $repoRoot rev-parse HEAD).Trim()
    sources       = [ordered]@{
        botImprover = [ordered]@{
            repository   = $lock.repository
            ref          = $Ref
            baseRelease  = $ReleaseTag
            releaseAsset = [ordered]@{ url = $ReleaseAssetUrl; sha256 = (Get-FileSha256Lower $baseZip) }
        }
        counterStrikeSharp = [ordered]@{
            repository = 'roflmuffin/CounterStrikeSharp'
            version    = $CssVersion
            asset      = [ordered]@{ url = $CssAssetUrl; sha256 = (Get-FileSha256Lower $cssZip) }
            note       = "base package bundled $baseApiVersion; framework-owned runtime (api/bin/dotnet/gamedata/lang + example configs) replaced with official $CssVersion; Bot-Improver plugins/shared/core.json preserved"
        }
        botRandomizerBuild = [ordered]@{
            project       = 'addons/counterstrikesharp/plugins/BotRandomizer/BotRandomizer.csproj'
            configuration = 'Release'
            dotnetSdk     = (& $dotnetExe --version)
            cssApiPackage = $apiVersion
            outputs       = [ordered]@{}
        }
        botProfiles = [ordered]@{
            format  = 'vpk2 (byte-compatible with upstream v1.4.4 packaging; builder round-trip validated)'
            default = 'Medium (upstream default; root overrides/botprofile.vpk)'
            files   = [ordered]@{}
        }
    }
    targetCs2     = [ordered]@{ detectedBuild = $targetCs2Build; note = 'read-only detection at build time' }
    excludedFromBase = @('Panel v1.4.4.exe', 'backup/ (Panel-owned gameinfo.gi toggles)')
    payloadFiles  = [ordered]@{}
}
foreach ($f in @('BotRandomizer.dll', 'BotRandomizer.deps.json', 'BotRandomizer.pdb', 'charm_placements.json', 'cosmetic_catalog.json')) {
    $manifest.sources.botRandomizerBuild.outputs[$f] = (Get-FileSha256Lower (Join-Path $brTarget $f))
}
foreach ($level in @('High', 'Low', 'Medium')) {
    $manifest.sources.botProfiles.files[$level] = (Get-FileSha256Lower (Join-Path $upstreamDir "overrides\$level\botprofile.db"))
}
foreach ($k in $fileManifest.Keys) { $manifest.payloadFiles[$k] = $fileManifest[$k] }

# ---------------------------------------------------------------------------
# 10. Release artifact under E:\CS2MOD\releases\bot-baseline
# ---------------------------------------------------------------------------
$releaseDir = Join-Path $ReleasesRoot "$laneId-$stamp"
$releasePayloadDir = Join-Path $releaseDir 'payload'
New-Item -ItemType Directory -Force -Path $releasePayloadDir | Out-Null
Copy-Item -Path (Join-Path $payload '*') -Destination $releasePayloadDir -Recurse -Force

$manifest | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $releaseDir 'baseline-manifest.json') -Encoding UTF8
Set-Content (Join-Path $releaseDir 'THIRD_PARTY-NOTICES.txt') -Encoding UTF8 -Value @"
CS2 Local Kit bot-baseline ($laneId) — third-party components
=============================================================

1. CS2-Bot-Improver (base release $ReleaseTag + BotRandomizer/botprofile.db from $Ref)
   - repository: https://github.com/ed0ard/CS2-Bot-Improver
   - license: GNU Affero General Public License v3.0 (upstream LICENSE)
   - this artifact redistributes unmodified upstream release content plus a
     rebuild of the BotRandomizer plugin and candidate botprofile.db data from
     the pinned ref; provenance in baseline-manifest.json

2. CounterStrikeSharp $CssVersion (framework-owned runtime)
   - repository: https://github.com/roflmuffin/CounterStrikeSharp
   - license: GNU General Public License v3.0 with a special exception permitting
     plugins/derivatives (software referencing the published .NET packages) under
     the MIT license (upstream LICENSE at tag v$CssVersion)

3. MetaMod:Source (bundled with the Bot-Improver release)
   - project: https://wiki.alliedmods.net/Category:Metamod:Source_Documentation
   - redistributed as part of the upstream CS2-Bot-Improver release package

Source acquisition and hash verification are recorded in baseline-manifest.json.
This artifact is for local testing of CS2 Local Kit only.
"@

Set-Content (Join-Path $releaseDir 'MANUAL-TEST.txt') -Encoding UTF8 -Value @"
bot-baseline Lane A 手动实机测试说明(用户执行;Agent 不会自动启动 CS2)
MANUAL in-game test for bot-baseline Lane A
====================================================================

重要:这个 release 目录不是可执行程序。运行时文件(payload\)已经/将由
Install-BotBaseline.ps1 安装进你的 CS2 game\csgo。你只需要按下面步骤启动游戏。
IMPORTANT: this release directory is not an executable. The runtime payload is
installed into your CS2 game\csgo by Install-BotBaseline.ps1; just launch the game.

测试目标 CS2 build(构建时检测): $targetCs2Build

一、启动准备
1. Steam → 库 → Counter-Strike 2 → 右键属性 → 启动选项,填入: -insecure
   (本地插件测试必须;-insecure 状态下无法进入 VAC 官方服务器,属预期安全行为)
2. 测试期间不要在 Steam 里执行"验证文件完整性"(会把插件文件剥离)。
3. 可选:设置 → 游戏 → 启用开发者控制台(~),便于观察。

二、开始测试
1. 正常从 Steam 启动 CS2。
2. 可选自检:控制台输入 meta list,应能看到 CounterStrikeSharp;日志位于
   game\csgo\addons\counterstrikesharp\logs\。
3. 主菜单 → 开始游戏 → 与电脑玩家练习比赛(离线 Bot 局,任选地图)。
   Bot 数量与难度跟随你自己的设置(如 game\csgo\ServerConfig.vdf 中的
   bot_quota / bot_difficulty),请按你的设置预期观察。

三、观察清单(逐项记录,不要只总结成"能用/不能用")
- Bot 人数是否符合上述设置;
- Bot AI、瞄准、移动、投掷物行为;
- TAB 计分板显示;
- 比分是否正常累计;
- 回合正常切换;半场换边时没有意外的 mp_restartgame / 比分清零;
- 死亡、重生正常;
- 连续若干回合没有稳定崩溃;
- Bot 头像 / 名字 / 档案行为(BotRandomizer,默认难度 Medium);
- Bot 饰品(BotRandomizer 启用的范围内);
- FPS 与帧节奏(没有新增的持续卡顿或异常长帧)。
验收标准以 docs/MANUAL-ACCEPTANCE.md 第 2 节为准。

四、单独的可选高危探针
上游近期有 knife/drop 相关崩溃报告:任何故意的下刀/生成刀测试必须放在
普通比赛稳定性确认之后单独进行,不要混在前几分钟的常规观察里。

五、测试后
1. 收集现场诊断(只读,不会启动游戏):
   pwsh -File scripts\bot-baseline\Collect-BotBaselineDiagnostics.ps1
2. 如需完全恢复到安装前状态:
   pwsh -File scripts\bot-baseline\Restore-BotBaseline.ps1 -Apply

IN-GAME ACCEPTANCE ITEMS (docs/MANUAL-ACCEPTANCE.md section 2): bot count,
bot AI/aim/movement/nades, TAB scoreboard, score accumulation, round
transitions and halftime without unexpected mp_restartgame, death/respawn,
several consecutive stable rounds, bot profile/name/avatar behaviour, bot
cosmetics where enabled, FPS and frame pacing. Knife-drop probing is a
separate optional high-risk step after ordinary stability is established.
"@

$zipPath = Join-Path $ReleasesRoot "$laneId-$stamp.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $releaseDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
$releaseSha = Get-FileSha256Lower $zipPath

# standalone manifest next to the zip carries the artifact hash; the manifest
# inside the zip cannot contain its own hash
$manifest.releaseArtifact = [ordered]@{
    path  = $zipPath
    sha256 = $releaseSha
}
$manifest | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $ReleasesRoot "$laneId-$stamp.manifest.json") -Encoding UTF8

Write-Host ''
Write-Host "RELEASE_DIR : $releaseDir"
Write-Host "RELEASE_ZIP : $zipPath"
Write-Host "RELEASE_SHA : $releaseSha"
Write-Host "PAYLOAD_FILES: $($fileManifest.Count)"
