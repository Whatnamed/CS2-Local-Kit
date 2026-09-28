# CosmeticsLab.Common.ps1 — shared helpers for the cosmetics-lab C1 experiment scripts.
# Dot-source from the other scripts; never run directly.

$ErrorActionPreference = 'Stop'

$script:Cs2ModRoot = 'E:\CS2MOD'
$script:C1GameinfoLine = 'Game	csgo/addons/metamod'
$script:C1CfgName = 'cosmeticslab_c1.cfg'
$script:C1FixtureInstalledRelPath = 'addons\counterstrikesharp\configs\plugins\InventorySimulator\inventories.json'

function Write-C1Step {
    param([string]$Message)
    Write-Host "== $Message" -ForegroundColor Cyan
}

function Write-C1Ok {
    param([string]$Message)
    Write-Host "   OK  $Message" -ForegroundColor Green
}

function Write-C1Fail {
    param([string]$Message)
    Write-Host "   FAIL $Message" -ForegroundColor Red
}

function Get-C1FileSha256 {
    param(
        [Parameter(Mandatory = $true)][string]$Path
    )
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Get-C1FileSha256: file not found: $Path"
    }
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        return [System.Security.Cryptography.SHA256]::HashData($stream)
    }
    finally {
        $stream.Dispose()
    }
}

function Get-C1FileSha256Hex {
    param(
        [Parameter(Mandatory = $true)][string]$Path
    )
    return ([BitConverter]::ToString((Get-C1FileSha256 -Path $Path))).Replace('-', '').ToLowerInvariant()
}

function ConvertTo-C1Hex {
    param([byte[]]$Bytes)
    return ([BitConverter]::ToString($Bytes)).Replace('-', '').ToLowerInvariant()
}

# Locate the real CS2 install. Detection must be based on appmanifest_730.acf presence
# inside a library's steamapps folder; path probing alone hits the stale D:\Steam skeleton.
function Find-C1Cs2Root {
    $steamRoots = @('D:\Steam', 'E:\SteamLibrary', 'C:\Program Files (x86)\Steam')
    $libraryFiles = @()
    foreach ($root in $steamRoots) {
        $lib = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $lib -PathType Leaf) { $libraryFiles += $lib }
    }
    if ($libraryFiles.Count -eq 0) {
        throw 'Find-C1Cs2Root: no libraryfolders.vdf found under known Steam roots.'
    }

    $libraryPaths = New-Object System.Collections.Generic.List[string]
    foreach ($lib in $libraryFiles) {
        $content = Get-Content -LiteralPath $lib -Raw
        $matchesFound = [regex]::Matches($content, '"path"\s+"([^"]+)"')
        foreach ($m in $matchesFound) {
            $p = $m.Groups[1].Value -replace '\\\\', '\'
            if (-not $libraryPaths.Contains($p)) { $libraryPaths.Add($p) }
        }
    }

    foreach ($library in $libraryPaths) {
        $manifest = Join-Path $library 'steamapps\appmanifest_730.acf'
        if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) { continue }
        $acf = Get-Content -LiteralPath $manifest -Raw
        $installdir = [regex]::Match($acf, '"installdir"\s+"([^"]+)"').Groups[1].Value
        if (-not $installdir) { continue }
        $csRoot = Join-Path (Join-Path $library 'steamapps\common') $installdir
        $cs2Exe = Join-Path $csRoot 'game\bin\win64\cs2.exe'
        if (Test-Path -LiteralPath $cs2Exe -PathType Leaf) {
            return $csRoot
        }
    }
    throw 'Find-C1Cs2Root: no library with a valid CS2 appmanifest + cs2.exe was found.'
}

function Test-C1Cs2Running {
    $proc = Get-Process -Name 'cs2' -ErrorAction SilentlyContinue
    return ($null -ne $proc)
}

function Read-C1Json {
    param([Parameter(Mandatory = $true)][string]$Path)
    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

# Verify that every payload file in the manifest exists on disk (relative to $Root)
# and matches its recorded sha256. Returns the verified manifest object.
function Assert-C1Payload {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [string[]]$Roles = @()
    )
    $manifest = Read-C1Json -Path $ManifestPath
    $checked = 0
    foreach ($entry in $manifest.payload) {
        if ($Roles.Count -gt 0 -and $Roles -notcontains $entry.role) { continue }
        $full = Join-Path $Root $entry.path
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) {
            throw "Assert-C1Payload: missing payload file: $($entry.path)"
        }
        $actual = Get-C1FileSha256Hex -Path $full
        if ($actual -ne $entry.sha256) {
            throw "Assert-C1Payload: hash mismatch for $($entry.path)`n     expected $($entry.sha256)`n     actual   $actual"
        }
        $checked++
    }
    Write-C1Ok ("payload verified: {0} file(s) hash-matched (manifest {1})" -f $checked, (Split-Path $ManifestPath -Leaf))
    return $manifest
}

