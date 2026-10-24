# NadeSystem 1.41.9.2 recovery candidate

This is an independent candidate over the installed Medium + Human Bot parent
and its installed five-file compatibility layer. It is not a Bot baseline upgrade.
No UI, Human fixture, runtime lock, Steam file, launch option, AI strategy, match
setup adapter, aim DLL or compatibility-layer file is part of its payload.

## Source and bounded change

- Upstream: `ed0ard/CS2-Bot-NadeSystem@009ce03005af68ba68dae62967bddc8e92ad172c`, AGPL-3.0.
- [PR #9](https://github.com/ed0ard/CS2-Bot-NadeSystem/pull/9), checked 2026-10-23:
  open, unmerged; exact head `d089e2129a3cc5d9c2d344117ea793ece3b522fa`.
  Only its Windows SmokeCreate pattern is backported; seven parameters and Linux
  pattern are unchanged. HE/Molotov native factories and Flash entity/spawn route
  are retained. Unique static matches support a candidate, not runtime ABI proof.
- Data destination and the `localkit:nades:v1` capability contract reuse
  `scripts/bot-experiment/build_match_candidate.py` from match-setup `e6b7f4a`.
  This layer copies all 48 exact original JSON files to the actual
  `ModuleDirectory/grenades` read path; it retains the old `data/NadeSystem` copies.
- Diagnostics add no timers, tick scans or gameplay policy. They aggregate
  rejection counters in existing branches, log database/map counts, mode changes,
  round-end/status summaries, and the first three created entities per type/epoch.
  Already queued creation is cancelled when its mode/map epoch changes, including
  switching Off. No radio announce or command echo increments success.
- `ByType.<flash|he|molotov|smoke|decoy>.Attempts` counts requests reaching the
  projectile creation method, after decision/budget gates. `Created` increments
  only after a valid native/entity factory result and required spawn/owner setup;
  Flash is counted after DispatchSpawn and Teleport. `Failed`, `Failures`, and
  `Skips` expose null/invalid factory results, exceptions, zone, facing, live enemy,
  blindable enemy, freeze period, cooldown, takeover, money/budget and mode gates.
  Some non-Max information/probability/schedule rejections are grouped. These are
  plugin counters, not counts of Valve AI throws or proof of explosion/effect.
- Sessions change per map; counters reset on mode changes, with an ending summary
  logged first. Capability reads and `bot_nades` readback cannot increment Created.

Original and patched complete sources, AGPL license, source hashes and unified
patch ship with the candidate. `Build-Nades14192.py` restores only the existing
local NuGet cache and builds net10.0 with the original API 1.0.373 reference.
The generated deps.json must be byte-identical to the installed one; no API DLL
is bundled into the plugin. Use an existing .NET 10 SDK explicitly if PATH only
contains a .NET runtime. Rebuild example, from the task worktree:

```powershell
python -B scripts/compatibility/Build-Nades14192.py --archive '<original-source.zip>' --output 'temp/new-build' --dotnet '<dotnet-sdk.exe>'
```

Alternatively, the corresponding-source ZIP is directly buildable: extract
`sources/NadeSystem-patched-source.zip` to an empty directory, use .NET 10 to
`dotnet restore NadeSystem.csproj --source '<existing-nuget-cache>' -p:NuGetAudit=false`,
then `dotnet build NadeSystem.csproj -c Release --no-restore -p:SourceRevisionId=009ce03005af68ba68dae62967bddc8e92ad172c`.
The project retains its original package reference and complete source. A copy
of the patch/build script is also supplied for provenance; its repository mode
expects this project's `bot_experiment.py` and evidence templates.

Automated checks (never point the synthetic harness at a real game):

```powershell
dotnet run --project tests/NadeEvidenceChecks -c Release
python -B scripts/compatibility/Test-Nades14192.py --candidate '<candidate-dir>' --manifest-sha256 '<exact-digest>'
```

Public entry checks use all production guards. The fault-only phase operates on
a positively identified private fake tree, mocks CS2 discovery, caches immutable
fake parent inventories, and replaces repeated ancestor syscalls with a strict
fake-home path bound. It still performs real copies, hashes, atomic replacements,
deletes and byte-for-byte restoration checks. After each case the full game tree,
both parent trees and private preset sentinel are checked from disk. No mocked
guard or fault-injection switch is exposed by the production PowerShell entry.

## Transaction and exact recovery

The candidate owns exactly one DLL replacement and 48 new flat JSON files, plus
its own marker `game/csgo/.cs2-local-kit-nades-14192.json`. Both installed parent
IDs, markers, complete transaction/backup inventories, 631 existing game files,
module directory identities, current build and game binaries are pinned. Unknown
files, existing destination JSON, changed parents or game images fail closed.
An interrupted/active history must be restored by its exact new ID before retry.

All public actions preview unless `-Apply` is explicit. Installation first checks
the complete bundle, CS2 closed and Steam idle, verifies a DLL preimage and stages
all 49 payload files, records the transaction, exclusively creates its marker,
and then replaces files. An ordinary write failure rolls back its own layer;
hard interruption retains a recoverable exact record. Restore verifies current
images and the DLL backup, replaces the old DLL and removes only the 48 owned
files. The existing empty directory, old data, five-file compatibility fix, Bot
parent, Human and personal data remain. Unknown drift stops recovery.

The new layer checks Steam's app/build/install path and completed idle transfer
state plus pinned binaries/files, rather than requiring byte identity of the
whole mutable Steam manifest. Ordinary LastPlayed changes must not invalidate
its own recovery. A build change, incomplete update or game binary drift blocks.
This does not change either old transaction or bypass their original guards.
The old five-file layer's existing Restore currently refuses its own whole-Steam-
manifest drift; this is recorded separately and is not repaired by this candidate.

Use the delivered fixed `Invoke-ExactNades14192.ps1` for Preview and, only after
authorization, `-Action Install -Apply`. Preserve its returned NEW install ID.
For this layer's exact read-only recovery preview:

```powershell
pwsh -NoProfile -File '<fixed-entry>' -Action Restore -InstallId '<new-nade-install-id>'
# Only after separate authorization, append -Apply.
```

Never substitute either parent ID. Restore Nades before attempting an older
layer's Restore; the original older-layer file/module guards reject this overlay.
The package does not auto-install or auto-restore any layer, and never runs CS2.

## Mirage real-game acceptance (pending)

Only after installation is separately authorized: the user starts the existing
offline `-insecure` entry on `de_mirage`. Keep the same teams/Bot count and Medium
profile for both runs. Preserve Human settings. Do not use a match-setup candidate
or presume that saved Controller preferences are already applied in the game.

1. Run `css_localkit_nades_status`. Require `Kind=localkit-nades-v2`, correct map,
   DatabaseCount=3703, MapCount=344, and map types Flash=97, HE=75, Molotov=84,
   Smoke=86, decoy=2. Capture Session/Epoch. A new map reloads data and changes
   session; no cached earlier session counts as current evidence.
2. Run `bot_nades off`, then `css_localkit_nades_status`. Observe ordinary rounds
   and the four configured zones below. Record status again: this plugin's
   Attempts/Created must remain zero and no `localkit-nades-created` event may
   appear for this Off epoch. Off does not disable Valve's own grenade AI, so
   visible base-game throws alone cannot fail/pass the plugin Off comparison.
3. Run `bot_nades max`; capture its NEW epoch and play fresh rounds. Max still
   requires live opposing players, valid native Bots outside player takeover,
   unfrozen round, trigger zone/height, money and freeze-end budget. Flash needs
   a blindable enemy within 1300 units and LoS/view cone. HE/Molotov need a live
   enemy within 300 units of landing and no firing within one second. Smoke needs
   facing dot >= 0, and respects overlap/cooldown. Max is not unconditional spam.
4. For each of the four exact IDs in `mirage-points.json`, run
   `css_localkit_nades_points <full-id>`; this is read-only and reports the real
   source point, velocity, landing, zone radius and current Bot positions/money.
   It also reports DistanceXY, HeightDelta, FacingDot and takeover/native-Bot state.
   Put an uncontrolled Bot at the reported release XY (100-unit radius, Smoke
   150); the feet target is release Z minus 64 and abs HeightDelta must be <=85.
   In a local cheats-only test, native `bot_place` can help place a Bot from your
   view; verify the resulting transform with the read-only command, rather than
   assuming the placement was exact. Do not use bot takeover: this plugin skips it.
   Have a live opposing player near the recorded landing point. For Flash, face
   that landing with clear LoS; for HE/Molotov, stay within 300 and let the Bot
   stop firing. For Smoke, make the Bot face the recorded velocity hemisphere.
   Use fresh rounds with enough starting money so the freeze-end budget is funded.
   Other nearby lineups may win first; confirm the event's exact Id/Type, inspect
   Skips, and retry in a fresh round. Do not fabricate a missing successful throw.
5. Require each type's actual `Attempts > 0` and `Created > 0`, valid entity/type
   in `localkit-nades-created`, and visually confirm flash detonation/blindness,
   HE explosion, Molotov fire and Smoke cloud. Record exact event Id, Bot, Entity,
   Session/Epoch and a video/screenshot/log per type. A nonzero Created counter
   without the matching in-game effect is a failure/pending effect, not PASS.
6. Reapply Off and repeat comparable zones/rounds with a new epoch. Save
   `css_localkit_nades_status` and `logs/log-NadeSystem*.txt` / `log-all*.txt` from
   both modes. Compare evidence within each epoch, not across resets. Check Human,
   TAB/score and normal round flow. Close CS2. No actual Restore is part of this
   task; exact rollback remains a separately authorized action.

The packaged Mirage points are existing input data, not synthetic test throws.
Source/build/static/isolated transaction success is not real-game acceptance.
