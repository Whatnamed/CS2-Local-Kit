# Bot experimental candidate

这是单一 CS2 安装上的临时实验事务，不是 accepted runtime，也不是可独立启动的第二份游戏。
它将 MetaMod 1473→1481、Bot 文件、Human/其他可执行插件隔离和 gameinfo 修改放入同一 install ID。
CSS 1.0.376 保留原样；正式 Bot/Human locks 不参与候选升级。

需要已有 PowerShell 7 和 Python 3.12+。脚本不下载依赖、不启动或结束 CS2。
默认 preview；只有 `Install` / `Restore` 加 `-Apply` 才写入。此轮交付没有应用真实安装。

## 候选身份与 ownership

- `experiment-manifest.json` schema 1 / lane `bot-experiment`：精确 Bot ref/递归 gitlinks、MetaMod 官方 ZIP URL/hash/选中文件、CSS host、五项游戏 binary/build、模块 inventory、基线 preimages、bundle 完整文件 hash 表。
- `MANIFEST.sha256` 是便捷副本。运行前与交付报告中的 manifest SHA256 和 ZIP SHA256 交叉核对；命令必须显式提供 manifest digest，不自动选择“最新包”。
- `framework/` 仅有经完整 ZIP 路径审查的 CS2 x64 `metamod.2.cs2.dll` 和 loader `server.dll`；其他游戏、平台及 loader declarations 不整树覆盖。原文件逐项备份，事务临时负责其恢复。
- `bot/` 包含三项 native、八项 managed entry projects、相应 APIs/Harmony、NadeSystem data、非 Rush cfg 和 Medium profile。`subclass_create` 快捷键被剔除；RoundDamageRecap、Rush trees、Panel、Human runtime 不部署。
- preview 枚举 `created / overwritten / modified / isolated / retained`。所有 CSS/Human 原内容只核验/保留或隔离，不修改其 DLL、fixture、私人预设。
- 独立 `install-record.json` schema 1 保存 install ID、root、候选 digest、每项 pre/post hash/type、created directories 和状态。`preimages/` 在第一项 live 写入之前全部备齐；`isolated/` 保存移动出的原插件树。
- 默认备份 `E:\CS2MOD\backups\bot-experiment\<install-id>`；active marker 关联唯一事务。不得改用旧 Bot/Human restore 或靠最近时间猜 backup。
- 对真正的 CS2 根目录显式调用；脚本不会自动寻找第二个安装。fixture/fake mode 只能用于 worktree `temp/` 中带哨兵且没有 PE executable 的合成树。

## 将来手动操作

以下命令是未来实机测试入口。首次操作前先关闭 CS2；本轮只在 fake tree 执行过 Apply。

```powershell
$candidate = 'E:\CS2MOD\releases\bot-experiment\<exact-candidate-id>'
$sha = '<manifest SHA256 from delivery report>'
$tool = Join-Path $candidate 'tools/Invoke-BotExperiment.ps1'
$cs2 = 'E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive'

# 完整安装预览，包含需要隔离的 Human 和其他可执行模块清单；零写入
pwsh -File $tool -ReleaseDir $candidate -ManifestSha256 $sha -Cs2Root $cs2 -IsolateHumanCosmetics

# 人工确认 exact preview 后才使用；打印唯一 installId 和 backupDir
pwsh -File $tool -Action Install -ReleaseDir $candidate -ManifestSha256 $sha -Cs2Root $cs2 -IsolateHumanCosmetics -Apply

# 关闭游戏后，只读核验 installed image、backup 和恢复可用性
pwsh -File $tool -Action Diagnostics -InstallId '<exact-install-id>'

# 恢复预览；全量 preflight 通过后才加 -Apply
pwsh -File $tool -Action Restore -InstallId '<exact-install-id>'
pwsh -File $tool -Action Restore -InstallId '<exact-install-id>' -Apply
```

目标五项 binary、Steam build、框架/Human hash、startup entry 或模块清单变化时，停止使用该包；
不在 installer 里重扫猜新 signature，不自动升级 CSS，不放宽正式 pin。
默认 BackupRoot 非默认时，每次 Restore/Diagnostics 都必须显式传入同一 `-BackupRoot`。

## 失败与恢复

安装出现可捕获失败会以相同 record 全量 preflight 后回滚。进程被中止或机器重启后，保留打印的 exact ID/backup，
关闭 CS2，用同一个 restore 入口预览并恢复；`installing/restoring/restore-failed` 状态允许辨认已知 pre/post images 后续做。
所有文件操作都限制在验证后的相对路径，拒绝 junction/reparse、大小写冲突、未知覆盖与重复安装。

任何 live 或 backup 漂移（包括游戏/用户改动本事务拥有的 cfg/plugin 文件）、Human 目录重新出现、备份缺失，
均在恢复写入之前拒绝。保存整个 backup/record 和报错；不要删除 active marker、改 manifest/hash、清空插件目录或加 Force。
需人工定位并保存发生漂移的内容，再据其身份单独决策；本工具没有强制丢弃用户修改的入口。
不能从 fake-tree PASS 推断真实 KHook/CSS/游戏加载、aim、性能或恢复后的 Human 渲染已经通过。

## 可复现验证

仓库运行 `python scripts/bot-experiment/Test-BotExperiment.py`，测试仅在 worktree `temp/bot-experiment-tests/` 写入。
构建使用 `Build-BotExperiment.ps1` 的显式 evidence worktree、官方 archive、目标 root 和已有 .NET SDK；
它对历史输出先验证 SHA256，再组装，缺失的 API hash 通过精确 source 的离线重建补齐。source/许可证随包保留。
正式 `scripts/bot-baseline` 未放宽或改写。
