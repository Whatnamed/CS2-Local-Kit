# Bot + Human 最小实机验收

1. 关闭 CS2，校验固定联合候选 SHA、完整安装 preview、exact backup/install ID；确认 `humanPolicy: preserve` 与 Human 全部文件身份。具体是否已安装以交付记录为准，不重复安装活动事务。
2. 安装成功后，由用户自行启动 Windows offline listen-server。核实 MetaMod 1481、CSS 1.0.376、三项 native、八项 managed、InventorySimulator 与 APIs 成套加载；检查 missing schema/signature、KHook、IPC 或可选功能降级日志。
3. 特别检查 Windows listen-server 的 Head/Body/Mixed aim；再检查 movement、买枪、nades、Bot 人数、Bot cosmetics。没有实测前，不宣称上游 head-aim issue 已修复。
4. 在同一联合会话检查真人 CT/T 枪皮、刀型/动画/涂装、手套、音乐盒及死亡重生、换边；确认 TAB、score、回合正常，无意外 restart/比分重置或 crash。
5. 用户已接受该基线观察到的 FPS 下降，只检查是否有明显新增的严重卡顿；不要求原版 FPS，不安排逐组件性能 A/B 或专门跑分。
6. 关闭 CS2，Diagnostics / Restore preview，通过后按 exact ID Restore -Apply。核对 MetaMod1473原 DLL/loader、CSS、Human/其他隔离插件、gameinfo、私人 cfg/fixture/preset 全部还原。
7. 用户再启动普通模式，验证原饰品加载/呈现、重生和换边。恢复后的文件身份验证与真实游戏呈现分别报告；联合实机验收前不提升正式 accepted pin。

遇到 crash 或半安装，先关闭游戏并保留 backup/record/日志。禁止重复安装、换用旧 restore 或强制覆盖。
恢复拒绝时按 README 的漂移处理流程停止；不自动启动下一轮实验。
