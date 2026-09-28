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
    },
    "loadoutIdentity": {                        // optional, preserved-but-not-applied
      "sharedWeaponLinks": { "<weaponDefindex>": true }
    }
  },

  "t": { "weapons": {...}, "knife": {...}, "gloves": {...}, "loadoutIdentity": {...} },

  "musicKitId": <int | null>
}
```

Notes:

- `knife.presets` may hold several knife identities; only `knife.selected` is
  projected. There is deliberately no rotation/cycling data model in v1 — quick
  knife cycling is a later, separate feature.
- `loadoutIdentity` is a lossless preservation area for legacy loadout-identity
  data (e.g. legacy `sharedWeaponLinks`). The v1 projector does NOT execute it;
  per-team weapon entries already carry their own cosmetics.
- Agents / stickers / charms / graffiti are out of scope for v1; legacy values
  found during migration are recorded in the migration report, not the schema.

## Validation

`scripts/cosmetics-lab/HumanPreset.Common.ps1` contains the validator
(`Test-HumanPresetV1`). Catalog-level ID checks (defindex / paint kit / music kit
exist in the pinned ByMykel/CSGO-API snapshot) run in the migration and projector
scripts against the pinned cache under
`E:\CS2MOD\app-data\cosmetics-lab\catalog\<commit>\`.

A non-private example lives next to this file: `human-preset.v1.example.json`.
Real personal presets live only under `E:\CS2MOD\presets\human\` and are never
committed.
