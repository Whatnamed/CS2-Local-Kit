# 2026-10-20 Bot 离线兼容验证

## 决策

**可以准备独立、可回滚的实验候选；当前 accepted 环境不能直接安装/运行它。**
本轮没有发现仍需等待上游修复的 signature、Aim offset 或 Windows 构建阻塞。
这只是进入实验准备阶段的结论，不代表 package 已制作、回滚已验证、运行时兼容或 performance gate 已通过。

候选是实时核实的 `ed0ard/CS2-Bot-Improver main=e18c029a76b7515ea5cb15cb08c41f03de4c0a57`，
使用它的精确 gitlinks，没有拼接组件各自的浮动 latest，也没有修改第三方源码。
决策机器可读记录：[decision.json](2026-10-20-bot-offline/decision.json)。

## 本机与保护边界

- 基线 `origin/main=fa4367730d8bd6e79b44c5980a81718b3c531b8c`，fetch 后与本地 main 无分叉，起始 clean。
- 调查 worktree：`E:\Projects\CS2-Local-Kit\wt-bot-offline-20261020`，branch `codex/bot-offline-20261020`。
- 本机 `steam.inf`：`1.41.9.1 / ClientVersion 2000933 / SourceRevision 11133601`；Steam manifest：`BuildId 25966348`。
  相对 10 月 15 日的 `2000932 / 25947352`，版本标识已变；重新计算的 server、engine2、client、schemasystem、cs2.exe 五项 hashes 却全部相同。
  不把版本号变化描述为这些 binary 已变化，也不因此跳过本轮重扫。
- 实装 MetaMod/CSS/patched Human DLL 及当前 Human gamedata 七项身份匹配原记录；MetaMod startup entry 为 1。
- 没有安装/启动 CS2，没有修改 accepted Human runtime、游戏文件、用户配置或 `runtime/*.lock.json`。
  [local-facts.json](2026-10-20-bot-offline/local-facts.json)、[保护验证](2026-10-20-bot-offline/verification.json)。

## 精确源码与构建组合

| 组件 | umbrella gitlink |
|---|---|
| BotController | `582e9132c10b9729e3cce8f0670a9273eb7a49bd` |
| BotHider | `9a77fbaf1ee106ec45a451f482e64f26127f2f21` |
| BotVision | `9884848b340434c906ecd1b0f7a0d1d5e8ca140c` |
| BotAI | `0c46c6757b03e74eb1b42219a9786101c93b954f` |
| BotAimImprover | `d796572b6d258b938a9d8db3c5d4c802a46049f7` |
| BotBuy | `11884e0564b8b57bee5f4729a5fca781bd5e6749` |
| BotRandomizer | `1c7c3ebad4b984bf8c4c75511b71cc109ab820bd` |
| BotState | `9e9f8f1b2fd2f04b216c7398bdcb4736327946b5` |
| NadeSystem | `009ce03005af68ba68dae62967bddc8e92ad172c` |

三项 native 使用同一 `cs2_sdk b973e57b5d2f2c8c258e7bdf6a48d97553d357ab`，
其精确 SDK 为 `s2sdk b85301c79b6320f3cbf7d519dd334795c227426b`、
MetaMod `021f06b67e796b0a578d0c7bc0baa84a97fb3d43`（tag `2.0.0.1481`）。
完整递归 refs、来源与依赖说明见 [provenance.json](2026-10-20-bot-offline/provenance.json)。
源码只放 ignored `temp/`，未 vendor 进项目。

## Windows signatures 与 patch bytes

使用仓库现有只读 PE executable-section scanner，对精确源码重新提取输入；JSON 保留来源文件 hashes、pattern、RVA、file offset、expected/actual bytes。

| 范围 | 结果 |
|---|---|
| 候选自身全部 Windows native JSON / managed 内嵌 bindings 与 patches | **81/81 unique** |
| installed CSS 全部 Windows signatures，独立报告 | **28/28 unique** |
| BotAI patch targets | **40/40 unique，40/40 expected-original PASS** |
| BotState patch targets | **2/2 unique，2/2 expected-original PASS** |

候选 81 条组成：Controller 13、Hider 7、Vision 9、BotAI 40、Aim 1、Randomizer 2、State 6、NadeSystem 3。
BotBuy 没有直接内嵌 Windows signature。范围不包含 disabled Linux variants、RoundDamageRecap 或 Human 插件。
本次合计 109 条；此前 107 条包含相同的 CSS 28 条及 BotState 4 条，本次 State 新增两项 FOV entry hooks。

此前 `Vision_ApproachBody_SkipSkillCheck` 的两处匹配已经由**上游**延长 pattern 解除：
`F3 0F 10 40 0C 0F 2F ? 76 ? 80 BF ? 5C 00 00 00 74 ?` 只匹配 RVA **`0x32856d`**，
patchOffset 8，原字节 **`76 33`**，满足 `76 ?`。没有本地挑选第一处或猜测性延长 pattern。

[候选完整扫描](2026-10-20-bot-offline/signatures.json)、[CSS 独立扫描](2026-10-20-bot-offline/css-signatures.json)。
唯一匹配与 byte PASS 只证明静态目标定位和原字节，不能证明 hook/patch 执行后的语义、ABI 或安全性。

## Native / schema offsets

从本机 server.dll 只读解析静态 class/field registration；类记录唯一、所有字段名可解析、offset 在 class size 内；
继承字段另追踪基类记录。没有加载 DLL 或查询 live SchemaSystem。

