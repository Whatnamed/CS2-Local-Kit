# 2026-10-22 Controller 本地模式与 Human 共存门禁

**离线代码、事务与 Controller 发布包已完成；真实增强联合模式仍被兼容性门禁阻断。**
没有制作或认证可安装到当前 build 的 Bot + Human 包，没有真实安装 Bot，也没有启动 CS2。
早期 v1.3.0 的一次关键绑定检查达到停止条件；未找到身份可信、可在当前 build 安全交付的低开销旧版。
本记录是本次交付证据，不替代正式 runtime locks 或实机验收。

## Git 与整合

- 起点主工作区：`E:\Projects\CS2-Local-Kit\CS2-Local-Kit`，`main@e4fd3d134f890c9d07e5d6be2120ac1d00cfc675`，clean。
- 用户所指 `temp/prompts/bot-toggle-human-20261022.md` 起初不存在；实际匹配的 Plan 是 `temp/prompts/cs2-local-kit-mode-switch-next-2026-10-22.md`。已按该 Plan 执行，并在本任务 worktree 保存请求文件名副本。
- 新 worktree：`E:\Projects\CS2-Local-Kit\wt-bot-toggle-human-20261022`，branch `codex/bot-toggle-human-20261022`。
- 先保留提交整合已验收的 `codex/bot-ui-20261020@a24a2ed98da6b4e990b9880029bb53d0d69342fc` 与调查分支 `codex/bot-v144-20261022@5759c98e737f97dca13355cc20ff6ea161d84329`。
  UI 分支 fast-forward 后合入调查分支，形成 `0ffdfb0eb3f891e99d0a263fc85b7583db7e5baf`；基线 188 项测试通过后，主工作区 fast-forward 到该提交并 push `origin/main`，远端 SHA 已核对。
- 本轮新实现留在开发分支供远端复审，不自动合入 main。事务提交 `fa1d542`，Controller/Core 提交 `65798bf262c7e6dd490b0bc2cf5dcf1816ee78af`；发布包绑定后者，后续证据提交不改变包内代码。

既有 [Bot UI 记录](2026-10-20-bot-ui-integration.md)和 [v1.4.4 停止记录](2026-10-22-bot-v144-retest.md)均保留历史定位。没有重开 v1.4.4 修复，也没有改正式 runtime lock。

## 当前机器与联合阻塞

只读的真实服务输出见 [actual-readiness.json](2026-10-22-bot-toggle-human/actual-readiness.json)：

- 当前 CS2：`1.41.9.1 / ClientVersion 2000934 / BuildId 26087496`；Steam StateFlags 为 4，目标 build 一致，下载与暂存完成；CS2 未运行。
- 旧 install ID `45ca2c5899234180aad9b8927a338862` 仍为 `restored`，没有活动 marker 或未恢复事务；未对这个 ID 再执行 Restore Apply。
- MetaMod 1473 / CSS 1.0.376 / patched InventorySimulator 及已安装 gamedata 的文件身份匹配。**这只证明文件身份，当前 build 的 Human 游戏内呈现仍未重新验收。**
- 白名单 e18c029 隔离候选锁定 `2000933 / 25966348`。Controller 显示“候选 build 已过期”，不允许 preview/install；即使 build 相同，旧隔离策略也不能作为“增强人机 + 我的饰品”启用。

有限共存检查见 [coexistence-preflight.json](2026-10-22-bot-toggle-human/coexistence-preflight.json)。BotRandomizer 的选人及写入路径限制 `IsBot: true`，Human 主 partial 的 reload loop 排除 Bot；但两者都注册 GiveNamedItem pre-hook，玩家筛选不能证明共享原生 Hook 安全。
旧 Bot 包使用 MetaMod 1481 / CSS 376，Human 基线为 MetaMod 1473 / CSS 376；精确 API 文件身份已记录。RayTraceImpl 仅有空目录 scaffold，实际没有 DLL，候选也不部署 RayTrace。
当前 build 缺少 Bot/native 和共享依赖的联合兼容证据，因此没有生成真实 `humanPolicy: preserve` 候选，没有通过修改状态字段绕过核查。

