# Bot experimental candidate

这是单一 CS2 安装上的临时实验事务，不是 accepted runtime，也不是可独立启动的第二份游戏。
它将 framework、Bot 文件、经核查的可执行插件隔离和 gameinfo 修改放入同一 install ID。
历史候选隔离 Human；日常联合模式只接受保留 Human 且有完整共存证据的独立候选。
CSS 1.0.376 保留原样；正式 Bot/Human locks 不参与候选升级。

需要已有 PowerShell 7 和 Python 3.12+。脚本不下载依赖、不启动或结束 CS2。
默认 preview；只有 `Install` / `Restore` 加 `-Apply` 才写入。构建候选不会应用真实安装；安装事实以精确事务 record 和 Diagnostics 为准。

## 候选身份与 ownership

- `experiment-manifest.json` schema 1 / lane `bot-experiment`：精确 Bot ref/递归 gitlinks、MetaMod 官方 ZIP URL/hash/选中文件、CSS host、五项游戏 binary/build、模块 inventory、基线 preimages、bundle 完整文件 hash 表。
- `MANIFEST.sha256` 是便捷副本。运行前与交付报告中的 manifest SHA256 和 ZIP SHA256 交叉核对；命令必须显式提供 manifest digest，不自动选择“最新包”。
- `framework/` 仅有经完整 ZIP 路径审查的 CS2 x64 `metamod.2.cs2.dll` 和 loader `server.dll`；其他游戏、平台及 loader declarations 不整树覆盖。原文件逐项备份，事务临时负责其恢复。
- `bot/` 包含三项 native、八项 managed entry projects、相应 APIs/Harmony、NadeSystem data、非 Rush cfg 和选中难度的 profile。`subclass_create` 快捷键被剔除；RoundDamageRecap、Rush trees、Panel、Human runtime 不部署。
- `Build-BotExperiment.ps1 -Difficulty Low|Medium|High` 分别生成全新不可变候选（默认 Medium）。每包仅有一个安装 VPK，`profiles/<level>/botprofile.db` 来自固定上游 Git blob；manifest `profile` 绑定 difficulty、source DB hash 和 VPK hash，校验 VPK 结构、嵌入 DB 与 checksum。该信息进入 install record 和恢复 preflight。原 Medium-only 历史包保持不变。
- 安装后不直接更改难度/VPK。先按精确 ID 恢复，再选择新候选安装；用户偏好不写入 active transaction。瞄准/道具仅生成 `bot_aim mixed|head|body; bot_nades off|less|normal|more|max`，需用户在本地控制台执行；复制不代表生效。
- preview 枚举 `created / overwritten / modified / isolated / retained`。所有 CSS/Human 原内容只核验/保留或隔离，不修改其 DLL、fixture、私人预设。
- 独立 `install-record.json` schema 1 保存 install ID、root、候选 digest、每项 pre/post hash/type、created directories 和状态。`preimages/` 在第一项 live 写入之前全部备齐；`isolated/` 保存移动出的原插件树。
- 默认备份 `E:\CS2MOD\backups\bot-experiment\<install-id>`；active marker 关联唯一事务。不得改用旧 Bot/Human restore 或靠最近时间猜 backup。
- 对真正的 CS2 根目录显式调用；脚本不会自动寻找第二个安装。fixture/fake mode 只能用于 worktree `temp/` 中带哨兵且没有 PE executable 的合成树。

## 联合模式合约

manifest 可选 `humanPolicy`，缺省为历史 `isolate`；`preserve` 必须由调用方明确提供 `-PreserveHumanCosmetics`，不能同时提供 `-IsolateHumanCosmetics`。
联合模式沿用同一事务、完整 baseline/模块 inventory 与 pre/post image 校验，且要求 `coexistence`：

- `status: ready-for-manual-test`、与 `targetBuild` 完全相同的 `reviewedBuild`、外部核查证据的 `evidenceSha256`；
- `humanFiles` 与 baseline 中全部 InventorySimulator 插件、配置、gamedata 文件的 hash 完全一致，至少包含 DLL、`inventories.json` 和 gamedata；payload 不得覆盖这些路径；
- `isolatedModules` 与预检枚举出的其他可执行模块的完整身份完全相同；未知第三方模块拒绝安装，Human 不得隔离。

这些字段是候选的审查契约，不能用手填 `ready` 替代共享 MetaMod/CSS/API/native Hook 的兼容核查。Controller 仍须将候选及其完整 manifest digest 编译进白名单；缺少通过门禁的联合候选时只显示阻断原因。
`Build-BotExperiment.ps1` 保留历史隔离构建入口。`build_joint_candidate.py` 读取显式指定且完整校验的历史 payload 和绑定当前五项 binary 的兼容核查；检查全部 Bot/CSS/Human 签名与补丁原字节、前置文件及完整 Human 清单，再生成新的不可变联合包。Bot DLL、API、profile 与对应源码保持原身份；只更新构建证据、事务工具及当前目标的精确 preimages。不会自动选择最新 build 或将旧包原地改标。

