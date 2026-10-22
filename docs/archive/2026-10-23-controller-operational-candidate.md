# Controller operational candidate — 2026-10-23

This is dated candidate evidence, not real-game acceptance. Product IA remains defined by `docs/CONTROLLER-UX.md` at `096dbc6`.

## Scope and evidence

| Stage | Result | Limit |
| --- | --- | --- |
| P0 | Steam manifest idle on build `26087496`; `steam.inf` reports `1.41.9.1` / `2000934`. Existing Medium preserve-Human transaction has 633 recorded entries with no observed postimage drift. Pure Human MetaMod differs as expected while that joint transaction is active. | Read-only snapshot on this date; repeat before any live operation. |
| P1–P2 | Controller inspects active transaction against its own manifest/record and stages exact old Restore → fresh target preflight → target Install with a durable preparation journal. Read-only live previews identified all new Low/Medium/High bundles as requiring a switch, including Medium → new Medium. | No live Restore/Install was executed. |
| P3 | Match request is map-specific, expires after two hours and is claimed once per session; adapter reads back lineup and Aim/Nades internal state. Unsupported ordinary-mode auto lineup is disabled. | CS2 listen-server launch path, actual lineup, behavior and performance remain unverified in game. |
| P4 | Synthetic active Bot → Human fixture update → exact Restore/transfer and drift/interruption cases pass. Original Bot record and preimages stay immutable. | Human model, animation and skin display remain unverified in game. |
| P5 | Two primary pages plus secondary settings; hidden, non-activating WPF probe passed 63 checks and produced 28 nonblank captures across width/DPI and task states. | Native popup, focus, physical keyboard/mouse and real game still need manual observation. |
| P6 | `pending-real-game`. | Requires explicit authorization for live Restore/Install and user-run CS2. No `Accepted` claim. |

Checks from the task worktree: `dotnet test tests/CS2LocalKit.Core.Tests/CS2LocalKit.Core.Tests.csproj -c Release` (261/261); `python scripts/bot-experiment/Test-BotExperiment.py` (40/40); each of the three new candidate bundles passed `Test-BotExperimentCandidate.py` through the public PowerShell entry with 108 actual payload files, zero preview writes and byte-for-byte synthetic baseline recovery. The MatchSetup adapter built with zero warnings. Controller self-contained `win-x64` publish hashed 408 payload files and passed its private-data boundary check. These checks do not prove an actual CS2 match.

The new immutable candidates are pinned in `src/CS2LocalKit.App/BotCandidates.json`; historical bundles remain available for exact recovery. The release manifest carries its source Git SHA. No Steam launch option, live CS2 file, game process or runtime pin was changed by this task.

## P6 manual gate

1. With CS2 closed, recheck Steam build and idle state, active transaction identity, exact Restore preview and target candidate hash. The agent must receive explicit authorization before live Restore/Install. Keep the old backup and install record.
2. Prepare new Medium through Controller, then verify the new install record, target manifest/profile, file hashes, MetaMod/CSS and Human fixture. A successful preview or synthetic install is insufficient.
3. The user starts an offline `-insecure` local listen-server match on the selected map using a start path confirmed on this machine. No persistent Steam launch-option edit is part of this candidate. Until that path is verified, Controller offers application to the next match rather than a start button.
4. First match: FaZe Copenhagen 2024 versus Spirit Shanghai 2024, user replaces one seat, 5v5, Medium / Mixed / Normal. Check actual roster, names, TAB, score, first round, round end, respawn and side switch against the request/session readback.
5. Check swapped CT/T, historical and mixed rosters, duplicate persona/name, custom Unicode name and smaller counts. On different maps check Aim Head/Body and Nades Off/Max with actual gameplay evidence, including nonzero nade routes for Max. Record any mismatch rather than treating an internal mode string as proof.
6. With CS2 closed between writes, apply CT/T gun, knife, glove and music changes while the new Bot transaction is active. Verify presentation after user-run game restart/respawn and side switch. Sample FPS at fixed map/count intervals.
7. With user approval and CS2 closed, exact Restore the **new** install ID and verify normal mode plus current Human fixture. Any decision to leave enhanced mode installed needs a separate authorized Install and post-check. Only after these observations can P6 be accepted, runtime pins updated or main considered for merge.
