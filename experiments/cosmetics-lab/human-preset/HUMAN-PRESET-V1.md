# HumanPreset v1 — CS2-Local-Kit canonical human cosmetics preset

This is the project-owned user preset format. InventorySimulator's EquippedV5 is a
**runtime projection target**, never the canonical user format.

## Design rules

- `schemaVersion = 1`; one file holds both CT and T.
- Weapons are stored by **real weapon defindex** (string keys of decimal numbers);
  cosmetic projection is separated from loadout identity.
- All persistent IDs are numeric (defindex / paint kit / music kit). Localized
  names are never keys.
- The canonical preset contains **no SteamID** and **no runtime implementation
  details** (no `uid`, no `hash`, no InventorySimulator team bytes 2/3).
- Unknown or not-yet-supported features must not be silently invented; migration
  records them in the migration report instead.
- **Weapon identity is always decided by CS2's own loadout.** When the game spawns
  a USP-S, the USP-S defindex preset applies; when it spawns a P2000, the P2000
  preset applies (M4A1-S / M4A4 likewise). Human cosmetics never rewrite ordinary
  weapon defindexes to match a preset, and v1 carries no CT/T cosmetic-sharing
  model: legacy `shared_weapon_links` from old presets is recorded in the
  migration report only, never in the canonical schema.

## Schema

```jsonc
{
  "kind": "cs2-local-kit/human-preset",
  "schemaVersion": 1,

  "ct": {
    "weapons": {
      "<weaponDefindex>": {
        "paint": <int paintKitId>,
        "seed": <int>,
        "wear": <float 0..1>,
        "nameTag": "<string, empty = none>",
        "statTrak": <int count | null = disabled>
      }
    },
    "knife": {
      "selected": <int knifeDefindex>,          // must be a key of "presets"
      "presets": { "<knifeDefindex>": { same shape as weapon entry } }
    },
    "gloves": {
      "enabled": <bool>,                        // false -> projector emits no glove
      "defindex": <int gloveDefindex>,
      "paint": <int paintKitId>,
      "seed": <int>,
      "wear": <float 0..1>
    }
  },

  "t": { "weapons": {...}, "knife": {...}, "gloves": {...} },

  "musicKitId": <int | null>
}
```

Notes:

- `knife.presets` may hold several knife identities; only `knife.selected` is
  projected. There is deliberately no rotation/cycling data model in v1 — quick
  knife cycling is a later, separate feature. A static knife preset that can be
  expressed and projected is all v1 supports.
- Agents / stickers / charms / graffiti are out of scope for v1; legacy values
  found during migration are recorded in the migration report, not the schema.
- `loadoutIdentity` (tried in an earlier v1 draft) was removed: legacy
  `shared_weapon_links` is legacy cosmetic-sharing state, not weapon identity,
  and belongs in migration reports only.

## Validation

`scripts/cosmetics-lab/HumanPreset.Common.ps1` contains the validator
(`Test-HumanPresetV1`), which rejects unknown fields including
`loadoutIdentity`. Catalog-level ID checks (defindex exists in the pinned
ByMykel/CSGO-API snapshot AND the paint kit belongs to that defindex, for
weapons, knives and gloves alike; music kit id exists) run in the migration and
projector scripts against the pinned cache under
`E:\CS2MOD\app-data\cosmetics-lab\catalog\<commit>\`.

A non-private example lives next to this file: `human-preset.v1.example.json`.
Real personal presets live only under `E:\CS2MOD\presets\human\` and are never
committed.

## Application pipeline

`Convert-HumanPresetToInventorySimulator.ps1` is a pure deterministic projection
(preset + SteamID -> EquippedV5 text). `Apply-HumanCosmeticsPreset.ps1` is the
single entrypoint that validates, projects, backs up the currently installed
fixture, atomically replaces the file InventorySimulator actually reads, hash
verifies and writes rollback metadata. The Controller UI reuses the apply
entrypoint; nothing else writes into the game directory. The projector's
SteamID comes from the private player state file
(`E:\CS2MOD\app-data\cosmetics-lab\player-state.json`) or an explicit
`-SteamId64` — never from the preset, never from Git.
