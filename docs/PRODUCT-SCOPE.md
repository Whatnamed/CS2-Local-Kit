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

本地单次对局的开局配置属于产品范围：真人 CT/T、双方 Bot 人数、五席位阵容与真人替代位置、带日期/来源的只读职业队历史快照、个人混编/自定义名，以及整局预设、最近使用和收藏。双方可独立选择同队或不同历史版本；同名、disabled、缺失及重复 persona 必须显式消歧或名称降级。

配置保存、开局待应用、运行时接收/读回和实际行为验证分别呈现。受控适配层仅在本地离线会话、插件就绪、真人选边且首回合/计分开始前一次性初始化；初始化后不因换边、重生或下回合重建阵容。失败给出原因，不以重复踢 Bot 或重开恢复。

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

Controller 是个人 Windows 离线游玩的配置与操作入口。**目标产品 IA、所有页面的命名、空间区域、控件、状态与任务验收以 [CONTROLLER-UX.md](CONTROLLER-UX.md) 为唯一 canonical 来源**；本文件只规定长期能力范围，不能从当前旧页面反推目标导航。

目标日常任务仅两项：**对局配置**与**饰品配置**。低频组件检查、更新、备份、诊断通过次级**系统设置**提供。对局方案属于对局配置，饰品方案属于饰品配置；不新增与日常任务并列的安装器、诊断或预设库首页。页面标题、区域标签与操作按钮采用一致、可读的中文语法，不向用户暴露内部 transaction、profile、fixture 等字段。

- **对局配置**：选择本地地图、普通/增强 Bot、实际支持的简单/中等/困难档位、Aim/Nades、CT/T、双方人数、固定职业队历史版本、自由组队与真人替代席位；保存对局方案；在当前安装与离线会话条件允许时安全准备/应用。选择偏好不等于已安装，也不等于本局实际生效。
- **饰品配置**：CT/T 枪械/刀具/手套/音乐盒；饰品方案的保存、复制、导入导出及安全应用。正常日常操作不能要求手工理解“编辑草稿→激活指针→文件投影”三个状态，也不能永久要求先卸载增强 Bot 才能修改饰品。
- **系统设置**：实际安装状态、兼容检查、备份恢复、数据管理与诊断信息；只有必要时就地引导进入，高风险写入始终保留精确预检、用户确认和恢复能力。
- **运行边界**：增强 Bot 的 Low/Medium/High 来自不可变安装 profile，不能宣称局中热切；Aim/Nades 与阵容必须靠真正的 CSS 适配层和对应会话的读回确认。可靠自动启动本地地图尚未得到实机证明前，不得宣称支持一键开始游戏；提供真实可行的下局应用路径。

当前代码中仍有历史五页导航、多个相互独立的难度偏好及实验事务限制，这是**待迁移的实现现状**，不是目标方案。新 UI 也不拥有 Bot AI、匹配进程或 HumanCosmetics 生命周期；实际运行由相应受控模块负责。尚未实机验证的行为必须明确与已验收能力分开。

未纳入本轮的能力：tray-first、overlay、公共服务器或比赛管理平台。

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
- 跨局赛事/联赛管理、动态 HLTV 同步与 Team CRUD 后台；
- 为了单机需求而维护完整 Bot-Improver fork；
- 与核心目标无关的 Local-Arena 历史功能。

旧 Local-Arena 的功能存在不构成新项目继续实现它们的理由。
