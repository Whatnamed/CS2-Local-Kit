# Local workspace and persistent data

这是本项目在当前 Windows 开发机上的固定布局约定。

## 1. Source workspace

父目录：

```text
E:\Projects\CS2-Local-Kit\
```

主 clone：

```text
E:\Projects\CS2-Local-Kit\CS2-Local-Kit\
```

额外 worktree 与主 clone 同级，例如：

```text
E:\Projects\CS2-Local-Kit\
├─ CS2-Local-Kit\
├─ wt-cosmetics-lab\
├─ wt-bot-baseline\
└─ wt-<task>\
```

不要把 worktree 建进主 clone 的子目录。

主 clone 主要用于：

- 同步远端；
- 阅读；
- 创建/管理 worktree；
- 保持一个容易理解的基线。

非琐碎实现默认使用独立 worktree。

## 2. Persistent project data

```text
E:\CS2MOD\
```

继续作为 CS2 Local Kit 的 **持久本地运行与测试数据区**。

它不是：

- Git source root；
- disposable cache；
- Agent temp；
- 可以随手 `git clean` 或整目录删除的地方。

允许长期保存：

```text
E:\CS2MOD\
├─ presets\
├─ releases\
├─ backups\
├─ diagnostics\
├─ app-data\
└─ legacy\
```

以上是推荐分类，不要求为了目录整齐立即迁移现有文件。已经位于 `E:\CS2MOD` 根目录的个人 preset 可以原地保留，等真正有迁移需求时再处理。

## 3. Presets

用户现有 preset 是重要个人数据。

规则：

- 不自动删除、格式化或覆盖；
- schema 迁移采用 copy/convert，再验证，不原地破坏旧文件；
- release package 不应捆绑用户私人 preset；
- preset importer 应允许保留原文件作为回退。

## 4. Releases

实验和正式测试 ZIP 长期放：

```text
E:\CS2MOD\releases\
```

不要因为它们是 generated artifact 就默认塞进短生命周期 cache。

release 应尽量包含：

- 可读版本/实验名；
- source HEAD / upstream pin 可追溯信息；
- hash；
- 对应 restore/install 说明。

仓库不提交大体积 generated ZIP。

## 5. Backups

任何会修改真实 CS2 安装的 installer / experiment，在需要备份时使用：

```text
E:\CS2MOD\backups\
```

备份不能混进源码 worktree。

未来 installer 应维护明确 ownership metadata，使 restore 不依赖“猜哪些文件是我们改的”。

## 6. Diagnostics

有后续排障价值的：

- game / CSS log；
- signature scan；
- runtime snapshot；
- crash evidence；
- package manifest；

可保存在：

```text
E:\CS2MOD\diagnostics\
```

一次性中间文件仍应留在 repo 的 `temp/` 或系统临时目录。

## 7. App data

Controller 的本地持久状态（预设、备份、诊断、图片 cache）使用：

```text
E:\CS2MOD\app-data\
```

或经过明确设计的系统 app-data 位置。

不要每轮实验生成一个难以发现的新 cache 路径。

## 8. Legacy

旧 `E:\CS2MOD` 内容无需因为新仓建立就删除。

旧 Local-Arena clone、历史 release 或诊断如用户认为仍有参考价值，可以继续留存；是否移动到 `legacy/` 由实际需要决定，不强制整理。

## 9. Repo-local temp

源码仓内部：

```text
temp/
```

只用于可以丢失的 task-local 内容。

判断原则：

> 下周还可能需要直接找到并使用的用户数据，不应该只存在 temp/cache。
