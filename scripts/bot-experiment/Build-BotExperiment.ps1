#Requires -Version 7
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EvidenceWorktree,
    [Parameter(Mandatory)][string]$MetaModArchive,
    [Parameter(Mandatory)][string]$Cs2Root,
    [Parameter(Mandatory)][string]$DotNetExe,
    [string]$ReleasesRoot = 'E:\CS2MOD\releases\bot-experiment',
    [ValidateSet('Low','Medium','High')][string]$Difficulty = 'Medium',
    [string]$PythonExe = 'python'
)
$ErrorActionPreference = 'Stop'
& $PythonExe (Join-Path $PSScriptRoot 'build_candidate.py') --evidence-worktree $EvidenceWorktree --metamod-archive $MetaModArchive --cs2-root $Cs2Root --dotnet $DotNetExe --releases-root $ReleasesRoot --difficulty $Difficulty
if ($LASTEXITCODE -ne 0) { throw 'Candidate assembly stopped. No live installation was performed; preserve task-local evidence.' }
