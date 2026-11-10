# Controller Windows 响应与 1.41.9.6 迁移

## Windows Release 对照

在同一台 Windows 机器以 150% DPI 运行两个 self-contained win-x64 Release，均使用当时实装的旧 Low 事务和本机清单及图片缓存。先启动并空闲约 12 秒，再切饰品页、滚动枪皮、切换并滚动音乐盒、Alt-Tab 切出和切回、最小化和恢复，最后切回对局页和打开系统设置。使用跨进程 UI Automation、滚轮与 Windows 窗口消息驱动，核对选中的页面、窗口前台和最小化状态。

通过 50 ms 周期的 Win32 WM_NULL 请求测量窗口消息响应，超时上限 2 秒。耗时包含调度和窗口消息等待。打开饰品页的 UI Automation 查找和选择用时从 2358 ms 降到 115 ms，打开系统设置从 754 ms 降到 84 ms。全程 WM_NULL P95 从 4.79 ms 降到 0.43 ms，超时从 7/881 降到 0/1252，最终包最大响应延迟为 275 ms，发生在启动阶段。

| 操作 | 最大消息等待 ms | 进程 CPU s | UI 线程 CPU s | 进程读入 MiB | 阶段末工作集 MiB |
| --- | ---: | ---: | ---: | ---: | ---: |
| 启动约 8 秒 | 317 → 275 | 10.91 → 11.02 | 0.33 → 0.38 | 580 → 713 | 298 → 304 |
| 空闲约 12 秒 | ≥2000 → 164 | 8.34 → 6.05 | 5.20 → 0.27 | 630 → 362 | 318 → 313 |
| 切到饰品页 | 1428 → 81 | 4.23 → 1.61 | 2.58 → 0.19 | 228 → 1.8 | 330 → 339 |
| 枪皮滚动 | ≥2000 → 0.8 | 4.16 → 0.59 | 3.55 → 0.06 | 349 → 1.6 | 340 → 334 |
| 切到音乐盒 | 62 → 44 | 1.17 → 1.17 | 0.33 → 0.20 | 8.9 → 2.6 | 344 → 336 |
| 音乐盒滚动 | ≥2000 → 14 | 4.62 → 0.36 | 4.27 → 0.03 | 348 → 1.8 | 347 → 343 |
| Alt-Tab 切出 | 35 → 0.3 | 0.31 → 0.05 | 0.14 → 0.00 | 5.4 → 0.9 | 342 → 342 |
| Alt-Tab 切回 | ≥2000 → 0.8 | 1.31 → 0.20 | 1.22 → 0.00 | 132 → 1.3 | 344 → 326 |
| 最小化 | 1959 → 41 | 2.50 → 0.06 | 2.44 → 0.00 | 219 → 1.3 | 342 → 327 |
| 恢复窗口 | 60 → 51 | 0.30 → 0.20 | 0.11 → 0.02 | 8.1 → 0.9 | 342 → 333 |
| 切回对局配置 | ≥2000 → 43 | 2.86 → 0.17 | 2.66 → 0.02 | 217 → 1.3 | 343 → 332 |
| 打开系统设置 | 52 → 0.7 | 1.27 → 0.30 | 0.92 → 0.06 | 131 → 0.9 | 350 → 336 |

前后首个 HWND 分别在 866 ms 和 914 ms 出现。进程峰值工作集从约 350 MiB 到 345 MiB。I/O 来自进程计数，包含缓存读取。当前权限无法启用 WPR 内核采样，物理磁盘和 GPU 帧时间未测。启动阶段仍包含一次身份检查，采样窗口内进程读取量上升。原始 .NET EventPipe 采样和窗口指标保存在本机 `E:\CS2MOD\diagnostics\controller\performance-20261110`。

### 采样与修复

旧包的高频 UI 线程调用链是 `DispatcherTimer → RefreshRuntime → Notify → BotViewModel.Observe → Readiness → InspectInstalledCore`。SHA-256 流式读入、路径检查和受审 build 的二进制校验占据最常见的忙栈。日常应用返回后的广泛属性通知也进入同一条重查路径。最终包 UI 线程采样以 Dispatcher 等待和少量 WPF 窗口合成为主，原安装检查调用链从采样忙栈消失。

周期观察和真实应用的 I/O 在后台执行。运行时读回只发布变动的字段，安装面板和命令读取已观察的状态。现有显式核查和事务写入门禁仍执行完整校验。图片缓存文件查找与解码在后台执行，WPF 探针用完整目录验证枪皮实例化 3/21 行、音乐盒 4/26 行，并在滚动后正确实例化末行。保留原有图集布局和卡片功能。

123 项定向测试通过，覆盖日常保存、有限重试、模式和难度、运行时读回、Bot 事务和图片缓存。WPF 探针通过 67 项检查并捕获 28 张画面，覆盖 CT/T、图集、布局和安全确认。最终 Release 的 408 项 payload 与发布 manifest 逐一匹配。

## 发布与实际迁移

- Controller Release 在 `E:\CS2MOD\releases\controller\controller-performance-20261110\payload\CS2LocalKit.App.exe`。发布源码为 `3f0eb2139fdce734d62b29bc4315e16dc0a41074`，发布 manifest SHA-256 为 `66d2e64fb433ce38996791f176729b998931b5cd60513c6d182fa654f6bb356a`。
- 实际 Steam 环境为 `1.41.9.6 / 2000942 / 26445667`。目标为已登记的 `E:\CS2MOD\releases\bot-experiment\bot-daily-14196-dc94191`，manifest SHA-256 为 `fc58bb0bf376469b43c203e5dcda648f12d0d9d0e445b3e06d6cf88a322452a2`。
- CS2 和 Controller 关闭后，通过发布版的 `BotExperimentService.PreviewSwitchAsync` 和 `ApplySwitchAsync` 执行。旧 Low `177e9652205d4d75a4cda34d76934547` 精确恢复至三层可玩底座，经新的 Install 预检后安装 daily。操作 `5ed3f85e4d174c14aa892078a4f99ca1` 已完成，活动安装 `16e57dae11224ee9ad7be411fa47c1f7`。
- 安装后的只读 Diagnostics、`InspectInstalled`、`MatchRuntimeContract.Validate`、模块清单和 688 项事务后映像均匹配。现有 531 项备份条目、个人 preset 与应用数据、8 个受保护 Human 文件和三个父事务的备份及标记逐项一致。父事务为 Bot `b613a8c709c7440998346cb802bb790f`、兼容层 `5808afffdc44440ab3f0de2159cdb436`、Nades `9f9849ca97804c2dbd7b1307bba4580c`。旧记录仅改为 `restored`，恢复流程保留了空的 `restore-staging` 目录。
- 只读验收读到候选默认文件态 EnhancedHuman / Medium。个人日常设置仍为 EnhancedHuman / Low，下一次正常打开新 Controller 时自动核对并应用。此次操作未启动 CS2 或应用饰品方案，游戏内效果待用户验收。
