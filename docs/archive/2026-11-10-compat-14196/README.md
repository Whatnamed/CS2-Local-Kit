# CS2 1.41.9.6 兼容候选

代码基线是 `codex/compat-14195-20261103@50cf838f5ce54a3737763df680aeb4c1a9f3900f`。11 月 4 日调查分支 `c54d76d` 只包含证据，用于核对实装组件和手套路径。

## 本机与拦截路径

- 本机 Steam public 为 `1.41.9.6 / 2000942 / 26445667`，StateFlags=4。相对 1.41.9.5，`client.dll` 与 `server.dll` 更新，`cs2.exe`、`engine2.dll`、`schemasystem.dll` 哈希一致。MetaMod 启动项存在。
- 活动整合层仍为旧 Low `177e9652205d4d75a4cda34d76934547`。685 项事务记录中 679 项与磁盘一致。其余 6 项均为 retained，包含四个游戏 DLL、`steam.inf` 和 `gamemode_retakecasual.cfg`。Retake 的规范化文本与 Valve 构建的跟踪副本一致，冻时由 7 改为 5，回合重启延迟由 5 改为 3。[GameTracking 2000942](https://github.com/SteamDatabase/GameTracking-CS2/commit/cce7bf1f190144c1c08fb0fe75fd3c9514f2637f)
- Windows UserAssist 最后一次记录的 Controller 是 `controller-compat-14195-20261103\payload\CS2LocalKit.App.exe`，App/Core 与该发布清单一致。使用其 Core 对真实活动事务只读运行 `BotHumanFixtureSidecar.Inspect`，得到「Steam/CS2 build 与联合候选不匹配，保留 Human 数据」。这就是 `FixtureApplier` 在投影和写入前调用的身份检查。

## 适配与上游取舍

1.41.9.6 使用与此前相同的精确受审核查。固定 Steam build/client 身份、五个二进制哈希、六个 retained 前后镜像及五个原始 1.41.9.4 manifest digest。在内存中读取受审视图，恢复仍核对原事务、模块、Human 与三层父事务。候选的 112 个 payload 与 1.41.9.5 daily 对应文件逐项一致。移除原候选携带的旧 Valve Retake 副本，使新 cfg 作为 retained 文件参与预检和精确恢复。日常难度、普通/增强切换、Aim/Nades、阵容、Human 合约均沿用。

| 核查 | 取舍 |
| --- | --- |
| [Bot Improver v1.4.8](https://github.com/ed0ard/CS2-Bot-Improver/releases/tag/v1.4.8) 与本机 e18c029 组合 | 现有 Windows 绑定扫描通过。保留已封装的性能改动、Bot 模块和本地适配器。 |
| [BotHider ChangeLevel](https://github.com/XBribo/CS2-Bot-Hider/commit/3f2dba5c7c796b6cdd4b682d727d1435a2cc6037) 与 [绑定检查](https://github.com/XBribo/CS2-Bot-Hider/commit/bdb32dc25017f19) | 实装从成员指针绑定虚表 slot 40。当前 engine2 的主虚表该槽仍指向 `StartChangeLevel`。上游把第三个指针形参从 `void*` 改为 `const char*`，两者使用同一个 Windows x64 参数位，旧回调忽略该参数并返回 `Ignore`。上游的绑定失败处理属于新版身份模式链，`ScriptIsBot` 则是本机版本没有的 Hook。保留现有 BotHider。 |
| [BotController a46b15a](https://github.com/XBribo/CS2-Bot-Controller/commit/a46b15a865c135a95f44688bd964da478f83a51a) | 上游增加 disconnect/level shutdown 时的每槽与全局清理，覆盖 replay binding、All/Aim 锁、武器锁和购买计划。实装对应源码有卸载清理，当前日常路径无槽复用遗留状态的实机证据。保留已封装的 BotController。 |
| [InventorySimulator 3.8.1](https://github.com/ianlucas/cs2-css-inventory-simulator/releases/tag/3.8.1) 与 [3.7.2 返回类型修正](https://github.com/ianlucas/cs2-css-inventory-simulator/commit/d7b866f34bcd5ed44d18fe3f0270f847baf07384) | 3.8.1 调整宠物鸡字段。3.7.2 的四处 `MemoryFunctionVoid` 修正了声明，实装 3.3.0 的相应调用均丢弃返回值。当前错误在 fixture 写入前触发，已用实装 gamedata 验证 Windows Human 签名。保留实装 DLL 与 C1.1 启动补丁。 |
| [CSS 1.0.379](https://github.com/roflmuffin/CounterStrikeSharp/releases/tag/v1.0.379) | v1.0.379 含 1.41.9.3 Schema、实体查找和网络字段缓存修复。实装 1.0.376 的 Windows PostThink 已有相同签名，对应源码也已有按参数及返回类型区分的函数缓存。保留该构建与 MetaMod git1481。 |

手套的上游 `HandleSpawn` 仍在 `NextWorldUpdate` 中调用 `RefreshGloves`，差异在于附带宠物处理。后者仍用 bodygroup、延迟 `UpdateWearables` 和 `m_nEconGlovesChanged` 通知。实装的 `SetWearables` 与 `SetModelFromClass` Windows 签名已包含对应更新。当前证据仍无法把第三人称显示异常归因到刷新时序，保留现有模型刷新路径。

静态检查覆盖 123 个唯一命中的 Windows 签名、46 个可解析的已检查 Schema 字段偏移及 429 条 Aim 指令的非重定位形状。BotAI 仍有一项原始字节 guard 跳过，`TBot_BombsiteSearch_UseKnownPlantedSite` 本次读到 `E8 20 51 F9 FF`，保留原 guard。ABI 检查只用于决定是否需要改动静态绑定。

## 一次迁移

1. 关闭 CS2 和旧 Controller，打开新 Controller 的 `payload\CS2LocalKit.App.exe`。
2. 在「系统设置」依次点「检查组件预览」「安装/升级组件」，确认目标为新版 daily。Controller 精确撤回活动 Low 整合层，保留三个父事务与 Human，再重新完整预检并安装统一候选。
3. 在「饰品方案」选择个人预设并点「应用到 CS2」。按需要设置日常模式、难度、Aim/Nades 和阵容，等 Controller 显示已写入后，由用户自行以 `-insecure` 启动本地对局验证。

候选、Controller、事务和验证结果见 [evidence.json](evidence.json)。真实游戏内饰品、TAB、换边和性能待实机验收。
