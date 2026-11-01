# 2026-11-01：Enhanced Bot 性能回移与 CS2 1.41.9.4 候选

**三档完整候选及新 Controller 已构建，静态、构建和隔离验证通过。未安装候选，未进行游戏内验收，未提升 accepted pins。**

## 基线和本机事实

- 源码起点：`codex/compat-14193-20261030 @ da8f7f758e1bf0b082bdff98665ea22ddbe773aa`。
- 本轮 worktree：`E:\Projects\CS2-Local-Kit\wt-bot-perf-14194-20261101`；branch：`codex/bot-perf-14194-20261101`。
- 实际 `steam.inf`：`1.41.9.4 / ClientVersion 2000939 / SourceRevision 11191540`；Steam `BuildId 26334290 / StateFlags=4`。未启动 CS2。
- 活动安装仍为旧 Medium 整合 `cf73e2b17dde4b98808bb42780ea59c6`，manifest `3542f17be9d9a7e04a3408d7816cf1a68124ffc7c0b2cadfa5b252cfa68341d1`，状态 `installed`。
- 三层父事务仍为 Bot `b613a8c709c7440998346cb802bb790f`、compat `5808afffdc44440ab3f0de2159cdb436`、Nades `9f9849ca97804c2dbd7b1307bba4580c`。marker 与完整备份 inventory 匹配；原事务、原 manifest、preimages 不改。
- 相对原安装的 685 项 postimage，仍只有已解释的 7 项漂移：4 个游戏 DLL、`steam.inf`、CSS gamedata 和 etag。相对 14193，只有 server/client DLL 与 `steam.inf` 更新；engine/schema/cs2.exe 和 CSS gamedata 身份未再变化。

证据：[local-facts.json](2026-11-01-bot-performance-14194/local-facts.json)、[protection.json](2026-11-01-bot-performance-14194/protection.json)。保护快照覆盖全部实际备份、personal presets、app-data、安装 addons/cfg/overrides、启动项和 Steam manifest，仅记录路径/类型/哈希，不分发私人配置内容。

## 采用的性能改动

选择独立回移，不整体替换 Bot Improver。四个组件保持原 pinned base，加下列确切 upstream patch；这些不是完整的新 upstream release。

