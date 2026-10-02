# Scripts

这里存放项目拥有的可重复自动化入口。

预期类别：

- dependency fetch / verify；
- build；
- package；
- install；
- restore；
- compatibility probe；
- preset migration。

## 规则

- 脚本必须显式区分 source workspace 与 `E:\CS2MOD` 持久数据区。
- generated release 默认输出到 `E:\CS2MOD\releases`，不是 Git 仓库。
- backup 默认输出到 `E:\CS2MOD\backups`。
- diagnostic 如需长期保留，输出到 `E:\CS2MOD\diagnostics`。
- 任何删除/覆盖真实 CS2 文件的脚本都必须先知道 ownership；不能通过模糊目录扫描猜测。
- destructive 操作应尽量支持 dry-run / preview。
- 下载第三方 artifact 时记录来源 ref，并在可行时校验 SHA-256。
- 不把第三方完整源码 tree 当构建产物提交进本仓库。
- 脚本不能自动把“build 成功”解释成“游戏内通过”。

## Compatibility recovery / Bot baseline

- `cosmetics-lab/Recover-MetaModCompatibility.ps1`：读取 Human lock 中显式的
  compatibility candidate，只更新列出的 MetaMod win64 DLL 和 startup entry。
  默认 preview；`-Apply` 前核验 archive、accepted Human DLL 与 CSS native/API 身份，
  保存 schema-2 backup，验证 CSS/Human 文件树未变化。不升级 accepted build。
- `bot-baseline/Build-BotBaseline.ps1`：从 Bot lock 的精确 main/gitlink refs 构建，
  native release tag 必须匹配 gitlink；BotController native/API 成对。
  Bot package 不拥有 shared MetaMod/CSS core、配置或 gamedata，安装前验证 host prerequisite。
  upstream subclass_create 快捷绑定不进入本次 package；Rush tree source 留作第二层测试。
- `bot-baseline/Install-BotBaseline.ps1`：默认 preview；`-IsolateHumanCosmetics`
  才隔离 Human 插件（不移动 fixture/preset），shared CSS startup declaration 保留。
  `-Difficulty Low|Medium|High` 选择 profile，并记录实际写入 hash；切换前 restore，不直接改 live VPK。
- `bot-baseline/Restore-BotBaseline.ps1 -BackupDir <exact backup>`：默认 preview。
  schema-2 对 created/overwritten/modified 记录安装内容身份；全套 live/backup preflight
  通过后才写入。漂移或 isolated target 冲突拒绝全部恢复，created file 只按匹配 hash 删除，
  retained shared file 永不删除或回写。schema-1 缺少可信安装身份，必须人工审查，不能回退到旧脚本强删。
  同一入口也恢复 Human MetaMod compatibility backup。
- `bot-baseline/Test-BotBaselineOwnership.ps1` 与 `Invoke-BotBaselineRoundTripTest.ps1`
  在 fake tree 验证 preview、漂移拒绝、shared host/Human baseline 保存及恢复。
  `Collect-BotBaselineDiagnostics.ps1 -BackupDir <exact backup>` 只读记录当前身份和 restore 可用性；
  旧日志或文件存在不代表本次实机 PASS。
