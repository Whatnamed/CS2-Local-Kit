# 后续最小实机验收 — 全部 pending

1. 记录同地图、画质、人数、位置、预热与固定采样时长的 vanilla 基线。关闭 CS2，校验候选 SHA、完整安装 preview、exact backup/install ID；明确隔离 Human。
2. 手动使用 `-Apply` 后，再由用户自行启动 Windows offline listen-server。核实 MetaMod 1481、CSS 1.0.376、三项 native、八项 managed 与 APIs 成套加载；检查 missing schema/signature、KHook、IPC 或可选功能降级日志。
3. 特别检查 Windows listen-server 的 Head/Body/Mixed aim；再检查 movement、买枪、nades、Bot 人数、Bot cosmetics。没有实测前，不宣称上游 head-aim issue 已修复。
4. 连续若干回合确认 TAB、score、回合/半场换边、死亡重生，无意外 restart/比分重置、crash 或持续卡顿。
5. 同条件 vanilla→candidate→vanilla；记录 FPS、1% low、frame time p50/p95/p99 和 long frames，不把历史 FPS 区间当作本轮新测量。
6. 关闭 CS2，Diagnostics / Restore preview，通过后按 exact ID Restore -Apply。核对 MetaMod1473原 DLL/loader、CSS、Human/其他隔离插件、gameinfo、私人 cfg/fixture/preset 全部还原。
7. 用户再启动原 Human 环境，验证原饰品加载/呈现、重生和换边。两侧 gate 通过前不改正式 locks，也不提升为 accepted。

遇到 crash 或半安装，先关闭游戏并保留 backup/record/日志。禁止重复安装、换用旧 restore 或强制覆盖。
恢复拒绝时按 README 的漂移处理流程停止；不自动启动下一轮实验。
