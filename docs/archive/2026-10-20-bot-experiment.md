# 2026-10-20 Bot 实验候选交付证据

结论：**独立、可回滚候选已准备好供后续人工实机尝试**。本轮没有安装到真实 CS2，也没有启动游戏；不声明 gameplay、性能、CSS/MetaMod 运行时兼容或 Human 渲染通过。

## Git 与产物身份

- 已验收离线调查 `f9b8a4039808f745bb3a4a029704f7804e9ea64c` 已 fast-forward 合入并 push `main`。主工作区 clean；旧调查 worktree 保留。
- 实现 worktree：`E:\Projects\CS2-Local-Kit\wt-bot-experiment-20261020`；branch：`codex/bot-experiment-20261020`，本轮不合入 main。
- 候选由 `18782f94c8bda1ae28fb61859e666abd46083465` 构建；之后提交只添加本交付证据。
- 目录：`E:\CS2MOD\releases\bot-experiment\bot-experiment-e18c029-73ce542bf7ec`。
- ZIP：上述目录同名加 `.zip`；SHA256：`b39c37f127eab4a8e5170a665ee53c505cd41be7dd7990c83e09e1432aa9f6f8`。
- `experiment-manifest.json` SHA256：`b653450b5566c4a2539e1a274c3166c22aa82526ff6cd06f83b095d2fe5eabdc`。
- manifest/record schema 均为 1；105 个安装文件、162 个 bundle 文件。完整本机 preimage manifest 随候选保留，不把用户配置或其完整清单提交 Git。

冻结 Bot `e18c029a76b7515ea5cb15cb08c41f03de4c0a57` 和九个组件及递归 gitlinks，见 [candidate descriptor](../../experiments/bot-experiment/candidate-20261020.json)。正式 `runtime/*.lock.json` 未改变。

| Native | Managed bridge / API | Native version |
| --- | --- | --- |
| BotController | BotControllerImpl + BotControllerApi | 0.8.0 |
| BotHider | BotHiderImpl + BotHiderApi | 0.6.0 |
| BotVision | native only | 0.4.0 |

另外六个 managed entry 为 BotAI、BotAimImprover、BotBuy、BotRandomizer、BotState、NadeSystem，合计八个。三项 native 和八个 managed entry DLL 复用已验收调查的 SHA256 验证输出；两个 API 从相同精确 source 使用已有 .NET 10.0.401 和本地 NuGet cache 离线重建。无新开发依赖安装，无第三方源码修补。各 DLL hash、项目引用版本见 [delivery.json](2026-10-20-bot-experiment/delivery.json)。编译引用 CSS 373/375/376 的差别如实记录；宿主固定 376，运行时加载仍待实机证明。

Medium profile、Nade data、非 Rush cfg 随包提供；剔除 `subclass_create` 风险快捷键，排除 Rush trees、RoundDamageRecap、Panel 和 Human runtime。静态 JSON 使用不可变 Git blob；历史构建副本仅 CRLF 差异经核对后不作为源替代。对应源码归档、source index 与许可证随 bundle 提供，大文件不提交 Git。

## Framework ownership 与恢复合约

官方 Windows MetaMod 1481 ZIP 来源固定为 `https://mms.alliedmods.net/mmsdrop/2.0/mmsource-2.0.0-git1481-windows.zip`，SHA256 `c4f825ed226635ce13d4c822fcf837e036baf83d30233ba54dde3b303d30379c`。审查完整 archive entries 后只选择 CS2 x64 的 `metamod.2.cs2.dll` 与 loader `server.dll`；[完整清单](2026-10-20-bot-experiment/metamod-archive-inventory.json) 和 [105 项安装文件/hash](2026-10-20-bot-experiment/candidate-files.json) 可复审。

MetaMod 1473→1481 的两个文件、Bot、Human/其他可执行插件隔离、gameinfo SearchPaths 同属一个 install ID；CSS 1.0.376 全部宿主文件保持原样并检查 preimages（仅排除易变 logs）。不整树覆盖 addons，不使用旧 Bot restore 恢复 framework。

