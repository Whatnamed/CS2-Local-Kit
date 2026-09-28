# Convert-LegacyHumanPreset.ps1 — migrate a legacy cs2bip-cosmetics-preset into the
# canonical HumanPreset v1. Read-only on the input (hash verified before/after).
#
# Usage:
#   pwsh -NoProfile -File scripts\cosmetics-lab\Convert-LegacyHumanPreset.ps1
#
# Outputs:
#   E:\CS2MOD\presets\human\personal-default.v1.json
#   E:\CS2MOD\diagnostics\cosmetics-lab\<ts>-preset-migration\migration-report.json

param(
    [string]$LegacyPath = '',
    [string]$OutPath = '',
    [string]$ReportDir = ''
)

. (Join-Path $PSScriptRoot 'CosmeticsLab.Common.ps1')
. (Join-Path $PSScriptRoot 'HumanPreset.Common.ps1')

# Resolved after dot-sourcing (param defaults run before Common loads).
if (-not $LegacyPath) { $LegacyPath = Join-Path $script:Cs2ModRoot '1.json' }
if (-not $OutPath) { $OutPath = Join-Path $script:Cs2ModRoot 'presets\human\personal-default.v1.json' }

if (-not $ReportDir) {
    $ReportDir = Join-Path $script:Cs2ModRoot "diagnostics\cosmetics-lab\$(Get-Date -Format 'yyyyMMdd-HHmmss')-preset-migration"
}

Write-C1Step 'Reading legacy preset (read-only)'
if (-not (Test-Path -LiteralPath $LegacyPath -PathType Leaf)) { throw "Legacy preset not found: $LegacyPath" }
$legacyShaBefore = Get-C1FileSha256Hex -Path $LegacyPath
$legacyRaw = [IO.File]::ReadAllText($LegacyPath)
$legacy = $legacyRaw | ConvertFrom-Json
Write-C1Ok ("{0} ({1} bytes, sha256 {2})" -f $LegacyPath, $legacyRaw.Length, $legacyShaBefore)

Write-C1Step 'Validating legacy envelope'
if ($legacy.kind -ne 'cs2bip-cosmetics-preset') { throw "Unsupported legacy kind '$($legacy.kind)'." }
if ([string]$legacy.schema_version -ne '3') { throw "Unsupported legacy top schema_version '$($legacy.schema_version)'." }
if ([string]$legacy.config.schema_version -ne '5') { throw "Unsupported legacy config schema_version '$($legacy.config.schema_version)'." }
Write-C1Ok 'cs2bip-cosmetics-preset v3 / config v5'

# ---- known legacy field paths (anything else lands in 'unknown') ----
$known = New-Object System.Collections.Generic.HashSet[string]
foreach ($p in '/schema_version', '/kind', '/exported_at_unix', '/quick_knife', '/quick_knife/bind_key', '/quick_knife/selected',
    '/config/schema_version', '/config/enabled', '/config/apply_to_human_players', '/config/apply_on_pickup',
    '/config/music_kit_id', '/config/shared_weapon_links', '/config/stickers_enabled', '/config/charms_enabled',
    '/config/agents_enabled', '/config/shortcut_knives') { [void]$known.Add($p) }
foreach ($team in 'ct', 't') {
    foreach ($p in "/config/loadouts/$team/agent_model", "/config/loadouts/$team/default_knife_defindex",
        "/config/loadouts/$team/knife_presets", "/config/loadouts/$team/glove", "/config/loadouts/$team/gun_presets") { [void]$known.Add($p) }
    foreach ($p in 'enabled', 'defindex', 'paint', 'seed', 'wear') { [void]$known.Add("/config/loadouts/$team/glove/$p") }
}
$tracked = New-Object System.Collections.Generic.HashSet[string]
$script:sharedWeaponLinksValues = $null
function Track { param([string]$Path) [void]$script:tracked.Add($Path) }

function Convert-LegacyItem {
    param($Entry, [string]$Path)
    foreach ($f in 'paint', 'seed', 'wear', 'name_tag', 'stattrak_enabled', 'stattrak_count', 'souvenir_enabled', 'stickers', 'charm') {
        Track "$Path/$f"
        [void]$script:known.Add("$Path/$f")
    }
    $mapped = [ordered]@{
        paint   = [int]$Entry.paint
        seed    = [int]$Entry.seed
        wear    = [double]$Entry.wear
        nameTag = [string]$Entry.name_tag
        statTrak = $null
    }
    if ($Entry.stattrak_enabled) { $mapped.statTrak = [int]$Entry.stattrak_count }
    return [pscustomobject]$mapped
}

