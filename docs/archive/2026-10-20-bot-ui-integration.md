# 2026-10-20 Bot 实验 Controller 集成证据

实现目标为本地离线实验接入，不提升正式 runtime pin 或 accepted 状态；未启动 CS2，游戏内效果与性能仍 pending。

## 交付身份

已验收 `codex/bot-experiment-20261020@e4fd3d134f890c9d07e5d6be2120ac1d00cfc675` 已安全 fast-forward 合入并 push main，主工作区 clean。实现位于新 worktree `E:\Projects\CS2-Local-Kit\wt-bot-ui-20261020`，branch `codex/bot-ui-20261020`；本轮不合入 main。

Controller 自包含 win-x64 Release：`E:\CS2MOD\releases\controller\controller-bot-ui-20261020-46185b1\payload\CS2LocalKit.App.exe`，由 `46185b14300078db961a84a4853949f35dd950aa` 构建，404 个 payload 文件。[artifact 身份](2026-10-20-bot-ui-integration/controller-artifact.json)记录完整 release manifest 的 SHA256。后续提交仅添加交付/安装证据。

Bot upstream 保持 `e18c029a76b7515ea5cb15cb08c41f03de4c0a57`，九个组件和递归 gitlinks 不变；MetaMod 1481 / CSS 1.0.376。正式 Bot/Human locks、旧 `scripts/bot-baseline` 均无改动，旧 Medium-only 历史产物未编辑。新候选目录如下，完整 ZIP/manifest hashes 见 [profile-candidates.json](2026-10-20-bot-ui-integration/profile-candidates.json)。

