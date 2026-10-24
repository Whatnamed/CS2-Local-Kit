# Bounded 1.41.9.2 recovery patches

These are candidate-only patches for the existing Medium + Human combination,
not new accepted runtime locks. Exact artifacts, build/scan results, recovery
commands and remaining manual checks are in the dated compatibility archive.

| Patch | Base and source |
| --- | --- |
| `css-hl2sdk.patch` | CSS `653d651f1ac09ac1ddb423d588f871b891038860`, SDK `625bfd4e39816ad0e1eae3b3144a3b5425c5c7a0`; backport [CSS #1468](https://github.com/roflmuffin/CounterStrikeSharp/pull/1468) / [HL2SDK #438](https://github.com/alliedmodders/hl2sdk/pull/438). |
| `css-postthink.patch` | Installed CSS 1.0.376 gamedata; Windows signature from [CSS #1467](https://github.com/roflmuffin/CounterStrikeSharp/pull/1467). |
| `botvision-s2sdk.patch` | BotVision `9884848b340434c906ecd1b0f7a0d1d5e8ca140c`, s2sdk `b85301c79b6320f3cbf7d519dd334795c227426b`; same SDK method insertion as [s2sdk #116](https://github.com/alliedmodders/s2sdk/pull/116). |
| `botvision-density.patch` | Frozen BotVision gamedata; only the Windows density pattern from [e951169](https://github.com/XBribo/CS2-Bot-Vision/commit/e95116913a025a35d99573c9de59dd71b9cd299d). |
| `botaim-pbot.patch` | BotAimImprover `d796572b6d258b938a9d8db3c5d4c802a46049f7`; one Windows offset changed from 0x14F8 to 0x1500, confirmed by current server.dll schema registration. Other offsets and Linux remain unchanged. |

CSS source remains GPL-3.0; BotVision and BotAimImprover remain AGPL-3.0 under
their original terms. Only these small patch files are stored here; external
source trees stay outside the production source tree. The local candidate
includes licenses and complete corresponding sources plus recursive dependency
refs and build instructions for every rebuilt DLL.

Build entry points: `scripts/compatibility/Build-Css14192.ps1`,
`Build-BotAdapters14192.py`, then `Build-Recovery14192.py`. The latter requires
an exact sealed review and installed parent identity. Recovery uses the narrow
`Invoke-Recovery14192.ps1` entry, preserves the parent transaction and never
starts CS2. No Controller/UI code or accepted runtime lock changes are needed.
