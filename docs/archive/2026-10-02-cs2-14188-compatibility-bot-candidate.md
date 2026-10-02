# CS2 1.41.8.8 compatibility recovery / Bot candidate

2026-10-02，本机执行证据。此文件是一次调查的归档，不是 accepted runtime 的新定义。
执行基线为远端 `main@753c6d24c6e3a0c542aa5280b82982e425b0a984`；
工作树 `E:\Projects\CS2-Local-Kit\wt-14188-recovery`，branch `codex/cs2-14188-bot-recovery`。
旧 `experiment/bot-baseline` 的 `1bdc890` / `13c14a7` 经审查迁移 tooling，未 merge 旧 branch。

## Human：恢复与初始 preflight

本机实际 `steam.inf` / appmanifest：`1.41.8.8 / ClientVersion 2000922 / BuildId 25640462`。
Valve 更新后 `gameinfo.gi` 没有 MetaMod startup entry，插件目录和私人 fixture 仍在。

重新查询官方下载目录与 GitHub：Windows dev build 最新为 `2.0.0-git1473`；
MetaMod source 的 `9c49d4c9733d94605901fb80148ea8c553f059d3` 是 `Bump hl2sdk-manifests`。
CounterStrikeSharp release/main 仍为 `v1.0.376 / 653d651f1ac09ac1ddb423d588f871b891038860`，
InventorySimulator main 仍为 `fade4449aaa6d5153261c855d0cc12d7dacfefdd`。

只替换 `addons/metamod/bin/win64` 中的 `metamod.2.cs2.dll` 和 `server.dll`；
只在 SearchPaths 插入 `Game csgo/addons/metamod`。移除这条插入后，内容与备份完全一致。
整个 `addons/counterstrikesharp` 文件树在恢复前后身份一致，包含 accepted patched DLL 与私人 fixture；
preset/app state 不在写入范围。

| 对象 | SHA256 |
|---|---|
| MetaMod git1473 官方 Windows ZIP | `2b3cd479afd46c149fd1969d2688f6b565542dd94a323e0de81d7825791a23f7` |
| 已安装 `metamod.2.cs2.dll` | `c22d420985050a1692194249044f173e268e5d1424997315392671fde1db7413` |
| 已安装 MetaMod `server.dll` | `d669855b95f881104e577188e8d93207e7ddaa098b97d19878abce612b1d00ba` |
| CSS v1.0.376 官方 runtime ZIP | `4f2777545a0940351450440288e7487509d539f770e66cf1c30cc2419084c5f8` |
| 已安装 CSS native DLL | `1353cbbf1b92e2a12f1dd36cb37fcdd34d31bc7da40ece47a873f8d7eb1e9c9c` |
| 已安装 CSS API DLL | `e81f05d960c37ee623d552733fc919e206c8c1d16157886342d06a2f4d0dab17` |
| 保留的 accepted patched InventorySimulator DLL | `3a807316195b5c0d31d7aee5490128c36a994eb8764528717097d2ad636d8489` |

备份：`E:\CS2MOD\backups\cosmetics-lab\20261002-124834714-metamod-compatibility`。
schema-2 restore preview 通过，3 个目标及其备份身份均匹配；未执行真实 restore。
Human 诊断：`E:\CS2MOD\diagnostics\cosmetics-lab\20261002-125403-posttest`。
目录命名来自既有 collector；本次是**静态 preflight**，不是新游戏 session 的 post-test PASS。
最新 CSS log 仍是 2026-09-29，6h 窗口内没有新日志，无法从旧日志证明此次 loader 实际加载。

Runtime Status 从 lock 的 `compatibilityCandidate` 读取目标 build 和 loader/native/API hashes；
当前返回 `candidate-hash-match`，build 与历史 accepted baseline 比较仍返回 `changed`。
这个状态是磁盘身份验证，不是版本兼容性或运行时成功声明。

Controller 新发布目录：`E:\CS2MOD\releases\controller\controller-20261002-130546`，
source HEAD `4f00822`；404 个文件已生成 hash manifest，私人数据边界检查通过。
新 status 使用发布目录内的 lock 进行 CLI 验证。未重新宣称桌面 UI 或真实游戏验收 PASS。

## Bot：candidate 包完成，真实安装未执行

