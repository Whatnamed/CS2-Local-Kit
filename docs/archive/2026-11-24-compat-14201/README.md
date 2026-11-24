# CS2 1.42.0.1 Windows 兼容维护

本机 Steam public 为 `1.42.0.1 / 2000950 / 26879581`，起点为 `ef64cf363504f079ee78dd6118dd7c52e9153722`。Steam 替换了五个游戏二进制、五个模式配置和 `gameinfo.gi`。模式配置及启动文件正文与 [Valve build 跟踪副本](https://github.com/SteamDatabase/GameTracking-CS2/commit/10495120218488f938b2ad7b574b314f87b6e10c) 一致。仅按 `review-14201.json` 的源候选 digest、目标文件身份和 retained 前后映像撤回旧 daily 整合层。

## Windows ABI

- [CSS #1502](https://github.com/roflmuffin/CounterStrikeSharp/issues/1502) 在同 build 的 Linux 专服定位了 `ISource2Server` 虚表变化。Windows 检查定位到 `CSource2Server` 主虚表 RVA `0x19848c0`，slot 14 为新增的转发函数，slot 19 接收 `KeyValues*`，slot 20 对应 `GameFrame`。现有 CSS、BotHider 的 `GameFrame` Hook 以及 BotController 的 `GetEntity2Networkable` 调用使用旧布局。
- MetaMod 采用 [2.0 git1491](https://github.com/alliedmodders/metamod-source/releases/tag/2.0.0.1491) 的两项 CS2 x64 loader，含 Hook 卸载修复。CSS 采用 [v1.0.380](https://github.com/roflmuffin/CounterStrikeSharp/releases/tag/v1.0.380) 正式 Windows managed API，加上以同一 tag 源码和 [s2sdk #128](https://github.com/alliedmodders/s2sdk/pull/128) 重编的 native DLL。两套 .NET 10.0.3 runtime 树逐项一致。固定本机 gamedata，关闭自动覆盖。
- CSS 1.0.380 的 `CBasePlayerPawn_RemovePlayerItem` Windows 模式在新 build 命中两处。扩展到调用返回后比较武器指针的 `cmp rax, rbx`，对应同一条目的 Linux 模式语义，排除检查两个实体有效性的另一个命中。
- 保留 patched InventorySimulator 3.3.0 与 C1.1 lifecycle patch。原 Windows Human 签名仍可唯一定位。[3.8.3](https://github.com/ianlucas/cs2-css-inventory-simulator/releases/tag/3.8.3) 的 19 个 Windows 模式也可定位，仍无证据要求换掉本地 lifecycle 实现。AstraSkins 1.2.8 给出了新目录数据和 CSS 380 依赖参考。WeaponPaints build-482 的探员重设 bodygroup 和手套重刷针对该插件的 `SetModel` 路径。它未解释本机已见黑块，保留 InventorySimulator 自己的 `UpdateWearables` 刷新；WeaponSkins 与 Bot Aim 另有 [上游冲突报告](https://github.com/ed0ard/CS2-Bot-Improver/issues/258)。

## Bot 上游取舍

[v1.4.9](https://github.com/ed0ard/CS2-Bot-Improver/releases/tag/v1.4.9) 更新 Windows/Linux signatures 和 offsets、减少换弹并修订 Retake。之后的 `2382656` 更新模式配置、Retake 树、BotAI `3444d02` 和 BotController `1f87839`，没有解决当前 Windows listen-server 启动崩溃的公开记录。[#254](https://github.com/ed0ard/CS2-Bot-Improver/issues/254) 涉及更新后的启动与 Cobblestone 无投掷物，[#259](https://github.com/ed0ard/CS2-Bot-Improver/issues/259) 针对本轮更新，[#257](https://github.com/ed0ard/CS2-Bot-Improver/issues/257) 由完整重装解决了缺少 native DLL 和 BotHider managed API 混装。不得据此把其中一个 DLL 单独替换进本机组合。v1.4.9 解包后没有新版 `de_cbble` 投掷物文件。本机已有 Valve `de_cbble.vpk`。

本候选只加载框架和 Human 插件，使用 Valve 普通人机。增强人机的三个 native VDF 与 CSS managed 入口由现有事务精确隔离，可恢复。用户的 Low / Medium / High、Aim / Nades、阵容和自动应用数据留待恢复完整 Bot ABI 后继续使用。本候选保留 Valve 新版模式配置，并只在 `SearchPaths` 增加 MetaMod 入口。

## 饰品目录

[ByMykel `cd3054f`](https://github.com/ByMykel/CSGO-API/commit/cd3054f67f3a314a6ebb91eae8e7f371d380af) 对应本机 Steam 内容 manifest `5924479096069937098`。英文、简中双语快照按数值 ID 合并。相同刀型和显示名的原 paint ID 与新版 `1478–1483` 并存，旧版由 `legacy_model` 标记，原预设继续按原 ID 读取。Singapore 2026 贴纸保留目录和图片来源，Human preset 当前无贴纸应用项。

真实 Windows listen-server 启动、对局与饰品呈现待用户实机验收。
