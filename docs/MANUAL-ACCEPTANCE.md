# Manual in-game acceptance

自动化测试可以证明代码结构、配置转换、文件 ownership 和部分状态机性质；它不能证明真实 CS2 客户端呈现与比赛体验。

以下项目必须由用户在真实游戏中验证。

## 1. 验收记录规则

- Agent 不自动启动 CS2。
- 未实机执行的项目标记为“未验证”，不能根据代码推断为 PASS。
- 如果需要长期留档，一次具名版本/build 的结果可以写入 `docs/archive/`；本文件本身不记录当前 PASS/FAIL 状态。
- 每次测试尽量只改变一个变量，避免把 Bot runtime、Human cosmetics 和 installer 同时换掉后无法归因。

## 2. Bot baseline

在 HumanCosmetics 未加载的基线下验证：

- Bot 人数符合当前游戏/配置预期；
- Bot AI、aim、movement、nade 等核心行为正常；
- TAB scoreboard 正常显示；
- team score 正常累计；
- 换边/半场不会发生意外 `mp_restartgame` 或比分重置；
- 正常死亡、重生、回合切换；
- 连续若干回合无稳定 crash；
- Bot profile / cosmetics 只在预期模块启用时出现；
- FPS 与 frame pacing 可接受，没有新增持续卡顿或异常 long frame。

如果 Bot baseline 本身失败，不进入 HumanCosmetics 归因。

## 3. Gun cosmetics

逐项验证：

- weapon model/identity 与实际 loadout 一致；
- cosmetic preset 正确显示；
- 更换/购买/拾取后的行为符合当前产品定义；
- 重生后可靠；
- 不出现 P2000 model + USP-S skin 一类 identity/cosmetic 混合；
- Human cosmetic 失败不会影响 Bot 或比赛状态。

重点抽查至少：

- USP-S / P2000；
- M4A4 / M4A1-S；
- P250；
- 一把普通 rifle。

## 4. 普通枪械 identity（非目标验证）

普通枪械 identity 由 CS2 自己的 loadout 决定，cosmetics 层不参与。验证的是"cosmetics 没有改变 identity"这一否定性事实：

- 玩家在 loadout 选择 USP-S 时实际获得 USP-S（原生行为），其皮肤/cosmetic 按该真实 defIndex 呈现；
- 选择 P2000 时实际获得 P2000，同理；
- M4A1-S / M4A4 同理；
- cosmetic layer 不负责"把错误武器改成正确武器"，也不得重写普通枪械 defIndex；
- 换边和重生后保持正确。

## 5. Knife

当前 C4 的验收对象是"预设里选定的那一把刀"。用 `\` 在多个 knife defIndex 之间轮换属于尚未实现的 quick knife capability（PRODUCT-SCOPE 的 deferred 列表），因此下面的轮换序列只是**将来**该能力进入验收时的 gate，不能当作本轮 C4 必须已经实现的行为。

### 5.1 当前必须验证：选定的 knife

- 实际 knife identity 等于预设选定的 defIndex；
- first-person model 与 animation 属于该 knife；
- 该 knife 自己的 skin 正确呈现；
- HUD icon 正确；
- 重生与换边后保持正确；
- 无额外地面刀、无临时多余手枪；
- 无持续切刀 / 音效 / HUD 抖动；
- 不 crash。

### 5.2 未来 quick knife 的验收 gate（deferred）

默认轮换：

```text
507 → 515 → 508 → 500 → 525 → 512 → 507
```

每一步都分别检查：

- 实际 knife identity；
- first-person model；
- animation；
- 对应目标 knife 自己的 skin；
- HUD icon；
- 一次输入只前进一步；
- 无额外地面刀（除非某个已明确接受的机制设计需要）；
- 无临时多余手枪；
- 无持续切刀/音效/HUD 抖动；
- 不会第二次按键跳过；
- 死亡/重生后状态合理；
- 不 crash。

如果出现“目标刀动作 + 默认刀模型”之类情况，要分别记录 model / animation / skin，不要统一写成“刀坏了”。

## 6. Gloves

CT/T 分别验证：

- enable/disable；
- glove type；
- paint；
- 重生；
- 换边；
- 与 knife/gun 独立。

## 7. Music kit

验证：

- 配置 ID 正确呈现（具体 ID 由用户 preset 决定）；
- MVP / scoreboard 等相关显示按实际游戏行为检查；
- 不影响其他 cosmetics 生命周期。

## 8. Isolation / failure containment

至少做一次禁用或故障模拟：

- HumanCosmetics OFF 时 Bot baseline 与比赛行为不变；
- HumanCosmetics 初始化失败时不会修改 bot quota、score、team 或 restart game；
- Bot cosmetics disabled 时 Human cosmetics 不因此失效（除非明确共享的 framework 整体失效）。

## 9. Installer / restore

正式进入 installer 阶段后额外检查：

- install 前状态可识别；
- install 后只出现预期文件；
- restore 能回到安装前状态；
- 用户 preset / cfg / 第三方未知文件不丢失；
- Online/normal CS2 使用方式不被残留文件意外污染。
