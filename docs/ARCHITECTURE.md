# Architecture

## 1. 总体原则

CS2 Local Kit 采用 **组合而不是大 fork** 的结构。

```text
                  ┌───────────────────────────┐
                  │       Local controller     │
                  │   install / config / UX    │
                  └─────────────┬─────────────┘
                                │
                  ┌─────────────▼─────────────┐
                  │     Assembly / Installer   │
                  └──────────┬───────┬────────┘
                             │       │
                ┌────────────▼─┐   ┌─▼─────────────────┐
                │ Bot Runtime  │   │ HumanCosmetics    │
                │ external pin │   │ project-owned     │
                └──────────────┘   └─────────┬─────────┘
                                             │
                                  ┌──────────▼──────────┐
                                  │ Compatibility layer │
                                  │ schema/native/sigs  │
                                  └─────────────────────┘
```

Bot 与 Human cosmetics 必须是两个独立 failure domain。

## 2. Bot Runtime

Bot runtime 是外部组件，不是本仓库的主源码树。

默认策略：

- 通过 machine-readable lock pin repository/ref/hash；
- package/install 阶段获取并校验；
- upstream 更新先独立验证，再更新 pin；
- 默认不 submodule、不 vendor 整仓、不长期维护 merge-heavy fork。

只有在有明确、长期、无法通过配置或 upstream 修复解决的问题时，才允许建立最小 patch queue；patch 必须独立记录来源和必要性。

## 3. HumanCosmetics

HumanCosmetics 是本项目拥有的独立能力层。当前已验证的实现形态是：pinned 外部 runtime（InventorySimulator，exact upstream + 项目最小 lifecycle patch）负责 engine 侧 projection，项目拥有 HumanPreset domain、catalog、projection 与 apply/rollback（见 `src/CS2LocalKit.Core`）。

未来如果自建插件，它可以拥有：

- human preset domain；
- gun cosmetic projection；
- knife rotation orchestration（快捷换刀，延后功能）；
- glove；
- music kit；
- Human cosmetics 所需的 compatibility bindings。

它 **不能拥有**：

- 普通枪械 identity override（identity 由 CS2 自己的 loadout 决定，见 §6）；
- Bot AI；
- Bot quota；
- Bot profile；
- match restart；
- score；
- halftime/team switch；
- team lineup；
- match/statistics framework。

如果 HumanCosmetics 因 CS2 更新无法安全初始化，应 fail closed：禁用自身并给出明确诊断，而不是通过反复 retry 或修改比赛状态“自救”。

## 4. Compatibility boundary

所有高更新敏感操作应尽量集中，例如：

- native function signature；
- schema-dependent field access；
- low-level econ attribute writer；
- game-build-specific behavior probe。

业务代码不应到处散布 hard-coded signatures 或版本判断。

兼容层应具备：

- startup validation；
- 清晰 capability 状态；
- 不支持时停止对应能力；
- 足够日志帮助比较社区参考实现。

## 5. Knife identity 与 cosmetic projection 分离

刀型 identity、model/animation 与 paint application 是不同问题。

当前架构只确定以下原则：

- 先获得一个真实、可靠的目标 knife identity；
- 再对目标 knife 应用其 preset；
- 不因为某个社区实现使用 `ChangeSubclass`、Kill/Give 或 `subclass_create` 就把该机制写死进 architecture。

具体机制必须通过 `experiments/` 的最小 probe 和真实 CS2 验证后再进入 production plugin。

## 6. Gun identity 与 skin 分离

普通枪械 identity 不存在独立的 "LoadoutIdentity" 层：

```text
CS2 自己的 loadout / equipment
    ↓
实际生成的 weapon entity（真实 defIndex）
    ↓
GunCosmetics
    ↓
按真实 defIndex 查询并应用 cosmetic preset（不改 defIndex）
```

