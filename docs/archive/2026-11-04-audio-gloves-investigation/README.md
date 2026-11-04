# 游戏音频重复噪声与 Human 手套显示调查（2026-11-04）

调查基线：`codex/compat-14195-20261103@50cf838f5ce54a3737763df680aeb4c1a9f3900f`。本报告记录这台机器、这些会话的证据，不替代 canonical 定义，也不把调查假设升级为已确认缺陷。时间均为北京时间（UTC+08:00）。

## 1. 结论与停止点

| 对象 | 状态 | 结论 |
| --- | --- | --- |
| 11 月 4 日第一场持续重复噪声 | **尚无法确认根因** | **高置信故障域判断**：CS2 会话内的音频生成、解码或输出流状态异常，比游戏主线程持续阻塞更符合现有证据。尚不能区分 Valve 音频内部、Windows 渲染端交互与插件产生的异常声音事件。 |
| 数日前“先声音异常、后无响应” | **尚无法确认** | 找到 11 月 1 日的 AppHangB1，可作为候选历史事件；用户不能确认日期，不能将其直接认作该次故障，更不能与最新一次合并归因。 |
| 四份现存 CS2 crash dump | **已定位发生阶段** | 均为进程退出时 CSS native 模块 `FAST_FAIL_INVALID_ARG`。今晚的 dump 属于没有报告噪声的第二场，不能用它解释第一场噪声。CSS 内部的具体析构对象尚无匹配 PDB 证明。 |
| 每次开局、仅本人第三人称手套异常 | **显示层高置信推断；根因尚无法确认** | 持久装备数据存在；问题集中在展示阶段的 Human wearable/model projection。两次延迟刷新是可疑时序，但没有展示现场的 entity、item 与模型数据，不能认定某个 Hook 失效。 |
| 偶发换边后局内手套缺失 | **尚无法确认** | 不能归入“纯展示无害”。插件刷新遗漏和游戏原生客户端刷新问题均有依据；现有上游修复已经安装。 |

**不提交运行时代码修复，不升级组件，不 Install/Restore，不改游戏文件、Steam 启动项、Windows 设置或私人配置，不自动启动游戏。** 本次仅提交报告与脱敏证据。纯开场展示异常暂不值得引入额外模型 Hook；偶发局内缺失只在能区分服务端数据与客户端呈现后再决定修复。

## 2. 当前真实安装

读取了 `AGENTS.md`、PRODUCT-SCOPE、ARCHITECTURE、UPSTREAMS、LOCAL-WORKSPACE、MANUAL-ACCEPTANCE、COMPATIBILITY-WATCH、CONTRIBUTING，以及实际磁盘与事务记录。

- 主工作区：`E:\Projects\CS2-Local-Kit\CS2-Local-Kit`；分支 `codex/bot-e18c029-human-20261022`，HEAD `88ce715bca4f2c1cfaa9ffddb576b03f0df22233`，开始时干净。
- 独立调查 worktree：`E:\Projects\CS2-Local-Kit\wt-audio-gloves-20261104`；分支 `codex/investigate-audio-gloves-20261104`，从上述审查基线创建，开始时干净。
- 实际 CS2：Steam public，`1.41.9.5 / ClientVersion 2000940 / build 26393695`，`StateFlags=4`；路径 `E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive`。
- 实际活动整合事务仍是 **Low performance** `177e9652205d4d75a4cda34d76934547`，manifest `fc09b409160d071a60a3d013fbb89d108d704ff4b65731dff96626823f006012`。**不是** `bot-daily-14195-557eea6`，也不是历史 Medium `cf73...`。
- 父层 marker 仍指向 Bot `b613a8c709c7440998346cb802bb790f`、compat `5808afffdc44440ab3f0de2159cdb436`、Nades `9f9849ca97804c2dbd7b1307bba4580c`。本次未修改这些层；没有把“撤回 Low”理解成“原版 CS2”。
- 重新核验活动记录的 **685 个文件 postimage**：680 个相同；5 个差异全部是 retained 的四个游戏 DLL 与 `steam.inf`，精确匹配已审查的 14195 变更。未发现插件文件漂移。详见 [evidence.json](evidence.json)。
- 实际组件为既有 MetaMod git1481、CSS 1.0.376 的本地 14192 native backport、Bot Improver e18c029 组合及选择性性能补丁、Aim/Nades 适配、LocalKitMatchSetup、Human InventorySimulator 3.3.0+C1.1。不能用 lock 中早期 Bot baseline 的版本代替这套实际安装。
- 两场 CSS 日志均确认加载 BotAI、BotAimImprover、BotBuy、BotControllerImpl、BotHiderImpl、BotRandomizer、BotState、InventorySimulator、LocalKitMatchSetup、NadeSystem；native 日志确认 BotController `0.8.0-local.perf14194`、BotHider `0.6.0@9a77fba`。
- 实际 Steam 命令行：`-steam -insecure -novid +exec cosmeticslab_c1`。是本地客户端/listen-server 场景；现存日志地图为 Dust2。Nades 的 `Mode=normal` 是道具策略，不能解释成 Controller 的“普通人机”模式。
- 调查时 CS2 与 Controller 均未运行，Steam 在运行。本机有多份 Controller 发布目录；无法仅由目录存在断言本次使用了哪个 App exe。14195 Controller 的可用发布记录不等于它已迁移游戏安装。

