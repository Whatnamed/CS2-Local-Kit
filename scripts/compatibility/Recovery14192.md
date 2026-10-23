# CS2 1.41.9.2 / 26144732 Medium + Human recovery candidate

This build-specific package preserves the existing active Medium + Human
installation. It writes only CSS native core, CSS PostThink gamedata,
BotVision DLL/smoke-density gamedata and BotAimImprover DLL. The original Bot payload and corresponding
sources are under `baseline-bot/`; Human files, personal fixtures and presets
are retained by exact hash and are not redistributed here.

CSS is rebuilt from v1.0.376 with the one-method SDK backport from CSS #1468 /
HL2SDK #438. Authorization callbacks remain enabled. CSS #1467 supplies the
PostThink signature; BotVision e951169 supplies the smoke-density signature.
BotVision's frozen s2sdk also receives the one-method insertion, because its
game-time adapter calls GetServerGlobals. BotAimImprover's Windows PBot offset
changes from 0x14F8 to 0x1500, verified in the current server's schema records.
Managed API, MetaMod1481, other Bot DLLs, Medium VPK and Human DLL stay unchanged.
The two corresponding-source ZIPs contain complete patched source, recursive
dependencies, refs, licenses and build instructions.

Only use `tools/Invoke-Recovery14192.ps1`, with the exact release directory and
manifest SHA256 from the adjacent `.artifact.json`. `Preview` is read-only.
`Install -Apply` requires separate user authorization. It requires CS2 closed,
Steam idle, the exact build, original active record/backups, module inventory
and every protected baseline file. It creates an independent recovery record
under `E:\CS2MOD\backups\compat-14192\<new-install-id>` before writes.

Save the returned new install ID. `Restore -InstallId <new-install-id>` previews;
add `-Apply` only when authorized. This restores the five pre-candidate files
and removes this layer's marker. It leaves the original Bot transaction active
and byte-identical. This returns the previous joint installation, including its
known CSS crash on this build; it does not downgrade CS2. Never edit the old
record or bypass its guards. Its separate old Restore remains blocked by the
CS2 update and is not required for this recovery.

Known non-blocking limits: one BotAI bombsite CALL patch is skipped by its
existing expected-byte guard; NadeSystem's custom smoke factory signature is
unresolved and its CSS wrapper rejects a null function handle. Recorded/special
smoke replay is not claimed repaired. Other Bot AI, Human rendering and current
native private/vtable layouts still require a real game. No new offsets are
invented and no weak/failing patch is force-applied.

Shortest manual gate after an authorized install: start through the existing
offline launch entry, reach the menu and enter one local map; play several
Medium Bot rounds, check Bot combat/movement/nades, TAB/score, CT/T Human gun,
knife/glove/music kit, respawn and a side change. No new crash or sustained
severe stutter. Exit CS2 and collect current CSS/Bot logs. Agents do not start
CS2 or alter Steam files/launch arguments. Static/build/transaction results
are not gameplay acceptance.