## 可验收的 Controller 与事务能力

沿用现有「人机」页、`BotExperimentService`、`BotExperimentGuard` 和 Python 事务引擎：

- 显示“普通人机 + 我的饰品”与“增强人机 + 我的饰品”两个离线本地目标模式；当前模式来自磁盘/事务，保存的偏好不会伪装为安装成功。
- 显示当前/候选 build、ref、manifest、具体阻断原因、预览清单、进行中/成功/失败/需恢复状态、精确 install ID 和备份目录。
- 仅对通过门禁的固定联合候选开放启用；完整 bundle 校验后调用原事务，确认后 Apply 内再次 preview；结束后重读磁盘/记录并运行 Diagnostics。成功 stdout 不能单独证明安装成功。
- 保留旧固定隔离包的 Diagnostics / Restore 能力。活动事务、marker 缺失、损坏或多重记录不会按“最近备份”猜恢复对象。
- 联合策略保留 Human 插件、fixture、gamedata 和私人 preset。活动事务期间冻结 Human Apply/Restore 与 MetaMod 启动修复，先精确恢复普通模式再修改饰品，保持原文件身份和恢复边界。
- `-PreserveHumanCosmetics` 是原事务的显式扩展，必须匹配候选策略；联合 manifest 需要完整 Human preimages、精确 reviewed build、共存证据 digest 和经核查的其他隔离模块身份。未知模块或任意 drift 拒绝。
- 执行前和逐项修改期间检查 CS2 关闭及 Steam manifest 稳定。遇更新变化时保留救援记录，不覆盖新文件、不放宽 hash，也不靠手动复制恢复。

合约与将来显式入口见 [事务 README](../../scripts/bot-experiment/README.md)。现有 builder 仍只组装历史隔离包；本次没有把旧包重新标注为联合包。
模式 UI 和事务支持可以独立复审接受，**真实 Bot + Human 游玩能力及性能尚不可验收**。这些模式也不承诺官方联机支持。

## v1.3.0 单次可行性检查

