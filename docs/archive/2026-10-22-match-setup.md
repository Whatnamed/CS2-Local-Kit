# 2026-10-22 Bot 修复候选与开局配置

## A：真实环境只读核查

起点 `88ce715bca4f2c1cfaa9ffddb576b03f0df22233`，GitHub main 为 `bf03b042d5f3be90201894cc5d50921521e19b16`；独立 worktree `wt-match-setup-20261022`。
当前 CS2 `1.41.9.1 / ClientVersion 2000934 / BuildId 26087496`；活动 install ID `b613a8c709c7440998346cb802bb790f`，`humanPolicy=preserve`。
CS2 已关闭，原包的 Diagnostics 和精确 Restore **preview** 均成功。真实 Restore/Install 未执行。

已确认：固定 [NadeSystem source](https://github.com/ed0ard/CS2-Bot-NadeSystem/blob/009ce03005af68ba68dae62967bddc8e92ad172c/NadeSystem.cs) 的 `DataDir` 为 `ModuleDirectory/grenades`，而本机该目录 JSON 数为 0；另一条 `data/NadeSystem/grenades` 有 48 个 JSON。源码 `LoadDb` 只枚举前一目录，因此这套数据没有被它加载。历史日志只保留 plugin load，没有保存 `Server.PrintToConsole` 的 DB 数字，不能伪造启动时打印的数量。

Aim 日志在 2026-10-22 16:52:37 与 18:00:48 两次记录 `Active: first override`，分别为 glock / p250、LEFT_CHEST。这证明既有 Hook 有实际写入路径；不能由此推出所有档位切换均生效、命中率或实际 Head 效果。

新不可变候选将 48 个文件放到插件真正读取的位置。最小 patch queue 由 `scripts/bot-experiment/build_match_candidate.py` 生成：exact Git export、唯一 anchor 校验、原/新源 hash、离线构建及独立 patched source ZIP；原来源 archive、许可证和 provenance 均保留。仅增加命令反馈与内部状态读回 capability，以及 aim 写入 / nade 成功创建后的 announce 计数，不改原生 signature、offset、投掷策略或 AI。每次应用模式重置计数/epoch，以免把旧模式的行为当新模式证据。Nades off 的零计数本身不证明行为，需要对照实机观察。

三档修复候选均通过公开 PowerShell 入口的真实 payload 合成 roundtrip：完整文件集/hash 校验，preview 零写入，Human 保留，精确恢复后路径/类型/内容完全相同。候选未覆盖活动安装；最终 UI 使用的完整候选身份在后续交付证据中记录。

## FPS 的有界结论

游戏关闭，无当前进程样本、逐帧 capture、同一会话开头/中段/后段 FPS 或 Bot 数量证据，**无法复现或归因**。源码可见 Nade 高频路径，但多处状态按 round/map 清理；没有可证明的单局增长路径，未作猜测性性能修复，也不把修复数据缺失描述为 FPS 优化。

`scripts/bot-experiment/Collect-MatchPerformance.ps1` 只观察用户已启动的 CS2，最多 30 分钟，每两秒保存进程 private/working-set/handles/threads/CPU，保留相关日志与会话状态；可附用户已有 PresentMon CSV。不安装 capture 工具，不启动游戏。没有逐帧源时 FPS/frametime/long frames 记 unknown。实机时按同图、设置、Bot 数量记录开头/中段/后段回合及对应 frame capture；该性能调查到此停止。

## B：版本化对局领域与阵容数据

`Core/MatchSetup` 定义 Team / PlayerIdentity / RosterSnapshot / CustomRoster / MatchSetupPreset，五席位与真人替代位置分离，按 CT/T 映射计算最终人数。内置数据通过不可变集合提供；只允许复制成新 ID 的自定义阵容。名称限制按 BotHider 的 31 UTF-8 bytes 合约，阻止 console delimiter。显式 persona ID 消歧，disabled / 缺失 ID 降级为名称；同局重复 persona 只绑定第一次，后出现的同名显示带序号，不伪造 persona 唯一性。

数据导出读取 exact Git blobs：40 支队、25 支推荐、40 份来源快照，加 19 份逐赛事核实的 2024–2025 历史阵容，覆盖 Plan 中 17 支重点队伍。每份快照有代表日期/赛事/来源，日期不是推断的转会有效期。来源快照日期取 upstream commit；不做实时同步。5,376 个索引项含 4,861 enabled account ID 和 515 个原 `disabled_players` 名称；后者用明确的 `disabled-<hash>` 本地目录键，SteamId=0，不能当 native persona。4,743 个 distinct enabled nicknames；200 个上游席位的四项缺失名称保持未绑定，详见 `data/match-setup/provenance.json`。历史数据与 upstream 数据均保留原名，不凭相似昵称猜 alias。

个人 library 与 next-match 请求只写 `app-data/match-setup`；Human 文件不参与。library 保存整局预设、favorites、recent、last-used 与 custom rosters。原子替换前保存具名备份；import 相同 ID 相同内容去重，不同内容拒绝并报告冲突；未知/损坏 schema 保留原文件，禁止猜测迁移。当前没有已存在的旧 MatchSetup schema，测试覆盖未知版本迁移拒绝、损坏文件保留与备份回退。开局请求另有 request ID 和 preset digest。

B 阶段 .NET 自动化 **220/220 PASS**（原有 198 + 22 个 domain/store cases），覆盖替代席位、人数、双方映射、同名、转会跨队重复、disabled/custom、import/export/version/backup 与 built-in 只读。初次 tamper 测试替换了请求中不存在的文案，已改为真实修改 `friendlyBots` 字段后验证 checksum 拒绝。
