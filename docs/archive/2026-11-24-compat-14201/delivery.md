# 1.42.0.1 本机交付记录

- Steam public `1.42.0.1 / 2000950 / 26879581`。活动运行时为普通人机 + Human 饰品候选，MetaMod `2.0.0-git1491`、CounterStrikeSharp `1.0.380-local.14201`、InventorySimulator `3.3.0 + C1.1`。增强人机加载器处于隔离状态，当前游戏目录保留 Valve 模式配置与唯一 MetaMod 启动项。
- Runtime candidate 位于 `E:\CS2MOD\releases\bot-experiment\human-14201-16f2ab4`，manifest SHA-256 为 `bc7955ac6270406048828c7bb6c6ac67e09916073cd021aea7612e62193cf501`，包含 9 个框架 payload 文件及恢复工具。源事务 `16e57dae11224ee9ad7be411fa47c1f7` 精确撤回。新事务 `c129944d29bc4999a2946e7dd39c8037` 已安装，核对 682 项事务后映像，包含 11 个已隔离加载入口。三层父事务和原恢复映像仍可校验。备份位于 `E:\CS2MOD\backups\bot-experiment\c129944d29bc4999a2946e7dd39c8037`。
- 原 Human fixture 通过既有侧车机制移交，SHA-256 迁移前后一致。150 个受保护的个人文件及恢复文件通过逐项校验，包含饰品预设、日常模式/难度/阵容配置和上次应用数据。保存的增强模式下次仍可读取，Controller 在当前候选上提示等待兼容更新。
- Controller 位于 `E:\CS2MOD\releases\controller\controller-compat-14201-20261124\payload\CS2LocalKit.App.exe`。发布源码 `1a92e3242d36893563120639bb52bb213d52f7e3`，408 个 payload 文件与 manifest 一致，manifest SHA-256 为 `2163ec5f6abc1637c5d1c8b829aa62283603e03650ebd0da929cc5d72aad3026`。发布版窗口启动和消息响应通过。真实安装回读确认普通人机就绪、增强不可启用，地图列表包含 `de_cbble`。
- 本机英文和简中清单包含 2,226 个可索引漆面和 110 个音乐盒，旧数值 ID 与个人预设验证通过。120 个新增刀皮和 8 个新增音乐盒的 128 张缩略图已入缓存。Singapore 2026 的 772 个贴纸记录及图片地址已纳入本机清单，贴纸资料供目录使用。
- 294 项 .NET 测试及 62 项 WPF 检查通过。合成验证覆盖旧 daily 跨构建撤回、新候选精确安装与恢复、隔离失败回滚、写入启动项中断恢复、已保护文件漂移拒绝。静态查验覆盖 28 条修补后的 CSS 签名、14 条实装 Human 签名、46 个已解析 Schema 字段及 429 条 Aim 指令形状。原 BotAI 的 `TBot_BombsiteSearch_UseKnownPlantedSite` 字节 guard 仍会跳过该处原位 patch。历史未直接解析的 `CCSBot.m_gameState` 仍无本机静态偏移结论。

本机日志与 SHA-256 核验记录位于 `E:\CS2MOD\diagnostics\compatibility\20261124-14201`。用户实机验收点为本地 `-insecure` 启动、普通人机开局，以及 Human 枪皮、刀皮、音乐盒和手套效果。Windows listen-server 游戏内行为仍待验收。
