# HumanPreset.Common.ps1 — canonical HumanPreset v1 validation + pinned catalog helpers.
# Dot-source from the cosmetics-lab scripts; never run directly.

$ErrorActionPreference = 'Stop'

$script:HumanPresetKind = 'cs2-local-kit/human-preset'
$script:HumanPresetSchemaVersion = 1
$script:CatalogCommit = '8a71e35c0489ac3093661af713525f2f0ebe1ad7'
$script:CatalogSourceUrl = "https://raw.githubusercontent.com/ByMykel/CSGO-API/$script:CatalogCommit/public/api/en"
$script:CatalogCacheRoot = Join-Path $script:Cs2ModRoot "app-data\cosmetics-lab\catalog\$script:CatalogCommit"

# Returns a list of human-readable problems; empty list = valid.
function Test-HumanPresetV1 {
    param([Parameter(Mandatory = $true)]$Preset)
    $errors = New-Object System.Collections.Generic.List[string]

    if ($Preset.kind -ne $script:HumanPresetKind) { $errors.Add("kind must be '$($script:HumanPresetKind)' (got '$($Preset.kind))") }
    if ($Preset.schemaVersion -ne $script:HumanPresetSchemaVersion) { $errors.Add("schemaVersion must be $($script:HumanPresetSchemaVersion) (got '$($Preset.schemaVersion))')") }

    foreach ($team in 'ct', 't') {
        $t = $Preset.PSObject.Properties[$team]
        if (-not $t -or $null -eq $t.Value) { $errors.Add("missing team object '$team'"); continue }
        $o = $t.Value

        foreach ($section in 'weapons', 'knife') {
            if (-not $o.PSObject.Properties[$section]) { $errors.Add("$team.$section missing"); }
        }
        if (-not $o.PSObject.Properties['gloves']) { $errors.Add("$team.gloves missing"); }

        if ($o.PSObject.Properties['weapons']) {
            foreach ($p in $o.weapons.PSObject.Properties) {
                if ($p.Name -notmatch '^\d+$') { $errors.Add("$team.weapons key '$($p.Name)' is not a numeric defindex") }
                $errors += (Test-HumanPresetItemEntry -Entry $p.Value -Path "$team.weapons[$($p.Name)]")
            }
        }

        if ($o.PSObject.Properties['knife'] -and $null -ne $o.knife) {
            $k = $o.knife
            if (-not $k.PSObject.Properties['selected'] -or $k.selected -notmatch '^\d+$') {
                $errors.Add("$team.knife.selected missing or not numeric")
            }
            if (-not $k.PSObject.Properties['presets']) {
                $errors.Add("$team.knife.presets missing")
            } else {
                $keys = @($k.presets.PSObject.Properties | ForEach-Object Name)
                foreach ($key in $keys) {
                    if ($key -notmatch '^\d+$') { $errors.Add("$team.knife.presets key '$key' is not a numeric defindex") }
                    $errors += (Test-HumanPresetItemEntry -Entry $k.presets.$key -Path "$team.knife.presets[$key]")
                }
                if ($k.PSObject.Properties['selected'] -and $k.selected -match '^\d+$' -and ($keys -notcontains $k.selected)) {
                    $errors.Add("$team.knife.selected ($($k.selected)) has no preset in $team.knife.presets")
                }
            }
        }

        if ($o.PSObject.Properties['gloves'] -and $null -ne $o.gloves) {
            $g = $o.gloves
            if (-not $g.PSObject.Properties['enabled'] -or $g.enabled -isnot [bool]) {
                $errors.Add("$team.gloves.enabled missing or not boolean")
            }
            if ($g.enabled) {
                foreach ($f in 'defindex', 'paint', 'seed', 'wear') {
                    if (-not $g.PSObject.Properties[$f] -or $null -eq $g.$f) { $errors.Add("$team.gloves.$f required when gloves enabled") }
                }
                if ($g.PSObject.Properties['wear'] -and ($g.wear -lt 0 -or $g.wear -gt 1)) { $errors.Add("$team.gloves.wear out of range 0..1") }
            }
        }

        if ($o.PSObject.Properties['loadoutIdentity'] -and $null -ne $o.loadoutIdentity) {
            foreach ($p in $o.loadoutIdentity.PSObject.Properties) {
                if ($p.Name -eq 'sharedWeaponLinks') {
                    foreach ($l in $p.Value.PSObject.Properties) {
                        if ($l.Name -notmatch '^\d+$' -or $l.Value -ne $true) {
                            $errors.Add("$team.loadoutIdentity.sharedWeaponLinks entry '$($l.Name)' must be a numeric defindex mapped to true")
                        }
                    }
                } else {
                    $errors.Add("$team.loadoutIdentity.$($p.Name) is not a known v1 loadoutIdentity field")
                }
            }
        }
    }

    if (-not $Preset.PSObject.Properties['musicKitId']) { $errors.Add('musicKitId missing (use null for none)') }
    elseif ($null -ne $Preset.musicKitId -and $Preset.musicKitId -notmatch '^\d+$') {
        $errors.Add("musicKitId must be a positive integer or null (got '$($Preset.musicKitId))')")
    }

    # Canonical presets never carry runtime or private identifiers.
    $raw = $Preset | ConvertTo-Json -Depth 20
    foreach ($forbidden in 'steamid', 'steamId64', 'uid', '"hash"') {
        if ($raw -match [regex]::Escape($forbidden)) { $errors.Add("forbidden field '$forbidden' found in canonical preset") }
    }
    return $errors
}

