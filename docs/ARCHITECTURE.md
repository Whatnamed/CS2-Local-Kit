# Architecture

## 1. 总体原则

CS2 Local Kit 采用 **组合而不是大 fork** 的结构。

```text
                  ┌───────────────────────────┐
                  │       Local controller     │
                  │   install / config / UX    │
                  └─────────────┬─────────────┘
                                │
                  ┌─────────────▼─────────────┐
                  │     Assembly / Installer   │
                  └──────────┬───────┬────────┘
                             │       │
                ┌────────────▼─┐   ┌─▼─────────────────┐
                │ Bot Runtime  │   │ HumanCosmetics    │
                │ external pin │   │ project-owned     │
                └──────────────┘   └─────────┬─────────┘
                                             │
                                  ┌──────────▼──────────┐
                                  │ Compatibility layer │
                                  │ schema/native/sigs  │
                                  └─────────────────────┘
```

Bot 与 Human cosmetics 必须是两个独立 failure domain。

## 2. Bot Runtime

Bot runtime 是外部组件，不是本仓库的主源码树。

默认策略：

- 通过 machine-readable lock pin repository/ref/hash；
- package/install 阶段获取并校验；
- upstream 更新先独立验证，再更新 pin；
- 默认不 submodule、不 vendor 整仓、不长期维护 merge-heavy fork。

只有在有明确、长期、无法通过配置或 upstream 修复解决的问题时，才允许建立最小 patch queue；patch 必须独立记录来源和必要性。

## 3. HumanCosmetics

HumanCosmetics 是本项目拥有的独立插件。

它可以拥有：

- human preset domain；
- gun cosmetic projection；
- loadout identity adapter；
- knife rotation orchestration；
- glove；
- music kit；
- Human cosmetics 所需的 compatibility bindings。

它 **不能拥有**：

- Bot AI；
- Bot quota；
- Bot profile；
- match restart；
- score；
- halftime/team switch；
- team lineup；
- match/statistics framework。

如果 HumanCosmetics 因 CS2 更新无法安全初始化，应 fail closed：禁用自身并给出明确诊断，而不是通过反复 retry 或修改比赛状态“自救”。

## 4. Compatibility boundary

所有高更新敏感操作应尽量集中，例如：

- native function signature；
- schema-dependent field access；
- low-level econ attribute writer；
- game-build-specific behavior probe。

业务代码不应到处散布 hard-coded signatures 或版本判断。

兼容层应具备：

- startup validation；
- 清晰 capability 状态；
- 不支持时停止对应能力；
- 足够日志帮助比较社区参考实现。

## 5. Knife identity 与 cosmetic projection 分离

刀型 identity、model/animation 与 paint application 是不同问题。

当前架构只确定以下原则：

- 先获得一个真实、可靠的目标 knife identity；
- 再对目标 knife 应用其 preset；
- 不因为某个社区实现使用 `ChangeSubclass`、Kill/Give 或 `subclass_create` 就把该机制写死进 architecture。

具体机制必须通过 `experiments/` 的最小 probe 和真实 CS2 验证后再进入 production plugin。

## 6. Gun loadout identity 与 skin 分离

枪械同样分两层：

```text
LoadoutIdentity
    ↓
实际 USP-S / P2000 / M4A1-S / M4A4 entity
    ↓
GunCosmetics
    ↓
只修改该真实 weapon 的 cosmetic state
```

GunCosmetics 不得通过改 defindex 来修复错误 loadout。

## 7. Installer / ownership

安装器未来必须显式知道自己拥有的文件。

原则：

- 不把整个 CS2 目录视作项目 ownership；
- 不覆盖未知第三方文件和用户 cfg；
- 写入前可备份；
- restore 只恢复/删除本项目明确拥有或记录过的文件；
- release 与 restore 产物使用 `E:\CS2MOD` 持久目录；
- 安装失败不应留下半套不可识别 runtime。

## 8. Panel

Panel 是配置与操作界面，不是 runtime coordinator 的隐式状态机。

Panel 允许：

- 编辑配置；
- 显示 runtime / compatibility 状态；
- 调用显式 install/restore/package workflow；
- 打开 releases/diagnostics。

Panel 不通过“隐藏模式切换”批量启停无关插件来制造产品状态。

## 9. 数据与 catalog

项目自己的 preset schema 与第三方 catalog 分离。

第三方 catalog 可以生成本地只读数据，但必须记录来源和版本。用户 preset 只保存稳定 ID，不依赖本地化字符串。

## 10. 实验到生产

尚未证实的 runtime 机制先进入 `experiments/`。

只有当：

1. 最小 probe 可重复；
2. 实机行为符合预期；
3. failure mode 可控；
4. 没有跨域副作用；

才将该机制提炼到 production plugin。

实验代码不得因为“已经写了很多”自动获得架构权威。