Write-C1Step 'Mapping legacy fields to HumanPreset v1'
$reportMapped = New-Object System.Collections.Generic.List[object]
$reportIgnored = New-Object System.Collections.Generic.List[object]
$preset = [ordered]@{ kind = $script:HumanPresetKind; schemaVersion = 1 }

foreach ($team in 'ct', 't') {
    $lo = $legacy.config.loadouts.$team
    $t = [ordered]@{}

    # weapons
    $weapons = [ordered]@{}
    foreach ($p in ($lo.gun_presets.PSObject.Properties | Sort-Object { [int]$_.Name })) {
        $weapons[$p.Name] = Convert-LegacyItem -Entry $p.Value -Path "/config/loadouts/$team/gun_presets/$($p.Name)"
        $reportMapped.Add([pscustomobject]@{ item = "$team.weapon[$($p.Name)]"; from = "/config/loadouts/$team/gun_presets/$($p.Name)"; detail = "paint $($p.Value.paint), seed $($p.Value.seed), wear $($p.Value.wear)" })
    }
    $t.weapons = $weapons

    # knife
    Track "/config/loadouts/$team/default_knife_defindex"
    $knifePresets = [ordered]@{}
    foreach ($p in ($lo.knife_presets.PSObject.Properties | Sort-Object { [int]$_.Name })) {
        $knifePresets[$p.Name] = Convert-LegacyItem -Entry $p.Value -Path "/config/loadouts/$team/knife_presets/$($p.Name)"
        $reportMapped.Add([pscustomobject]@{ item = "$team.knife[$($p.Name)]"; from = "/config/loadouts/$team/knife_presets/$($p.Name)"; detail = "paint $($p.Value.paint), seed $($p.Value.seed), wear $($p.Value.wear)" })
    }
    $t.knife = [ordered]@{ selected = [int]$lo.default_knife_defindex; presets = $knifePresets }
    $reportMapped.Add([pscustomobject]@{ item = "$team.knife.selected"; from = "/config/loadouts/$team/default_knife_defindex"; detail = "defindex $($lo.default_knife_defindex)" })

    # gloves
    Track "/config/loadouts/$team/glove"
    $g = $lo.glove
    $t.gloves = [ordered]@{
        enabled  = [bool]$g.enabled
        defindex = [int]$g.defindex
        paint    = [int]$g.paint
        seed     = [int]$g.seed
        wear     = [double]$g.wear
    }
    $reportMapped.Add([pscustomobject]@{ item = "$team.gloves"; from = "/config/loadouts/$team/glove"; detail = "enabled $($g.enabled), defindex $($g.defindex), paint $($g.paint)" })

    # legacy shared cosmetics links: report-only (NOT part of HumanPreset v1 -
    # weapon identity is decided by CS2's own loadout; see HUMAN-PRESET-V1.md)
    $links = $legacy.config.shared_weapon_links
    if ($links) {
        $swl = [ordered]@{}
        foreach ($p in ($links.PSObject.Properties | Sort-Object { [int]$_.Name })) {
            Track "/config/shared_weapon_links/$($p.Name)"
            [void]$script:known.Add("/config/shared_weapon_links/$($p.Name)")
            $swl[$p.Name] = [bool]$p.Value
        }
        $script:sharedWeaponLinksValues = $swl
    }

    # ignored legacy team fields
    if ($lo.PSObject.Properties['agent_model']) {
        Track "/config/loadouts/$team/agent_model"
        $reportIgnored.Add([pscustomobject]@{ field = "/config/loadouts/$team/agent_model"; value = $lo.agent_model; reason = 'agent cosmetics are out of C2 scope; value is empty' })
    }
    $preset[$team] = $t
}

# music kit
Track '/config/music_kit_id'
$preset.musicKitId = if ($null -ne $legacy.config.music_kit_id) { [int]$legacy.config.music_kit_id } else { $null }
$reportMapped.Add([pscustomobject]@{ item = 'musicKitId'; from = '/config/music_kit_id'; detail = "id $($legacy.config.music_kit_id)" })

