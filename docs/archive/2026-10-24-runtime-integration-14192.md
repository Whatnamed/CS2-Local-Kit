# 2026-10-24：当前可玩基线上的统一 Controller 整合候选

**状态：隔离准备与交付已完成；真实安装、三档游戏内验证及新安装的真实恢复尚未授权/验收。**
本记录不提升 accepted runtime pins，不把 Controller 成功、签名或合成事务当成游戏效果。

## 当前事实和保护边界

本机 CS2 为 `1.41.9.2 / ClientVersion 2000935 / BuildId 26144732`。用户在本任务开始时明确补充：Medium + Human 已能启动游玩，NadeSystem 已真实安装，烟、闪、雷、火经过实机验证。旧日期 archive 的 pending 描述是历史记录，不覆盖这次用户反馈。

本轮重新读取并核验的三层实际身份：

| 层 | 当前 install ID | 本轮处理 |
| --- | --- | --- |
| 原 Bot Medium + Human | `b613a8c709c7440998346cb802bb790f` | 保留 marker、记录、manifest、备份及其 ownership |
| 五文件兼容修复 | `5808afffdc44440ab3f0de2159cdb436` | 保留已验证 CSS/BotVision/Aim 修复身份与备份 |
| NadeSystem 修复 | `9f9849ca97804c2dbd7b1307bba4580c` | 保留 DLL `75e2573c6db9e60488a75b466468eb791fb108c1ccfd17cac3f70f3b51da8687` 和插件实际读取目录中的 48 份 JSON |

Nade 层公开 Restore 的只读预检通过；完整保护清单含 681 个当前文件/marker。私人 fixture 不进入发布包。本轮没有真实 Install/Restore，没有启动 CS2，没有修改 Steam 启动选项或原始安装记录。

## 整合行为

以 `codex/operational-closure-20261023@de5ecc6` 的 Controller/MatchSetup 为基础，选择性复用 `codex/compat-14192-20261023@cc74ad0` 与 `codex/nades-14192-20261023@18e8b78` 的源码和已验证产物，未机械 merge 分支。

新的独立整合事务保留三层父安装。完整候选均为 **110-file payload**，含当前 CSS 文件身份、BotVision 修复、固定 Nade DLL/数据库、当前构建的 MatchSetup/Core、Aim Hook 证据及各自 profile。相同文件保留在原位；当前 Medium 实际差异为 Aim DLL 与三个新 adapter/Core 文件，加独立 marker。Low/High 再切换精确 profile VPK。三档 Bot/CSS/Nade 二进制与数据相同。

Controller 使用原有「对局配置」「饰品配置」和次级「系统设置」。日常操作是选择配置后「准备对局」：完整只读预览、用户确认、需要时撤回上次整合层、重新完整预检并安装目标、提交下局请求。不会启动游戏或伪造难度热切换。系统设置的恢复只撤回**新整合 ID**，返回上述当前可玩的 Medium + Human + NadeSystem 基线，保留最新个人饰品。它不代表卸载全部增强 Bot或切换为纯原生普通模式；不得在整合层活动时直接恢复父层。

Human fixture 更新沿用原 sidecar，校验其余全部 postimages 与父备份后才能应用。切换时挂起旧 fixture、精确撤回整合层、安装新候选再移交；最终撤回后可从匹配原 manifest/基线/当前副本的 completed sidecar 再准备。原个人预设和三个父层备份不修改。

Steam manifest 的完整旧 hash 仍留在原记录中作为 provenance；修正后的兼容工具按 app/build/安装目录和 idle/完整传输核查，并保留游戏二进制、目标文件、父备份和 marker 校验。不同操作之间允许 LastPlayed 变化，写入期间原始 manifest 变化仍阻断。没有改写旧包或旧记录来接纳新文件；修正工具复用原 manifest 作为输入，其隔离测试包含元数据漂移允许及 app/build/state 漂移拒绝。

## Aim、命令与官方差异

