# CosmeticsLab C1/C1.1 — 实机测试步骤（由你手动执行）

> **当前安装状态 = C1.1 patched probe**（patch 说明见 `c1_1-startup-lifecycle-patch/README.md`）：
> C1 exact-upstream 已判定 startup FAIL，本轮装的是只改一行 lifecycle 的 patched 构建
> （MetaMod 2.0 build 1469、CSS 1.0.376 不变）。

## C1.1 startup gate（第一次启动只看这四项，通过前不要做皮肤验收）

1. CS2 能正常启动、进入主菜单（不闪退）；
2. InventorySimulator 正常加载，CSS 日志里**不再出现** `Global Variables not initialized yet`；
3. 能进入本地离线对局；
4. 没有立即崩溃。

四项全过 → 继续下面的完整皮肤验收。任一项失败 → 停下，把现象告诉 Agent，不要继续。

---

安装已完成，**你不需要在游戏或 Steam 库存里预先设置任何皮肤**——所有测试用的皮肤数据（P250 皮肤、CT/T 刀、CT/T 手套、Music Kit 28）已经写进本地配置文件，由 InventorySimulator 在游戏内自动投影到你身上。

你唯一要做的"设置"只有下面这一步启动项。

## 启动

1. 打开 Steam → 库 → Counter-Strike 2 → 右键「属性」→「启动选项」，填入：

```text
-insecure -novid +exec cosmeticslab_c1
```

   - `-insecure`：本地离线模式必需；
   - `+exec cosmeticslab_c1`：**必填**，它会把实验用的防护 convar（假 API 地址、关闭所有联网功能）在你进服务器之前生效；
   - 测完这轮实验后，这串启动项可以删掉，不影响游戏本身。

2. 通过 Steam 正常启动游戏（本机没有装任何 Panel / 启动器，直接点开始就行）。
3. 只玩本地/离线对局。**测试期间不要连任何公共服务器或社区服务器。**
4. 开控制台（`~` 键）确认有一行：`[CosmeticsLab-C1] experiment convars applied`。没有这行说明启动项没生效，停下来告诉我。

本次安装内容（供核对）：MetaMod 2.0 build 1469、CounterStrikeSharp 1.0.376、InventorySimulator 3.3.0（原版上游，未改动）、本地皮肤配置文件、实验专用 cfg。**没有** Bot-Improver、没有 Local-Arena、没有其他任何插件。

## 基线稳定性（先看这个，再看皮肤）

- 进图后先感受 FPS / 画面流畅度是否正常，再去检查皮肤；
- 走动、打几个回合、重生几次；
- Bot 应该是原版行为（名字带原生"电脑玩家"前缀），没有任何增强 Bot 的迹象。

## CT 侧检查（逐项单独看）

- 测试枪：买/拿一把 **P250**，应该显示 **Asiimov（二西莫夫）** 皮肤，枪本身仍是普通 P250；
- 刀是真正的 **爪子刀（Karambit）**，第一人称模型正确、动画正确、显示 **渐变之色（Fade）**；
- 手套：**运动手套 | 潘多拉魔盒（Sport Gloves | Pandora's Box）**；
- 不允许出现：HUD 抖动、反复发枪、声音循环、多出额外的刀。

死亡重生至少 3 次，每次都复查一遍。

## T 侧检查（正常换边即可）

- P250 换成 **See Ya Later** 皮肤（不是 CT 那把的状态）；
- 刀是真正的 **蝴蝶刀（Butterfly）**，模型/动画正确，显示 **虎牙（Tiger Tooth）**；
- 手套：**驾驶手套 | 血绯纹理（Driver Gloves | Crimson Weave）**——和 CT 明显不同，且换边后手套不能消失。

死亡重生至少 3 次，每次都复查一遍。

## 音乐盒

Music Kit 28（New Beat Fund — Sponge Fingerz）：看主菜单/回合开始/MVP 时刻能观察到的地方是否生效。

## 稳定性浸泡

再多打几局，注意记录任何异常：崩溃、FPS 下降、模型/动画不对、皮肤材质缺失、换边或重生后手套丢失、可疑的联网行为、控制台里的 CSS/原生报错。

**C1 不要测**：快捷换刀（`\`）、M4A1-S/M4A4 切换、USP-S/P2000 切换——这些明确不在本轮范围。

## 测完之后

把结果告诉 Agent（如果某一项不对，按"枪 / 刀 / 手套 / 音乐 / 框架"哪一类出问题来说）。诊断收集（`Collect-CosmeticsLabDiagnostics.ps1`）由 Agent 跑或你跑都行，证据会落在 `E:\CS2MOD\diagnostics\cosmetics-lab\<时间戳>-posttest\`。

## 如需完整还原

```powershell
pwsh -NoProfile -File scripts\cosmetics-lab\Restore-CosmeticsLab.ps1 -BackupDir <安装时打印的备份目录> -Apply
```