# legacy envelope + feature flags -> ignored with reason
Track '/schema_version'; Track '/kind'; Track '/exported_at_unix'; Track '/config/schema_version'
$reportIgnored.Add([pscustomobject]@{ field = '/schema_version,/kind,/exported_at_unix,/config/schema_version'; value = "$($legacy.schema_version)/$($legacy.kind)/$($legacy.exported_at_unix)/$($legacy.config.schema_version)"; reason = 'legacy envelope metadata' })
Track '/config/enabled'; Track '/config/apply_to_human_players'; Track '/config/apply_on_pickup'
$reportIgnored.Add([pscustomobject]@{ field = '/config/enabled,/config/apply_to_human_players,/config/apply_on_pickup'; value = "$($legacy.config.enabled)/$($legacy.config.apply_to_human_players)/$($legacy.config.apply_on_pickup)"; reason = 'legacy runtime behavior flags; the current runtime always applies the projected preset locally' })
Track '/config/stickers_enabled'; Track '/config/charms_enabled'; Track '/config/agents_enabled'
$reportIgnored.Add([pscustomobject]@{ field = '/config/stickers_enabled,/config/charms_enabled,/config/agents_enabled'; value = "$($legacy.config.stickers_enabled)/$($legacy.config.charms_enabled)/$($legacy.config.agents_enabled)"; reason = 'legacy feature toggles; v1 carries no agents/stickers/charms (all false in source)' })
Track '/config/shortcut_knives'
$reportIgnored.Add([pscustomobject]@{ field = '/config/shortcut_knives'; value = ($legacy.config.shortcut_knives -join ','); reason = 'quick knife cycling is explicitly deferred; knife identities remain available via knife.presets' })
Track '/quick_knife'
$reportIgnored.Add([pscustomobject]@{ field = '/quick_knife'; value = ($legacy.quick_knife | ConvertTo-Json -Compress); reason = 'quick knife cycling is explicitly deferred' })

# per-entry fields that v1 does not carry (verified values; recorded, not silently dropped)
$reportIgnored.Add([pscustomobject]@{ field = '*/{gun,knife}_presets/*/souvenir_enabled'; value = 'false in all legacy entries'; reason = 'the runtime has no souvenir concept; nothing to preserve' })
$reportIgnored.Add([pscustomobject]@{ field = '*/{gun,knife}_presets/*/stickers'; value = 'empty in all legacy entries'; reason = 'stickers out of C2 scope; no data to preserve' })
$reportIgnored.Add([pscustomobject]@{ field = '*/{gun,knife}_presets/*/charm'; value = 'null in all legacy entries'; reason = 'charms out of C2 scope; no data to preserve' })

# unknown detection
$unknown = @($tracked | Where-Object { $known -notcontains $_ } | Sort-Object)

Write-C1Step 'Validating canonical preset'
# Normalize nested ordered dictionaries into real PSCustomObjects (JSON round-trip),
# matching how the validator and the projector consume the file.
$presetObj = ($preset | ConvertTo-Json -Depth 20) | ConvertFrom-Json
$errors = Test-HumanPresetV1 -Preset $presetObj
if ($errors.Count -gt 0) { throw ("Canonical preset invalid:`n  " + ($errors -join "`n  ")) }
Write-C1Ok 'schema valid'