在限定的 `E:\CS2MOD` release / backup / diagnostics 与 Downloads 历史包信息中，未找到最初六月使用时期的原始 DLL/hash 证据。
本机 legacy Git 中的 source object 与官方 [v1.3.0 tag](https://github.com/ed0ard/CS2-Bot-Improver/releases/tag/v1.3.0) 核对为 `1ceb47b9c6fa837f1656e2772763e2ab7c408158`，它仍只是时间线候选，不能证明当年实际安装过它。
来源、官方 Windows asset 的 metadata-only digest、局部 source archive digest 见 [v130-provenance.json](2026-10-22-bot-toggle-human/v130-provenance.json)；未下载或加载该 Windows release DLL。

只检查 BotAI 的 39 项 Windows patch 定义和 Aim 主绑定，共 40 项。使用现有 PE executable-section scanner 对当前 `server.dll`（SHA256 `05e6f04972bb2983f463c08824c3133626e0703e881443589288b68e200acc4d`）做一次只读扫描：

| 结果 | 数量 / 明确问题 |
| --- | --- |
| Unique | 24，其中 `OnAudibleEvent_GlobalHearRange` 的 expected original bytes 不匹配 |
| Missing | 15，包括 `BotAimImprover/PickNewAimSpot` |
| Ambiguous | 1：`AttackState_CanStrafe_jne` 匹配 19 处 |
| 旧硬编码 offsets | `0x59A4 / 0x5A10 / 0x5A14 / 0x1298` 尚无当前 layout 证据 |

完整 [scan](2026-10-22-bot-toggle-human/v130-critical-scan.json) 和 [summary](2026-10-22-bot-toggle-human/v130-summary.json) 保存各 source hash、pattern、位置与原字节。
大量关键绑定失败已经排除“小而明确”的修复路径，**立即停止**：没有重新识别 Hook、修补 offsets、继续逐组件 ABI 审计、编译旧版、制作候选或运行 FPS A/B。
不能由旧版本号或静态 unique 数量推出今天的性能或稳定性，未找到可信低开销旧版。

## 离线验证与发布

| 验证 | 结果与范围 |
| --- | --- |
| .NET Core/UI | 198/198 PASS，见 [log](2026-10-22-bot-toggle-human/dotnet-tests.log) |
| 新联合事务 | 8/8 PASS，Normal→联合→Normal 重复往返、四阶段失败自动恢复与硬中断、恢复中断续做、Human drift、损坏 backup、未知模块、游戏更新、公开 PowerShell wrapper |
| 旧隔离事务回归 | 32/32 PASS，见 [log](2026-10-22-bot-toggle-human/legacy-transaction-tests.log) |
| 原生 WPF Release | PASS，见 [ui-smoke.json](2026-10-22-bot-toggle-human/ui-smoke.json)；目标选择不改变实际模式，过期候选的 preview/apply 禁用 |
| Release 清单 | 独立核对 404 个 payload 文件与完整集合全部匹配，private data 检查通过 |
| 真实环境保护 | 前后 hash/type/inventory 比较无变化，见 [protection-verification.json](2026-10-22-bot-toggle-human/protection-verification.json) |

关键可复现测试入口：

```powershell
dotnet test tests/CS2LocalKit.Core.Tests/CS2LocalKit.Core.Tests.csproj -c Release
python -B scripts/bot-experiment/Test-BotExperiment.py CoexistenceTests
python -B scripts/bot-experiment/Test-BotExperiment.py TransactionTests
```

使用本机已有 .NET SDK 10.0.401、离线 NuGet cache、PowerShell 7 和 Python；未更改机器级工具链配置。
原生窗口验证使用隔离 app-data 与合成的公开候选 build/ref header，没有复制私人 fixture 或把合成包当真实候选。实际 readiness 另从真实 `E:\CS2MOD` 只读获取。
Computer Use Sky runtime 不可用，改用限定本任务 Controller 窗口的 Windows UI Automation / PrintWindow；测试实例均已结束。
窗口截图：[普通模式](2026-10-22-bot-toggle-human/controller-normal.png)、[增强模式被阻断](2026-10-22-bot-toggle-human/controller-joint-blocked.png)。

发布目录：`E:\CS2MOD\releases\controller\controller-bot-toggle-human-20261022-65798bf`。
可执行文件：`payload\CS2LocalKit.App.exe`，self-contained `win-x64`；源码 HEAD `65798bf262c7e6dd490b0bc2cf5dcf1816ee78af`。
Release manifest SHA256：`dc97d3c0e4de70a699ca0f49df89d1731d3ce39919aaba657e83b7710d76c6ef`。
完整 [release manifest](2026-10-22-bot-toggle-human/controller-release-manifest.json)、[publish log](2026-10-22-bot-toggle-human/controller-publish.log)、[验证摘要](2026-10-22-bot-toggle-human/validation-summary.json)与[证据校验表](2026-10-22-bot-toggle-human/evidence-sha256.json)一并归档。

真实游戏的 addons/cfg、五项 binary、gameinfo、steam.inf/Steam manifest，以及 `E:\CS2MOD` 的全部 presets/app-data/backups 和仓库 runtime locks 前后相同。
root-level 私人 preset 不在任何写入路径，未打开其内容；发布物不包含私人预设或游戏备份。

## 恢复入口与最小下一步

当前没有活动 Bot，**不需要恢复，也不应重跑已 restored 的旧 ID**。将来有效事务的恢复入口仍在「人机」页：选普通模式 → 检查恢复条件 → 按原 install ID 明确确认恢复。
CLI 救援使用原候选包内 `tools/Invoke-BotExperiment.ps1 -Action Restore -InstallId <exact-id> -BackupRoot <original-root>`，先零写入 preview，通过后才加 `-Apply`。任何 live/backup drift 都保留记录并停止，不能改用旧 Human restore 或手动覆盖文件。

本轮无需用户进游戏。下一步先取得**适用于当前 build、共享依赖与恢复门禁均通过的联合候选**，再在后续明确授权的一次受控安装后短时游玩，集中看 Bot、Human 饰品、FPS 与是否闪退。没有这种候选前不安排游戏测试；不追加组件 A/B 或长时间跑分。
