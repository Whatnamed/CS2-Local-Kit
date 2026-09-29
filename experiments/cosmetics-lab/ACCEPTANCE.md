# CosmeticsLab acceptance history

Runtime under test: InventorySimulator (see `runtime/inventory-simulator.lock.json`).
Real-game results, in order. Evidence dirs live under `E:\CS2MOD\diagnostics\cosmetics-lab\`.

## C1 — exact upstream InventorySimulator 3.3.0 — **FAIL (runtime/startup)**

2026-09-28. MetaMod 2.0-git1469 + CounterStrikeSharp 1.0.376 + unpatched upstream
`fade4449`. cs2.exe crashed during startup (two launches, identical WER signature:
faulting module `coreclr.dll` 10.0.326.7603, exception `0x80131623`, offset
`0x2d21ee`, no dump retained). CSS log recorded a managed `NativeException: Global
Variables not initialized yet` from `Utilities.GetPlayers()` reached via
`Load() -> OnFileChanged()` on every launch. Evidence: `20260928-221018-posttest`
(incl. `windows-crash-evidence`).

Isolation A (plugin dir removed, everything else identical) → CS2 started and ran
normally → failure domain converged to InventorySimulator.

## C1.1 — single-line startup lifecycle patch — **PASS**

2026-09-28. Upstream `fade4449` + `inventory-simulator/c1_1-startup-lifecycle-patch`
(`Load()` calls `Inventories.Load(file)` instead of `OnFileChanged(null, file)`;
`OnFileChanged` itself unchanged). Hypothesis confirmed: the early player enumeration
was the startup-crash domain.

Startup gate PASS (menu reachable, plugin loaded without the NativeException, local
game enterable, no immediate crash), then the full static cosmetic gate PASS on
CS2 1.41.8.5: FPS/frame pacing normal; CT/T gun cosmetics correct; CT Karambit +
T Butterfly identity/first-person model/animations/finish correct; gloves correct
across respawns and side change; Music Kit displayed as expected (display correct,
injection causality not separately isolated at that time); no crash, give loop,
HUD shake, sound loop or extra entities. Evidence: `20260928-234052-posttest`
(CSS log 23:06:46 `Finished loading plugin InventorySimulator`, zero errors in the
session window).

The patched build is the accepted runtime baseline (`accepted-as-patched` in the
lock); unpatched upstream 3.3.0 stays at startup-fail and is never marked accepted.

## C2 — personal preset pipeline — **PASS**

2026-09-29. Chain under test: legacy personal preset (`E:\CS2MOD\1.json`)
→ HumanPreset v1 (canonical) → deterministic EquippedV5 projection → C1.1 runtime.

The user played a complete local match with their migrated personal cosmetics and
observed **no** crash, FPS regression, respawn/side-switch problem or cosmetic
projection problem — every migrated item (CT/T gun skins, CT Karambit Fade with
name tag, T Butterfly Autotronic, Sport Gloves Slingshot both sides, Music Kit 78)
matched the legacy `1.json` configuration. Music Kit switching from 28 (C1.1 test
fixture) to 78 (personal preset) also confirmed injection causality. Evidence:
`20260929-235446-preset-migration` + fixture hashes in
`E:\CS2MOD\backups\cosmetics-lab\20260929-000235-c2-fixture` /
`20260929-004745-preset-apply` (the installed fixture content is unchanged by the
later C2 cleanup: projection sha256 `01a8a7494b4565b0bcccff4bdd1ff6a9fc26108f9fd162edc6921c26db34700c`).

## Post-C2 cleanup (this round, no runtime change)

`loadoutIdentity/sharedWeaponLinks` removed from HumanPreset v1 (legacy
cosmetic-sharing state, not weapon identity — weapon identity is always decided by
CS2's own loadout; legacy links now live in the migration report only). Knife
catalog validation now checks paint-kit membership per knife defindex in both
migration and projector. SteamID no longer derives from a fixed historical backup:
it comes from `E:\CS2MOD\app-data\cosmetics-lab\player-state.json` (private) or an
explicit `-SteamId64`. Projection (pure) and installation
(`Apply-HumanCosmeticsPreset.ps1`: cs2-closed check, validate, project to staging,
backup, atomic replace, hash verify, auto-rollback, rollback metadata) are
separate entrypoints; the C4 Controller reuses this apply entrypoint rather than duplicating it.

## C3 — Human Cosmetics Controller Foundation（2026-09-29，无 runtime 变化）

产品层建立：`src/CS2LocalKit.Core`（HumanPreset v1 domain、serialization、validator、
pinned catalog service、preset store、deterministic projection、apply/rollback、
runtime status）+ `src/CS2LocalKit.Controller`（薄 CLI）。关键结果：

- .NET projector 与 C2 accepted PowerShell projector **byte-identical**（example golden
  在测试套件中锁定；真实 personal preset + verified SteamID 本机复算 = 安装 fixture
  sha256 `01a8a749…` 完全一致）；
- apply/rollback fake-tree 验证通过（备份、原子替换、hash 校验、失败自动回滚、restore
  latest）；
- runtime/compatibility status 只读建模（build match、patched dll hash match、fixture
  hash、active preset、latest apply/rollback 可用性）；
- canonical docs 同步修正普通枪械 identity 定义：identity 永远由 CS2 自己的 loadout
  决定，USP-S/P2000、M4A1-S/M4A4 identity override 为明确非目标（PRODUCT-SCOPE §3.1/3.2、
  ARCHITECTURE §3/§6、MANUAL-ACCEPTANCE §4、COMPATIBILITY-WATCH §6）；
- 真实 CS2 安装的 runtime/fixture 在本阶段未被修改（.NET golden 与已安装 fixture 一致）。

## C4 — Controller UI（2026-09-29 起，进行中）

`src/CS2LocalKit.App`（WPF Controller）建立在 C3 的 Core 入口之上：饰品浏览（武器 / 刀型 /
手套 / 音乐盒）、预设管理、runtime status 与 Save / Apply / Restore。它只调用 Core 的
projection 与 apply 事务，不在 UI 层复制其语义。

已由用户在本机真实游戏完成的验收（2026-09-29）：

- Controller Save → Apply，然后进入 CS2（1.41.8.6 / buildId 25588766）实测；
- 枪械、刀、手套、音乐盒外观均正确；
- 重生、换边未观察到问题；无 crash、HUD / 音频循环或性能异常；
- 因此 HumanPreset → projection → InventorySimulator → CS2 主链在本轮视为通过；runtime pin
  的 tested build 已据此更新，证据级别见 `runtime/inventory-simulator.lock.json` 的
  `acceptedRuntime.retestOnNewerBuild`（用户口头验收 + 本机 build 事实，不是自动化证明）。

仍未收口的部分：C4.3 的 presentation / image 缺陷 —— 预览图与当前选择不同步、批量预览只
加载一部分、滚动条与卡片密度观感。这些都在 UI 层，不改变上面的 runtime 结论；在用户复测
C4.3 新 release 之前，不宣称整个 C4 最终 PASS。

## Still deferred (explicitly)

Quick knife cycling (`\`), knife rotation, agents/stickers/charms, tray-first lifecycle.
(M4A1-S/M4A4 and USP-S/P2000 identity override is an explicit non-goal, not a
deferred task - see PRODUCT-SCOPE §3.1/3.2.) C3+ work must not reopen C1/C1.1 or
modify the accepted runtime patch.