- 游戏生成 USP-S 就读取 USP-S defIndex 的 preset；生成 P2000 就读取 P2000 的 preset；M4A1-S / M4A4 同理。
- GunCosmetics 不得通过改 defindex 来修复、伪装或"选择"任何普通枪械 identity。
- USP-S / P2000、M4A1-S / M4A4 的 identity override 是明确非目标，不是 deferred 功能。
- knife identity 是唯一例外：未来快捷换刀（延后功能）可能主动改变 knife identity，但那属于独立的、需单独实机验证的机制。

## 7. Installer / ownership

安装器未来必须显式知道自己拥有的文件。

原则：

- 不把整个 CS2 目录视作项目 ownership；
- 不覆盖未知第三方文件和用户 cfg；
- 写入前可备份；
- restore 只恢复/删除本项目明确拥有或记录过的文件；
- release 与 restore 产物使用 `E:\CS2MOD` 持久目录；
- 安装失败不应留下半套不可识别 runtime。

### 7.1 MetaMod Startup-Entry Repair 合约

游戏更新覆盖 `gameinfo.gi` 是常见的外部副作用。本项目为此定义了独立且窄范围的 MetaMod 启动项修复合约：
- 仅在 CS2 关闭且 accepted framework（MetaMod、CSS、patched InventorySimulator）文件身份完全匹配时允许执行；
- 仅修改 `game/csgo/gameinfo.gi` 中的 `SearchPaths` 块；
- 写入前自动创建可校验备份与修复记录；
- 写入后严格核验启动项与修改范围，失败自动回滚；
- 重复执行严格幂等；
- 结构异常或目标不唯一时 fail closed；
- 不触碰 preset、fixture 或 framework 二进制文件。

## 8. Panel

Panel 是配置与操作界面，不是 runtime coordinator 的隐式状态机。

Panel 允许：

- 编辑配置；
- 显示 runtime / compatibility 状态；
- 调用显式 install/restore/package workflow；
- 打开 releases/diagnostics。

Panel 不通过“隐藏模式切换”批量启停无关插件来制造产品状态。

Controller 的「对局配置」通过固定候选白名单和参数列表调用包内版本明确的实验事务，不在 C# 复制安装器。普通与增强本地模式的目标偏好不代表真实安装状态。活动联合安装按自身 manifest、精确记录和磁盘 postimage 核实；无活动事务时才按纯 Human 基线核实。联合候选必须绑定当前 build、Human 完整文件身份、共享依赖及共存核查证据；缺失证据时禁用启用入口。统一日常底座的 `dailyPlay` 合约列出三档已核验 profile 来源和有限的 Bot 加载文件；安装记录、manifest 与父事务保持原样。难度只原子替换受管 VPK；普通/增强模式仅移动本项目 Bot DLL/VDF、profile 与两个增强 CFG 入口到明确禁用位置，不停用 Human 或共享框架。文件来源、允许映像、build、未知文件和路径保护保持严格；运行时变更先保存，Controller 在游戏关闭时使用 Human 操作互斥门安全应用。中断的有限映像可读回并收敛，未知映像拒绝覆盖。旧整合层首次迁移仍经只读预览、用户确认、精确 Restore 和统一底座 Install，保留 journal 与 Human sidecar 移交。

旧隔离实验仅用于精确恢复；联合事务保留 Human 插件、fixture 与预设。活动联合事务中，只有其余 postimage、Human 保留文件和 build 均核实后，Human fixture 才能通过独立 sidecar 更新；原 Bot manifest、install record 和备份保持不变。精确恢复前把 fixture 暂时放回记录的基线，恢复结束后验证并重放最新版，切换候选时将其移交新事务。Bot 文件操作与 Human 写入共用进程间排他 gate。活动事务期间仍阻止 MetaMod 启动修复。执行前及文件变更期间检查游戏关闭、Steam 安装状态稳定；漂移时保留事务与备份并拒绝覆盖。恢复只能使用原事务的精确 install ID，执行成功后还须复核磁盘及记录。这些路径的真实游戏效果与真实安装/恢复仍待实机验收。

