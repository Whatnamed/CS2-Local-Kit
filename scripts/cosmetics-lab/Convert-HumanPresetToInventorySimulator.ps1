# Convert-HumanPresetToInventorySimulator.ps1 — deterministic projection from the canonical
# HumanPreset v1 to the InventorySimulator EquippedV5 text. PURE projection: it writes the
# projected JSON to -OutPath and does nothing else (no game-directory writes, no install).
# Installing is Apply-HumanCosmeticsPreset.ps1's job.
#
# The projector is a pure mapping: CT -> team 3, T -> team 2, weapons by defindex, the
# selected knife identity per team, gloves only when enabled, music kit id. loadoutIdentity
# does not exist in v1 (weapon identity is decided by CS2's own loadout). uid/hash are
# derived deterministically from preset coordinates (no random, no clock).
#
# Usage:
#   pwsh -NoProfile -File scripts\cosmetics-lab\Convert-HumanPresetToInventorySimulator.ps1 [-OutPath <file>]
#
# SteamID source: private player state (E:\CS2MOD\app-data\cosmetics-lab\player-state.json)
# or explicit -SteamId64. Never from the preset, never from Git.

param(
    [string]$PresetPath = '',
    [string]$OutPath = '',
    [string]$SteamId64 = ''
)

. (Join-Path $PSScriptRoot 'CosmeticsLab.Common.ps1')
. (Join-Path $PSScriptRoot 'HumanPreset.Common.ps1')

if (-not $PresetPath) { $PresetPath = Join-Path $script:Cs2ModRoot 'presets\human\personal-default.v1.json' }
if (-not $OutPath) { $OutPath = Join-Path $script:Cs2ModRoot 'app-data\cosmetics-lab\inventory-simulator\inventories.json' }
if (-not $SteamId64) {
    $playerStatePath = Join-Path $script:Cs2ModRoot 'app-data\cosmetics-lab\player-state.json'
    if (-not (Test-Path -LiteralPath $playerStatePath -PathType Leaf)) {
        throw "No player state found at $playerStatePath - pass -SteamId64 explicitly (the apply workflow does this)."
    }
    $playerState = Read-C1Json -Path $playerStatePath
    $SteamId64 = [string]$playerState.steamId64
}
if ($SteamId64 -notmatch '^\d{17}$') { throw "SteamID64 '$SteamId64' is not a 17-digit id." }

Write-C1Step 'Validating canonical preset'
$preset = Read-C1Json -Path $PresetPath
$errors = Test-HumanPresetV1 -Preset $preset
if ($errors.Count -gt 0) { throw ("Preset invalid:`n  " + ($errors -join "`n  ")) }
$cache = Get-HumanPresetCatalogCache
$index = Get-HumanPresetCatalogIndex -Cache $cache
$unresolved = New-Object System.Collections.Generic.List[string]
foreach ($team in 'ct', 't') {
    $o = $preset.$team
    foreach ($p in $o.weapons.PSObject.Properties) {
        $wid = [int]$p.Name
        if (-not $index.byWeapon.ContainsKey($wid)) { $unresolved.Add("$team.weapon[$($p.Name)]: unknown defindex"); continue }
        if (-not $index.byWeapon[$wid].paints.ContainsKey([string]$p.Value.paint)) { $unresolved.Add("$team.weapon[$($p.Name)]: paint $($p.Value.paint) unknown for $($index.byWeapon[$wid].name)") }
    }
    foreach ($p in $o.knife.presets.PSObject.Properties) {
        $wid = [int]$p.Name
        if (-not $index.byWeapon.ContainsKey($wid)) { $unresolved.Add("$team.knife[$($p.Name)]: unknown defindex") }
        elseif (-not $index.byWeapon[$wid].paints.ContainsKey([string]$p.Value.paint)) { $unresolved.Add("$team.knife[$($p.Name)]: paint $($p.Value.paint) unknown for knife $($index.byWeapon[$wid].name)") }
    }
    if ($o.gloves.enabled) {
        if (-not $index.byWeapon.ContainsKey([int]$o.gloves.defindex)) { $unresolved.Add("$team.gloves: unknown defindex $($o.gloves.defindex)") }
        elseif (-not $index.byWeapon[[int]$o.gloves.defindex].paints.ContainsKey([string]$o.gloves.paint)) { $unresolved.Add("$team.gloves: paint $($o.gloves.paint) unknown") }
    }
}
if ($null -ne $preset.musicKitId -and -not $index.musicById.ContainsKey([string]$preset.musicKitId)) {
    $unresolved.Add("musicKitId $($preset.musicKitId): not in catalog")
}
if ($unresolved.Count -gt 0) {
    throw ("Unresolved catalog items - refusing to project (do not guess replacements):`n  " + ($unresolved -join "`n  "))
}
Write-C1Ok "schema + catalog valid (commit $($index.commit))"