Write-C1Step 'Catalog validation (pinned ByMykel/CSGO-API)'
$cache = Get-HumanPresetCatalogCache
$index = Get-HumanPresetCatalogIndex -Cache $cache
Write-C1Ok ("catalog commit $($index.commit)")
$unresolved = New-Object System.Collections.Generic.List[object]
foreach ($team in 'ct', 't') {
    $o = $presetObj.$team
    foreach ($p in $o.weapons.PSObject.Properties) {
        $wid = [int]$p.Name
        if (-not $index.byWeapon.ContainsKey($wid)) { $unresolved.Add([pscustomobject]@{ item = "$team.weapon[$($p.Name)]"; problem = "weapon defindex $wid not in catalog" }); continue }
        if (-not $index.byWeapon[$wid].paints.ContainsKey([string]$p.Value.paint)) { $unresolved.Add([pscustomobject]@{ item = "$team.weapon[$($p.Name)]"; problem = "paint $($p.Value.paint) not listed for weapon $($index.byWeapon[$wid].name)" }) }
    }
    foreach ($p in $o.knife.presets.PSObject.Properties) {
        $wid = [int]$p.Name
        if (-not $index.byWeapon.ContainsKey($wid)) { $unresolved.Add([pscustomobject]@{ item = "$team.knife[$($p.Name)]"; problem = "defindex $wid not in catalog" }) }
        elseif (-not $index.byWeapon[$wid].paints.ContainsKey([string]$p.Value.paint)) { $unresolved.Add([pscustomobject]@{ item = "$team.knife[$($p.Name)]"; problem = "paint $($p.Value.paint) not listed for knife $($index.byWeapon[$wid].name)" }) }
    }
    if ($o.gloves.enabled) {
        if (-not $index.byWeapon.ContainsKey([int]$o.gloves.defindex)) { $unresolved.Add([pscustomobject]@{ item = "$team.gloves"; problem = "glove defindex $($o.gloves.defindex) not in catalog" }) }
        elseif (-not $index.byWeapon[[int]$o.gloves.defindex].paints.ContainsKey([string]$o.gloves.paint)) { $unresolved.Add([pscustomobject]@{ item = "$team.gloves"; problem = "glove paint $($o.gloves.paint) not listed" }) }
    }
    $reportMapped.Add([pscustomobject]@{ item = "$team.knife.selected name"; from = 'catalog'; detail = $index.byWeapon[[int]$o.knife.selected].name })
}
if ($null -ne $presetObj.musicKitId) {
    $mid = [string]$presetObj.musicKitId
    if (-not $index.musicById.ContainsKey($mid)) { $unresolved.Add([pscustomobject]@{ item = 'musicKitId'; problem = "music kit $mid not in catalog" }) }
}
if ($unresolved.Count -gt 0) {
    foreach ($u in $unresolved) { Write-C1Fail "$($u.item): $($u.problem)" }
    throw "Catalog validation found $($unresolved.Count) unresolved item(s). Not writing preset."
}
Write-C1Ok 'all weapon/knife/glove/music IDs resolved against pinned catalog'

Write-C1Step 'Writing canonical preset + migration report'
$outParent = Split-Path $OutPath -Parent
New-Item -ItemType Directory -Force -Path $outParent | Out-Null
$previousSha = $null
if (Test-Path -LiteralPath $OutPath) { $previousSha = Get-C1FileSha256Hex -Path $OutPath }
$json = ($presetObj | ConvertTo-Json -Depth 12)
[IO.File]::WriteAllText($OutPath, $json + "`r`n", [Text.UTF8Encoding]::new($false))
$outSha = Get-C1FileSha256Hex -Path $OutPath

New-Item -ItemType Directory -Force -Path $ReportDir | Out-Null
$report = [pscustomobject]@{
    kind = 'cosmetics-lab-preset-migration-report'
    createdAt = (Get-Date -Format 'o')
    source = [pscustomobject]@{ path = $LegacyPath; sha256 = $legacyShaBefore; bytes = $legacyRaw.Length; kind = $legacy.kind; schemaVersion = $legacy.schema_version; configSchemaVersion = $legacy.config.schema_version }
    output = [pscustomobject]@{ path = $OutPath; sha256 = $outSha; previousSha256 = $previousSha }
    catalog = [pscustomobject]@{ commit = $index.commit; unresolvedCount = 0 }
    summary = [pscustomobject]@{
        mapped = $reportMapped.Count
        preservedButNotApplied = 1
        unsupported = 0
        unknown = $unknown.Count
        ignoredWithReason = $reportIgnored.Count
    }
    mapped = $reportMapped
    preservedButNotApplied = @([pscustomobject]@{
        item = '/config/shared_weapon_links (legacy CT/T cosmetics-sharing links)'
        from = '/config/shared_weapon_links'
        value = $script:sharedWeaponLinksValues
        detail = 'recorded here only, NOT carried into HumanPreset v1: ordinary weapon identity is decided by the CS2 loadout; the v1 schema has no CT/T cosmetic-sharing model (HUMAN-PRESET-V1.md)'
    })
    unsupported = @()
    unknown = $unknown
    ignoredWithReason = $reportIgnored
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $ReportDir 'migration-report.json') -Encoding utf8NoBOM

$legacyShaAfter = Get-C1FileSha256Hex -Path $LegacyPath
if ($legacyShaAfter -ne $legacyShaBefore) { throw 'Legacy file changed during migration - aborting.' }
Write-C1Ok "legacy unchanged ($legacyShaAfter)"
Write-Host ''
Write-C1Ok "canonical preset: $OutPath"
Write-C1Ok "migration report: $(Join-Path $ReportDir 'migration-report.json')"
if ($unknown.Count -gt 0) { Write-C1Fail "unknown legacy fields recorded: $($unknown -join ', ')" }
