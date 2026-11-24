#Requires -Version 7
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SourceRoot,
    [Parameter(Mandatory)][string]$BuildRoot,
    [Parameter(Mandatory)][string]$CMakeExe
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $SourceRoot).Path
$sdk = Join-Path $source 'libraries/hl2sdk-cs2'
if ((git -C $source rev-parse HEAD) -ne '2273dd1631a5719f2cacc6e9abbf67ac0bc30bc0' -or
    (git -C $sdk rev-parse HEAD) -ne 'a508eb5ed5fb36f4067fa36a1c33f5fec7d73066') { throw 'CSS v1.0.380 or SDK source drift.' }
$patch = Join-Path $PSScriptRoot 's2sdk-128.patch'
git -C $sdk apply --unidiff-zero --check $patch
if ($LASTEXITCODE -ne 0) { throw 'SDK patch does not apply to the pinned source.' }
git -C $sdk apply --unidiff-zero $patch
if ($LASTEXITCODE -ne 0) { throw 'SDK patch failed.' }
$env:SEMVER = '1.0.380-local.14201'
$env:GITHUB_SHA_SHORT = '2273dd1+s2sdk128'
& $CMakeExe -S $source -B $BuildRoot -G 'Visual Studio 17 2022' -A x64 -DCMAKE_BUILD_TYPE=Release
if ($LASTEXITCODE -ne 0) { throw 'CSS CMake configure failed.' }
& $CMakeExe --build $BuildRoot --config Release --parallel 5
if ($LASTEXITCODE -ne 0) { throw 'CSS native build failed.' }
