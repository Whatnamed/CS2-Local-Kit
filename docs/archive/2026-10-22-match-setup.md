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

## C：候选自动应用与实际 WPF 验证

Core 的有界 `PreMatchSession` 和独立 `LocalKitMatchSetup` CSS adapter 按阶段执行一次性初始化。先校验安装/build/files，再等待插件与真人选边，校准 CT/T、清理默认 Bot 一次、添加一次、释放默认 persona 并读回、绑定目标身份并读回、应用行为与队名并读回。初始化期间使用至多 24 次单次 timer；选边等待依靠事件恢复，不常驻轮询。完成后不改阵容、比分、换边或回合；异常停止并显示原因。热加载不获准重建当前图。

Controller 保留安装状态与精确恢复入口，新增双侧战队/历史版本/五席位、persona 搜索、自定义阵容 CRUD/import/export、随机混编、人数/替代位置、整局预设/收藏/最近/上局复用、强度/Aim/Nades、预览与独立排队。草稿和已排队快照明确区分。运行时状态以 request/digest、进程 PID/start 和地图 session 关联；上次会话不会显示为本局验收。原活动安装缺少新适配层时显示不可自动应用。

.NET **251/251 PASS**；CSS 1.0.376 / pinned BotHiderApi 的真实 adapter 构建 **0 warnings / 0 errors**。覆盖阶段顺序、重复通知的幂等策略、缺插件/真人/目标 Bot、先选不同侧、额外旁观 Bot、比分锁定、身份 staging、模式读回、更新/文件漂移拒绝、坏 library 保留、队伍/历史/自定义/导入导出和排队快照。

实际 WPF 窗口在完全隔离的 `temp/match-ui-data` 中运行，game root 指向不存在的合成目录。Computer Use Sky runtime 不可用，改用限定 PID + 精确窗口标题的 Windows UI Automation 和 PrintWindow。真实控件选择 T、替代席位 4、Body/Off 后点击排队，实际 JSON 验证通过；再选择 Head，UI 显示草稿不同而原请求仍为 Body。已检查 1024 DIP 宽度的滚动布局、底部按钮及屏幕命中 PID。截图与结构化证据见同名 archive 子目录。该 probe 未启动 CS2，不能证明游戏内自动应用或玩法效果；窗口已关闭。

## D：最小人工验收入口与停止点

当前活动安装仍为原 Medium 联合包，**没有新适配层**。可以先审查 Controller 与保存配置，但此时不会自动应用到原安装。真正体验新候选之前，必须另行明确授权原 ID 的真实 Restore，再对新 profile 做完整 preview/确认安装；本轮仅完成原包 Diagnostics / Restore preview。禁止叠加安装或现场覆盖文件。变更没有合并 main，正式 runtime pin 未提升。

授权安装新候选之后，最小步骤：

1. Controller「人机」页选 Medium，保存为下局；我方 FaZe 的 Copenhagen 2024、敌方 Spirit 的 Shanghai 2024，真人替代一个席位，确认预览 5 vs 5。若有同名/未绑定提示，核对名称降级或从明确 persona ID 中选择。由用户启动 `-insecure` 离线地图，先在 CT，再以 T 开新图。确认人数、名字、队名/logo、TAB/score，并刷新应用状态；用 `css_localkit_status` 留存对应 request/session/epoch。状态“行为验证不足”不能判玩法 PASS。
2. 切同队另一历史版本再开新图，然后保存跨队选手和自定义名的自由阵容再开新图。检查相同队伍/跨版本重复选手的消歧；换边、死亡重生、下一回合不重复加 Bot、不清比分、不自动重开。分别检查 Human CT/T 枪皮、刀、手套、音乐盒。
3. 以相同地图、人数和设置对照 Aim Head/Body 与 Nades Off/Max，每份请求都重新载图，确认内部模式/epoch 与实际 Hook/投掷行为。Off 的零计数不足以证明抑制；Max 还需要当前地图 lineup 数和实际投掷。记录一局开头/中段/后段回合、FPS、frametime/long frames。可在用户已运行游戏时执行 `Collect-MatchPerformance.ps1 -Seconds 1800`，附已有 `-PresentMonCsv`；没有逐帧源的指标保留 unknown。
4. 退出 CS2 后，另行明确授权对**新安装的精确 ID**执行 Diagnostics、Restore preview 和真实 Restore；核对原始 hashes/目录/配置与 Human，随后由用户在普通模式复验。原活动事务的 preview PASS、合成树的精确 roundtrip 都不能替代这两次真实恢复与游戏呈现验收。

本轮停止于“可供复审和授权后实机测试的候选交付”。游戏内自动应用、Aim 两档/Nades Off-Max、Human 呈现、TAB/score/换边及真实 Restore 均 pending；FPS 逐局下降的原因未获证实，未作 Bot AI/原生组件性能重构。

## 固定完整候选与白名单

完整三档均为 108-file payload，包含独立 adapter、Core/embedded catalog 和两个最小 patched managed 组件。对应适配层源码 HEAD 为 `c556b665b19f2d965b7e5c67c7c6a0222ed00f3b`；source ZIP 与其 SHA、各源文件 before/after hash 随 manifest 固定。精确路径/digest 见 `2026-10-22-match-setup/fixed-candidates.json`。三个候选通过各自公开 PowerShell 工具的完整真实 payload 合成 Install/Diagnostics/Restore roundtrip，恢复后路径/类型/hash 完全一致，两个 preview 零写入，未指向真实游戏目录。原六包完整保留，Controller 新增三项白名单并优先选开局配置候选，旧事务仍按原 digest 恢复；相应 .NET 验证总数 **252/252 PASS**。
