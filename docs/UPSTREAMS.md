# Upstreams and provenance

本项目区分 **runtime dependency、framework dependency、compatibility reference、data source**。这些角色不能混用。

精确 pin 不写在本文档，machine-readable current state 见 `runtime/*.lock.json`。

## 1. Runtime dependency

### ed0ard/CS2-Bot-Improver

角色：Enhanced Bot runtime。

策略：

- 作为外部 pinned component 使用；
- 默认不 fork 为项目代码根；
- 默认不把完整源码 vendor / submodule 到本仓库；
- 更新先在独立 Bot baseline 中验证，再修改 lock；
- 如必须维护差异，优先最小 patch queue，并明确 upstream commit 与原因。

它的 Bot cosmetics 只负责 Bot；Human cosmetics 不通过修改 BotRandomizer 来实现。

许可证当前为 AGPL-3.0。未来如果 package 分发其二进制或修改版本，必须保留适用的许可证、来源和相应义务；不要因为本项目是个人工具就删除 attribution。

### ianlucas/cs2-css-inventory-simulator

角色：**candidate** Human cosmetics runtime（CosmeticsLab 实验候选），不是已接受的 production dependency。

当前状态：

- 以 exact upstream release 二进制形式在 `experiments/cosmetics-lab` 中验证；
- 不 fork、不修改其 DLL / gamedata，不 vendor 源码；
- Human cosmetics 的故障域必须与 Bot runtime 隔离，本组件只服务 Human 玩家自己；
- 只有真实游戏验收（C1/C2 gate）通过后，才考虑从 candidate 升级为架构依赖并改写本文档。

许可证当前为 MIT；分发其原样二进制时保留 attribution。

## 2. Framework dependencies

### MetaMod:Source

角色：CS2 native plugin loader / runtime 基础。

### roflmuffin/CounterStrikeSharp

角色：Managed plugin framework、schema/API/gamedata 事实来源。

策略：

- CS2 大更新后优先检查其正式 release、schema 与兼容性修复；
- 不仅凭“版本号更新了”就升级所有插件；
- package runtime 与编译 API 需要有明确兼容证据。

## 3. Compatibility references

这些仓库用于观察独立实现如何应对当前 CS2，不自动成为 dependency。

### Ayrton09/AstraSkins

用途：

- Human econ attribute / skin application；
- gamedata / native writer 兼容变化；
- 当前 CS2 更新后的快速故障信号。

当前许可证：MIT。

### Staaar0/WeaponSkins

用途：

- 另一套 Human cosmetics runtime；
- CT/T loadout / preset domain 参考；
- 对照不同 refresh / knife 实现的行为。

当前许可证：GPL-3.0。

### Nereziel/cs2-WeaponPaints

用途：

- 较长时间积累的社区 Issues；
- weapon/knife class 与 defindex 映射参考；
- client rendering / refresh 历史故障样本。

当前许可证：GPL-3.0。

### Bot-Improver / BotRandomizer

除作为 Bot runtime 组成部分外，也可用于理解 Bot-side econ 与当前游戏行为。不要把 Bot-only 已验证行为直接推断成人类玩家行为。

## 4. Catalog source

### ByMykel/CSGO-API

角色：CS2 item/catalog 数据候选来源，包括 skins、agents、music kits 等。

当前许可证：MIT。

策略：

- catalog 更新应可追溯到明确 source ref；
- generated data 与用户 preset 分离；
- 本地化名称只用于显示，稳定数值 ID 才是持久化主键；
- 本地缓存按 locale 分目录（`catalog/<commit>/en`、`catalog/<commit>/zh-CN`），`en` 是 identity 来源，`zh-CN` 只按数值 ID 附加显示名；旧的单目录英文缓存继续可读；
- 缓存写入是显式动作（Controller 的"准备本地清单"），启动与校验路径不访问网络。

## 5. Source-copy policy

参考一个仓库不等于可以直接复制代码。

在复制任何非琐碎第三方实现前必须：

1. 确认许可证；
2. 判断复制是否会改变本项目许可证/分发义务；
3. 记录来源路径和 commit；
4. 优先使用独立重实现或外部 dependency，避免无必要的源码混合。

尚未为本项目自己的代码选择最终 license 时，更不能随意把 GPL/AGPL 实现复制进 production tree。

## 6. Legacy repository

`Whatnamed/Local-Arena` 是历史实现与问题证据来源，不是 upstream。

允许按需参考：

- 旧 preset 数据；
- 用户已经确认过的需求；
- 通用诊断经验；
- 纯数据转换思路。

不默认迁移：

- PlayerKnifeCustomizer；
- PlusMatchCoordinator；
- TeamLineupInjector；
- mode lifecycle；
- old installer ownership；
- Local-Arena Panel；
- 历史 runtime state machines。

是否复用任何旧代码都必须重新审核，而不是因为“以前写过”就复制。