重新查询 upstream main 仍为 `9848ac892653db6a256c32524dddf6a05cf4383c`。
当前正式 gitlinks 全部初始化并核验：

| upstream path | actual ref |
|---|---|
| `addons/BotController` | `451c7ba8ddb007eeb4d72edf291f6e0cbb051e56` |
| `addons/BotHider` | `12069c25a756deedc72f8fc116ac3b18045d1223` |
| `addons/BotVision` | `33ab01fc5ec4e27b6f07aff653d54638ad84ebcb` |
| `addons/counterstrikesharp/plugins/BotAI` | `1bcd6dbe310fc059081baa9b56f6145b8a628967` |
| `addons/counterstrikesharp/plugins/BotAimImprover` | `c3d10f5f5e31302589032bb579dfc342c9ea5639` |
| `addons/counterstrikesharp/plugins/BotBuy` | `ff0f47750058f6b16344e7e1712db3b5e65d70b2` |
| `addons/counterstrikesharp/plugins/BotRandomizer` | `5b16e1447f4e5d3032a1625ac368c90f81b929ac` |
| `addons/counterstrikesharp/plugins/BotState` | `30e791f2f615452b3403f352e731116632a71887` |
| `addons/counterstrikesharp/plugins/NadeSystem` | `21788dc3fb61ca62db6a14b9657fd61cbbc888f1` |

Native releases 的 tag commit 必须等于上述 gitlink：BotController **v0.7.0**（native/API 成对），
BotHider v0.5.1，BotVision v0.3.0。没有采用已更新的 BotController v0.7.1。
下载 ZIP hashes 见 `runtime/bot-improver.lock.json.nativeAssets`，全部核验通过。
BotController source wrapper 的 expected ABI 为 22；包内 native DLL 的
`BotController_GetVersion` export bytes 为 `b8 16 00 00 00 c3`，静态证明返回 22。
这不是在 CS2 内调用 native API 的实测。

所有 7 个 managed plugins 从各自 pin 编译；CSS API reference 保留 upstream 自己的版本，
不把其升级到统一版本，不部署私有旧 CSS API DLL。BotState 用同 ref release 的 shared API 编译。
编译仅有 upstream 的 `BotState._isFreezeTime` unused-field warning，未修改 upstream 源码。

最终 release：`E:\CS2MOD\releases\bot-baseline\bot-baseline-main-20261002-130601`。
ZIP SHA256：`58386f947880e38102e7021cd17f80d24b32d677b155ada53bd6ae4f64255248`。
source HEAD：`4f008227e9e4ac2d9f87e5dbea3f9dddded273da`。
111 个 payload files，ZIP 内逐文件 hashes 与 manifest 全部一致；对应 pinned source archives 与 licenses 随包。

Bot package 不包含 shared MetaMod/CSS host core/config/gamedata，不包含 Human 插件或私人数据。
真实 installer preview 验证 host loader/native/API prerequisite 并通过：97 created、14 overwritten；
显式 `-IsolateHumanCosmetics` 只隔离 InventorySimulator 插件目录，保留 shared CSS VDF。
真实 Bot install **未执行**，本机继续处于 Human 单独可测状态。

上一次完整 framework package preview 曾因 CSS `gamedata.json` hash 不同被拦截；
核查差异仅为末尾换行。本次采用共享 host 作为 prerequisite 的边界，避免 Bot 更新或占有这套文件。

High/Low/Medium profiles 从 pinned main 打包，Medium 为默认。
installer `-Difficulty` 的实际 profile hash 纳入安装记录。切换需先 restore 再重装；
直接改 live VPK 会按真实 drift 被拒绝恢复。
Normal cfg 的 upstream subclass_create 快捷绑定从本候选包中去除，改动记在 manifest。
Rush behavior-tree sources 仅随包留存为 optional material，未部署或宣称 Rush ready。

## Ownership / 自动化证据

- schema-2 记录 created/overwritten/modified 的安装后 SHA256；只有 live 仍匹配才能删除或回写。
- 全套 restore preflight 验证 live、备份、isolated directory identity 与目标冲突，任何漂移拒绝全部写入。
- 相同 shared 文件标记 retained；它们永不参与 Bot restore 写入，即使后来由另一 workstream 更新。
- legacy schema-1 无可信安装身份，拒绝自动 restore；不能回退到旧脚本按路径强删。
- 路径限定在 recorded root，并拒绝 reparse point；created file 只删除单文件，不递归清目录。
- ownership probes 验证 preview 无写入、created/shared/overwritten/gameinfo drift 拒绝、
  corrupted isolated backup / conflicting live plugin 拒绝、retained host 保存和 round-trip 内容等价。