兼容核查的 `ready-for-manual-test` 只允许受控联合安装与用户实机测试；signature、schema、回调源码审查及合成 roundtrip 不证明真实加载或 Hook 执行安全。核查保留私有 offsets、Hook 顺序、Harmony/IPC 等运行时验证限制，不通过猜 offsets 或更换 Bot 版本消除这些限制。

```powershell
python -B scripts/bot-experiment/build_joint_candidate.py --source '<exact-verified-source-bundle>' --source-manifest-sha256 '<source-sha256>' --evidence '<compatibility.json>' --evidence-sha256 '<review-sha256>' --root 'E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive'
```

## 将来手动操作

以下是将来具备独立联合候选时的显式入口，不授权使用过期候选。首次操作前先关闭 CS2；具体交付是否已安装以带日期证据和精确 install ID 为准。

```powershell
$candidate = 'E:\CS2MOD\releases\bot-experiment\<exact-candidate-id>'
$sha = '<manifest SHA256 from delivery report>'
$tool = Join-Path $candidate 'tools/Invoke-BotExperiment.ps1'
$cs2 = 'E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive'

# 联合候选完整安装预览；保留 Human，零写入
pwsh -File $tool -ReleaseDir $candidate -ManifestSha256 $sha -Cs2Root $cs2 -PreserveHumanCosmetics

# 人工确认 exact preview 后才使用；打印唯一 installId 和 backupDir
pwsh -File $tool -Action Install -ReleaseDir $candidate -ManifestSha256 $sha -Cs2Root $cs2 -PreserveHumanCosmetics -Apply

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
安装与恢复还检查 Steam `StateFlags`、目标 build 和下载/暂存完成状态，并在每项 live 修改前检查 CS2 进程及 Steam manifest 是否变化。更新中断时保留救援记录，待更新停止后重新预检，不绕过 build/hash 门禁。
需人工定位并保存发生漂移的内容，再据其身份单独决策；本工具没有强制丢弃用户修改的入口。
不能从 fake-tree PASS 推断真实 KHook/CSS/游戏加载、aim、性能或恢复后的 Human 渲染已经通过。

## 可复现验证

仓库运行 `python scripts/bot-experiment/Test-BotExperiment.py`，测试仅在 worktree `temp/bot-experiment-tests/` 写入。
构建使用 `Build-BotExperiment.ps1` 的显式 evidence worktree、官方 archive、目标 root 和已有 .NET SDK；
它对历史输出先验证 SHA256，再组装，缺失的 API hash 通过精确 source 的离线重建补齐。source/许可证随包保留。
正式 `scripts/bot-baseline` 未放宽或改写。

Controller「人机」页使用编译进程序的精确候选白名单，校验完整 bundle 后调用包内同一入口。安装/恢复按钮先预览，再独立确认；每次 Apply 内再次完整预检。偏好保存在 `app-data/bot-experiment/preferences.json`，操作 stdout/stderr 保存在 `diagnostics/bot-experiment/`。存在活动实验（包括 marker 缺失的未恢复历史）时，Human Apply/Restore 和 MetaMod1473 启动修复被阻止；不能用旧恢复路径覆盖活动实验。

## 开局配置候选

`build_match_candidate.py --upstream <exact-source-checkout> --dotnet <existing-sdk> --adapter <Release/net10.0>`
从原 preserve-Human 三档候选派生新不可变目录；先按 pin 导出并离线重建最小 Aim/Nades 诊断 patch，
再加入本仓库的 `LocalKitMatchSetup`。适配层先以 CSS 1.0.376 / 原 BotHiderApi 编译，构建时指定
`-p:BotHiderApiPath=<pinned-shared-api.dll>`。调用 builder 前提交对应源：包内 source ZIP 使用当前 HEAD，
包括 Core、适配层与数据目录；完整 bundle、source archive 和 manifest 分别记录 digest。

配置保存于 `app-data/match-setup`。只在新图、插件就绪、唯一真人选边、首个计分回合之前初始化一次，
每阶段读回人数、controller 身份、persona、模式 epoch；死亡/换边/后续回合不重建。
`css_localkit_status` 读取本图诊断；“内部行为已观察”仍须玩法验收。旧活动安装不带该适配层，不能原地覆盖。
新旧候选并存，Diagnostics / Restore 始终按 install record 的精确 digest 选择原包。
