# Build-InventorySimulatorPatch.ps1 — reproduce the C1.1 patched InventorySimulator build.
#
# Builds the exact-upstream source (pinned commit) with exactly one lifecycle patch applied,
# so C1 (control) and C1.1 (probe) differ only in that line. Outputs land in
# E:\CS2MOD\releases\cosmetics-lab\inventory-simulator-c1_1-patched-<ts>\.
#
# Requires a .NET 10 SDK (task-local bootstrap is fine, pass -DotNetExe).

param(
    [string]$DotNetExe = '',
    # Where to keep the ephemeral source tree + tooling (git-ignored).
    [string]$WorkDir = ''
)

. (Join-Path $PSScriptRoot 'CosmeticsLab.Common.ps1')

$wtRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $WorkDir) { $WorkDir = Join-Path $wtRoot 'temp\c1_1-build' }
$patchFile = Join-Path $wtRoot 'experiments\cosmetics-lab\inventory-simulator\c1_1-startup-lifecycle-patch\c1_1-startup-lifecycle.patch'
$upstreamCommit = 'fade4449aaa6d5153261c855d0cc12d7dacfefdd'
$upstreamUrl = 'https://github.com/ianlucas/cs2-css-inventory-simulator.git'

if (-not $DotNetExe) {
    $candidate = Join-Path $wtRoot 'temp\tooling\dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $candidate) { $DotNetExe = $candidate } else { $DotNetExe = 'dotnet' }
}

Write-C1Step "Fetching upstream source at pinned commit $upstreamCommit"
$srcDir = Join-Path $WorkDir 'src'
if (Test-Path -LiteralPath $srcDir) { Remove-Item -LiteralPath $srcDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $srcDir | Out-Null
git init -q $srcDir
git -C $srcDir remote add origin $upstreamUrl
git -C $srcDir fetch -q --depth 1 origin $upstreamCommit
git -C $srcDir checkout -q FETCH_HEAD
$head = (git -C $srcDir rev-parse HEAD).Trim()
if ($head -ne $upstreamCommit) { throw "Fetched HEAD $head != pinned $upstreamCommit" }
Write-C1Ok "source at $head"

Write-C1Step 'Applying single-line lifecycle patch'
git -C $srcDir apply $patchFile
$diff = git -C $srcDir diff
$changedFiles = @(git -C $srcDir diff --name-only)
if ($changedFiles.Count -ne 1 -or $changedFiles[0] -ne 'source/InventorySimulator/InventorySimulator.cs') {
    throw "Unexpected changed files after patch: $($changedFiles -join ', ')"
}
# The produced diff must match the committed patch file (compared with normalized line
# endings, since git checkout may translate EOLs in the working tree).
$noBom = [System.Text.UTF8Encoding]::new($false)
$regen = Join-Path $WorkDir 'regen.patch'
[System.IO.File]::WriteAllText($regen, (($diff -join "`n") + "`n"), $noBom)
$norm = { param([string]$Path) ([IO.File]::ReadAllText($Path)) -replace "`r`n", "`n" }
if ((& $norm $regen) -ne (& $norm $patchFile)) {
    throw 'Regenerated diff does not match the committed patch file.'
}
Write-C1Ok 'diff == committed patch file'

Write-C1Step 'Building (dotnet build -c Release, net10.0)'
& $DotNetExe build (Join-Path $srcDir 'InventorySimulator.csproj') -c Release --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with $LASTEXITCODE" }
$pluginOut = Join-Path $srcDir 'bin\Release\plugins\InventorySimulator'
foreach ($f in 'InventorySimulator.dll', 'InventorySimulator.deps.json') {
    if (-not (Test-Path -LiteralPath (Join-Path $pluginOut $f))) { throw "Build output missing: $f" }
}
Write-C1Ok 'build succeeded'

Write-C1Step 'Assembling C1.1 release (plugin from build; lang/gamedata pinned to upstream release)'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$releaseDir = Join-Path $script:Cs2ModRoot "releases\cosmetics-lab\inventory-simulator-c1_1-patched-$stamp"
$payloadDir = Join-Path $releaseDir 'payload'
New-Item -ItemType Directory -Force -Path (Join-Path $payloadDir 'addons\counterstrikesharp\plugins\InventorySimulator') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $payloadDir 'addons\counterstrikesharp\gamedata') | Out-Null