| 难度 | `E:\CS2MOD\releases\bot-experiment\` 下的目录 |
| --- | --- |
| Low | `bot-experiment-e18c029-37ce3ef2465c` |
| Medium | `bot-experiment-e18c029-b45f4f543985` |
| High | `bot-experiment-e18c029-60697f7d93f2` |

每个变体仍为 105 个安装文件；相应 `profiles/<level>/botprofile.db` 来自精确 Git blob，VPK 校验结构、CRC/MD5 及嵌入的完整 DB。三个 DB/VPK 身份互不相同，manifest 与 install record 均记录所选 profile/hash；恢复 preflight 核对同一选择。安装后不能直接热改 VPK，必须先恢复再安装新变体。

三项 native、八项 managed entry 及其他 payload 保持原已验收 DLL/数据 hash。两个 API 从相同冻结 source 重新构建，产生不同文件 hash；[Controller API](2026-10-20-bot-ui-integration/BotControllerApi-verification.json) 和 [Hider API](2026-10-20-bot-ui-integration/BotHiderApi-verification.json) 对比了原包与三个新包，程序集名称/版本、类型、方法和字段签名及方法 IL 均一致。其重建日志、各项 hash 与对应源码/许可证随不可变 bundle 保留。不把 API/静态证据作为运行时兼容证明。

## UI 与事务语义

「人机」与饰品/预设/状态平级，沿用现有 WPF Theme/ViewModel。独立状态卡显示本机/目标 build、候选 digest、安装难度、精确 install ID 和 backup。进入人机页隐藏 Human preset header/footer；活动实验在 Core mutation boundary 阻断 Human Apply/Restore 和 MetaMod1473 启动修复，状态解释为实验隔离/占用，不把 Human 原配置当作损坏。

Low/Medium/High 是安装前选择。Mixed/Head/Body 与五档道具仅是待执行命令 `bot_aim ...; bot_nades ...`；复制成功提示须在本地控制台执行，未声称游戏已应用。Head 的 Windows listen-server 行为仍待实机验证。Bot 偏好位于独立 `app-data/bot-experiment/preferences.json`，不写 Human preset。

Controller 只接受编译进程序的三项精确候选白名单，核验 manifest/bundle 的全部文件及路径、Python 3.12+ 后，以 `ProcessStartInfo.ArgumentList` 异步调用包内 `tools/Invoke-BotExperiment.ps1`。不拼 shell、不执行用户输入脚本、不在 C# 写游戏文件。安装/恢复按钮需先预览再独立确认，每次 Apply 内再次做完整 preflight；服务与 UI 防重复操作，运行中禁用文件修改。

MetaMod 两项 overlay、Bot、Human/其他执行插件隔离、gameinfo 和 profile 同属原实验事务。恢复按精确 install ID/BackupRoot，不猜最新记录；缺 marker 的未恢复历史和歧义记录也阻止 Human 写入。漂移/坏备份拒绝写入，没有 Force。stdout/stderr 与 exit code 保存在独立 `diagnostics/bot-experiment/`。

## 验证

- [32 项 Python 事务测试](2026-10-20-bot-ui-integration/transaction-tests.log) PASS（原 28 项 + profile 回归），132.840 秒。三种难度分别通过 roundtrip、四阶段可捕获失败回滚、四阶段硬中断续做及重试；覆盖 VPK/DB/record 不一致、配置/hash 漂移拒绝等原有保护。
- [188 项 Core/UI 测试](2026-10-20-bot-ui-integration/dotnet-tests.log) PASS，含原 173 项与 15 项新增 case。覆盖候选白名单/路径/脚本 hash、运行中拒绝、参数列表、Apply 前重做 preview、服务并发、精确恢复 ID、UI 选择/偏好/确认取消、漂移与中断状态、Human mutation guard、歧义历史。
- 三个新候选各使用**包内公开 PowerShell 入口、全部 105 个实际 payload**在合成基线安装与恢复；[Low](2026-10-20-bot-ui-integration/actual-candidate-Low-test.json)、[Medium](2026-10-20-bot-ui-integration/actual-candidate-Medium-test.json)、[High](2026-10-20-bot-ui-integration/actual-candidate-High-test.json) 均 PASS，安装/恢复 preview 零写入，恢复后文件路径/类型/全部内容 hash 一致。未执行 DLL。
- 原生 WPF Release 实际窗口 smoke：导航、三档难度选择、[15 种 aim/nades 命令复制](2026-10-20-bot-ui-integration/ui-command-matrix.json)、默认 Medium 完整真实安装 preview、独立 Human footer 隐藏均通过。[窗口截图](2026-10-20-bot-ui-integration/controller-bot-ui-preview.png)。Computer Use runtime unavailable，使用 Windows UI Automation 与 DPI-aware、仅针对本 Controller 的点击；未模拟游戏输入。
- 构建/restore 使用现有 .NET 10.0.401、本地 NuGet cache、PowerShell 7、Python 3.12.10，未安装新开发依赖。

## 实际安装与保护核对

**已执行一次真实 Medium 安装，保持 installed 供用户自行实机测试；未启动游戏，未执行真实 Restore Apply。** 代码已先 push 至 `46185b14300078db961a84a4853949f35dd950aa`，然后执行安装。

- 当前 CS2：1.41.9.1 / ClientVersion 2000933 / BuildId 25966348，五项 binary hashes 与冻结候选一致；1473/CSS376/Human preimages、完整 module inventory 和 startup 检查通过。安装前全保护快照 6,711→6,712 个文件，无游戏/原有私人文件变化，仅新增项目自己的 Bot 偏好文件。
- 候选：`bot-experiment-e18c029-b45f4f543985`；ZIP SHA256 `cbf7e2d030fad3ad7cbff41359a294b4c0eaebef37bf3415e13cc7a0e52730c8`；manifest SHA256 `e6f028042b7fc778d62df9a17211ea9701b6e8bab034da4b3170e8331b3483b4`。
- 唯一 install ID：`45ca2c5899234180aad9b8927a338862`；backup：`E:\CS2MOD\backups\bot-experiment\45ca2c5899234180aad9b8927a338862`。record schema 1，完整原记录和 preimages 保存在此目录。[安装摘要](2026-10-20-bot-ui-integration/real-installation.json)可复审，不提交含私人 baseline 清单的完整 record。
- 同一事务：created=92（包含 marker）、overwritten=14、modified=1、isolated=3、retained=515。隔离 InventorySimulator、PlusMatchCoordinator、RayTraceImpl 的执行插件树；其原文件/配置进入受保护备份，不更改 Human preset/fixture。MetaMod 两个 DLL、Bot 和 gameinfo/profile 全部 postimages 验证通过。
- 安装后的公开 CLI Diagnostics 与精确 Restore preview PASS，包含 installed images、所有备份和 retained files。[真实保护核对](2026-10-20-bot-ui-integration/protection-postinstall.json)预期/实测均 6,797 个文件，unexpectedChanges=[]；正式 locks、Human fixture、私人预设和所有既有 backups 原样保留。新增实验 backup 和 Bot 自有偏好单独列明。
- [原生 Controller 安装态验证](2026-10-20-bot-ui-integration/ui-installed-verification.json) PASS：正确 ID/Medium、难度不可热改、Human Apply 和 MetaMod1473 repair 禁用、GUI 精确恢复预览通过。[安装态截图](2026-10-20-bot-ui-integration/controller-bot-ui-installed.png)。测试窗口已关闭；未启动 CS2。

持久诊断/恢复入口：`E:\CS2MOD\diagnostics\bot-experiment\installed-45ca2c5899234180aad9b8927a338862\`。其中 `Restore-ExactBotExperiment.ps1` 固定包内入口、install ID 和 BackupRoot，已测试默认零写入 preview；只有以后用户显式加 `-Apply` 才恢复：

```powershell
# 先关闭 CS2；默认只预览
pwsh -File 'E:\CS2MOD\diagnostics\bot-experiment\installed-45ca2c5899234180aad9b8927a338862\Restore-ExactBotExperiment.ps1'
# 全量 preflight 通过后，用户决定恢复时才执行
pwsh -File 'E:\CS2MOD\diagnostics\bot-experiment\installed-45ca2c5899234180aad9b8927a338862\Restore-ExactBotExperiment.ps1' -Apply
```

不使用旧 Bot/Human Restore 处理该框架 overlay，不删除 backup 或 marker。漂移/冲突时保留原记录，工具会拒绝写入。当前无未解决工程 blocker；安装文件身份通过不等于真实 DLL 加载、aim/道具或性能通过。

## 剩余用户实机验收

仅在本地离线条件自行启动游戏，确认 MetaMod1481/CSS376、三 native/八 managed/API 成套加载与日志无错误。执行 UI 复制的命令，逐项核对 Head/Body/Mixed 和道具策略；检查 movement/买枪/nades、TAB/score、死亡重生/换边、异常 restart/crash。固定地图、人数和设置比较 vanilla→candidate→vanilla 的 FPS/1% low/frametime 与卡顿。结束后停机，按精确 ID 检查恢复并 Apply，核验原 MetaMod1473/CSS/Human/私人文件，并人工复验 Human 呈现。Low/High 的游戏内难度表现亦未验收。Rush、完整 Panel、Steam 启动设置不在本轮范围。
