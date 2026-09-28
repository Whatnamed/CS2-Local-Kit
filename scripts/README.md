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
