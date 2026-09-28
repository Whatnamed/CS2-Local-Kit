# Project-specific agent rules

本文件只记录 **CS2 Local Kit 长期稳定、跨任务适用的 Agent 规则**。  
当前任务、阶段进度、临时 blocker、某次测试结果、某个候选 SHA 或 Implementation Plan 不得写入本文件。

全局沟通、Git、工具与 Agent 约定不在这里重复。

## Canonical sources

- `docs/PRODUCT-SCOPE.md`：产品目标、长期行为和明确非目标。
- `docs/ARCHITECTURE.md`：模块边界、ownership 与失效隔离原则。
- `docs/UPSTREAMS.md`：第三方 dependency / reference / provenance 政策。
- `docs/LOCAL-WORKSPACE.md`：本机项目目录、worktree 与持久数据位置。
- `docs/MANUAL-ACCEPTANCE.md`：必须通过真实 CS2 实机验证的行为。
- `docs/COMPATIBILITY-WATCH.md`：CS2 / framework / upstream 更新后的兼容性核查流程。
- `CONTRIBUTING.md`：开发、worktree、commit、push 与交付规则。
- machine-readable runtime pin 以 `runtime/*.lock.json` 为准；文档不要复制一份会过期的精确版本事实。

旧 `Whatnamed/Local-Arena` 只作为 legacy reference。需要复用旧需求、数据或实现经验时必须按需审查，不得把旧仓库视为新项目 current truth。

## Local workspace

项目的固定本地布局见 `docs/LOCAL-WORKSPACE.md`。核心规则：

- 主源码工作区：`E:\Projects\CS2-Local-Kit\CS2-Local-Kit`。
- 额外 Git worktree 必须作为它的兄弟目录放在 `E:\Projects\CS2-Local-Kit\` 下。
- `E:\CS2MOD` 是持久的本地 CS2 项目数据区，不是 cache，也不是 disposable temp。预设、release、备份、诊断和必要 app data 可以长期保存在那里。
- 未经用户明确要求，不得批量清理、迁移或重置 `E:\CS2MOD`。

## Worktree isolation

- 非琐碎实现默认在独立 branch + worktree 中进行；主工作区保持可读、可审查的基线。
- Agent 只能修改明确分配给自己的 worktree。构建、测试、格式化和 Git 操作都必须以该 worktree 为 cwd。
- 不修改兄弟 worktree，不跨 worktree reset / clean / prune。
- 未经明确授权，不 force-push、不删除远端 branch、不重写共享历史。
- 开始和结束任务都报告：worktree path、branch、HEAD、`git status`。

## Remote-first review contract

用户通过网页 ChatGPT 从 **远端 GitHub** 复审实现，本地工作树不是复审事实来源。

因此一个需要代码/文档变更的任务只有在以下条件全部满足后才能宣称完成：

1. 必要验证已运行；
2. 变更已形成合理 commit；
3. branch 已 push 到 `origin`；
4. 本地工作树状态已报告；
5. 最终回复提供可供远端审查的 branch 和 HEAD SHA。

未 push 的本地修改不得描述为“已完成”“已交付”或要求用户直接验收。

## Runtime ownership boundaries

- Enhanced Bot runtime 是外部 pinned component；默认不在本仓库维护其完整源码 fork。
- Human cosmetics 是本项目拥有的独立能力。
- Human cosmetics 不得拥有或修改 Bot AI、Bot 数量、比赛重启、比分、换边、team lineup、Bot profile 或其他 match lifecycle。
- Bot runtime 与 Human cosmetics 的故障域必须隔离：饰品失效不能导致 Bot / TAB / score / normal match flow 一并变化。
- 未经明确证据，不把某个社区插件当前实现升级为项目 canonical architecture。
- 游戏更新敏感的 signature / native binding / schema 操作应集中在可替换的 compatibility boundary 内。

## Engineering discipline

- 先验证最小技术假设，再扩大实现。尤其是 knife entity identity、client model/animation、loadout identity 等，不能由静态推理代替实机证据。
- 不因为“测试能过”就宣称模型、动画、HUD、FPS、TAB、score 或换边行为正确。
- Agent 不自动启动 CS2；游戏内验收由用户执行。
- 不引入永久 Tick / Frame 高频扫描来补偿生命周期不确定性，除非有明确需求与性能证据。
- 安装、恢复、更新和文件 ownership 属于高风险区域；写入用户 CS2 环境前必须定义可恢复边界。
- 不为单一 bug 顺手重构无关模块，不做理论性无止境加固。
- 达到当前验收目标后应收口，而不是继续 audit → fix → audit 扩张范围。

## Documentation hygiene

- 当前任务计划、执行 Prompt、handoff、临时调查材料放 `temp/`，该目录被 Git 忽略。
- 某次 CS2 build、某日兼容性调查、一次实机失败等有长期追溯价值的证据，必要时放到 `docs/archive/` 并带日期；它们不是 current truth。
- release 说明只有在真实 release 值得长期记录时才建立；不要提前维护空的版本百科。
- canonical 文档只记录仍然成立的长期定义；发现过时时应修正原文，而不是在多个文档追加互相矛盾的“最新说明”。