- 完整候选 fake-tree round trips（Medium / Low）通过；最终 Medium 115 entries、0 failed，
  shared host、Human plugin 与 fixture 在恢复后均与安装前文件 hashes 一致。
- Core/Controller 最终 Release tests **154/154 PASS**；UI test fixture 补齐真实 SearchPaths 结构，
  comment-only MetaMod 引用不能被误识别为有效 startup entry。
- 所有新 PowerShell 脚本 AST parse、`git diff --check` 通过。
- candidate Bot JSON gamedata + live CSS core gamedata 的 57 个 Windows hex patterns，
  在当前 PE executable sections 内均有唯一匹配。
  该扫描不验证 vtable/offset、内嵌 C# signatures、内存 layout 或运行时 hook/行为。

完整诊断、构建/测试输出、signature scan 及可重放脚本：
`E:\CS2MOD\diagnostics\bot-baseline\20261002-final-preflight`。

## 状态与下一道 gate

用户于初始 preflight 询问回复“尚未测试”。后续部分实机证据见下。Agent 未启动 CS2。
InventorySimulator `accepted-as-patched` / accepted hash 与历史 tested build `1.41.8.6` 保留；
`1.41.8.8 + git1473` 在单独 compatibilityCandidate 下保持 `manual-game-pending`。
Bot pin 与 artifact 仅为 candidate，testedCs2Build 仍为 null。

先测试 Human：启动/本地地图、MetaMod/CSS/InventorySimulator 加载、枪/刀/手套/音乐盒、
respawn/side change、无 crash/HUD/audio loop 或性能异常。
通过后关闭 CS2，再 preview / install Bot candidate（单独隔离 Human），正常至少一局，
检查 TAB/score/round/side change、difficulty、Bot identity/cosmetics、knife/drop crash；
再 restore 并重测 Human。Rush 是第二层。

现有证据足以进入手测，无自动化 blocker；也没有证据足以宣布新游戏 compatibility PASS、
Bot baseline stable 或无需等待 upstream。失败时按当前 session 日志归因，不能由静态扫描代替实机 gate。

来源：
[MetaMod 官方 Windows builds](https://mms.alliedmods.net/mmsdrop/2.0/)、
[MetaMod manifests bump](https://github.com/alliedmodders/metamod-source/commit/9c49d4c9733d94605901fb80148ea8c553f059d3)、
[CSS v1.0.376](https://github.com/roflmuffin/CounterStrikeSharp/releases/tag/v1.0.376)、
[InventorySimulator pin](https://github.com/ianlucas/cs2-css-inventory-simulator/commit/fade4449aaa6d5153261c855d0cc12d7dacfefdd)、
[Bot current-main candidate](https://github.com/ed0ard/CS2-Bot-Improver/tree/9848ac892653db6a256c32524dddf6a05cf4383c)。

## 15:58 后续反馈：Human 饰品生效，完整 gate 待确认

用户反馈“测了，饰品有效果了”。这确认实际 cosmetic projection 已恢复，
但没有单独确认枪/刀/手套/音乐盒全项、respawn/side change 或无 crash/HUD/audio/performance 异常。
不将这条反馈扩大为完整验收 PASS。

只读 post-test 证据：`E:\CS2MOD\diagnostics\cosmetics-lab\20261002-155843-posttest`。
当前 build 仍为 `1.41.8.8 / 2000922`，patched InventorySimulator DLL SHA256 仍匹配 accepted hash。
`log-cssharp20261002.txt` 记录两次 CSS startup：15:41:18 和 15:42:40；
InventorySimulator 分别于 15:41:19.969、15:42:41.738 完成加载，日志没有 ERROR/exception。
这验证当前实际加载链，并支持用户的饰品生效观察；它不单独证明全部 gameplay 稳定性。
采集时未检测到 cs2.exe，未执行 Bot install；新的 framework/build 继续保持 candidate。
