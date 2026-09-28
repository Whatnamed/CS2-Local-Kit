# CS2 Local Kit

CS2 Local Kit 是一个独立的 **Windows 本地 / 离线 CS2 人机工具项目**。

项目刻意把两个问题分开：

- Enhanced Bot：使用经过本机验证的外部 pinned runtime；
- Human cosmetics：由本项目维护一个范围很小、与 Bot/比赛状态隔离的真人饰品层。

本仓库从干净 Git 历史开始，**不是** Local-Arena 或 CS2-Bot-Improver 的 fork。旧 `Whatnamed/Local-Arena` 只保留为 legacy reference，不再作为新项目 upstream。

## 核心原则

- Bot runtime 与 Human cosmetics 分属独立 failure domain。
- 优先 pin 外部组件，不维护不必要的大型 downstream fork。
- 把 CS2 更新敏感的 schema / native signature / econ binding 收敛到小的 compatibility boundary。
- fail closed：饰品失效不能顺带改变 Bot 数量、比分、换边、比赛重启或正常游戏流程。
- build / unit test 不能替代真实 CS2 对 model、animation、skin、HUD、FPS、TAB、score 的验证。
- preset、release、backup、diagnostic 和长期 app data 使用固定持久目录，不塞进 disposable cache。

## 文档入口

先读 [docs/README.md](docs/README.md)。

主要长期文档：

- [产品范围](docs/PRODUCT-SCOPE.md)
- [架构](docs/ARCHITECTURE.md)
- [Upstream 与 provenance](docs/UPSTREAMS.md)
- [兼容性核查](docs/COMPATIBILITY-WATCH.md)
- [本地目录与持久数据](docs/LOCAL-WORKSPACE.md)
- [实机验收](docs/MANUAL-ACCEPTANCE.md)
- [开发流程](CONTRIBUTING.md)
- [Agent 规则](AGENTS.md)

## 开发状态

仓库当前优先建立可验证的基础设施和最小实验，不提前把未证实的 CS2 runtime 机制写成正式架构。

尚未证实的代码进入 `experiments/`；通过实机验证并确认 failure boundary 后，再提炼到 production module。

项目只面向本地/离线使用，不以 Valve matchmaking、FACEIT 或公共 community-server cosmetics 为目标。