### 8.1 已验证安装基线上的整合事务

当既有 Bot、兼容修复和 NadeSystem 已分别形成可追溯的活动事务时，整合候选可以把它们作为只读基线，而非先卸载这些已验证组件。`runtimeIntegration` 合约绑定三个父 install ID、marker、原 manifest、完整备份 inventory、当前游戏 build/五项 binary、共享组件与 Human 文件身份。独立整合 marker 和新 install ID 仅拥有本轮差异；文件相同的组件保持原位。不得修改或重新签署父记录来接纳漂移。

三档切换先核实活动整合事务的精确恢复条件及目标基线，撤回当前整合层，再完整预检并安装目标。撤回整合层返回它记录的可玩基线，并不代表已卸载所有增强 Bot。Human fixture 的侧车更新可在切换中移交；最终撤回保留最新个人配置，后续准备只能从可核验的已恢复侧车取得这份配置。未经记录的 fixture 变化仍拒绝。

Controller 的短期文件观察缓存只服务状态展示；显式刷新清空缓存，串行安装/恢复/准备不使用该缓存。包身份校验、真实事务预检和运行时请求合约仍完整执行。Steam 的 app/build/安装路径及下载/暂存状态属于安装身份；LastPlayed 等普通元数据可以在不同操作间变化，但写入期间仍固定原始 manifest 内容。

## 9. 数据与 catalog

项目自己的 preset schema 与第三方 catalog 分离。

第三方 catalog 可以生成本地只读数据，但必须记录来源和版本。用户 preset 只保存稳定 ID，不依赖本地化字符串。

## 10. 实验到生产

尚未证实的 runtime 机制先进入 `experiments/`。

只有当：

1. 最小 probe 可重复；
2. 实机行为符合预期；
3. failure mode 可控；
4. 没有跨域副作用；

才将该机制提炼到 production plugin。

实验代码不得因为“已经写了很多”自动获得架构权威。

## 11. 有界开局配置

`Core/MatchSetup` 拥有版本化阵容、选手身份、整局预设、人数/替代席位与 CT/T 映射。内置快照不可编辑，个人副本与 BotHider 原文件分离。`MatchSetupStore` 的 `daily-play.json` 是当前配置的唯一权威（revision、保存时间、阵容开关与完整快照）；`library.json` 是显式保存的可复用方案库。旧 `next-match.json` 不再参与日常路径，保留原文件但不领取、不覆盖、不重放。

`LocalKitMatchSetup` 是独立候选 CSS 适配层，与 Human cosmetics 无调用或 ownership 关系。每张新地图读取持久配置快照，异步校验活动事务、五项 binary、payload、CSS 与 Human 身份，并检查 `-insecure` listen server。Aim/Nades 通过各自 capability 独立写入和 mode/epoch 读回；一个模块缺失或阵容失败不阻断另一模块。无当前地图投掷线路时明确报告证据不足。

阵容默认随机：不清理 Bot、不改 quota/队名。显式启用时复用 `PreMatchSession` 与 BotHider，限定唯一真人、选边后、首回合/计分前窗口，一次性清理/添加、临时身份释放、persona 绑定和队名读回。同名、disabled、缺失和重复 persona 保留现有降级规则。初始化后只观察，重生、后续回合与换边不重建；失败停止本次阵容修改，不重启比赛或无界修复。

地图/team/round 事件与有上限的一次性 timer 驱动运行时，不新增 Tick/Frame 扫描。诊断绑定配置 revision、安装 ID、进程启动时间、地图 session、实际 profile/mode、人数、独立模块状态与 epoch。Controller 将不同 revision、旧进程或陈旧地图报告视为历史；文件写入、运行时读回与真实行为验收分开。该适配层不建设完整 Match 系统。
