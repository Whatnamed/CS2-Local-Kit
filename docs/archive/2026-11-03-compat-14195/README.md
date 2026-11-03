# CS2 1.41.9.5 compatibility candidate — 2026-11-03

本次从已交付的 `codex/daily-play-20261101@4280e29d2e0419b7a36fc5735fd26573fb3a40fb` 派生。只适配固定新构建与一次性迁移入口，不改日常流程。候选保持未安装、实机待验收；不提升 accepted runtime pins。

## 本机与安装事实

- Steam public：`1.41.9.5 / ClientVersion 2000940 / BuildId 26393695`，StateFlags=4。五项二进制完整 SHA256 见 [local-facts.json](local-facts.json) 和 `scripts/compatibility/review-14195.json`。
- 相对 14194：`engine2.dll`、`schemasystem.dll`、`client.dll`、`server.dll` 更新；`cs2.exe` 未变。
- 实际活动事务是 Low performance 整合 `177e9652205d4d75a4cda34d76934547`，manifest `fc09b409160d071a60a3d013fbb89d108d704ff4b65731dff96626823f006012`，仍为 installed；不是日常候选，也不是历史 Medium `cf73...`。
- 原 685 项 postimage 只有四个游戏 DLL 与 `steam.inf` 漂移，均为 retained。插件 module inventory、Human、启动项与其他事务文件匹配。MetaMod 入口仍在，无需单独修复启动项。
- 三层父事务：Bot `b613a8c709c7440998346cb802bb790f`、compat `5808afffdc44440ab3f0de2159cdb436`、Nades `9f9849ca97804c2dbd7b1307bba4580c`；marker 和完整备份 inventory 匹配。

## 签名、Schema 与偏移

