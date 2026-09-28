# Publish-Controller.ps1 — builds and packages self-contained Windows Controller UI
[CmdletBinding()]
param(
    [string]$Cs2ModRoot = 'E:\CS2MOD',
    [string]$Configuration = 'Release',
    [string]$DotNetExe = '',
    [string]$OutputDir = ''
)

$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)
    Write-Host "== $Message" -ForegroundColor Cyan
}

function Write-Ok {
    param([string]$Message)
    Write-Host "   OK  $Message" -ForegroundColor Green
}

function Write-Fail {
    param([string]$Message)
    Write-Host "   FAIL $Message" -ForegroundColor Red
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

# 1. Resolve dotnet CLI
if (-not $DotNetExe) {
    $candidate = Join-Path $repoRoot 'temp\tooling\dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $candidate) {
        $DotNetExe = $candidate
    } else {
        $candidate2 = 'E:\Projects\CS2-Local-Kit\wt-cosmetics-lab\temp\tooling\dotnet\dotnet.exe'
        if (Test-Path -LiteralPath $candidate2) {
            $DotNetExe = $candidate2
        } else {
            $DotNetExe = 'dotnet'
        }
    }
}

Write-Step "Using .NET host: $DotNetExe"
$dotnetVer = (& $DotNetExe --version).Trim()
Write-Ok ".NET SDK version: $dotnetVer"

# 2. Determine output release directory
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
if (-not $OutputDir) {
    $releasesRoot = Join-Path $Cs2ModRoot 'releases\controller'
    $OutputDir = Join-Path $releasesRoot "controller-$stamp"
}

$payloadDir = Join-Path $OutputDir 'payload'
if (Test-Path -LiteralPath $payloadDir) {
    Remove-Item -LiteralPath $payloadDir -Recurse -Force
}
New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null

# 3. Publish self-contained win-x64
Write-Step "Publishing CS2LocalKit.App (win-x64, self-contained, $Configuration)..."
$projectPath = Join-Path $repoRoot 'src\CS2LocalKit.App\CS2LocalKit.App.csproj'

$env:DOTNET_ROOT = Split-Path -Parent $DotNetExe
$env:PATH = "$($env:DOTNET_ROOT);$($env:PATH)"

& $DotNetExe publish $projectPath `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -o $payloadDir `
    /p:PublishSingleFile=false `
    --nologo `
    -v minimal

if ($LASTEXITCODE -ne 0) {
    Write-Fail "dotnet publish failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

$exePath = Join-Path $payloadDir 'CS2LocalKit.App.exe'
if (-not (Test-Path -LiteralPath $exePath)) {
    Write-Fail "Expected executable not found: $exePath"
    exit 1
}
Write-Ok "Published executable: $exePath"

# 4. Verify runtime lock copy exists
$lockInPayload = Join-Path $payloadDir 'runtime\inventory-simulator.lock.json'
if (-not (Test-Path -LiteralPath $lockInPayload)) {
    Write-Step "Copying runtime lock into payload..."
    $runtimeDir = Join-Path $payloadDir 'runtime'
    New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null
    Copy-Item (Join-Path $repoRoot 'runtime\inventory-simulator.lock.json') -Destination $lockInPayload
}
Write-Ok "Runtime lock present in payload: $lockInPayload"
Write-Step "Computing file hashes and checking private data boundaries..."
$files = Get-ChildItem -Path $payloadDir -Recurse -File
$fileHashes = [ordered]@{}

foreach ($f in $files) {
    $rel = $f.FullName.Substring($payloadDir.Length).TrimStart('\', '/').Replace('\', '/')
    $stream = [System.IO.File]::OpenRead($f.FullName)
    $hash = [BitConverter]::ToString([System.Security.Cryptography.SHA256]::HashData($stream)).Replace('-', '').ToLowerInvariant()
    $stream.Dispose()
    $fileHashes[$rel] = $hash

    # Boundary check: ensure no private SteamID or preset JSON leaked into release
    if ($f.Extension -eq '.json') {
        $content = [System.IO.File]::ReadAllText($f.FullName)
        if ($content -match '7656\d{13}' -and $content -notmatch '76561198000000000') {
            Write-Fail "PRIVATE DATA LEAK DETECTED in payload file: $rel"
            exit 1
        }
    }
}
Write-Ok "Hashed $($files.Count) files. No private data detected."

# 6. Read git metadata and runtime lock
$gitHead = (git -C $repoRoot rev-parse HEAD).Trim()
$lockJson = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'runtime\inventory-simulator.lock.json') | ConvertFrom-Json

$manifest = [ordered]@{
    kind = 'cs2-local-kit/controller-release'
    releaseId = "controller-$stamp"
    createdAt = (Get-Date).ToString('yyyy-MM-ddTHH:mm:sszzz')
    gitHead = $gitHead
    targetFramework = 'net10.0-windows'
    runtimeIdentifier = 'win-x64'
    selfContained = $true
    configuration = $Configuration
    coreVersion = '1.0.0'
    uiVersion = '1.0.0'
    runtimeLock = [ordered]@{
        ref = $lockJson.ref
        tag = $lockJson.tag
        status = $lockJson.status
        testedCs2Build = $lockJson.testedCs2Build
        patchedDllSha256 = $lockJson.acceptedRuntime.patchedDllSha256
    }
    manifestFiles = $fileHashes
}

$manifestPath = Join-Path $OutputDir 'release-manifest.json'
[System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 10))
Write-Ok "Manifest written: $manifestPath"

Write-Step "Controller UI Release successfully published to: $OutputDir"
Write-Host "   Payload: $payloadDir"
Write-Host "   Executable: $exePath"
Write-Host "   Manifest: $manifestPath"
