# CosmeticsLab C2 — 个人 preset 实机测试步骤（由你手动执行）

> **当前安装状态**：C1.1 patched InventorySimulator（实机已验收）+ **你的个人皮肤配置**。
> 皮肤数据不再是 C1 的测试组合，而是从你自己的 `E:\CS2MOD\1.json` 迁移生成
> （链路：`1.json` → HumanPreset v1 → 投影 → 运行时；迁移报告见
> `E:\CS2MOD\diagnostics\cosmetics-lab\20260929-004716-preset-migration\migration-report.json`）。
> MetaMod 2.0 build 1469 / CSS 1.0.376 / patched DLL / gameinfo.gi / 实验专用 cfg 全部未动，
> 本轮只替换了皮肤数据文件。C2 实机验收已 PASS（见 `../ACCEPTANCE.md`）。

## 启动（与之前完全相同）

Steam → CS2 → 属性 → 启动选项：

```text
-insecure -novid +exec cosmeticslab_c1
```

正常启动进本地离线对局即可。

## 重点验证：游戏内表现是否 = 你的 1.json 配置

### CT 侧

- 枪皮（持枪/购买对应武器查看）：
  - 沙鹰 **Sunset Storm 壱**、双枪 **Hemoglobin**、AWP **Sun in Leo**、
    M4A4 **Buzz Kill**、UMP-45 **Fade**、电击枪 **Charged Up**、
    MP9 **Hot Rod**、P250 **Mehndi**、M4A1-S **Hot Rod**、USP-S **Dark Water**
- 刀：**Karambit（爪子刀）| 渐变之色（Fade）**，名字标签应为 **"蓝钢"**
- 手套：**Sport Gloves | Slingshot**

### T 侧（正常换边）

- 枪皮：沙鹰 **Sunset Storm 壱**（与 CT 相同）、双枪 **Hemoglobin**、
  Glock **Ghost Protocol**、AK-47 **Bloodsport**、AWP **Sun in Leo**（与 CT 相同）、
  UMP-45 **Fade**、Tec-9 **Fuel Injector**、电击枪 **Charged Up**、
  P250 **Mehndi**、SG 553 **Heavy Metal**
- 刀：**Butterfly（蝴蝶刀）| Autotronic**
- 手套：**Sport Gloves | Slingshot**（与 CT 相同配置）

### 音乐盒

**Austin Wintory《The Devil Went Clubbing In Georgia》**（ID 78）。上轮 C1 测试时游戏里
显示的是 28 号，本轮换成 78 后如果主菜单/回合音乐变成了这个包，即可确认音乐注入生效。

### 流程要求

- CT/T 各自检查完枪、刀、手套后，死亡重生至少 3 次再复查；
- 换边后手套不应消失；
- 不应有 HUD 抖动、反复发枪、声音循环、多余武器；
- FPS / 帧稳定性与上轮 C1.1 验收时一致。

**仍然不要测**：快捷换刀（`\`）、M4A1-S/M4A4 或 USP-S/P2000 的身份切换——这些属于后续阶段。

## 测完之后

把结果告诉 Agent（按"某把枪 / 刀 / 手套 / 音乐"指出哪一项对不上）。诊断收集照旧：
`Collect-CosmeticsLabDiagnostics.ps1`。

## 如需回退到上一轮 C1.1 测试皮肤

```powershell
Copy-Item -LiteralPath "E:\CS2MOD\backups\cosmetics-lab\20260929-000235-c2-fixture\inventories.json" -Destination "E:\CS2MOD\app-data\cosmetics-lab\inventory-simulator\inventories.json"
```

（然后把该文件再复制到
`游戏目录\game\csgo\addons\counterstrikesharp\configs\plugins\InventorySimulator\inventories.json`，
或者直接让 Agent 执行。）