- 对新本机 binary 复用既有扫描输入：**123/123 Windows patterns 唯一命中**，覆盖 CSS、Human、BotAI、Aim、NadeSystem、BotController、BotHider、BotVision。
- BotAI 的 40 处 expected-original 检查仍是 39 处通过。`TBot_BombsiteSearch_UseKnownPlantedSite` 的实际 `E8 30 51 F9 FF` 与旧 14194 证据完全相同，仍由原 guard 跳过；未把历史限制当作本次新增变化，也未强行开启该 AI patch。
- 本机静态 Schema registration 对照的 **43 个既有字段偏移全部不变**，包括 `CCSPlayerPawn.m_pBot=0x1500`，Aim 的 `m_targetSpot=0x5994`、`m_enemy=0x5a00`、`m_isEnemyVisible=0x5a04`。私有 `m_gameState` 仍不直接注册，未声称验证其 offset。
- `CCSGameRules` 字段数由 190 变为 191，classSize 仍为 `0x11450`。新增 `m_bQueuedMatchmakingReportedRoundStatsPendingSend` 位于 `0xe18`。上游实际变更见 [GameTracking diff](https://github.com/SteamDatabase/GameTracking-CS2/commit/1a3016b053eb006daaf741396f0d0416388519ca)，本机原始注册记录见 [schema.json](schema.json)。
- 现有消费字段：BotHider 的 `m_bPickNewTeamsOnReset=0xdc6`；LocalKitMatchSetup 的 `m_bFreezePeriod=0xd8`、`m_bWarmupPeriod=0xd9`、`m_totalRoundsPlayed=0x120`；NadeSystem 同样读取 FreezePeriod/TotalRoundsPlayed。这些字段都存在；BotHider 用 Schema 名称查找，managed 插件经 CSS Schema 属性查找，不把后续字段的静态布局写进适配层。新增字段没有实际消费方，不需要改动业务或重建插件。
- Aim 函数 429 条反汇编指令的 mnemonic/非重定位 operands 与既有证据一致；比较略去 branch target 与 RIP-relative relocation，不能据此声称目标 ABI 已实机验证。六个 framework interface 标识仍存在。

证据：[signatures.json](signatures.json)、[schema.json](schema.json)、[native-checks.json](native-checks.json)。静态检查不等于 Hook、native ABI、TAB、换边、渲染或 FPS 验收。

## 组件与适配范围

全部 113 份日常 runtime payload 继续使用 `bot-daily-14194-b72e06b` 的精确文件：MetaMod git1481、CSS 1.0.376 + 既有 native backport、Bot Improver pinned e18c029 组合、性能 backports、Aim/Nades evidence、BotHider/BotController、LocalKitMatchSetup 与其 Core、Human InventorySimulator 均不重建或升级。

保留配置自动保存、独立难度、Aim/Nades 按地图独立接收、可选持久阵容、普通/增强切换。新 Controller 的 App/Core 因 embedded 构建核查与候选白名单而重新发布；游戏内适配器/Core 不消费该安装核查资源，继续复用原二进制。

上游核查见 [upstream-review.json](upstream-review.json)。[CSS v1.0.379](https://github.com/roflmuffin/CounterStrikeSharp/releases/tag/v1.0.379) 及 main 的 typed memory-cache 修复已核对；Bot Improver 最新 release 仍为 v1.4.7。没有发现本构建要求整体升级的断点。GitHub 连接未返回 GameTracking 内容，相关 diff 由只读 `gh api` 获取。

在既有 Python/C# reviewed-manifest boundary 注册 `review-14195.json`：仅允许列出的五个 14194 performance/daily 原 manifest，通过新 build、五 binary hash 与五份 retained 前后身份核验后，生成内存中的 reviewed view。原 manifest、install record 与 preimages 不改写。未知 build、binary、模块、Human 或父备份漂移继续拒写。14193/14194 原 review 继续保留。

封装复用原候选内容，生成新的不可变候选及 recovery tools；不是编辑旧 manifest。入口：`scripts/compatibility/Build-DailyCompatibility14195.py`，在已提交、干净 worktree 执行。固定源 digest 和所有文件由原 `load_candidate` 校验，实际活动层先做只读精确恢复预检。

## 迁移与验收

交付候选：`E:\CS2MOD\releases\bot-experiment\bot-daily-14195-557eea6`，manifest SHA256 `58b58f7aa793f59f5d167b9876a205d4ae62e84dccaa29c9f40de39f6e5b2db4`。匹配 Controller：`E:\CS2MOD\releases\controller\controller-compat-14195-20261103\payload\CS2LocalKit.App.exe`，source HEAD `2ac64d69e7edee98013d78eca47dedef0b897e62`。

验证通过：49 项相关 .NET 测试；注册候选后的白名单检查；新 14195 与原 14193 各 10 项跨构建恢复检查；18 项日常迁移/profile/普通增强切换检查；14 项实际交付包公开 PowerShell 入口检查。跨构建检查覆盖恢复中断续作、未知 binary/gamedata/Human/父备份漂移拒写；日常检查覆盖有限中断映像、三档和模式切换后的精确恢复。

用发布后的 App/Core DLL 对真实旧 Low 安装完成三档只读迁移预览，全部指向同一个新日常底座；probe 的 runner 明确拒绝 `-Apply`。408-file self-contained Controller 发布及私人数据边界检查通过。[payload-checks.json](payload-checks.json) 确认 113 份 runtime payload 与全部日常/三层父事务/Human 合约相同。[protection.json](protection.json) 确认实际游戏文件、旧候选、活动备份、三个父备份、个人 presets/app-data/Human 备份和 Steam manifest 均未被本次工作修改。未修改 UI，不追加 WPF 视觉或性能套件。

[exact-restore-preview.json](exact-restore-preview.json) 记录了实际 Low 层的只读恢复预检：撤回后的 targetBuild、baselineFiles、gameBinaries、moduleInventory、runtimeIntegration 和 Human 身份与新日常候选一致。新包安装仍必须在撤回后重新完整实时预检。

候选、Controller 精确身份及最终验证结果见 [delivery.json](delivery.json)。最短真实操作：

1. 关闭 CS2，打开交付 Controller。系统设置 → 检查组件预览 → 安装/升级组件。
2. 确认后由 Controller **只精确撤回活动 Low 整合层**，保留三个父事务及 Human，再完整预检并安装统一日常底座。不要手动逐层 Restore，不要直接在活动 marker 上 Install。
3. 设置增强模式、难度、Aim/Nades，先保持阵容关闭，等待“已写入”；用户自行以 `-insecure` 启动 CS2，在游戏内任选本地地图，不需要准备对局。
4. 之后验证可选阵容、跨图读回、关闭游戏后的难度/普通增强切换、TAB/比分/换边以及 Human CT/T 枪械、刀、手套、音乐盒。运行中修改模式/难度只保存；关闭游戏并打开 Controller 应用后再启动。

真实 Install/Restore、游戏启动及上述游戏内效果均未执行。本记录达到候选交付后即收口，不附加性能优化或新 UX 工作。