| Aim 使用字段 | 本机静态 schema | 候选值 | 结果 |
|---|---|---|---|
| CCSPlayerPawn::m_pBot | `0x14F8` | `0x14F8` | 一致，旧冲突解除 |
| CCSBot::m_targetSpot | `0x5994` | `0x5994` | 一致 |
| CCSBot::m_enemy | `0x5A00` | `0x5A00` | 一致 |
| CCSBot::m_isEnemyVisible | `0x5A04` | `0x5A04` | 一致 |

三项 native 源码的 40 项不同 schema 字段均找到静态依据，包括 CBot::m_pPlayer=`0x18`、
CBaseEntity::m_fFlags=`0x388` / m_iTeamNum=`0x344`、CCSPlayerController::m_hPlayerPawn=`0x8EC`、
Controller movement/buttons/weapon services 及 Vision 的属性链。
Vision 的 CBasePlayerWeapon::m_AttributeManager 来自 CEconEntity 基类，静态继承偏移为 `0xA58`；没有把未直接声明误判成缺字段。

仍然未知：native JSON 中 **45 项 configured offsets/vtable entries** 的完整私有布局/调用约定；
BotAI `m_gameState=0x5120` 和 CSGameState 内部成员没有本轮 schema 布局证明。
live schema 初始化、实际对象指针、KHook relocation、trace/烟雾/HE/shotgun 可选能力仍须运行时验证。
没有用 signature PASS 替代这些证据，也没有猜测性修改 offsets。

[schema 原始记录](2026-10-20-bot-offline/schema.json)、[configured-only offsets 清单](2026-10-20-bot-offline/configured-offsets.json)。

## 实际离线构建

- **BotController、BotHider、BotVision native Windows x64 Release 全部成功**。
- **8 个 managed entry projects 全部成功**：BotAI、Aim、Buy、Randomizer、State、NadeSystem、HiderImpl+API、ControllerImpl+API。
  BotVision 是 native-only，不存在需要配对构建的 managed half。
- 使用已有 MSVC `19.44.35229.0`、Windows SDK `10.0.26100.0`、CMake `3.31.6-msvc6`、.NET SDK `10.0.401`，
  以及精确 s2sdk 自带 `protoc 3.21.8`。虽然上游 TECH 建议 VS2026，实测现有 VS2022 Build Tools 足以完成上述构建。
- NuGet restore 只使用现有本机 cache；native 构建在源码 gitlinks 检出后离线完成，没有安装任何新工具链/开发依赖。
  源码获取曾遇 Windows 长路径问题，用命令局部 `git -c core.longpaths=true` 解决，没有改 global Git、Registry 或网络设置。
- BotState 首次因缺少 `libs/BotControllerApi.dll` 失败；按 csproj 既有 HintPath 放入本轮精确 Controller 构建的 API 后通过，
  未修改源码/项目文件。最终保留上游 `CS0414 _isFreezeTime` 一条警告。
  native protobuf 生成保留上游缺省 proto2 / unused-import 警告，未为消警修改第三方文件。

[native commands / exit codes / output hashes / logs](2026-10-20-bot-offline/native-builds.json)、
[managed commands / exit codes / output hashes](2026-10-20-bot-offline/managed-builds.json)。
生成的 DLL 和 CMake task-local package stage 只是构建输出，不是完整实验包、正式 release 或安装 payload。

## 实验准备与停止边界

最新 native 的 PLUGIN_SAVEVARS 使用 `ISmmAPI::GetDetourInterface` / exported KHook；上游
[Hider v0.5.2](https://github.com/XBribo/CS2-Bot-Hider/releases/tag/v0.5.2)、
[Controller v0.7.2](https://github.com/XBribo/CS2-Bot-Controller/releases/tag/v0.7.2)、
[Vision v0.3.1](https://github.com/XBribo/CS2-Bot-Vision/releases/tag/v0.3.1) 均明确要求 **MetaMod 1481+**。
这些 release 不是当前 main gitlinks，不能拿旧 release native 拼接本轮 managed 输出。

**本机 accepted MetaMod 1473 不满足要求。** 准备实验时必须使用独立的 1481+ framework manifest/root，
保留 accepted Human/framework 原始身份与精确恢复清单；CSS 1.0.376 与新 framework 的运行组合尚未实测。
现有 Build-BotBaseline 强制使用正式 lock，Bot install/restore 合约将 shared framework 排除在 ownership 外，
不能修改正式 pin 来绕过，也不能声称它已覆盖 MetaMod 升级回滚。本轮没有制作此新合约、修改 installer、下载/安装 MetaMod runtime 或建立实验包。

因此：源码与构建条件已具备，可以开展后续有界实验候选准备；**当前直接安装仍被 framework 与恢复边界挡住**，
它们需要独立实验准备，不是本轮发现的上游源码故障。性能改善仍无本机实测证据，原 performance gate 不变。
至此停止，不扩展成新一轮上游审计或 runtime 修复。

## 后续最小实机验收（本轮全部未执行）

1. 先完成独立 framework/文件 ownership、安装前 hashes、备份与精确恢复预检；不把 accepted Human 环境升级当实验前提。
2. Human 不加载的 Windows listen-server：确认 1481+ 与 CSS/三项 native/managed 成套加载，记录 missing signature/schema、IPC、hook 和可选功能降级日志。
3. 同地图、画质、人数、位置和固定采样时长，预热后 **vanilla → 候选 → vanilla**；记录 frame time p50/p95/p99、1% low、long frames，复核历史卡顿问题。
4. 少量连续回合检查 aim/movement/nades、Bot 人数、TAB、score、死亡/重生、回合/半场换边，无意外 restart、比分重置或 crash。
5. 精确 restore 后核对 protected hashes，并确认原 Human 环境可恢复正常加载；通过前不提升正式 Bot pin 或宣称性能验收通过。
