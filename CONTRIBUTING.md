# Development workflow

本项目的开发流程围绕 **独立 worktree、可重复验证、远端 GitHub 复审** 设计。

## 1. 固定目录

主仓库：

```text
E:\Projects\CS2-Local-Kit\CS2-Local-Kit
```

worktree 父目录：

```text
E:\Projects\CS2-Local-Kit\
```

持久运行数据：

```text
E:\CS2MOD\
```

完整职责见 `docs/LOCAL-WORKSPACE.md`。

## 2. 开始任务

先在主仓库同步远端：

```powershell
Set-Location E:\Projects\CS2-Local-Kit\CS2-Local-Kit
git fetch origin --prune
git status
git branch --show-current
git rev-parse HEAD
```

非琐碎实现使用独立 worktree，例如：

```powershell
git worktree add "..\wt-cosmetics-lab" -b "experiment/cosmetics-lab" origin/main
Set-Location "..\wt-cosmetics-lab"
```

branch 名按任务语义使用 `experiment/`、`feature/`、`fix/`、`maintenance/` 等前缀即可，不建立额外复杂命名体系。

## 3. 开发约束

- 只写当前 worktree。
- 不把第三方完整源码 tree 复制进仓库作为便利缓存。
- 临时 clone、调查文件、Agent prompt 和一次性输出使用 `temp/`。
- 真正需要长期保存的 release、preset、backup、diagnostic 不放 `temp/`，而使用 `E:\CS2MOD`。
- 不自动启动 CS2。
- 未经明确任务，不修改用户实际 CS2 安装。
- 如果任务涉及安装/恢复脚本，必须先定义文件 ownership、备份和精确恢复行为。

## 4. Commit 规则

提交按可理解的逻辑边界拆分；不要为了“干净历史”把一个复杂任务硬压成巨型单提交，也不要把每个小编辑拆成噪声 commit。

提交前至少执行与当前改动匹配的验证，并检查：

```powershell
git diff --check
git status --short
```

不要提交：

- `temp/`
- build cache
- 本机 preset / app data
- generated release ZIP
- 用户 CS2 安装备份
- 未经许可证审查复制来的第三方源码

## 5. Push 是完成条件

用户从网页侧通过远端 GitHub 审查实际实现，因此 **commit 但未 push 不算完成**。

任务结束前：

```powershell
git status
git log -1 --oneline
git push -u origin <branch>
```

最终报告必须包含：

- worktree path
- branch
- pushed HEAD SHA
- `git status`
- 运行过的验证
- 尚未进行的真实 CS2 实机验证

如果 push 失败，应报告失败，不得把本地 commit 当作已经可供用户审查的交付。

## 6. Remote review 之后

只有用户/网页复审确认后，才根据任务需要 merge、继续修复或收口。

不要因为一次 review 又自动启动下一轮“顺手优化”。如果原验收标准已经满足，停止。

## 7. 临时与历史材料

- `temp/prompts/`：长任务 Implementation Plan、执行 Prompt、handoff；Git ignored。
- `docs/archive/`：只有对未来维护确实有价值的、带日期的实机/兼容性证据。
- canonical docs：只保留长期有效定义，不记录任务流水账。
