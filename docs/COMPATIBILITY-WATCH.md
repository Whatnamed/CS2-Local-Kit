# Compatibility Watch

CS2 更新频繁。目标不是保证“永远不坏”，而是让故障快速定位并限制在最小范围。

本文档描述长期核查流程，不记录某一天的当前版本状态。

## 1. 触发条件

出现以下任一情况时执行兼容性核查：

- CS2 game build 更新；
- MetaMod / CounterStrikeSharp 发布兼容性版本；
- Bot runtime upstream 发布新版本或当前 pin 出现已知问题；
- Human cosmetics 的 native binding / schema 报错；
- 用户观察到 crash、FPS 明显变化、model/animation/skin 异常、TAB/score/match 行为变化。

## 2. 先记录本机事实

不要先猜是“游戏大更”或“插件 bug”。

至少记录：

- `steam.inf` / 实际 CS2 build；
- 关键 game binary hash（需要时）；
- MetaMod 版本；
- CounterStrikeSharp runtime 版本；
- Bot runtime pin；
- HumanCosmetics plugin build/ref；
- 出错日志和可重复步骤。

本机实际 binary 是当前运行事实来源；网页发布信息只能辅助解释。

## 3. Framework 层

检查：

1. CounterStrikeSharp 最新 release / changelog；
2. schema 更新；
3. 与当前错误相关的 signature / offset / API 修复；
4. MetaMod 当前兼容要求。

不要因为有新版就无条件替换整个 runtime；只在证据表明需要时升级候选并重新验收。

## 4. Bot runtime 层

检查 CS2-Bot-Improver：

- 最新 release；
- current main 最近兼容 commit；
- 与当前症状匹配的 Issues；
- 必要时继续追踪其直接组件 upstream，例如 BotHider / BotVision。

Bot baseline 必须独立于 HumanCosmetics 测试，避免跨域归因。

## 5. Human cosmetics reference set

至少对照：

- AstraSkins；
- WeaponSkins；
- WeaponPaints；
- BotRandomizer（仅 Bot-side 行为）。

重点寻找：

- 相同 CS2 build 是否出现同类故障；
- 是否共同修改同一个 signature/schema/API；
- 不同项目是否使用不同机制而只有其中一种仍然工作。

社区实现是证据，不是自动答案。

## 6. Capability probe

对更新敏感功能优先运行最小 probe，而不是直接重写完整插件。

典型 probe：

- existing gun paint；
- knife identity/model/animation（仅未来快捷换刀场景，延后）；
- knife paint；
- glove；
- music kit。

普通枪械 identity 不是 probe 对象：它由 CS2 自己的 loadout 决定，不属于 cosmetics 的更新敏感面。

每个 probe 应尽量只验证一个技术假设。

## 7. 失效分类

观察到问题时先归类：

```text
Framework/runtime load
Native signature/schema
Bot-only behavior
Human weapon identity
Human cosmetic projection
Client rendering/cache
Installer/ownership
Performance
Match/HUD side effect
```

不要用一个“万能生命周期修复”同时处理多个类别。

## 8. 更新 lock 的条件

外部 component 的新 ref 只有在：

- 自动化 build/static validation 通过；
- 相关 capability probe 通过；
- 必要的真实游戏验收通过；

后才从 candidate 提升为 accepted pin。

“upstream 最新”不等于“本机 accepted”。

## 9. 留存证据

只有对未来维护有复用价值的兼容性调查才写入 `docs/archive/`。

普通一次性日志和下载内容留在 `E:\CS2MOD\diagnostics` 或 task-local `temp/`，不要把仓库变成逐日 devlog。