[Bot Improver #201](https://github.com/ed0ard/CS2-Bot-Improver/issues/201) 本轮核查仍 open，维护者确认过指针问题；没有将“升级到某版本”的评论当成本机行为验收。对当前 `server.dll` 的 PickNewAimSpot 唯一入口做反汇编，确认实际访问 `Enemy=0x5A00`、`IsVisible=0x5A04`、`TargetSpot=0x5994`；PBot 继续使用已有 schema 确认的 `0x1500`。这些仍是静态证据。

Aim 沿用固定 `d796572` 源码，只补 PBot、诊断与 Head 的 AWP 例外：原 Head 分支对 AWP 仍使用躯干优先，现对全部武器使用 head-first 优先序，遮挡时仍按既有可见性策略降级。新增 `css_localkit_aim_status` 和 capability v2 证据，区分 HookCalls、controller/visibility misses、实际目标内存写入与 XYZ 读回、各选点计数、独立伤害 hitgroup 观察。命令响应或 mode 读回不增加写入成功数；首次少量采样输出与 epoch 绑定。真正 Head/Body 效果仍 pending。

NadeSystem **没有重新编译或换回旧 DLL**，保留 SmokeCreate 修复、插件目录数据路径、实际创建后的计数、Off epoch 队列取消以及 `css_localkit_nades_status` / `css_localkit_nades_points`。不把 announce 当创建，也不把零计数当实际 Off 对照。

命令的有界源码核查：

| 操作 | 真实路径和实机判据 |
| --- | --- |
| `bot_aim head/body/mixed` | 经过实际 PostHook/target 写入读回，Head/Body 同图同武器可比场景；按 epoch 保存 Hook、选点、伤害观察，不能只看 Mode |
| `bot_nades off/max` | 固定 Nade 数据库/地图点数、实际创建与可见烟/闪/雷/火；Off 用可比场景，区分 Valve AI |
| `br_reroll [all\|slot]` | 固定 Randomizer 将 slot 放入 pending 集合，下一次安全 spawn/round-prestart 才 consume 并 reroll；排队成功不等于已换装 |
| `bot_buy`、武器名 | 固定 cfg alias 写 `bot_loadout`；`bot_buy.cfg` 清空它，BotBuy 在非空 loadout 时跳过自选购买。必须核对下一回合实际武器/经济及恢复正常购买 |
| 阵容请求/命令 | 通过新 map 的一次性请求，人数、slot/UserId、persona、实际名字和队名分别读回；死亡/换边后只观察，不重置比分或重复加 Bot |

没有真实游戏进程，因此本轮未执行这些控制台命令，也未把源码检查记为运行有效。上述实机检查统一并入下方清单，不另行扩大 Bot 重开发。

[CSS v1.0.378](https://github.com/roflmuffin/CounterStrikeSharp/releases/tag/v1.0.378) 相对 376 的官方 compare 包含 49 个文件变化，除当前已补的 Windows PostThink/engine interface 变化，还迁移 s2sdk、更新 schemas/Protobuf/MetaMod 与其他 native API。核查未发现本轮已用路径必须替换现有已验证实现的依据，故保持当前 native DLL、managed API 与 SDK 修复，不全量升级。摘要和选定原始 diff 见 [css-review.json](2026-10-24-runtime-integration-14192/css-review.json)。

## 已执行验证

- **267/267 .NET tests**：包含原有产品/阵容/侧车测试及六项整合测试。验证三个父层不会被 Controller Restore、预览后备份漂移即使展示缓存有效也不能授权写入、marker 缺失阻断、Human 更新/撤回/再次准备链。
- 原 **40 项**事务回归通过；兼容修复工具 **23 项**检查通过，含新的 Steam 元数据场景。
- Medium 完整包 **29 项**检查通过：公开 PowerShell 入口、双 preview 零写入、完整 payload roundtrip、逐变更文件失败回滚、安装中断及恢复中断续做、游戏/Human/parent/未知模块/Steam 漂移拒绝。
- Low/High 各 **14 项**完整公开入口/恢复/保护检查通过；独立执行 `Medium → Low → High → Medium` 连续隔离安装，每次 VPK 与记录 profile 匹配、撤回后路径/类型/hash 完全相同。
- 当前真实安装的 Controller 服务三档只读准备预览通过，均返回保护三层基线的计划，没有执行 mutation。
- 发布版 WPF 的 **63 项交互检查、28 个非空截图**：双导航、阵容/席位/历史/自定义、草稿保持、CT/T 饰品保存/应用确认与取消、安全阻断、窄窗/DPI 矩阵。使用隐藏 HWND 与实际 WPF/UIA providers，未进行桌面点击注入。原生 popup、窗口 chrome、物理 DPI 和真实游戏联动仍待人工验证。
- self-contained `win-x64` Controller 发布；完整文件集合/hash、私人 JSON 边界检查、三个候选 ZIP 的 member 集合/hash 校验通过。具体身份见 [delivery.json](2026-10-24-runtime-integration-14192/delivery.json)。

真实源安装、三层备份、个人预设、app-data、Steam manifest 的前后比较另见 protection 证据。原始私人目录清单仅留本机 task temp，不提交。

## 最短授权入口

Controller：`E:\CS2MOD\releases\controller\controller-integrated-14192-20261024\payload\CS2LocalKit.App.exe`。

固定入口默认只读：

```powershell
$entry = 'E:\CS2MOD\diagnostics\runtime-integration-14192-20261024\Invoke-ExactIntegration14192.ps1'
pwsh -NoProfile -File $entry
```

**用户单独授权后才执行这一次 Medium 整合安装**，无需先撤回三个父层：

```powershell
pwsh -NoProfile -File $entry -Action Install -Apply
```

新 install ID 和精确备份路径由结果返回；不使用三个父 ID 代替新 ID。安装后仍由用户自行启动离线 CS2。平时通过 Controller 修改、保存和准备；Low/High 由同一 Controller 的受保护切换统筹，不要求管理安装脚本或激活指针。

撤回新整合层也需要对应授权，先用同一入口 `-Action Restore -InstallId '<new-id>'` 做只读预检，获得授权后才加 `-Apply`。未知或中断状态保留 journal 和备份，先核实精确记录，不盲目重试。

## 剩余实机验收清单

1. 授权安装 Medium 后，实际启动到离线 Mirage；确认 4+5 Bot 与真人占位、稳定启动、无明显持续卡顿，检查增强 Bot 与 Human 共存。
2. Controller 选择我方 FaZe 历史版本、对方 Spirit 历史版本；再更换历史快照、跨队自由选手与自定义名，CT/T 各开新图。核实人数、真实名字、TAB/队名、回合、死亡重生及换边，不重复加 Bot、不重置比分。
3. 同图同武器对照 Head/Body，分别留存 request/session/epoch、HookCalls、Points/实际写入与伤害观察，并实际判断瞄准差异。包含 AWP 与普通步枪，不以 mode 读回或签名 PASS 代替。
4. Off/Max 可比场景，核对 DB/MapCount 与实际创建，看到烟、闪、雷、火。Less/Normal/More 的模式/预算亦核查，不把 max 理解为必然每次投掷。
5. 实际执行 `br_reroll` 并经历安全 spawn，武器 alias 后进入下一回合，再 `bot_buy` 恢复正常购买，核查实际物品/经济；保存失败的具体日志和场景。
6. CT/T 枪皮、刀型/动画、手套、音乐盒分别修改、保存并应用，经历死亡重生/换边；增强 Bot 文件保持身份匹配。
7. 用户退出 CS2 后，单独授权 Controller 的 Low/High 真实切换，三档各进入游戏体验；再次回到 Medium。逐段记录同图同设置/人数的 FPS 与 frametime；没有逐帧源则 FPS/long frames 保持 unknown。
8. 对新安装精确 ID 的真实 Restore 单独授权后执行，核对原基线、三父层/备份、个人配置保留，再实际启动复验。合成 roundtrip 不替代这次真实恢复。

本轮不将任何一项 pending 标为通过，也不因为 Medium 原基线已可玩而提前判定整合任务全部完成。