相比 14194，游戏 binary 已更新；当前插件仍是原实际安装的二进制。14195 候选里的 runtime payload 本就复用此前文件，但候选本身未安装，不能把本次问题称为“安装了 14195 候选导致”。既有 123 项签名与 43 个字段偏移的静态证据仍可对应当前同一组游戏 hash；本次没有重复扫描，更不从静态通过推出声音或渲染正确。

## 3. 声音异常

### 3.1 用户事实与还原时间线

用户确认：今晚启动两次；第一场打约四五回合后声音变成持续、间断重复噪声；**画面、操作、Bot 都正常**。有线耳机。噪声发生后尝试拔插、切桌面、调整声音设置，均无效；退出 CS2 后消失，第二次启动恢复。旧事件的准确日期未知，曾怀疑 Windows 语音输入或开麦，后来无响应并关闭。

| 时间 | 证据与含义 |
| --- | --- |
| 11-01 19:27:53–19:33:01 | Steam PID 27320。19:32:57 WER AppHangB1，19:32:59 Application Hang 1002；Steam exit `0xCFFFFFFF`。仅为旧故障的候选会话，未建立对应关系。 |
| 11-01 21:57:45；11-03 22:33:03；11-04 00:18:55 | 另三次 Application Error，CSS native `0xC0000409`、RVA `0x238afc`，各有 dump。 |
| **11-04 21:50:57** | 第一场开始，PID **25716**；21:51:07 开始加载 native Bot，21:51:12–15 CSS 与十个插件加载完成。 |
| 21:53:16、21:54:26、21:55:17、21:57:07 | Nades 回合结算日志持续产生，投掷累计 5、16、27、36，没有 projectile failure；这不是 FPS 测量，但与“服务器持续卡死”不符。确切噪声起点没有日志。 |
| 21:57:17 | Audio/PlaybackManager 两条 23：释放独占/卸载资源标志均 false；未给出 CS2 PID，不能据此认定游戏切换了独占模式。 |
| 21:57:18 | FxSound.settings 的最后写入时间，当前保存 `power=0`。只能证明当时写过设置，不能恢复写入前的开关或路由。 |
| 21:57:41–42 | ShellExperienceHost 的短音频流；Audio/Operational 六条 65，三组 NVIDIA 输出端点分别经历 state 8→4（unplugged→not present）。不是 Realtek 端点。 |
| **21:58:00–21:58:21** | 第一场 map-ending、native unload，然后 Steam 不再跟踪 PID 25716，exit **1**；没有该 PID 对应的 WER crash、hang 或 dump。不能把 exit 1 标成“异常时崩溃”。 |
| **21:58:34–22:24:25** | 第二场 PID **25628**。用户报告重启后声音正常。22:24:12 native unload，22:24:13 CSS fast-fail，22:24:21 dump，22:24:25 Steam 移除 PID。 |

**时序校正：21:57 的设备变化与 FxSound 设置写入位于用户故障后排障期间，不能再用它们证明噪声由拔插、设备切换或 FxSound 操作触发。** 它们也不是音频驱动崩溃记录。

### 3.2 Dump：确实存在故障，但不在声音现场

