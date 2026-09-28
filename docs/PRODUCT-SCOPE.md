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
- 对玩家真实拥有/获得的枪械应用对应 preset。
- cosmetic apply 不得把一种枪械 identity 伪装成另一种枪械。
- P2000 / USP-S、M4A4 / M4A1-S 等 loadout identity 是独立问题，不能靠修改 skin writer 的 ItemDefinitionIndex 假装解决。
- 目标支持 PaintKit、Wear、Seed 等个人 preset 所需核心数据；额外字段只有在有实际需求时再扩展。

### 3.2 Loadout identity

至少需要可靠支持 CT 侧常见互斥 loadout 的真实武器选择，例如：

- USP-S / P2000；
- M4A1-S / M4A4。

这层只决定玩家实际拿到哪一种武器，不承担 cosmetic projection。

### 3.3 快捷换刀

默认轮换顺序固定为：

1. Karambit — 507
2. Butterfly Knife — 515
3. M9 Bayonet — 508
4. Bayonet — 500
5. Skeleton Knife — 525
6. Falchion Knife — 512

默认候选按键为 `\`。

要求：

- 一次按键只推进一个位置；
- 循环结束后回到第一把；
- 每个刀型使用自己的 preset；
- model、animation、skin 必须同时正确；
- 失败不能造成无限刷刀、HUD 抖动、持续音效、永久丢刀或 crash；
- 不把某个尚未实机验证的 `ChangeSubclass` / Kill-Give / drop-pickup 技术方案写成产品要求。

具体 knife replacement / creation mechanism 由最小实验和实机证据决定。

### 3.4 手套

- CT / T 可独立配置；
- 每边可独立启用/关闭；
- 不需要公共服务器权限/VIP系统。

### 3.5 音乐盒

- 支持本地玩家音乐盒 preset；
- 默认个人 preset 目标为 Music Kit ID 28；
- 不把音乐盒和刀/枪生命周期耦合。

## 4. Preset 与导入导出

- CT 与 T 配置在领域模型中明确分离。
- 支持 CT + T 整套 preset 一次导出 / 导入。
- 配置主键使用稳定数值 ID（DefIndex、PaintKit 等），本地化名称只用于 UI。
- 旧 `E:\CS2MOD` 中已有 preset 是迁移输入，不直接作为新 schema 的 canonical 定义。
- schema 必须带版本，以便未来迁移。

## 5. 本地控制界面

Panel / desktop controller 属于后续产品层，而不是 runtime correctness 的前置条件。

未来主要职责：

- runtime 状态与兼容性提示；
- Bot 配置入口；
- Human cosmetics preset 编辑；
- 导入 / 导出；
- 安装 / 恢复；
- release / diagnostics 的本地访问。

Panel 不应成为 Bot AI 或 cosmetics 生命周期的隐藏 owner。

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
