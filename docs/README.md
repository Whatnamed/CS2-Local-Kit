# Documentation map

文档按权威和寿命分层。不要把不同层级混在一起。

## Canonical / durable

这些文档描述当前仍然成立的长期事实：

- [PRODUCT-SCOPE.md](PRODUCT-SCOPE.md) — 产品目标、用户行为、功能边界与非目标。
- [ARCHITECTURE.md](ARCHITECTURE.md) — 模块边界、ownership、依赖方向和失效隔离。
- [UPSTREAMS.md](UPSTREAMS.md) — 外部 dependency、compatibility reference、catalog source 和 provenance 策略。
- [LOCAL-WORKSPACE.md](LOCAL-WORKSPACE.md) — 本机固定目录、worktree 与持久数据约定。
- [COMPATIBILITY-WATCH.md](COMPATIBILITY-WATCH.md) — CS2 / framework / upstream 变化时的核查流程。
- [MANUAL-ACCEPTANCE.md](MANUAL-ACCEPTANCE.md) — 必须通过真实游戏判断的长期验收项。
- 根目录 [AGENTS.md](../AGENTS.md) — 稳定 Agent 规则。
- 根目录 [CONTRIBUTING.md](../CONTRIBUTING.md) — 开发、Git、worktree、push 与复审流程。

machine-readable runtime pin 位于 `runtime/*.lock.json`。精确版本、commit、hash 等易变化事实优先放 lock，不在多个 Markdown 文件复制。

## Historical / dated evidence

未来如有必要，可建立：

```text
docs/archive/
```

用于保存某次 CS2 build 的兼容性调查、一次具有长期诊断价值的实机回归、重大架构迁移依据等。

规则：

- 文件名包含日期或明确版本；
- 历史证据解释“当时为什么这样判断”，不代表当前仍然成立；
- 不把普通每轮开发总结都沉淀成 archive。

## Release records

真实 release 如果需要长期说明，可建立 `docs/releases/`。二进制测试包和日常 Preview **不存进仓库**，放在 `E:\CS2MOD\releases`。

## Task-local material

`temp/` 是 disposable 工作区，适合：

- Implementation Plan
- execution prompt
- handoff
- upstream 临时 clone
- diff / log / diagnostic working copy

`temp/` 被 Git 忽略，不具备 canonical 权威。

## 文档维护原则

- 一个事实只选一个 canonical owner。
- 不通过“新增一份最新版说明”解决旧文档过时；应直接修正 canonical source。
- 当前任务、进度、blocker、候选 SHA 不进入 `AGENTS.md` 或产品/架构文档。
- 尚未通过实机验证的实现方案不得写成已确定的 runtime architecture。
