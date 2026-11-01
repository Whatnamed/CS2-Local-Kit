# Selective Bot performance backports

These are patches to external pinned components, not a full Bot Improver upgrade.
The component licenses and complete corresponding patched source travel with each candidate.

| Component | Pinned base | Selected upstream commit | Effect |
| --- | --- | --- | --- |
| BotState | `9e9f8f1b2fd2` / 1.10.2 | [8038f8a](https://github.com/ed0ard/CS2-Smarter-Bot/commit/8038f8a6d2ab0d354d5787841dc6fa25e92166b7) | Rebuild flash/controller discovery after entity lifecycle changes; retain native enumeration order and read live positions/visibility each tick. |
| NadeSystem | `009ce03005af` / 1.2.2 + existing LocalKit patch | [ca3b638](https://github.com/ed0ard/CS2-Bot-NadeSystem/commit/ca3b63875ae1b20bc59089cc1d2adbb61fa0416f) | Replace repeated cooldown/zone record searches with first-record indexes; retain type-specific duplicate-ID semantics. |
| BotRandomizer | `1c7c3ebad4b9` / 1.3.3 | [ded53e4](https://github.com/ed0ard/CS2-Bot-Randomizer/commit/ded53e4278af6def771fbf23ff46998428ea9b33) | Reuse rarity/finish pools per immutable catalog list, preserving weights, order and random draws. |
| BotController | `582e9132c10b` / 0.8.0 | [0c0209a](https://github.com/XBribo/CS2-Bot-Controller/commit/0c0209a5f08266571b3ea2a025b996a64272cd56) | Return before slot lookup/locking when no lock/replay is active; resolve profile requests from current controller/pawn handles. |

The Nade patch context includes the existing `LogEvidence("database-loaded")` call;
the upstream logic is unchanged. The native build also retains the previously
reviewed `IVEngineServer2::IsSpawnGroupLoadedForClient` SDK insertion. Managed API
package versions, exported BotController API, Nades Off–Max/creation evidence,
SmokeCreate repair, Aim Head/AWP policy and all 48 grenade data files are retained.

Bot Improver [v1.4.7](https://github.com/ed0ard/CS2-Bot-Improver/releases/tag/v1.4.7)
resolves to `91de1b49d429afad622776f8e0271ce61c56761c`; later main pins differ.
No claim is made that the four patched bases are those complete newer releases.
BotHider's affected-slot optimization depends on its newer identity/incarnation
implementation; its broader identity changes are excluded to preserve the current
TAB/lineup boundary. BotVision changes include smoke behavior and broader refactors,
so the existing repaired binary is retained. Randomizer's separate native item-view
destructor fix is not part of this pool-cache backport. BotAI's already skipped
bombsite patch remains guarded; no new AI behavior is enabled by this task.

Build with `scripts/bot-experiment/Build-BotPerformance14194.py`, supplying the
sealed 14193 Medium source manifest, an unused worktree-local output directory,
the existing .NET 10 SDK and VS 2022 CMake. All source archives and patch hashes,
build commands and DLL identities are recorded in `build-result.json` and the
candidate's `performancePatches`; corresponding source includes this build script.
Performance and native Hook behavior require real-game measurement.