| 模块 | 原版本 / base | 新候选 | 价值与保留语义 |
| --- | --- | --- | --- |
| BotState | 1.10.2 / `9e9f8f1b2fd2` | base + [8038f8a](https://github.com/ed0ard/CS2-Smarter-Bot/commit/8038f8a6d2ab0d354d5787841dc6fa25e92166b7) | 实体创建/生成/删除后才重建 flash/controller 发现缓存，地图开始/结束及卸载清理；保留原枚举顺序，仍从 live handle 读取位置/可见性。减少稳定 tick 的全实体枚举。 |
| NadeSystem | 1.2.2 / `009ce03005af` + SmokeCreate、LocalKit evidence | base + [ca3b638](https://github.com/ed0ard/CS2-Bot-NadeSystem/commit/ca3b63875ae1b20bc59089cc1d2adbb61fa0416f) | 每图建立 ID/ID+type 索引，替代 cooldown/zone 中重复 `FirstOrDefault`；重复 ID 保持第一条、类型筛选保持原语义。已有 grid、Off–Max 和 v2 创建证据保留。 |
| BotRandomizer | 1.3.3 / `1c7c3ebad4b9` | base + [ded53e4](https://github.com/ed0ard/CS2-Bot-Randomizer/commit/ded53e4278af6def771fbf23ff46998428ea9b33) | 对 immutable catalog list 复用 rarity/finish pools，减少每次 loadout 的 GroupBy/array 分配；权重、顺序、随机抽取次数相同。 |
| BotController | 0.8.0 / `582e9132c10b` | `0.8.0-local.perf14194` + [0c0209a](https://github.com/XBribo/CS2-Bot-Controller/commit/0c0209a5f08266571b3ea2a025b996a64272cd56) | 无 lock/replay 时在 slot 解析/互斥锁前返回；profile 查询按需解析当前 controller/pawn，校验 handle serial、slot 和 pawn，取消每次 Bot Update 的指针缓存。 |

Nade patch 仅调整 context 以保留已有 `LogEvidence("database-loaded")` 调用。native 重建保留此前的 `IVEngineServer2::IsSpawnGroupLoadedForClient` SDK 插入；未升级递归 SDK pin。新 native 的 44 个导出和两个 DLL 依赖与原版相同。

每档包含原许可证、原始 source archives 及新增 `sources/performance-corresponding-source.zip`；后者含四个实际 patched source tree、native 递归依赖、patch 和 build script。精确 base/archive/patch/DLL hash 与命令见 [performance-build-result.json](2026-11-01-bot-performance-14194/performance-build-result.json)。复现入口是 `scripts/bot-experiment/Build-BotPerformance14194.py`，候选封装入口是 `scripts/compatibility/Build-BotPerformanceCandidate14194.py`。

## 保持不变和筛选边界

以下 payload 与封存的 14193 对应档位逐文件哈希一致：

- BotHider 0.6.0 / `9a77fbaf1ee1`，BotHiderImpl/API、persona 与身份/阵容行为。
- BotVision 0.4.0 / `9884848b3404` + 已有 native/SDK compatibility repair。
- BotAI 1.8.13 / `0c46c6757b03`，BotBuy 1.0.14 / `11884e0564b8`。
- BotAimImprover 2.1.7 / `d796572b6d25` + 现有 PBot、Head/AWP、Hook/write evidence；BotController managed Impl/API。
- LocalKitMatchSetup 全部文件，双方 Bot 阵容、Low/Medium/High profile、Aim/Nades 通道。
- 48 份 Nade JSON，原 CT/T Human 插件、gamedata 与个人配置保留。
- MetaMod git1481，CSS 1.0.376 + 已有 14192 native backport；不更改 accepted runtime locks。

[v1.4.7](https://github.com/ed0ard/CS2-Bot-Improver/releases/tag/v1.4.7) tag 为 `91de1b49d429afad622776f8e0271ce61c56761c`，发布后的 main 更新了多个子模块。已对照这些实际 diff，未把 tag 名称当作模块身份。

BotHider 的 affected-slot 优化依赖其新版 incarnation/identity 实现；BotController 新版还移除了 legacy replay API；BotVision 新版包含 smoke 行为和大范围重构。这些不随本次性能 patch 进入候选，以保留当前 API/TAB/阵容路径。Randomizer 另有 native item-view 析构修复，本轮未移植，原生资源回收限制仍保留；其最新 `f5c645c` 实际只改 cosmetic catalog/tool dependencies。BotState 后续 ammo 行为修复不包含在本次缓存 patch 中。

[CSS v1.0.379](https://github.com/roflmuffin/CounterStrikeSharp/releases/tag/v1.0.379) 已发布；其 schema/entity lookup/network-flag 改动已核对，本机没有要求本次全量升级的失败证据。以上是本次取舍，不是对后续 upstream 的普遍结论。完整提交清单见 [upstream-review.json](2026-11-01-bot-performance-14194/upstream-review.json)。

## 最新构建适配

- 复用 14193 的 scanner 与输入，对实际最新 binaries 执行：**123/123 Windows signatures 唯一命中**。BotAI 40 处 expected bytes 中 39 处通过；旧 `TBot_BombsiteSearch_UseKnownPlantedSite` 仍不匹配，由原 guard 跳过，未强行开启新的 AI patch。
- 实际 server 静态 schema registration 的 **43 个选定字段 offset 均与 14193 相同**，含新按需 profile 路径使用的 `CBasePlayerController.m_hPawn` 与 `CCSPlayerPawn.m_pBot`。Aim 三个 offset 和 `m_pBot=0x1500` 不改；`m_gameState` 仍不直接注册，未发明 offset。
- 六个 framework interface 字符串仍存在。signature/schema/导出/标识检查不证明运行时 ABI、Hook、TAB、换边或渲染成功。
- 在既有 Python/C# `reviewed_manifest` boundary 增加固定 `review-14194.json`，直接绑定原三档 14192 manifest、新五个 game binary hash 及七份保留文件前后身份，继续保留 14193 review。没有改写旧记录或新增事务层。
- 新工具可以只读预检旧整合层的精确撤回；未知 binary/gamedata、Human、插件或父备份漂移继续拒绝。实际撤回仍只拥有最上方 integration delta，保留更新后的 Valve/CSS 文件及三层父事务。

证据：[signatures.json](2026-11-01-bot-performance-14194/signatures.json)、[schema.json](2026-11-01-bot-performance-14194/schema.json)、[interfaces.json](2026-11-01-bot-performance-14194/interfaces.json)、[native-api.json](2026-11-01-bot-performance-14194/native-api.json)。

## 候选身份与 Controller

候选根目录：`E:\CS2MOD\releases\bot-experiment\`。每档 110 个完整 payload targets，相对各自 14193 source **仅四个优化 DLL 改变**；48 份 Nade 数据及 profile 均保持原身份。

| 档位 | 目录 | experiment-manifest.json SHA256 |
| --- | --- | --- |
| Low | `bot-perf-14194-low-6196707` | `fc09b409160d071a60a3d013fbb89d108d704ff4b65731dff96626823f006012` |
| Medium | `bot-perf-14194-medium-6196707` | `d9e4893ba16d17449ee3e4f9d54c33507857f253a553fdbe812df82dd2d2bc84` |
| High | `bot-perf-14194-high-6196707` | `eed7d4e69e0ca89a5b7ddb9aef2c1d8b5a66cec5ec560eaef094921209f9e08a` |

候选 builder HEAD：`6196707acd3a6156f3e6fa35b957cd101d09e52f`。

新 Controller：`E:\CS2MOD\releases\controller\controller-bot-perf-14194-20261101\payload\CS2LocalKit.App.exe`，source HEAD `edc15b8757df444c3ec3574bf2afa4719c753a45`。408-file self-contained win-x64，白名单含上述精确 digest；原 release/Controller 全部保留。发布 manifest 和完整交付身份见 [delivery.json](2026-11-01-bot-performance-14194/delivery.json)。

## 验证与最短实机路径

- 四个修改模块的 managed/native build 通过；未引入新 package dependency 或 API upgrade。
- 从实际 patched source 提取 cache 方法，用 fake native 输入执行：lifecycle dirty/clear、枚举顺序、重复 ID/type 首条匹配、负坐标 grid、地图 reload、固定随机序列与 cache identity 检查通过。10,000 轮 weapon+knife 选择的线程分配为 cached `1,920,000` vs original `14,240,000` bytes；这是合成微测，不是游戏 FPS 数据。
- 269/269 .NET tests；40/40 事务回归；新 14194 和保留 14193 各 10 项跨构建检查通过，包括零写入 Preview、漂移拒绝、中断后继续、原记录/preimages/父备份保护。
- Low/High 各 14 项完整交付包隔离检查通过；Medium 41 项通过，覆盖每个实际写入点的失败 rollback、安装中断后恢复和 Restore 中断后继续。
- 隔离环境 `Medium → Low → High → Medium` 通过，每次精确恢复原可玩 substrate。
- 使用**实际发布的 App/Core DLL**，当前真实安装的 `InspectInstalled` 及三档准备只读预览通过；新的公开 wrapper 对当前 `cf73...` 精确 Restore 只读预检通过。Probe 拒绝任何 `-Apply`。
- 真实 Install/Restore/档位切换均未执行，未启动 CS2。真实备份、预设、app-data、插件/data、启动项与 Steam manifest 的最终保护快照一致。

结果见同目录 `performance-checks.json`、`payload-checks.json`、`transition-tests.json`、`transactions-*.json`、`profile-switches.json`、`controller-live-previews.json` 和 `restore-preview-summary.json`。

后续授权真实安装时，最短路径为：先由用户用当前旧 Medium 在同一地图、画质、Bot 数量、阵容、Aim/Nades 下采集基线；关闭游戏，在新 Controller 选择 Medium，检查准备预览后确认采用新候选。该流程撤回旧 integration delta，再对新包重新完整预检，不撤回三个父事务。不要绕过活动 marker 直接 Install；公开包的 `Preview` 安装入口要求已恢复的三层基线。

由用户再次启动同一离线地图，先核对插件加载、Aim Head/AWP、Nades Off/Max、TAB/比分/换边及 Human CT/T 饰品，再在相同热身时间、观察位置和回合窗口比较 FPS、平均 frametime、1% low/long frames。可用已有 `scripts/bot-experiment/Collect-MatchPerformance.ps1` 收集现有进程/日志并附用户的 PresentMon CSV；没有帧数据时 FPS/frametime/long-frame 保持 unknown。实际收益和四个改动的实机行为都仍待验证。
