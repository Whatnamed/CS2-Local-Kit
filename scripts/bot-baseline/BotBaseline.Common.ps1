#Requires -Version 7
# Shared helpers for CS2 Local Kit bot-baseline automation.
# Dot-sourced by the Build/Install/Restore/Diagnostics scripts in this directory.

Set-StrictMode -Version Latest

$script:Crc32Table = $null

[Flags()]
enum BaselineFileAction {
    None       = 0
    Created    = 1
    Overwritten = 2
    Modified   = 4
    Isolated   = 8
}

function Get-RepoRoot {
    # scripts/bot-baseline/<this file> -> repo root is two levels up
    return (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}

function Get-FileSha256Lower {
    param([string]$Path)
    if (Test-Path $Path -PathType Leaf) {
        return (Get-FileHash $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    return $null
}

function Find-Cs2Root {
    <#
        Detect the live CS2 installation root. Prefers the Steam library that owns
        appmanifest_730.acf; falls back to any library containing the folder (residue).
        Returns $null when nothing plausible is found.
    #>
    param([string]$ExplicitRoot)

    if ($ExplicitRoot) {
        if (-not (Test-Path (Join-Path $ExplicitRoot 'game\csgo'))) {
            throw "Explicit -Cs2Root '$ExplicitRoot' does not look like a CS2 install (no game\csgo)."
        }
        return (Resolve-Path $ExplicitRoot).Path
    }

    $steamPath = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
    if (-not $steamPath) { return $null }

    $libraries = @($steamPath)
    $libraryVdf = Join-Path $steamPath 'steamapps\libraryfolders.vdf'
    if (Test-Path $libraryVdf) {
        foreach ($line in Get-Content $libraryVdf) {
            if ($line -match '"path"\s+"([^"]+)"') {
                $libraries += ($Matches[1] -replace '\\\\', '\')
            }
        }
    }

    $owned = $null
    $residue = $null
    foreach ($lib in $libraries) {
        $candidate = Join-Path $lib 'steamapps\common\Counter-Strike Global Offensive'
        $manifest = Join-Path $lib 'steamapps\appmanifest_730.acf'
        if (Test-Path $candidate) {
            if ((Test-Path $manifest) -and -not $owned) { $owned = $candidate }
            elseif (-not $residue) { $residue = $candidate }
        }
    }
    if ($owned) { return $owned }
    if ($residue) { return $residue }
    return $null
}

function Test-Cs2Running {
    [CmdletBinding()]
    param()
    return [bool](Get-Process -Name 'cs2' -ErrorAction SilentlyContinue)
}

function Get-CsgoDir {
    param([string]$Cs2Root)
    return (Join-Path $Cs2Root 'game\csgo')
}

function Get-ChildFilesRecursive {
    # returns relative paths (forward slashes) of all files under $Dir
    param([string]$Dir)
    $root = (Resolve-Path $Dir).Path
    Get-ChildItem $root -Recurse -File | ForEach-Object {
        $_.FullName.Substring($root.Length + 1).Replace('\', '/')
    }
}

function Read-BotImproverLock {
    param([string]$RepoRoot)
    $lockPath = Join-Path $RepoRoot 'runtime\bot-improver.lock.json'
    if (-not (Test-Path $lockPath)) { throw "Lock file not found: $lockPath" }
    return (Get-Content $lockPath -Raw | ConvertFrom-Json)
}

# All destructive operations resolve inside their recorded root and reject links.
function Resolve-BaselinePath {
    param([string]$Root, [string]$RelativePath)
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $path = [IO.Path]::GetFullPath((Join-Path $rootPath $RelativePath))
    if ([IO.Path]::IsPathRooted($RelativePath) -or -not $path.StartsWith($rootPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path escapes ownership root: $RelativePath"
    }
    $probe = $path
    while ($probe) {
        if (Test-Path -LiteralPath $probe) {
            if ((Get-Item -LiteralPath $probe -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Reparse point in ownership path: $probe"
            }
        }
        $probe = Split-Path $probe -Parent
    }
    return $path
}

function Get-BaselineIdentity {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked ownership target: $Path" }
    if (-not $item.PSIsContainer) { return Get-FileSha256Lower $Path }
    $lines = @(Get-ChildItem -LiteralPath $Path -Recurse -Force | Sort-Object FullName | ForEach-Object {
        if ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked ownership target: $($_.FullName)" }
        $rel = $_.FullName.Substring($Path.TrimEnd('\', '/').Length + 1).Replace('\', '/')
        if ($_.PSIsContainer) { "D:$rel" } else { "F:$rel`:$(Get-FileSha256Lower $_.FullName)" }
    })
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($lines -join "`n")))).ToLowerInvariant()
}

function New-BaselineGameinfo {
    param([string]$SourcePath, [string]$OutPath, [string[]]$SearchPaths)
    $enc = [Text.UTF8Encoding]::new($false, $true)
    $raw = $enc.GetString([IO.File]::ReadAllBytes($SourcePath))
    $block = [regex]::Match($raw, '(?m)^[ \t]*SearchPaths[ \t]*\r?\n[ \t]*\{[ \t]*\r?\n')
    if (-not $block.Success) { throw 'SearchPaths block not found.' }
    $start = $block.Index + $block.Length
    $end = $raw.IndexOf('}', $start)
    if ($end -lt 0) { throw 'Unclosed SearchPaths block.' }
    $body = $raw.Substring($start, $end - $start)
    $indent = [regex]::Match($body, '(?m)^([ \t]*)Game[ \t]+').Groups[1].Value
    $nl = if ($raw.Contains("`r`n")) { "`r`n" } else { "`n" }
    $insert = ''
    foreach ($searchPath in $SearchPaths) {
        $matchesFound = [regex]::Matches($body, '(?m)^[ \t]*Game[ \t]+' + [regex]::Escape($searchPath) + '[ \t]*\r?$')
        if ($matchesFound.Count -gt 1) { throw "Duplicate search path: $searchPath" }
        if ($matchesFound.Count -eq 0) { $insert += $indent + "Game`t" + $searchPath + $nl }
    }
    [IO.File]::WriteAllBytes($OutPath, $enc.GetBytes($raw.Insert($start, $insert)))
}

# ---------------------------------------------------------------------------
# VPK builder (botprofile.vpk)
#
# Reverse-engineered byte layout of CS2-Bot-Improver v1.4.4 botprofile VPKs.
# Validated by reconstructing the shipped v1.4.4 files byte-for-byte.
#
#   header  (28 bytes) : magic 0x55aa1234, version 2, treeSize, fileDataSize,
#                        archiveMd5SectionSize = 0, otherMd5SectionSize = 48,
#                        signatureSectionSize = 0
#   tree    (37 bytes) : "db\0" " \0" "botprofile\0" + entry + 0xFFFF + "\0\0\0"
#                        entry = crc32(db):u32, preloadBytes=0:u16,
#                                archiveIndex=0x7FFF:u16, entryOffset=0:u32,
#                                entryLength=len(db):u32
#   data    (len(db))  : the raw botprofile.db bytes
#   md5 section (48B)  : md5(tree) | md5(<empty archive md5 section>) |
#                        md5(everything written so far, i.e. file bytes minus
#                            the final 16-byte md5)
# ---------------------------------------------------------------------------

function New-BotProfileVpk {
    <#
        Packs a botprofile.db into a botprofile.vpk byte-compatible with the
        upstream CS2-Bot-Improver v1.4.4 packaging format.
    #>
    param(
        [Parameter(Mandatory)] [string]$DbPath,
        [Parameter(Mandatory)] [string]$OutPath
    )

    $db = [System.IO.File]::ReadAllBytes($DbPath)

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)

    # tree
    $tree = New-Object System.IO.MemoryStream
    $tw = New-Object System.IO.BinaryWriter($tree)
    $tw.Write([System.Text.Encoding]::ASCII.GetBytes("db`0"))
    $tw.Write([System.Text.Encoding]::ASCII.GetBytes(" `0"))
    $tw.Write([System.Text.Encoding]::ASCII.GetBytes("botprofile`0"))
    $crc = Get-Crc32 -Bytes $db
    $tw.Write([uint32]$crc)
    $tw.Write([uint16]0)        # preload bytes
    $tw.Write([uint16]0x7FFF)   # archive index: self-contained
    $tw.Write([uint32]0)        # entry offset within data section
    $tw.Write([uint32]$db.Length)
    $tw.Write([uint16]0xFFFF)   # entry terminator
    $tw.Write([byte]0); $tw.Write([byte]0); $tw.Write([byte]0)  # tree terminator
    $tw.Flush()
    $treeBytes = $tree.ToArray()

    # header
    $bw.Write([uint32]0x55AA1234)
    $bw.Write([uint32]2)                 # version
    $bw.Write([uint32]$treeBytes.Length) # tree size
    $bw.Write([uint32]$db.Length)        # file data section size
    $bw.Write([uint32]0)                 # archive md5 section size
    $bw.Write([uint32]48)                # other md5 section size
    $bw.Write([uint32]0)                 # signature section size
    $bw.Flush()

    $bw.Write($treeBytes)
    $bw.Write($db)
    $bw.Flush()

    # md5 section
    $md5Tree = [System.Security.Cryptography.MD5]::Create()
    $h1 = $md5Tree.ComputeHash($treeBytes)
    $h2 = $md5Tree.ComputeHash([byte[]]@())
    $bw.Write($h1); $bw.Write($h2)
    $bw.Flush()
    $h3 = $md5Tree.ComputeHash($ms.ToArray())
    $bw.Write($h3)
    $bw.Flush()

    [System.IO.File]::WriteAllBytes($OutPath, $ms.ToArray())
    $bw.Dispose(); $ms.Dispose(); $tree.Dispose(); $md5Tree.Dispose()
}

function Read-BotProfileVpkEntry {
    <#
        Parses a botprofile.vpk in the layout described above and returns the
        embedded botprofile.db bytes plus structural fields (for validation).
    #>
    param([Parameter(Mandatory)] [string]$VpkPath)

    $data = [System.IO.File]::ReadAllBytes($VpkPath)
    $magic, $version, $treeSize, $fileDataSize, $archiveMd5Size, $otherMd5Size, $sigSize =
        [BitConverter]::ToUInt32($data, 0), [BitConverter]::ToUInt32($data, 4),
        [BitConverter]::ToUInt32($data, 8), [BitConverter]::ToUInt32($data, 12),
        [BitConverter]::ToUInt32($data, 16), [BitConverter]::ToUInt32($data, 20),
        [BitConverter]::ToUInt32($data, 24)

    if ($magic -ne 0x55AA1234) { throw "Not a VPK file: $VpkPath" }
    if ($version -ne 2) { throw "Unexpected VPK version $version in $VpkPath" }

    $tree = $data[28..(28 + $treeSize - 1)]
    # entry starts after "db\0 \0botprofile\0" (16 bytes)
    $entryOff = 16
    $crc = [BitConverter]::ToUInt32($tree, $entryOff)
    $preload = [BitConverter]::ToUInt16($tree, $entryOff + 4)
    $aidx = [BitConverter]::ToUInt16($tree, $entryOff + 6)
    $eoff = [BitConverter]::ToUInt32($tree, $entryOff + 8)
    $elen = [BitConverter]::ToUInt32($tree, $entryOff + 12)

    if ($preload -ne 0 -or $aidx -ne 0x7FFF -or $eoff -ne 0) {
        throw "Unexpected entry layout in $VpkPath (preload=$preload aidx=$aidx off=$eoff)"
    }

    $db = $data[(28 + $treeSize)..((28 + $treeSize) + $elen - 1)]
    return [pscustomobject]@{
        Crc         = $crc
        EntryLength = $elen
        FileDataSize = $fileDataSize
        Db          = $db
    }
}

function Get-Crc32 {
    # Standard CRC-32 (IEEE 802.3, same as zlib.crc32). Implemented in C# via
    # Add-Type because PowerShell's bitwise operators have unreliable numeric
    # promotion for uint32 math.
    param([byte[]]$Bytes)

    if (-not ('BotBaseline.Crc32' -as [type])) {
        Add-Type -TypeDefinition @"
namespace BotBaseline {
    public static class Crc32 {
        private static readonly uint[] Table = new uint[256];
        static Crc32() {
            for (uint i = 0; i < 256; i++) {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = ((c & 1) != 0) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
                Table[i] = c;
            }
        }
        public static uint Compute(byte[] bytes) {
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in bytes)
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
"@
    }
    return [BotBaseline.Crc32]::Compute($Bytes)
}

function Test-BotProfileVpkEmbedsDb {
    <#
        Returns $true when the botprofile.vpk embeds exactly the bytes of the
        given botprofile.db.
    #>
    param(
        [Parameter(Mandatory)] [string]$VpkPath,
        [Parameter(Mandatory)] [string]$DbPath
    )
    $entry = Read-BotProfileVpkEntry -VpkPath $VpkPath
    $db = [System.IO.File]::ReadAllBytes($DbPath)
    if ($entry.Db.Length -ne $db.Length) { return $false }
    for ($i = 0; $i -lt $db.Length; $i++) {
        if ($entry.Db[$i] -ne $db[$i]) { return $false }
    }
    return $true
}

function Test-VpkBuilderAgainstTemplate {
    <#
        Format proof: rebuild each shipped v1.4.4 botprofile.vpk from the db
        embedded inside it; the reconstruction must be byte-for-byte identical.
    #>
    param([Parameter(Mandatory)] [string[]]$TemplateVpks, [string]$TempDir)

    $results = @()
    foreach ($tpl in $TemplateVpks) {
        $entry = Read-BotProfileVpkEntry -VpkPath $tpl
        $tmpDb = Join-Path $TempDir ("roundtrip-" + [System.IO.Path]::GetRandomFileName() + ".db")
        $tmpVpk = Join-Path $TempDir ("roundtrip-" + [System.IO.Path]::GetRandomFileName() + ".vpk")
        try {
            [System.IO.File]::WriteAllBytes($tmpDb, $entry.Db)
            New-BotProfileVpk -DbPath $tmpDb -OutPath $tmpVpk
            $origHash = Get-FileSha256Lower $tpl
            $newHash = Get-FileSha256Lower $tmpVpk
            $results += [pscustomobject]@{
                Template = $tpl
                Match    = ($origHash -eq $newHash)
            }
        } finally {
            Remove-Item $tmpDb, $tmpVpk -Force -ErrorAction SilentlyContinue
        }
    }
    return @($results)
}