- 默认 preview，显式 root 和候选 manifest digest；只有 Install/Restore 的 `-Apply` 才写入。CS2 运行、五项 binary/build、框架/Human/CSS hash、startup/module 清单漂移均拒绝。
- preview 相对路径枚举 created、overwritten、modified、isolated、retained；所有原内容先备份并校验，再写任何目标文件。
- 默认 `E:\CS2MOD\backups\bot-experiment\<install-id>`，record 保存 root、候选 digest、pre/post hash/type、created directories 和状态。active marker 与精确 ID/backup 绑定，未恢复历史阻止重复安装。
- Human/其他执行插件移入受保护 isolated 备份；不改其 DLL、fixture 或私人 preset。MetaMod 原 DLL 和 gameinfo 有逐文件 preimage。
- Restore 先全量核验 installed images、所有备份、隔离冲突、保留 CSS 和新增模块，再开始写入。只删除本事务创建且 hash 相符的文件、移除已知空目录。
- 可捕获安装失败自动回滚；硬中断与恢复中断按同一 record 的已知 pre/post image 继续恢复。文件/备份漂移先拒绝，无 Force 或猜“最新 backup”入口。

入口和未来命令见 [README](../../scripts/bot-experiment/README.md)；候选内也有同一 README、MANUAL-TEST 和 `tools/Invoke-BotExperiment.ps1`。本轮真实 root 仅接受只读构建前置检查；所有 Apply 都发生于 task worktree 的 synthetic fake roots。

## 验证结果

1. [28 项事务测试](2026-10-20-bot-experiment/unit-tests.log) 全部 PASS，61.325 秒。覆盖默认/恢复 preview 零写入、完整 roundtrip、framework/Human isolation/Bot/gameinfo 四阶段失败自动回滚与硬中断恢复、恢复中断续做、创建/覆盖文件漂移、坏/缺备份、隔离目录冲突、CSS 漂移、新模块、重复安装/历史记录、进程 guard、路径逃逸/大小写/重复 JSON key、junction/reparse、缺 payload/错误 hash 等。
2. [最终实际包集成测试](2026-10-20-bot-experiment/actual-candidate-test.json) PASS。使用最终候选打包的 PowerShell 入口与全部 105 个实际 payload，基线是 539 个**合成文件**，没有复制真实私人内容；保留预设/未知插件哨兵。安装计划 isolated=5、overwritten=14、created=91、modified=1、retained=515；安装后验证 payload hashes，恢复前后文件路径集合、类型及所有内容 hash 完全一致。安装/恢复 preview 均零写入。这不等价于真实 DLL 加载测试。
3. 两个 PowerShell wrapper parser 与四个 Python 模块编译检查通过。正式 `scripts/bot-baseline` 未改，无需触发其变更条件回归。
4. [真实保护核对](2026-10-20-bot-experiment/protection.json)：全真实 CS2 tree、Steam manifest、`E:\CS2MOD` presets/app-data/既有 backups、正式 locks 共 6,028 个文件，前后 6,028、changedPaths=[]。只新增本任务 release 输出与 worktree 内文件；未安装、未启动 CS2。

当前具名 build 为 PatchVersion 1.41.9.1 / ClientVersion 2000933 / BuildId 25966348；五个关键 binary 与已合入的 [离线调查](2026-10-20-bot-offline.md) 身份一致。该调查的 81/81 Bot 与 28/28 CSS signatures、42/42 patch original bytes 和 Aim 静态 offsets 证据可沿用；45 项配置 offsets 的运行时语义仍未知，不把本轮 fake-tree 结果升级为 runtime 结论。

## 剩余最小实机验收（全部 pending）

用户以后决定试验时，关闭 CS2 后按明确 digest 预览、显式隔离并 Apply；记录唯一 install ID。随后人工检查 MetaMod 1481/CSS 376、三 native/八 managed/API 成套加载和日志；Head/Body/Mixed aim、movement/买枪/nades；TAB/score、换边、死亡重生与 crash。固定条件做 vanilla→candidate→vanilla 的 FPS、1% low、frametime/long frames 对比。最后停机、Diagnostics、Restore preview/Apply，核验原 MetaMod 1473/CSS/Human/配置恢复，并人工验证 Human 加载与呈现。详见 [MANUAL-TEST](../../scripts/bot-experiment/MANUAL-TEST.md)。

无未解决的候选组装/假树恢复阻塞；真实 KHook、IPC、CSS/framework 组合、aim 和性能仍是待验项。未更改正式 pin/status；本轮到候选交付结束。
