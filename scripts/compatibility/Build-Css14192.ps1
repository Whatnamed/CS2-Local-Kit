#Requires -Version 7
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SourceRoot,
    [Parameter(Mandatory)][string]$BuildRoot,
    [Parameter(Mandatory)][string]$CMakeExe
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $SourceRoot).Path
$expected = '653d651f1ac09ac1ddb423d588f871b891038860'
if ((& git -C $source rev-parse HEAD) -ne $expected) { throw 'Requires exact CSS v1.0.376 source.' }
$sdk = Join-Path $source 'libraries/hl2sdk-cs2'
if ((& git -C $sdk rev-parse HEAD) -ne '625bfd4e39816ad0e1eae3b3144a3b5425c5c7a0') { throw 'SDK pin drift.' }
# Backport CSS #1468 / HL2SDK #438 onto the installed release, preserving every
# authorization call and callback. No managed API or unrelated SDK update.
$header = Join-Path $sdk 'public/eiface.h'
$raw = [IO.File]::ReadAllText($header)
$anchor = "`tvirtual void`tMakeSpawnGroupActive( SpawnGroupHandle_t spawnGroup ) = 0;"
$addition = "`tvirtual bool`tIsSpawnGroupLoadedForClient( CPlayerSlot nSlot, SpawnGroupHandle_t spawnGroup ) = 0;"
if (-not $raw.Contains($addition)) {
    if (($raw.Split($anchor).Count - 1) -ne 1) { throw 'SDK insertion point is ambiguous.' }
    $newline = if ($raw.Contains("`r`n")) { "`r`n" } else { "`n" }
    [IO.File]::WriteAllText($header, $raw.Replace($anchor, $anchor + $newline + $addition), [Text.UTF8Encoding]::new($false))
}
$env:SEMVER = '1.0.376-local.14192'
$env:GITHUB_SHA_SHORT = '653d651+hl2sdk438'
& $CMakeExe -S $source -B $BuildRoot -G 'Visual Studio 17 2022' -A x64 -DCMAKE_BUILD_TYPE=Release
if ($LASTEXITCODE -ne 0) { throw 'CSS CMake configure failed.' }
& $CMakeExe --build $BuildRoot --config Release --parallel 4
if ($LASTEXITCODE -ne 0) { throw 'CSS native build failed.' }