用本机已有 WinDbg/CDB，离线读取 `%LOCALAPPDATA%\CrashDumps\cs2.exe.{8780,26320,22992,25628}.dmp`，检查异常上下文、主线程栈；最新 dump 另检查模块与所有线程的简栈。未 attach 或启动 CS2。

四份均为 `0xC0000409`，subcode **5 / FAST_FAIL_INVALID_ARG**，fault 在相同 CSS native RVA `+0x238afc`。不能把 Windows 的通用“security check failure or stack buffer overrun”文字直接解释成已证实的栈溢出。最新主线程栈结构是：

```text
counterstrikesharp.dll + 0x238afc  (int 29h, invalid-argument fail-fast)
counterstrikesharp.dll 内若干 native frames
ntdll!LdrShutdownProcess
ntdll!RtlExitUserProcess
CS2 退出路径
```

其余三份同样有 `LdrShutdownProcess`。日志 unload、Steam 退出时序与栈相互印证：这是反复出现的**退出期 CSS native 故障**。没有匹配该自编译 CSS DLL 的 PDB；CDB 显示的 `CreateInterface+巨大偏移`/`InvokeNative+巨大偏移` 只是邻近 export 的符号归属，不能当成真实函数名。不能进一步声称某个插件 Unload、某个 logger 或析构对象已定因。

11 月 1 日 AppHang 的 WER 目录只剩 Report.wer，没有现场线程 dump。挂起签名 `d2c1` 与 HangType 不能给出阻塞线程或调用栈。现有 crash dump 均不属于它。

因此：这四次退出故障已从“声音现场证据”中分离；本次不为音频问题顺带修改 CSS 退出流程。若它不造成丢失游戏数据或退出残留，无需把它新增成当前必须修复项。

### 3.3 Windows 与音频设备

