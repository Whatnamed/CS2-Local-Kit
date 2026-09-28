# CosmeticsLab — InventorySimulator C1 experiment

**Question C1 answers:** on the current Windows CS2 build, can the unmodified upstream
`ianlucas/cs2-css-inventory-simulator` v3.3.0 reliably project a local human player's team-scoped
weapon cosmetics, knife identity/cosmetics, gloves and Music Kit 28, without any Bot runtime and
without any legacy Local-Arena cosmetics code?

This is a feasibility/compatibility experiment, not the production HumanCosmetics implementation.
Runtime pin: `runtime/inventory-simulator.lock.json` (status `candidate` until the real-game gate passes).

## Layout

```text
runtime/inventory-simulator.lock.json        exact upstream pin (InventorySimulator + MetaMod + CSS)
experiments/cosmetics-lab/
  inventory-simulator/test-preset.json       non-private item selections + catalog provenance
  inventory-simulator/MANUAL-TEST.md         user-facing in-game test sequence
scripts/cosmetics-lab/
  CosmeticsLab.Common.ps1                    shared helpers (CS2 root detection, hashing, JSON)
  Prepare-CosmeticsLab.ps1                   build release payload/manifest under E:\CS2MOD\releases
  Install-CosmeticsLab.ps1                   backup + install into the real CS2 (or a fake tree)
  Restore-CosmeticsLab.ps1                   exact restore from a backup dir
  Invoke-CosmeticsLabRoundTripTest.ps1       fake-tree install/restore byte-identical round trip
  Collect-CosmeticsLabDiagnostics.ps1        read-only post-test evidence collection
```

## Private data boundary

- The user's SteamID64 and the actual `inventories.json` live only under
  `E:\CS2MOD\app-data\cosmetics-lab\inventory-simulator\` (plus the installed copy inside CS2).
- They are never committed to Git and never bundled into the generic release ZIP.
- `test-preset.json` contains only item data with catalog provenance.

## Network isolation

InventorySimulator reads the local file
`game\csgo\addons\counterstrikesharp\configs\plugins\InventorySimulator\inventories.json`
at plugin load; when the exact SteamID64 is present, its web fetch is skipped entirely
(verified against source `CCSPlayerControllerExtensions.FetchInventory` @ fade4449:
`if (!force && controllerState.Inventory != null) return;` before any HTTP call).

Defensive configuration (C1-owned `cfg\cosmeticslab_c1.cfg`, executed via launch option
`+exec cosmeticslab_c1`):

- `invsim_url "http://127.0.0.1:9"` — non-routable dummy endpoint, so a missing local entry
  cannot silently fall back to the public service (failed attempts are visible in CSS logs);
- `invsim_public_api_stattrak_increment 0`, `invsim_public_api_spray_consume 0` — upstream
  defaults for both are `true`, so these are explicitly disabled;
- `!ws`, web login, sprays disabled; `invsim_require_inventory 0`, `invsim_fallback_team 0`,
  `invsim_minmodels 1`.

No web refresh flow is used in C1.

## Workflow

```powershell
# 1. Build release payload + manifest (downloads nothing if assets already verified in temp)
pwsh -NoProfile -File scripts\cosmetics-lab\Prepare-CosmeticsLab.ps1

# 2. Generate the PRIVATE per-user fixture (SteamID64 stays in E:\CS2MOD, never in Git)
pwsh -NoProfile -File scripts\cosmetics-lab\Prepare-CosmeticsLab.ps1 -GenerateFixture -SteamId64 <steamid64>

# 3. Automated validation incl. fake-tree install/restore round trip
pwsh -NoProfile -File scripts\cosmetics-lab\Invoke-CosmeticsLabRoundTripTest.ps1

# 4. Real install (refuses while cs2.exe runs; creates backup + install record)
pwsh -NoProfile -File scripts\cosmetics-lab\Install-CosmeticsLab.ps1 -ExpectedSteamId64 <steamid64>

# 5. User performs MANUAL-TEST.md. Do not launch CS2 from automation.

# 6. Post-test evidence
pwsh -NoProfile -File scripts\cosmetics-lab\Collect-CosmeticsLabDiagnostics.ps1
```

## Acceptance / stop conditions

C1 is PASS only if the criteria in `docs/MANUAL-ACCEPTANCE.md`-style in-game checks hold
(see MANUAL-TEST.md): stable FPS, CT/T gun cosmetics, CT Karambit + T Butterfly identity with
correct model/animations/finish, gloves across spawns and side change, Music Kit 28, no give
loop / HUD shake / sound loop, no Bot/score/match side effects, and no external HTTP traffic.

A failure is categorized by subsystem (gun / knife / glove / music / framework / catalog) rather
than collapsed into "skins broken". Do not patch upstream in C1; do not proceed to the adapter
proof (C2) before C1 passes in real CS2.