function Get-C1StringSha256Prefix {
    param([string]$Text, [int]$Length = 16)
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Text)
    return ([BitConverter]::ToString([Security.Cryptography.SHA256]::HashData($bytes))).Replace('-', '').ToLowerInvariant().Substring(0, $Length)
}

$script:nextUid = 0
function New-RuntimeItem {
    # NOTE: no [hashtable] type on $Fields - that would coerce the OrderedDictionary
    # back to a plain hashtable and make key (and therefore JSON) order non-deterministic.
    param([string]$Coordinate, $Fields)
    $script:nextUid++
    $item = [ordered]@{}
    foreach ($k in $Fields.Keys) { $item[$k] = $Fields[$k] }
    $item.uid = $script:nextUid
    $item.hash = "hp1-" + (Get-C1StringSha256Prefix -Text $Coordinate)
    return $item
}

function Convert-PresetItem {
    param([string]$Coordinate, [int]$DefIndex, $Entry)
    $fields = [ordered]@{
        def      = $DefIndex
        paint    = [int]$Entry.paint
        seed     = [int]$Entry.seed
        wear     = [double]$Entry.wear
        nametag  = [string]$Entry.nameTag
        stattrak = $(if ($null -eq $Entry.statTrak) { -1 } else { [int]$Entry.statTrak })
    }
    return New-RuntimeItem -Coordinate $Coordinate -Fields $fields
}

Write-C1Step 'Projecting (CT->3, T->2)'
$ctWeapons = [ordered]@{}; $tWeapons = [ordered]@{}; $knives = [ordered]@{}; $gloves = [ordered]@{}
foreach ($team in 'ct', 't') {
    $teamByte = $(if ($team -eq 'ct') { 3 } else { 2 })
    $o = $preset.$team
    foreach ($p in ($o.weapons.PSObject.Properties | Sort-Object { [int]$_.Name })) {
        $item = Convert-PresetItem -Coordinate "$team/weapon/$($p.Name)" -DefIndex ([int]$p.Name) -Entry $p.Value
        if ($team -eq 'ct') { $ctWeapons[$p.Name] = $item } else { $tWeapons[$p.Name] = $item }
    }
    $selected = [string]$o.knife.selected
    $item = Convert-PresetItem -Coordinate "$team/knife/$selected" -DefIndex ([int]$selected) -Entry $o.knife.presets.$selected
    $knives[[string]$teamByte] = $item
    if ($o.gloves.enabled) {
        $g = $o.gloves
        $gloves[[string]$teamByte] = New-RuntimeItem -Coordinate "$team/gloves" -Fields ([ordered]@{
            def = [int]$g.defindex; paint = [int]$g.paint; seed = [int]$g.seed; wear = [double]$g.wear
        })
    }
}
$musicKit = $null
if ($null -ne $preset.musicKitId) {
    $musicKit = New-RuntimeItem -Coordinate "music/$($preset.musicKitId)" -Fields ([ordered]@{
        musicId = [int]$preset.musicKitId; stattrak = -1
    })
}

$fixture = [ordered]@{}
$fixture[$SteamId64] = [ordered]@{
    ctWeapons = $ctWeapons
    tWeapons  = $tWeapons
    knives    = $knives
    gloves    = $gloves
    musicKit  = $musicKit
}
$json = ($fixture | ConvertTo-Json -Depth 12) + "`r`n"

Write-C1Step 'Writing projection'
$outParent = Split-Path $OutPath -Parent
if (-not (Test-Path -LiteralPath $outParent)) { New-Item -ItemType Directory -Force -Path $outParent | Out-Null }
[IO.File]::WriteAllText($OutPath, $json, [Text.UTF8Encoding]::new($false))
$outSha = Get-C1FileSha256Hex -Path $OutPath
Write-C1Ok "projected EquippedV5 written: $OutPath (sha256 $outSha)"
Write-Host ("   steamId64 {0}; items: ctWeapons {1}, tWeapons {2}, knives {3}, gloves {4}, musicKit {5}" -f $SteamId64, $ctWeapons.Count, $tWeapons.Count, $knives.Count, $gloves.Count, ($(if ($musicKit) { 1 } else { 0 })))