- Windows 11 Home 25H2，`26200.9168`。Application、System、ReliabilityRecords、WER、CrashDumps，以及现有 Audio/Operational、PlaybackManager 已检查。Application/System 的留存覆盖候选历史日期；两条音频日志也覆盖 11 月 1 日，不能把这两天无相关记录解释成日志根本不存在。
- 最新第一场附近未见 audiodg crash、Audio 服务崩溃、显示驱动 TDR、WHEA 或与故障同时发生的驱动/系统安装事件。Reliability Monitor 的数据源也区分出了上述 AppHang 和退出期 crash。**无记录不排除驱动/端点级故障。** GlitchDetection 未启用；本次未开启它或其他诊断通道。
- 只读 Core Audio 查询：当前默认 console/multimedia 输出 **Realtek(R) Audio**；默认 communications 输出 **FxSound Speakers**；三种角色的输入都是 Realtek 麦克风。这个快照在故障后取得，不代表故障当时或 CS2 显式选中的端点。
- 当前 active render endpoints：Realtek、FxSound、网易虚拟音频设备，均为双声道 48 kHz。Realtek 是 32-bit container / 24 valid bits；FxSound 24-bit，网易 16-bit。不能宣称已发现 44.1/48 kHz 不匹配。
- Realtek `PKEY_AudioEndpoint_Disable_SysFx=1`，表示其对应的系统 APO effects 被禁用；注册的 Realtek System Effect 仍存在，两者不矛盾。该属性不能代表 FxSound、其他端点或整个处理链的状态。[Microsoft 属性定义](https://learn.microsoft.com/en-us/windows/win32/coreaudio/pkey-audioendpoint-disable-sysfx)
- Realtek driver `6.0.9239.1`，NVIDIA HD Audio `1.4.6.3`，FxSound device driver `14.1.0.0`；FxSound app 日志版本 `1.2.13.0`。驱动声明日期不是实际安装日期，不能仅因旧日期认定故障原因。
- 查询时 FxSound 进程未运行，保存状态 power=0；此前运行/是否经过它的虚拟路由不能由此排除。蓝牙耳机端点当前不连接，且用户确认这次用有线耳机，没有此次蓝牙 profile 切换的证据；没有发现 active USB headset endpoint。
- 没有取得故障时的 CS2 endpoint ID、实际流格式、空间音效状态与独占使用状态；现有记录也不能完整重建默认设备历史。未用猜测的注册表位来补写这些结论，未打开设置对话框抢占用户操作。
- CS2 microphone consent 最后记录为 11-03 21:47:42–22:33:02，不能当成今晚第一场开麦的证据，也不能定位旧声音故障。未发现可以证明 Windows 语音输入触发这两次异常的记录。
- Steam systemaudiomanager 日志只有其自身初始化信息；未找到本次会话的 CS2 console.log。CSS 日志是服务端插件日志，不会完整记录客户端 XAudio/声音引擎错误。

### 3.4 实际代码路径，而非按名称归因

Bot 代码取自实际 Low 包随附的 corresponding-source 与原始 pin archive；性能修改以同包 performance-corresponding-source 和项目 patch 为准。没有把 build 前 source 目录中已移除的锁误当成安装代码。

| 路径 | 调查结果与边界 |
| --- | --- |
| BotController `src/features/controller/BotController.cpp`：`HookedUpdate/Upkeep/UpdateLookAngles` | 安装的性能补丁移除了旧 slot-map mutex；无 lock/replay 时快速返回。仍有 engine detour；不因“没有旧 mutex”排除 native 错误。 |
| BotController `csharp/BotControllerImpl/BotControllerImplPlugin.cs`：`OnTick` | 清理完成任务队列、更新 replay；录制保存/加载走 Task.Run。没有普通游玩必然等待文件任务的同步 Wait/Result。异常任务堆积理论上可能拉长帧，但没有此场录制命令或积压证据。 |
| BotController `src/features/voice/VoiceSender.cpp`：`SendVoiceFrame` | **有** Opus voice 网络帧发送能力，不能说 BotController 完全不涉及语音。但已查的实际组件没有正常 AI/Nades 调用它的消费方，只有导出与 API 转发；不属于自动持续播放路径。 |
| BotAI `BotAI.cs`：`Load/Unload` | 启停时 signature/expected-byte guard、修改和恢复 server AI 指令，未注册 managed 每帧扫描或设备调用。被修改的原生 AI 仍需运行时证据，静态检查不等于排除。 |
| BotState `BotState.Tick.cs`：`OnTick` | 按有效 Bot 执行 combat/movement、flash 等有限遍历；它有 CPU 成本，没有发现与此次噪声联系成立的无限循环或同步 I/O。 |
| NadeSystem `NadeSystemPlugin.Lifecycle.cs`：`OnTick` | 正常模式每 4 tick 做区域检查，每 256 tick 做清理。不是 Windows 音频设备轮询。 |
| NadeSystem `NadeSystemPlugin.Audio.cs`：`AnnounceGrenadeThrow/EmitGrenadeThrowSound/SendNativeGrenadeRadio` | **确有** `pawn.EmitSound` 和 RadioText；由 projectile/replay throw 路径调用。第一场最后计数 44 次投掷、无工厂失败，不是持续音效调用失控的证据。计数不能单独证明声音 native 调用无误。 |
| CSS `src/core/managers/voice_manager.cpp`；`src/scripting/natives/natives_entities.cpp` | 有 engine SetClientListening hook 和 EmitSound native binding；它们处理语音路由/游戏声音事件，未发现本链路直接操作 WASAPI/Windows 默认设备的实现。 |
| InventorySimulator | 喷漆有 SprayCan EmitSound，但本机 cfg 将 spray/use/changer 关闭；音乐盒写装备身份，不是自建 PCM 混音器。没有证据把它与两次噪声关联。 |
| MetaMod / 项目适配层 | loader、插件/本地对局配置与事务管理；未发现本项目设置 snd_*、音频输出设备或安装 Windows 音频 Hook 的路径。不是对全部第三方二进制的形式化无错证明。 |

第一场日志只有常规 Admin 文件缺失警告，没有 managed exception storm；回合和投掷日志持续产生。**已经削弱的是“主线程整个卡死后重放最后音频缓冲”解释；未排除的是短时 stall 触发一个之后持续存在的音频故障。** 无现场 profiler/dump，不能替 Bot 或驱动作绝对排除。

### 3.5 上游与社区对照

- Valve [#3515](https://github.com/ValveSoftware/csgo-osx-linux/issues/3515) 是更改默认设备后仍用旧设备的历史问题，2024-05-16 已关闭；[#3256](https://github.com/ValveSoftware/csgo-osx-linux/issues/3256) 是 Linux 启动无声，维护者说明原问题在 2023 年 9 月已修复。两者都不能直接解释这次 Windows 持续重复噪声。
- Valve [#3793](https://github.com/ValveSoftware/csgo-osx-linux/issues/3793) 有 Windows、Discord+特定耳机的异常声音报告；[Source-1-Games #7918](https://github.com/ValveSoftware/Source-1-Games/issues/7918) 报告包含 CS2 的 `Xaudio2 Error 88880001`/失声。当前本机既无同一错误文本，也无已证实的 Discord/设备切换触发，不能套用其修复建议。
- Steam 的 [2017 Sound Loop](https://steamcommunity.com/app/730/discussions/0/1291817208492122967/) 属于 CS:GO；商店论坛如今标题叫 Counter-Strike 2 不代表当年就是 CS2。不能把其 NVIDIA 382.33 经验用于当前驱动。
- Reddit 有 [2025-10-02 中途 audio explosion](https://www.reddit.com/r/GlobalOffensive/comments/1nvrv2z/cs2_101_update_audio_explosion_midmatch/) 与 [2026-04-28 Cache patch 失声/输出变化](https://www.reddit.com/r/GlobalOffensive/comments/1syhitf/cache_patch_randomly_losing_sound/) 报告。它们证明原生游戏存在同类故障线索；前者随后失声、后者换路由，均不是本次“持续噪声且仍能操作”的同一故障证明。
- 简中搜索检索到大量 CS:GO `snd_mixahead` 老建议与泛化驱动教程；未取得能对应当前 build、症状与时间的可靠复现或修复。未据此修改 buffer、注册表或驱动。
- CSS、Bot Improver、BotController、InventorySimulator 的相关 Issue 搜索没有建立与本次会话的直接联系。CSS [#1176](https://github.com/roflmuffin/CounterStrikeSharp/issues/1176) 是旧版本、高人数 dedicated server 帧性能报告，不是此安装的持续重复噪声定因证据。

故障域排序：**CS2 会话音频状态/与输出流交互优先；Windows 设备/处理链与游戏声音事件输入仍待区分；持续主线程阻塞较弱。** 暂无足够证据把某个 DLL、麦克风、NVIDIA、FxSound 或有线耳机定为根因。1.41.9.5 改动是否增加该风险也尚不能确认。

### 3.6 最少的下一次证据，不等待随机 A/B

无需为此反复游玩四套组件组合。用户已试过的拔插、切桌面、调声音不用重做；当前也不建议重装驱动、换整套插件或设置固定 buffer。

下一次出现时，先保留 2–3 个区分度最高的信号，再退出恢复：

1. **噪声种类与游戏活性**：用手机录 10 秒，同时拍到连续画面/操作；记时间。记录是整段声音都变成噪声，还是某个投掷/无线电在循环，菜单声音是否也坏。不要先重启或连续切换多个设置。
2. **同一输出上的另一应用**：有条件时播放一小段熟悉的本地声音，观察它是否同时失真，并截取 CS2 当前音频输出名称。其他应用同样坏更支持共享输出/处理链；只有 CS2 坏更支持其流/混音状态，但仍不是绝对判决。不要为该观察再切默认设备或重启音效软件。
3. **故障控制台内容**：截图/复制最近的音频、XAudio、device、voice、long-frame 错误文本。CSS 日志已有，不需要新增永久日志。如再次无响应，保留当时 WER；若这些仍不足，可在明确请求下用现有 WinDbg 做一次仅线程/栈的 minidump 后 detach，限定一次、短时，避免 Task Manager 默认 full dump 的大体积和长暂停。本次未执行现场采样。

这三个信号能优先区分游戏流、共享输出链和整场卡顿，比无期限“关一个插件再打一晚”有效。一次没有复发不构成排除。

四种模式对照的现有机制边界：

| 模式 | 当前是否能直接安全对照 |
| --- | --- |
| Human+增强 Bot | 当前真实活动状态，可自然观察，不需要改安装。 |
| 仅 Human | 已交付但未安装的 daily 14195 底座具有“普通人机+Human”开关；当前旧 Low 层没有同等日常开关，撤回 Low 仍留下增强父层。不能建议直接点 Restore 得到 Human-only。 |
| 仅增强 Bot | 当前联合事务要求保留 Human；历史隔离实验不是当前 build 的即用安全开关。 |
| 原版 | 当前有 MetaMod 启动入口及多层事务；未确认一个无副作用的一次性禁用入口，不能靠删 DLL 或随口给一个启动参数实现。 |

本次不迁移到底座以获取对照能力，也不拆父事务。若将来用户本来就决定迁移 daily，再用受支持的普通/增强开关做 Human-only 与 combined 的最小比较，不能因诊断先增加安装风险。

## 4. 手套异常

### 4.1 已确认的表现与范围

截图是 Dust2 的 CT 开场队伍展示。可见手部与深灰手套轮廓，不能从颜色证明“模型没加载”或“材质完全缺失”；用户确认异常是本人，Bot 没有同样问题。正常局内通常正确，但补充了少量开局/换边后局内手套未加载、重开恢复的情形。因此不能写成“第一人称完全不受影响”。CT 截图成立，T 是否同样频繁没有独立证据。

截图角落的游戏版本时间字样不作为截图采集时间或音频起点。没有在游戏中检查 actor/entity，不能宣称展示人物与 live pawn 是同一个实例，也不能反过来断言一定是独立 clone。

### 4.2 本机 Human 数据、模型和身份边界

- 实际 InventorySimulator.dll SHA256 **`3a807316195b5c0d31d7aee5490128c36a994eb8764528717097d2ad636d8489`**，等于 accepted patched artifact。补丁仅将 Load() 中的玩家枚举初始化换成本地 Inventories.Load；没有删掉手套 spawn handler。
- 正在使用的 fixture 位于 `game/csgo/addons/counterstrikesharp/configs/plugins/InventorySimulator/inventories.json`；CT/T 手套条目均非空、type/paint/seed/wear 相同，各有独立 item identity。并非某一队没有保存手套。私人预设和身份未纳入提交。
- fixture 不配 Human agent；实际 launch cfg 为 `invsim_minmodels 1`、`invsim_fallback_team 0`、`invsim_ws_enabled 0`、`invsim_require_inventory 0`，公开 API 与喷漆关闭。按 pinned 代码，minmodels>0 使用默认 SAS/Phoenix agent identity；这次不支持把问题直接归到某个私人探员组合。
- BotRandomizer 的 ApplyAgent/ApplyGloves 路径检查 IsBot；BotHider 的 Harmony postfix 是将被管理 Bot 的 false 修正成 true，不能由它推出“真人被批量当 Bot”。没有证据显示本人人物收到 Bot 身份或 Bot glove override。
- 本地 fixture 在插件初始化时读入，controller state 从本地 inventory 取得装备。本机故障不具备“必须等远程 API 返回手套”的前提；不应盲目启用 require_inventory/websocket 修它。

### 4.3 精确调用链

以下均指 upstream `fade4449aaa6d5153261c855d0cc12d7dacfefdd`，加现存 C1.1 单行启动补丁。

1. [InventorySimulator.GameEvents.cs:28](https://github.com/ianlucas/cs2-css-inventory-simulator/blob/fade4449aaa6d5153261c855d0cc12d7dacfefdd/source/InventorySimulator/InventorySimulator.GameEvents.cs#L28) 的 `OnPlayerSpawn`，只给非 Bot 调用 `HandleSpawn`。
2. [CCSPlayerControllerExtensions.cs:115](https://github.com/ianlucas/cs2-css-inventory-simulator/blob/fade4449aaa6d5153261c855d0cc12d7dacfefdd/source/InventorySimulator/Extensions/CCSPlayerControllerExtensions.cs#L115) 的 `HandleSpawn`，**第一次 NextWorldUpdate** 才读取当时 TeamNum、Inventory 和 PlayerPawn，并调用 `RefreshGloves(hasGloves)`。
3. [CCSPlayerPawnExtensions.cs:28](https://github.com/ianlucas/cs2-css-inventory-simulator/blob/fade4449aaa6d5153261c855d0cc12d7dacfefdd/source/InventorySimulator/Extensions/CCSPlayerPawnExtensions.cs#L28) 的 `RefreshGloves`，先设置 `first_or_third_person,0` bodygroup；**第二次 NextWorldUpdate** 才调用 `ItemServices.UpdateWearables()`，随后在有手套时递增并通知 `m_nEconGlovesChanged`。
4. `CCSPlayer_ItemServicesExtensions.UpdateWearables` 调用 native `CCSPlayer_ItemServices_SetWearables`。它不是一次简单的 C# defIndex assignment。
5. [InventorySimulator.Hooks.cs:79](https://github.com/ianlucas/cs2-css-inventory-simulator/blob/fade4449aaa6d5153261c855d0cc12d7dacfefdd/source/InventorySimulator/InventorySimulator.Hooks.cs#L79) 的 `GetItemInLoadout` 是 Post hook：inventory 无效、原返回 item pointer 为零或找不到玩家时保留原结果；否则按请求的 team/slot 返回 projectable econ view。`LOADOUT_SLOT_CLOTHING_HANDS` 取该队手套，econ view cache 按 SteamID/team/slot 分开。没有现场证据说明零返回分支此次发生过。
6. `RegiveAgent` 才显式调用 `SetModelFromLoadout`、`SetModelFromClass`，再刷新 gloves；本机 minmodels>0 时它直接返回，而且主要是即时 inventory refresh 的路径。不能把此函数存在解释成“每次展示模型变更后都会执行”。普通 `HandleSpawn` 不主动重设模型。

**代码支持的假设**：若展示先读取手套、随后才发生 spawn 或两次 queued update，展示可能保留旧/默认 wearable；若 engine 在最后一次刷新后又更换模型，也可能覆盖结果。**尚缺的证据**：展示实际使用什么对象、在什么时候取 item/model，以及错误那次 refresh 前后的 team、pawn handle、EconGloves 和 changed counter。单纯多等两个 tick 不一定修好独立展示 actor；两个 NextWorldUpdate 也不能机械换算成保证生效的固定毫秒延迟。

### 4.4 3.2.2 与 3.3.0 的实际关系

[3.2.2](https://github.com/ianlucas/cs2-css-inventory-simulator/releases/tag/3.2.2) 的 [#146 / 6525d02](https://github.com/ianlucas/cs2-css-inventory-simulator/commit/6525d02f081b2130a03da3e9fd777796eb91f8c6) 添加 spawn→HandleSpawn→RefreshGloves，并让 RegiveAgent/RegiveGloves 复用此方法；取代旧式 bodygroup 来回切换。相关 [#144](https://github.com/ianlucas/cs2-css-inventory-simulator/issues/144) 是 listen-server 的 T→CT 后手套回默认，维护者复现并以 #146 关闭。

对 tag 做文件级 diff：3.3.0 **保留了这套手套时序**；后续主要修改 weapon wear/material cache 与本地 inventory 文件加载，并没有再修一次开局展示路径。[3.3.0 发布记录](https://github.com/ianlucas/cs2-css-inventory-simulator/releases/tag/3.3.0)

所以不需要降回 3.2.2、重复套 #146、重新启用已不存在的老 glove-fix convar，也不能以升级整个 InventorySimulator 代替定位。

### 4.5 游戏原生问题不能忽略

- Valve [#4634](https://github.com/ValveSoftware/csgo-osx-linux/issues/4634)，2026-09-25：Windows 换到 CT 后第一回合显示默认 gloves，下一回合恢复；评论有人称已好，也有人仍复现，不能读成 Valve 已正式确认修复。
- Valve [#4556](https://github.com/ValveSoftware/csgo-osx-linux/issues/4556)，2026-07-17：特定 CT agent+手套在练习和官方比赛换边后第一回合回默认；换默认探员不触发。与本机 minmodels=1 的配置不完全相同，是原生生命周期故障的反例证据，不是本案定因。
- Valve [#4215](https://github.com/ValveSoftware/csgo-osx-linux/issues/4215)：AnimGraph2 后 CT 探员默认手套颜色变黑，T 不受影响。它谈的是探员自带手套颜色；不能用来解释本机 custom glove item 为什么没有呈现。
- Steam [Gloves not showing in demo and first CT round](https://steamcommunity.com/app/730/discussions/0/587306992930184034/) 也有首 CT 回合与 demo glove 呈现差异。2024 年大厅 glove 问题、Linux 黑手/闪烁问题与当前展示不同，不据其修改本机模型或图形设置。

这些报告使“看起来相似→必定是我们漏刷”不成立；它们也未证明本案一定是 Valve Bug。当前最准确的分类是 **Human wearable 的生命周期/客户端呈现边界**，而非已确认的数据保存、BotHider、特定手套材质损坏或 native signature 故障。

### 4.6 最短验证与有条件的最小修复位置

不改组件，下一次正常进入 Dust2，只做一个短记录：**本人开场展示 → 第一回合第一人称 → 下一次正常 spawn**；自然换边时再记录同一序列，CT/T 注明。默认手套还是 custom 手套、是否下一次 spawn 自恢复分别记录，不用为截展示画面重开多次完整比赛。

判定：

- 仅 intro 错、同次 spawn 第一人称立即正常：停在展示问题，**不加额外全局 model/glove Hook**。目前没有可靠的低风险修复可以直接实施。
- 第一人称也错且下一 spawn 恢复：刷新或客户端 wearable 失效的优先级提高，但仍须分出“服务端数据错/没通知”与“服务端对、客户端没呈现”。原生 Valve 首 CT 回合问题仍是替代解释。
- 若后续现场证明服务端刷新早于最终模型并被覆盖：最小候选只落在 `CCSPlayerControllerExtensions.HandleSpawn`/`CCSPlayerPawnExtensions.RefreshGloves` 的调用时机，复用现有 `UpdateWearables` 和 changed counter；针对本人、有效 live pawn，核对 team/pawn generation，在已证实的模型完成边界做一次有限刷新。不是每 tick 重刷，不同时 RegiveWeapons/刀、不改 Bot、比分或换边。
- 若数据实际已正确、展示/客户端 actor 没取到它：不要修改 `GetItemInLoadout` 的零 pointer guard 或增加猜测的 EconView。当前没有证据支持安全的展示专用修补；按原生显示问题处理。

目前**没有已经证实的插件代码错误，故没有可宣称会修好的 diff**。若用户后续决定继续定位，必要证据只是首个 spawn/一次换边附近的 Human team、pawn、model、glove item 与 counter 的有限快照；本次未新建诊断插件或日志系统。

任何日后最小时序改动的实机 gate 都必须包含：开场第三人称+第一人称、CT/T 自然换边/重生、原枪皮/刀身份和动画、Bot 身份及 TAB/比分不变、私人 fixture hash不变。静态测试或多跑套件不能替代这些观察。

## 5. 已排除的前提、未排除的原因与交付

已经能排除的具体前提：

- 不是“已装最新 14195 daily 候选才发生”：实际仍为旧 Low 安装。
- 不是“手套 #146 修复漏装”：当前 DLL 对应的 3.3.0 已含它。
- 不是“CT/T 某队未保存手套”或“需要远程 API 才有手套”：实际本地数据与启动方式不支持这些前提。
- 不能用 22:24 CSS dump 证明第一场噪声由 CSS crash 引起：PID、会话及退出栈均不对应。
- 21:57 的拔插/设置操作不能作为初始触发：用户明确它们在噪声之后。

仍未排除：CS2 音频内部、其渲染流与 Windows 处理链交互、插件声音事件造成的输入异常；手套的最终模型/装备刷新时序、展示路径与 Valve 客户端原生问题。**没有足够证据做责任归属或承诺某项修复有效。**

无需继续做的工作：重复完整兼容性扫描、整体升级 CSS/Bot/InventorySimulator、重套 glove #146、为纯 intro 异常增加 Tick/model Hook、拿退出期 crash 当声音现场反复修、为偶发声音进行四套长时间随机游玩对照。也不要求现在迁移安装或修改 Windows 设置。

已完成的核验是 685-file postimage 与 14195 reviewed hashes、四份 dump 的异常上下文/退出栈、Windows/Steam/插件时间线、当前只读 Core Audio 状态、对应源码与 3.2.2→3.3.0 diff、上述上游 Issue 内容。没有 runtime 改动，未运行无关 .NET 测试、FPS 套件或真实游戏验收。原始 dump、私有日志与 fixture 不上传；task-local 分析输出位于本 worktree 的 `temp/investigation/`，脱敏摘要见 [evidence.json](evidence.json)。

调查在此停止，等待用户决定是否实施进一步的有限现场定位或修复。
