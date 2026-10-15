# 2026-10-15 离线兼容性、CT 开场手套与 Bot 性能实验调查

基线：fetch 后 `origin/main=66dc587dbefaa34cff9ab16fbf1b7596a267c01c`，主工作区 clean。
调查 worktree：`E:\Projects\CS2-Local-Kit\wt-investigate-20261015`，branch `codex/investigate-20261015`。
本文是具名 build 的历史证据，不改变 canonical 定义、runtime locks 或验收状态。
没有安装、修复、启动 CS2、生成正式包，也没有修改游戏、预设、Human/Bot runtime。
网络仅用于 GitHub 源码、Issue、release 与远端基线审查；构建恢复仅使用已有 NuGet cache。

## 结论

- 当前实装 **1.41.9.1 / 2000932 / BuildId 25947352**。MetaMod startup entry 为 **0**，本次保留原状。
- 六项 accepted framework/Human 文件及 10 月 9 日安装的 Human gamedata hashes 全部匹配。
  已安装 Human **14/14 signatures 唯一匹配**；这不证明 offsets、加载或渲染正确。
- Bot 10 月 9 日审查集合在新 build 上 **106/107 唯一匹配**，一个 BotAI patch pattern 变为两处。
  当前 pin 的 native JSON + installed CSS 另行扫描，范围见下表，不能和 107 条集合混为同一版本。
- 最新 BotAimImprover 已修正旧 build 的 `m_pBot`，但本机新 binary 的静态 schema registration
  为 **0x14F8**，与 umbrella 的 **0x1510**、组件最新的 **0x1518** 都不一致。
  **目前不具备准备可运行全套 Windows 性能候选的兼容性依据**；仅保留离线构建和实验设计。
- CT 开场动画手套问题属于用户报告，本次未复现。优先调查第三人称 bodygroup/材质刷新与 spawn 时序；
  没有足够证据实施 runtime 修复或升级。

## 本机与 10 月 9 日比较

机器事实见 [local-facts.json](2026-10-15-investigation/local-facts.json)。
Steam manifest `LastUpdated=2026-10-15 10:34:20 +08:00`，`LastPlayed=2026-10-14 18:49:55 +08:00`。
后者早于本次更新，不能把历史游玩反馈算作新 build 验收，也不能认定 CT 症状由此次更新引入。
本次开始时 `cs2.exe` 未运行。

| 项目 | 10 月 9 日最终证据 | 本次实测 |
|---|---|---|
| Patch / Client / BuildId | 1.41.9.0 / 2000930 / 25815307 | 1.41.9.1 / 2000932 / 25947352 |
| server.dll SHA256 | `9f31fc84164f7822e015dcd95ea958f9dd33d02308a646a2bee5c9b39bd6fc28` | `09e8c04c5f44040155d04fa79a3f43491d542a386f45807ed77785729e5925a9` |
| engine2.dll SHA256 | `4b8fe29ecdef4f492a602d9013cbfe5226561c9c0ba4361ce82e6f46d4f9bc04` | `6456df18d8f954394024c2c07da8c848df15a5ace74ce7390d78970ceb39ee21` |
| MetaMod SearchPaths entry | 修复后 1 | 0，未修复 |
| Human gamedata | 两条 Windows substitutions，SHA `f3d9facb…` | 完整 hash 相同，14/14 unique |
| MetaMod / CSS / patched Human DLL | accepted hashes | 六项全部相同 |
| Bot review signatures | 107/107 unique；42 个 patch targets | 106/107 unique；42 个 targets 共匹配 43 处，43 处 expected bytes 均相符 |

`client.dll`、`schemasystem.dll`、`cs2.exe` 完整 hashes 也记录在 local facts；10 月 9 日没有对应完整记录，
不虚构其变化比较。addons 中唯一 managed plugin DLL 为 `InventorySimulator.dll`；Bot 目录是空目录残留。
配置 fixture hash 与 10 月 9 日不同，属于本次开始前的状态；本次未改写，不能宣称私人配置跨日期未变。

## Signature / offset 检查范围和实际阻塞