# Build a hash snapshot {relativePath: sha256} of all files under $Root, recursively.
function Get-C1TreeSnapshot {
    param([Parameter(Mandatory = $true)][string]$Root)
    $snapshot = @{}
    if (-not (Test-Path -LiteralPath $Root)) { return $snapshot }
    $files = Get-ChildItem -LiteralPath $Root -Recurse -File -Force
    foreach ($f in $files) {
        $rel = $f.FullName.Substring($Root.Length).TrimStart('\', '/').Replace('\', '/')
        $snapshot[$rel] = Get-C1FileSha256Hex -Path $f.FullName
    }
    return $snapshot
}

# Ensure the SearchPaths block of a gameinfo.gi contains exactly the C1 metamod line.
# Byte-precise insert-only modification: the rest of the file is untouched, the file's
# own BOM state, newline style and indentation are preserved.
function Add-C1GameinfoMetamodLine {
    param(
        [Parameter(Mandatory = $true)][string]$GameinfoPath,
        [string[]]$RequiredLines = @('Game	csgo', 'Game	csgo_imported', 'Game	csgo_core', 'Game	core')
    )
    $bytes = [System.IO.File]::ReadAllBytes($GameinfoPath)
    $hadBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $noBomEnc = [System.Text.UTF8Encoding]::new($false)
    $raw = if ($hadBom) { [System.Text.Encoding]::UTF8.GetString($bytes) } else { $noBomEnc.GetString($bytes) }
    if ($hadBom -and $raw.Length -gt 0 -and $raw[0] -eq [char]0xFEFF) { $raw = $raw.Substring(1) }

    if ($raw -match 'csgo/addons/metamod') {
        throw 'Add-C1GameinfoMetamodLine: gameinfo.gi already references csgo/addons/metamod.'
    }
    foreach ($req in $RequiredLines) {
        if ($raw -notmatch [regex]::Escape($req)) {
            throw "Add-C1GameinfoMetamodLine: gameinfo.gi does not look like the expected stock SearchPaths (missing '$($req.Trim())'). Refusing to modify."
        }
    }

    # Determine newline style from the file itself (default CRLF, matching Valve files).
    $nl = if ($raw -match "`r`n") { "`r`n" } else { "`n" }

    # Locate "SearchPaths" then its opening brace on the next line.
    # \r? before $ is required: .NET multiline $ does not match before \r\n.
    $searchMatch = [regex]::Match($raw, '(?m)^([ \t]*)SearchPaths[ \t]*\r?$')
    if (-not $searchMatch.Success) { throw 'Add-C1GameinfoMetamodLine: SearchPaths block not found.' }
    $pos = $searchMatch.Index + $searchMatch.Length
    # Find the first { line after SearchPaths, consuming its full newline, so the insert
    # position lands exactly at the start of the next line.
    $rest = $raw.Substring($pos)
    $braceLine = [regex]::Match($rest, '(?m)^[ \t]*\{[ \t]*\r?\n')
    if (-not $braceLine.Success) { throw 'Add-C1GameinfoMetamodLine: SearchPaths block has no opening brace on the next line.' }
    $insertAt = $pos + $braceLine.Index + $braceLine.Length

    # Indentation: copy from the first Game line inside the block.
    $afterBrace = $raw.Substring($insertAt)
    $gameLine = [regex]::Match($afterBrace, '(?m)^([ \t]*)Game[ \t]')
    if (-not $gameLine.Success) { throw 'Add-C1GameinfoMetamodLine: no Game line found inside SearchPaths.' }
    $indent = $gameLine.Groups[1].Value

    $newRaw = $raw.Substring(0, $insertAt) + $indent + $script:C1GameinfoLine + $nl + $raw.Substring($insertAt)
    $outBytes = if ($hadBom) { [System.Text.Encoding]::UTF8.GetBytes($newRaw) } else { $noBomEnc.GetBytes($newRaw) }
    [System.IO.File]::WriteAllBytes($GameinfoPath, $outBytes)
    return $script:C1GameinfoLine
}