function Test-HumanPresetItemEntry {
    param([Parameter(Mandatory = $true)]$Entry, [Parameter(Mandatory = $true)][string]$Path)
    $errors = New-Object System.Collections.Generic.List[string]
    foreach ($f in 'paint', 'seed', 'wear') {
        if (-not $Entry.PSObject.Properties[$f] -or $null -eq $Entry.$f) { $errors.Add("$Path.$f missing") }
    }
    if ($Entry.PSObject.Properties['paint'] -and $Entry.paint -notmatch '^\d+$') { $errors.Add("$Path.paint must be a non-negative integer") }
    if ($Entry.PSObject.Properties['seed'] -and $Entry.seed -notmatch '^-?\d+$') { $errors.Add("$Path.seed must be an integer") }
    if ($Entry.PSObject.Properties['wear'] -and ($Entry.wear -lt 0 -or $Entry.wear -gt 1)) { $errors.Add("$Path.wear out of range 0..1") }
    if ($Entry.PSObject.Properties['nameTag'] -and $Entry.nameTag -isnot [string]) { $errors.Add("$Path.nameTag must be a string") }
    if ($Entry.PSObject.Properties['statTrak'] -and $null -ne $Entry.statTrak -and $Entry.statTrak -notmatch '^\d+$') {
        $errors.Add("$Path.statTrak must be null or a non-negative integer")
    }
    return $errors
}

# Ensure the pinned catalog snapshot exists locally (no floating latest, no per-run web
# dependency once cached). Returns paths.
function Get-HumanPresetCatalogCache {
    if (-not (Test-Path -LiteralPath (Join-Path $script:CatalogCacheRoot 'skins.json')) -or
        -not (Test-Path -LiteralPath (Join-Path $script:CatalogCacheRoot 'music_kits.json'))) {
        New-Item -ItemType Directory -Force -Path $script:CatalogCacheRoot | Out-Null
        foreach ($f in 'skins.json', 'music_kits.json') {
            $dest = Join-Path $script:CatalogCacheRoot $f
            Invoke-WebRequest -Uri "$($script:CatalogSourceUrl)/$f" -OutFile $dest
        }
    }
    return [pscustomobject]@{
        commit = $script:CatalogCommit
        skins = Join-Path $script:CatalogCacheRoot 'skins.json'
        musicKits = Join-Path $script:CatalogCacheRoot 'music_kits.json'
    }
}

# Load catalog lookup tables: weapon_id -> names, paint ids per weapon, music kit ids.
function Get-HumanPresetCatalogIndex {
    param([Parameter(Mandatory = $true)]$Cache)
    $skins = Read-C1Json -Path $Cache.skins
    $music = Read-C1Json -Path $Cache.musicKits
    $byWeapon = @{}
    foreach ($s in $skins) {
        if (-not $s.PSObject.Properties['weapon'] -or $null -eq $s.weapon) { continue }
        $wid = [int]$s.weapon.weapon_id
        if (-not $byWeapon.ContainsKey($wid)) { $byWeapon[$wid] = @{ name = $s.weapon.name; paints = @{} } }
        $pi = $s.paint_index
        if ($null -ne $pi -and -not $byWeapon[$wid].paints.ContainsKey([string]$pi)) {
            $byWeapon[$wid].paints[[string]$pi] = $s.name
        }
    }
    $musicById = @{}
    foreach ($m in $music) {
        $id = [string]$m.def_index
        # Prefer the non-StatTrak entry; some catalog entries have no market_hash_name at all.
        if (-not $musicById.ContainsKey($id) -and $m.market_hash_name -and -not $m.market_hash_name.StartsWith('StatTrak')) {
            $musicById[$id] = $m.name
        } elseif (-not $musicById.ContainsKey($id)) {
            $musicById[$id] = $m.name
        }
    }
    return [pscustomobject]@{
        byWeapon = $byWeapon
        musicById = $musicById
        commit = $Cache.commit
    }
}