| 集合 | 来源与范围 | 结果 |
|---|---|---|
| Human installed | 实装 inventory-simulator.json 全部 Windows signatures | 14/14 unique |
| Bot review | 重放 10 月 9 日 `72adfc2` 审查输入；最新 umbrella `55772a6` 的全部 gitlinks 相同 | 106/107 unique |
| Bot pinned JSON | `9848ac8` 历史 payload native JSON 与 installed CSS；不含 managed 内嵌 bindings/patches | 57/57 unique；63 个 configured offsets 只记录未证明 |

扫描使用现有 `scripts/compatibility/scan-windows-signatures.py`，只读 PE executable sections；报告保留
pattern、RVA、file offset、expected/actual bytes 和 binary hashes。输入来源许可证：Human MIT、Bot AGPL-3.0。
失败项 `BotAI/Vision_ApproachBody_SkipSkillCheck` pattern `F3 0F 10 40 0C 0F 2F ? 76 ?`：

- RVA `0x2d91fa`，patchOffset 8，原字节 `76 03`；
- RVA `0x32856d`，patchOffset 8，原字节 `76 33`。

两处都满足 `76 ?`，所以 expected-byte checks 全绿也无法确定正确目标。
审查 [BotAI.cs@a23f7e2](https://github.com/ed0ard/CS2-BotAI/blob/a23f7e24bd485cc23e0d9340ba74e8d8aa3ba6e0/BotAI.cs)
确认其调用 module-wide `NativeAPI.FindSignature(server, signature)` 后直接加 patchOffset，没有函数范围消歧。
未自动延长 pattern、择第一处或写 patch。[完整 Bot review 扫描](2026-10-15-investigation/bot-signature-scan.json)。

offset 必须和 signature 分开判断。只读解析 server.dll 的静态 schema class/field registration：
类名、class size、field count、field table、32-byte field record；要求类唯一、所有字段名可解析且 offsets 在类大小内。
这不是 live SchemaSystem 查询或 DLL 加载。[原始 records 与定位](2026-10-15-investigation/schema-static-evidence.json)。

| Schema field | 新 binary 静态注册 offset | 使用情况 |
|---|---|---|
| CCSPlayerPawn::m_pBot | 0x14F8 | BotAimImprover umbrella=0x1510；最新=0x1518，均不符 |
| CCSPlayerPawn::m_EconGloves | 0x1060 | Human 由 CSS schema API 获取，不据此写死新 offset |
| CCSPlayerPawn::m_nEconGlovesChanged | 0x1448 | 同上 |
| CBaseEntity::m_iTeamNum / m_fFlags | 0x344 / 0x388 | 最新 BotHider 启动时动态解析 schema |
| CCSPlayerController::m_hPlayerPawn | 0x8EC | 最新 BotHider 动态解析，失败时 fallback m_hPawn |

Human gamedata 五项 Windows configured offsets：DropWeapon vtable=28、m_pSOCache=104、m_Owner=40、
m_pInventory=112、m_UserID=168；与 pinned/最新 upstream 既有条目一致，**未由本次 signature scan 验证**。
Bot native configured offsets 留在 pinned JSON report；BotAimImprover 的 CCSBot 内部 targetSpot=0x5994、
enemy=0x5A00、visibility=0x5A04 没有本次布局证明。不能把 schema 局部结果推成所有 offsets/ABI 通过。

## CT 开场动画手套：事实与假设

用户报告：CT 开场动画手套错误且发黑，T 正常，历史普通游玩基本正常；未提供本次录制或逐帧对照。
[配置摘要](2026-10-15-investigation/human-config-summary.json) 已移除 SteamID、uid 和个人 hash：
CT/T 均为 `def=5030, paint=10073, seed=0, wear=0.06`，没有 `agents` override。
cfg 文件为 `invsim_fallback_team 0`、`invsim_minmodels 1`；离线只证明文件值，不能证明运行时 convars。
CT/T domain/projection 映射是 3/2，未见颠倒；相同手套参数降低单边参数错误的解释力。

审查精确 pin [fade4449](https://github.com/ianlucas/cs2-css-inventory-simulator/tree/fade4449aaa6d5153261c855d0cc12d7dacfefdd)：

- `GetItemInLoadout` Post-hook 使用 native 请求的 team/slot，查询 `CLOTHING_HANDS`，经自有 econ item view 返回。
  缓存键为 `(SteamID, team, slot)`，再比较 item.Hash / Stattrak，并非 CT/T 共用一项。
- `GetGloves` 先读本队，只有 fallback 开启才读另一队；`ApplyAttributes` 写 def、唯一 itemID、paint/seed/wear 等 attrs。
- `player_spawn` Post -> 下一次 world update -> `RefreshGloves`。
  后者立即 `SetBodygroup("first_or_third_person,0")`，再下一次 world update 调 `SetWearables`，
  有自定义手套才增加并标记 `m_nEconGlovesChanged`。这条路径没有显式恢复 bodygroup=1，也没有开场动画专用 hook。
- agent refresh 调 SetModelFromLoadout / SetModelFromClass、恢复 defusekit bodygroup，再 refresh gloves；
  `minmodels>0` 会跳过 RegiveAgent。这里的 CT/T 默认 map agent、模型子部件与出场场景仍可能不同。
- [#144](https://github.com/ianlucas/cs2-css-inventory-simulator/issues/144) 曾报告 T→CT 手套回默认而反向正常；
  [#146 / 6525d02](https://github.com/ianlucas/cs2-css-inventory-simulator/commit/6525d02f081b2130a03da3e9fd777796eb91f8c6)
  加入 spawn/agent refresh 修复，**已是当前 pin 的祖先**。它不是本次尚未安装的新修复，症状也不是黑色开场手套的直接复现。
- 截至调查，最新 upstream [62dd330](https://github.com/ianlucas/cs2-css-inventory-simulator/commit/62dd330a9867fef9c779d40e3c3da1d3d3e52709)
  新增 loadouts，此前有 pets、C# extension 改造与 signatures 更新；`RefreshGloves` 的上述主体仍相同，未发现对应开场发黑的直接修复。

交叉参考仅用于形成假设，不移植代码：
[AstraSkins cf46bf7 / SkinManager.cs](https://github.com/Ayrton09/AstraSkins/blob/cf46bf7510e543244f5cb87cb4ab64ace2635f0e/SkinManager.cs)
描述默认 agent glove 隐藏及第三人称刷新，并按目标状态在相邻帧切换 bodygroup；
[WeaponSkins df33aa3 / GloveService.cs](https://github.com/Staaar0/WeaponSkins/blob/df33aa34dac9108354538cfc76c420edf09d0f25/WeaponSkins/Game/GloveService.cs)
在 0.2s 后恢复为 1；
[WeaponPaints 6b1039d / WeaponAction.cs](https://github.com/Nereziel/cs2-WeaponPaints/blob/6b1039d63042b0bbadbf142054c86f79284fdb6a/WeaponAction.cs)
也用 0→1 切换，注释目标为避免模型重叠。三者使用的直接 econ 写入/refresh 机制与 InventorySimulator 不完全相同，
不能证明把其 bodygroup 最终值照搬过来就正确。定向 Issue 搜索未发现与本次完全相同的 CT 开场发黑证据。

优先级最高的**待实机假设**是：第三人称开场时默认 agent 手部与覆盖 glove 的显示/bodygroup 或材质缓存状态不一致；
其次是两次 world update 与出场场景创建顺序造成 refresh 时机差异。CT map agent/model 差异可解释单边表现，尚未证明。
现有 signature unique、两边相同配置及历史正常第一人称反馈，均不能排除 client material/cache 问题；
也没有证据归因为 Bot、全局光照、某一具体 agent 或某个 offset。开场场景到底复用哪一个 pawn/model 仍 pending。

## Bot 整合、离线构建与实验边界

[umbrella 55772a6](https://github.com/ed0ard/CS2-Bot-Improver/tree/55772a6a6a332e4f896223adbc7d2000749cfa1b)
相对 10 月 9 日只有三次 README 修改，九个 gitlinks 不变。
[组件 refs 与实际 diff](2026-10-15-investigation/bot-upstream-review.json) 区分整合 ref 与组件 latest：

| 组件 | umbrella → latest | 直接相关变更 |
|---|---|---|
| BotHider | 12069c2 → 2a7e958 | 新 s2sdk；[#39](https://github.com/XBribo/CS2-Bot-Hider/pull/39) 合入热路径优化 |
| BotAimImprover | 17e895b → 7157ff9 | Windows m_pBot 0x1510→0x1518，Linux 另修；新 build 仍不符 |
| BotController | 0ae8f18 → 6eac193 | 普通 weapon selection 尊重 hooks；新 s2sdk |
| BotVision | 33ab01f → bed1c89 | 新 s2sdk |
| BotState | ce28e5c → 962743f | 默认 horizontal FOV 120→180；保留已生效 flash blind window |
| BotRandomizer | 024f314 → f3be4e2 | catalog/package、版本、Linux constructor signature；Windows 两条 bindings 未变 |
| BotAI / NadeSystem / BotBuy | 未变 | 不自动解决本次 BotAI ambiguous pattern |

BotHider #39 的实际变化：managed `Valid()` 不再在每次 IsBot 查询时尝试重连，已有 2s refresh timer 的
GetManagedSlots 仍负责重连；native PackEntities 使用 thread-local 可复用 vector，取消重复建两个 vector 与线性去重。
递归深度、mutex 和 Post 恢复 FL_BOT 的路径仍在。它减少热路径开销，但没有这台机器的 FPS/frame-time 实测。
latest release 仍是 `v0.5.1@12069c2`，并不含该优化；不要把新 managed half 与旧 native release 拼成已验证组合。

使用现有 task-local SDK `10.0.401`、已有 `C:\Users\hasee\.nuget\packages` 作为唯一 restore source、
`NuGetAudit=false`：BotAimImprover `7157ff9`、BotHiderImpl + API `2a7e958` 的 Release build 均 **0 warnings / 0 errors**。
日志及输出 hashes 见 [offline-builds.json](2026-10-15-investigation/offline-builds.json)。未生成 package/install payload。
PATH 的 dotnet 只有旧 runtime；从 machine context 找到已有 SDK 后实际验证使用，没有安装 SDK。

MSVC 14.44 / CMake 已存在；本次进程未配置 HL2SDKCS2、MMSOURCE_DEV、CSGO_PROTO、PROTOC，PATH 无 protoc。
现有项目 tooling 目录未提供 s2sdk/metamod headers，不能用“有 C++ 编译器”替代 native prerequisites。
没有拉取依赖或补建工具链，**完整 Windows native BotHider/Controller/Vision build 未验证**。
[#172](https://github.com/ed0ard/CS2-Bot-Improver/issues/172) 与
[#181](https://github.com/ed0ard/CS2-Bot-Improver/issues/181) 查询时仍 OPEN，旧本机 performance gate 保持未通过。

未来先处理明确的 offset/pattern 阻塞与成对 native/managed 身份，再考虑独立实验候选；
不能凭本次 managed build 通过或 upstream 优化宣称“可安全运行/性能已修复”。不变更现有 Bot pin。

## 未来最小实机项（本次全部 pending）

1. 进入游戏前，另行按已有可恢复合约处理丢失的 MetaMod startup entry；本次没有执行。
2. 保持 accepted Human DLL/当前 preset、Human-only：同地图分别记录 CT/T 出场动画、首次第一人称、
   一次死亡重生和 T→CT 切换；分别记录 glove model、paint、黑色范围、默认 agent 与触发时序。
   先验证是否可稳定复现，再考虑仅一个变量的 bodygroup/refresh probe；不要直接改 preset、agent、DLL 三项。
3. Bot 静态阻塞消除、配对构建和恢复边界具备后，使用 Human 不加载的 Windows listen-server 实验：
   同地图/画质/人数/出生位置及固定采样时长，预热后 vanilla→候选→vanilla，记录 frame time p50/p95/p99、
   1% low 与 long frames；如需归因，保持其余组件相同，仅切换成对 BotHider 版本。
   同时检查 TAB、score、换边、重生与 crash。当前不执行任何游戏内或安装操作。

归档与 JSON 验证、保护文件前后比较见 [verification.json](2026-10-15-investigation/verification.json)。
