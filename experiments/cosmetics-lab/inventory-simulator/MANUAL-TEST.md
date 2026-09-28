# CosmeticsLab C1 — manual test sequence (user-run)

Install already placed:

- MetaMod 2.0 build 1469 (`addons\metamod`)
- CounterStrikeSharp v1.0.376 with runtime (`addons\counterstrikesharp`)
- InventorySimulator v3.3.0 exact upstream release (`addons\counterstrikesharp\plugins\InventorySimulator`)
- Private local inventory fixture (`configs\plugins\InventorySimulator\inventories.json`)
- Experiment config `cfg\cosmeticslab_c1.cfg` (local-only, dummy API endpoint)

No Bot-Improver, no Local-Arena, no other CSS plugins are installed.

## Launch

Launch CS2 through Steam with launch options:

```text
-insecure -novid +exec cosmeticslab_c1
```

`+exec cosmeticslab_c1` is REQUIRED: it applies the experiment convars (dummy `invsim_url`,
public API writes disabled) before you connect. Play only local/offline games. Do not connect
to any public/server browser match during this test.

Console should show: `[CosmeticsLab-C1] experiment convars applied`.

## Baseline stability

- Verify FPS / frame pacing feels normal BEFORE inspecting cosmetics.
- Play/move a few minutes, several spawns. No enhanced Bot runtime should be loaded
  (bot names keep the vanilla Chinese prefix, no Bot-Improver behavior).

## CT checks (verify each separately)

- Test gun: P250 gets the **Asiimov** finish and remains a normal P250.
- Knife is actually a **Karambit**, correct first-person model, correct animation set,
  **Fade** finish visible.
- Gloves: **Sport Gloves | Pandora's Box**.
- No HUD shake, no repeated give loop, no sound loop, no extra knife entities.

Die/respawn at least three times and re-check.

## T checks (switch sides normally)

- P250 uses the **See Ya Later** finish (not the CT state).
- Knife is actually a **Butterfly**, correct model/animations, **Tiger Tooth** finish.
- Gloves: **Driver Gloves | Crimson Weave** — different from CT, and do not disappear
  after the team change.

Die/respawn at least three times and re-check.

## Music

Music Kit 28 (New Beat Fund — Sponge Fingerz): observe main menu/round start/MVP cues as
applicable.

## Stability soak

Several more rounds. Note anything unusual: crash, FPS regression, model/animation mismatch,
missing skin material, glove loss on team change or respawn, unexpected network behavior,
CSS/native errors in console.

Do NOT test live knife cycling (`\`), M4A1-S/M4A4 or USP-S/P2000 switching in C1 — out of scope.

## After the test

Tell the agent the result (per-category if anything is off). Run diagnostics collection
(`Collect-CosmeticsLabDiagnostics.ps1`) or let the agent do it. Evidence lands under
`E:\CS2MOD\diagnostics\cosmetics-lab\<timestamp>-posttest\`.

## Full restore (if needed)

```powershell
pwsh -NoProfile -File scripts\cosmetics-lab\Restore-CosmeticsLab.ps1 -BackupDir <backup-dir-printed-by-install> -Apply
```
