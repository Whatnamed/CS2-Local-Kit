# Product Scope

## 1. 定位

CS2 Local Kit 面向 **Windows 本地 / 离线 CS2 人机游玩**。

目标不是建设公共服务器平台，而是把两类个人需求稳定组合起来：

1. 使用成熟的外部 enhanced-bot runtime 提升本地 Bot 体验；
2. 为本地真人玩家提供范围明确、独立运行的饰品能力。

项目应优先追求：

- 实机稳定；
- CS2 大更新后容易定位和恢复；
- 各能力故障相互隔离；
- 简单、可回滚的本地安装与配置；
- 不为了“功能齐全”引入与个人使用无关的服务器平台复杂度。

## 2. Bot 体验

Bot 行为由外部 pinned enhanced-bot runtime 提供，当前首选来源是 `ed0ard/CS2-Bot-Improver`。

本项目不以维护完整 Bot AI fork 为目标。

期望保留其有价值的 Bot 能力，包括但不限于：

- aim / combat 改进；
- movement；
- nade 行为；
- economy / buying；
- personality / profile；
- Bot 自身的武器皮肤、刀、手套、Agent、音乐盒等随机化体验。

具体启用项由实际兼容性和性能验收决定，而不是默认所有 upstream 模块必须加载。

## 3. 真人饰品

Human cosmetics 只作用于本地真人玩家，并与 Bot runtime 独立。

### 3.1 枪械

- CT / T 可分别配置。
- 普通枪械 identity 永远由 CS2 自己的 loadout / equipment 决定：游戏实际生成哪把武器，cosmetics 就按该武器真实 defIndex 查询并应用 cosmetic preset。
- 不修改普通枪械 defIndex；cosmetic apply 不得把一种枪械 identity 伪装成另一种枪械。
- P2000 / USP-S、M4A4 / M4A1-S 的 identity override 不是 deferred 功能，而是明确非目标。
- 目标支持 PaintKit、Wear、Seed 等个人 preset 所需核心数据；额外字段只有在有实际需求时再扩展。

### 3.2 Loadout identity（非目标）

USP-S / P2000、M4A1-S / M4A4 等互斥 loadout 的武器选择完全由玩家在游戏内自己的装备配置决定，本项目的 Human Cosmetics 不拥有、不模拟、不修复这一层：

- 玩家在 loadout 里选 USP-S 就拿到 USP-S，选 P2000 就拿到 P2000（CS2 原生行为）；
- cosmetic layer 不负责"把错误武器改成正确武器"；
- canonical preset 中不存在任何 loadout identity / CT-T 共享字段。

### 3.3 快捷换刀（延后）

快捷换刀是未来功能，也是 knife identity 作为特殊情况的唯一原因：它可能主动改变 knife identity，这与普通枪械 identity 的不变规则不同。本阶段不实现。

### 3.4 手套

- CT / T 可独立配置；
- 每边可独立启用/关闭；
- 不需要公共服务器权限/VIP系统。

### 3.5 音乐盒

- 支持本地玩家音乐盒 preset；
- 具体使用哪个 Music Kit 由用户 preset 决定（数值 ID 持久化）；
- 不把音乐盒和刀/枪生命周期耦合。

## 4. Preset 与导入导出

- CT 与 T 配置在领域模型中明确分离。
- 支持 CT + T 整套 preset 一次导出 / 导入。
- 配置主键使用稳定数值 ID（DefIndex、PaintKit 等），本地化名称只用于 UI。
- 旧 `E:\CS2MOD` 中已有 preset 是迁移输入，不直接作为新 schema 的 canonical 定义。
- schema 必须带版本，以便未来迁移。

## 5. 本地控制界面

桌面 Controller（`src/CS2LocalKit.App`）已经存在，是 Human cosmetics 的日常编辑与验收入口；它仍然是位于 runtime correctness 之上的产品层，不是领域语义的所有者。

当前已提供：

- runtime 状态与 CS2 build 兼容性提示；
- Human cosmetics 编辑：武器 / 刀型 / 手套 / 音乐盒，CT 与 T 分开；
- 编辑中预设、激活预设与游戏内配置三者分别呈现；
- 预设的新建 / 复制 / 删除 / 载入 / 设为激活 / 导入 / 导出；
- 写入游戏配置（Apply）与恢复上一次应用（Restore）；
- 固定版本清单快照的本地准备。

尚未提供，属于后续产品层职责：

- Bot 配置入口；
- tray-first 生命周期与 overlay；
- release / diagnostics 的浏览入口。

Controller 不应成为 Bot AI 或 cosmetics 生命周期的隐藏 owner：它只调用 Core 的 apply / restore 入口，不复制其语义。

## 6. 持久本地数据

release、preset、backup、diagnostic 和需要长期使用的 app data 应保存在固定持久数据区 `E:\CS2MOD`，而不是 disposable cache 或 Agent temp。

具体目录约定见 `LOCAL-WORKSPACE.md`。

## 7. 明确非目标

除非以后重新做产品决策，否则不建设：

- Valve matchmaking / FACEIT 支持；
- 公共 community server cosmetics 平台；
- 多用户账号系统；
- VIP / 权限商业化系统；
- MySQL/Web cosmetics 网站；
- Discord Bot；
- Match framework；
- Stats 平台；
- Team lineup 管理；
- 为了单机需求而维护完整 Bot-Improver fork；
- 与核心目标无关的 Local-Arena 历史功能。

旧 Local-Arena 的功能存在不构成新项目继续实现它们的理由。
