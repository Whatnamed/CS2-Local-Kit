# 日常增强人机流程交付 — 2026-11-01

本记录是一次构建与隔离迁移证据，实际安装仍为旧 Low 整合事务，不代表新候选已安装或实机验收。
完整身份和只读迁移预览见 [delivery.json](delivery.json)。长期定义已同步在原 PRODUCT-SCOPE、CONTROLLER-UX、ARCHITECTURE、LOCAL-WORKSPACE 与 MANUAL-ACCEPTANCE 中。

实现分离了统一底座安装和日常 profile/模式切换。当前日常配置自动保存，Aim/Nades 按地图独立读取，阵容默认关闭；启用时复用有界开局身份绑定。普通模式保留 Human/共享框架。历史 manifest、事务与三层父备份不改写，精确 Restore 接受有限的已核验受管映像并还原原始 preimage。

直接审阅本机 Local-Arena `f8792eae0b5c49bf891922776d45883a9ff299dc` 的 `set_difficulty_at`、Aim/Nades CFG、`mode_layout`/`mode_files`、`TeamLineupInjector` 和独立 Match 边界；未复制完整 Match 协调器或升级上游。

验证：

- 281 .NET tests；Adapter Release 编译零警告/错误。
- 原有 40 项事务测试。
- 实际统一包的旧 Low 整合迁移、九次 profile/模式诊断、未知映像拒写、有限中断状态和精确恢复。
- 实际完整包的公开 PowerShell 入口、逐项安装失败回滚、硬中断与逐项恢复中断续恢复；原三层父记录/备份保持一致。
- 63 项真实 WPF 离屏检查、28 张截图；保持双页导航和现有视觉。原生弹窗、真实输入/DPI/窗口外壳仍属于人工验证。
- 真实旧事务只读恢复预检通过；新旧 targetBuild、baselineFiles、gameBinaries、moduleInventory、runtimeIntegration 和 Human 基线完全一致。迁移后 Install 仍须重新执行完整实时预检。

最短真实操作（用户另行确认或自行执行）：

1. 关闭 CS2，打开 `E:\CS2MOD\releases\controller\controller-daily-20261101\payload\CS2LocalKit.App.exe`。
2. 系统设置 → 检查组件预览 → 安装/升级组件。确认后仅恢复当前活动整合层，再安装统一底座；不要手动恢复父层。
3. 选择增强模式、难度、Aim/Nades，保持阵容关闭；等“已写入”，自行以 `-insecure` 正常启动 CS2 并任选本地地图，不点击准备。
4. 另一次启用已选历史/自定义阵容，核对双方人数、persona/名字、TAB、换边和 Human CT/T 核心效果。关闭游戏后再切难度/普通模式，确认不重装底座。

游戏运行中改变 profile/模式只保存；若退出 Controller，必须先关闭游戏并重新打开 Controller 完成应用，再启动游戏。普通模式不加载增强 Aim/Nades 或阵容；受管增强 CFG 停用后可能有缺失入口提示。游戏内效果与 FPS 未验证；未进行真实 Install/Restore、启动项修改或游戏启动。