# Managed plugin binaries: from this patched build.
foreach ($f in 'InventorySimulator.dll', 'InventorySimulator.deps.json', 'InventorySimulator.pdb') {
    Copy-Item -LiteralPath (Join-Path $pluginOut $f) -Destination (Join-Path $payloadDir "addons\counterstrikesharp\plugins\InventorySimulator\$f")
}
# lang + gamedata: byte-identical to the upstream 3.3.0 release zip (control consistency),
# taken from the C1 release payload which was hash-verified against the upstream asset.
$c1Payload = Get-ChildItem -LiteralPath (Join-Path $script:Cs2ModRoot 'releases\cosmetics-lab') -Directory -Filter 'inventory-simulator-c1-*' |
    Where-Object { $_.Name -notlike '*c1_1*' } | Sort-Object Name -Descending | Select-Object -First 1
if (-not $c1Payload) { throw 'C1 exact-upstream release not found.' }
$srcLang = Join-Path $c1Payload.FullName 'payload\addons\counterstrikesharp\plugins\InventorySimulator\lang'
$dstLang = Join-Path $payloadDir 'addons\counterstrikesharp\plugins\InventorySimulator\lang'
Copy-Item -LiteralPath $srcLang -Destination $dstLang -Recurse
Copy-Item -LiteralPath (Join-Path $c1Payload.FullName 'payload\addons\counterstrikesharp\gamedata\inventory-simulator.json') -Destination (Join-Path $payloadDir 'addons\counterstrikesharp\gamedata\inventory-simulator.json')

Write-C1Step 'Writing C1.1 manifest'
$payload = @()
Get-ChildItem -LiteralPath $payloadDir -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($payloadDir.Length).TrimStart('\').Replace('\', '/')
    $role = if ($rel -like 'addons/counterstrikesharp/plugins/InventorySimulator/InventorySimulator.*') { 'inventory-simulator-patched' } elseif ($rel -like 'addons/counterstrikesharp/plugins/InventorySimulator/lang/*') { 'inventory-simulator' } else { 'inventory-simulator' }
    $payload += [pscustomobject]@{ path = $rel; role = $role; sha256 = Get-C1FileSha256Hex -Path $_.FullName }
}
$manifest = [pscustomobject]@{
    kind = 'cosmetics-lab-c1_1-patched-release'
    schemaVersion = 1
    createdAt = (Get-Date -Format 'o')
    experiment = 'inventory-simulator-c1_1-startup-lifecycle-patch'
    upstream = [pscustomobject]@{
        repository = 'ianlucas/cs2-css-inventory-simulator'
        tag = '3.3.0'
        commit = $upstreamCommit
        license = 'MIT'
    }
    patch = [pscustomobject]@{
        file = 'experiments/cosmetics-lab/inventory-simulator/c1_1-startup-lifecycle-patch/c1_1-startup-lifecycle.patch'
        summary = 'Load(): OnFileChanged(null, ConVars.File.Value) -> Inventories.Load(ConVars.File.Value); single line; OnFileChanged itself unchanged'
        hypothesis = 'startup crash caused by Utilities.GetPlayers() during Load() before engine global variables are initialized'
    }
    build = [pscustomobject]@{
        dotnet = (& $DotNetExe --version).Trim()
        targetFramework = 'net10.0'
        counterStrikeSharpApiReference = '1.0.375'
        note = 'lang/ and gamedata/ are byte-identical to the upstream 3.3.0 release zip; only the managed plugin binaries come from this build.'
    }
    controlRelease = $c1Payload.FullName
    payload = $payload
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $releaseDir 'manifest.json') -Encoding utf8NoBOM

Write-Host ''
Write-C1Ok "C1.1 release: $releaseDir"
foreach ($p in $payload) {
    if ($p.path -like '*InventorySimulator.*') { Write-Host ("   {0}  {1}" -f $p.sha256, $p.path) }
}
