# 2026-10-09 CS2 compatibility investigation

执行基线：远端 `Whatnamed/CS2-Local-Kit main@b50754cbbfbf03cd4f9650b844965d537b3e86c2`。
worktree：`E:\Projects\CS2-Local-Kit\wt-compat-20261009`；branch：`codex/compat-20261009`。
本归档记录本次证据；accepted/candidate 状态仍由 runtime locks 定义。

## 必须区分目标 build 与本机 build

用户指定目标为 `1.41.9.0 / BuildId 25815307`，但本机实际安装的
`steam.inf` / `appmanifest_730.acf` 是 `1.41.8.9 / ClientVersion 2000927 / BuildId 25738536`。
检测路径为 `E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive`，由有效 Steam library、
appmanifest 和 cs2.exe 定位。未启动 CS2，也未更新 Steam 或更改机器网络配置。

实际 `game/csgo/bin/win64/server.dll` SHA256：
`c98cc7ea3095a32683fc37fea804537b3ea6d89836fd4f5e55d593a82bb54ee3`。
本次所有 binary probes 都只能证明 **1.41.8.9 的静态结果**，不能证明 1.41.9.0 兼容。

## Human：只准备两条 Windows signature 更新

对比 [accepted source fade4449](https://github.com/ianlucas/cs2-css-inventory-simulator/tree/fade4449aaa6d5153261c855d0cc12d7dacfefdd)
与 [3.5.2 / 3c7eec5](https://github.com/ianlucas/cs2-css-inventory-simulator/tree/3c7eec5b923eba202543c425e1cf25555219546d)
的 `gamedata/inventory-simulator.json`。两条 Windows signature 变化如下；其他既有 Windows patterns 不变。

| Signature | 3.3.0 pattern | 3.5.2 pattern | 本机旧/新匹配数 |
|---|---|---|---|
| CCSPlayer_ItemServices::SetWearables | `40 55 41 56 48 83 EC ? 48 83 79` | `40 55 56 48 83 EC ? 48 83 79` | 1 / 0 |
| CCSPlayerPawn::SetModelFromClass | `48 89 5C 24 ? 56 57 41 56 48 83 EC ? 48 8B D9` | 同前缀追加 `48 81 C1` | 1 / 1 |

完整旧 gamedata 14/14 Windows patterns 唯一匹配；完整新 gamedata 17/18 唯一匹配，唯一零匹配为新 SetWearables。
完整新 gamedata 还含 Linux signature 更新和 chicken/pet 新条目；本候选不引入这些变更。
`SetModelFromClass` 在本机新旧 pattern 对应同一 RVA；`SetWearables` 的零匹配阻止当前安装。
扫描报告含每条原始 pattern、library、匹配数、RVA、file offset 和 binary hash：
`2026-10-09-compatibility/human-signature-scan.json`。

已提交最小 patch：`experiments/cosmetics-lab/inventory-simulator/compatibility/cs2-14190-windows-signatures.patch`。
在精确旧 gamedata 上 `git apply --check` 和 replay 通过，输出内容仅改变两条 Windows 字符串。
生成的 gamedata-only candidate 位于：
`E:\CS2MOD\releases\cosmetics-lab\inventory-simulator-gamedata-20261009-14190\inventory-simulator.json`，
SHA256 `f3d9facb4089fa1876438b247942f48da6f56e669e5a9c551df1c1ed6623c27a`（LF）。
目录中的 manifest 明确 `installed=false` 和 target static validation blocked。
Windows Git replay 可能转换 CRLF；内容按 LF 归一化后与候选 hash 一致。

没有覆盖已安装 gamedata 或 DLL；未改 MetaMod git1473 / CSS 1.0.376。
本机六个 framework/plugin identity 均与 lock 完全一致，包括 C1.1 patched DLL：
`3a807316195b5c0d31d7aee5490128c36a994eb8764528717097d2ad636d8489`。
已安装 gamedata 与 pinned 3.3.0 JSON 内容等价。
`gameinfo.gi` 的 SearchPaths 已有且仅有一个有效 MetaMod entry，所以不需要执行 repair。
完整身份与 startup 检查见 `2026-10-09-compatibility/local-facts.json`。

历史 `accepted-as-patched` / `testedCs2Build=1.41.8.8` 保留。
目标 `1.41.9.0` 单独记录为 **pending-real-game-retest**，且静态 target gate 因 binary 缺失未通过。
必须在目标 build binary 上重新扫描成功后才可应用 gamedata；之后仍需用户实机检验枪、刀、手套、音乐盒、
respawn、side change、HUD/audio、crash 和性能。没有用户实机证据不能标记 accepted。

## Bot：审查 main，保留现有 candidate

[umbrella main 72adfc2](https://github.com/ed0ard/CS2-Bot-Improver/compare/9848ac892653db6a256c32524dddf6a05cf4383c...72adfc283faad3b8fbe974533d55c49d48691333)
相对现有 candidate 9848ac8 前进 29 个 commits。精确 gitlinks 与组件 diff/commit 清单见
`2026-10-09-compatibility/bot-upstream-review.json`。

| Component | umbrella ref | 相对旧 candidate 的主要变化 |
|---|---|---|
| BotController | 0ae8f18 | 0.7.1：Linux spdlog ABI/build 修复；Windows gamedata 未变 |
| BotAI | a23f7e2 | Windows patch targets/expected bytes、game-state/FOV/offset 修正及 Linux patches；API 更新 |
| BotState | ce28e5c | 拆分源码、signature 更新、自定义 3D FOV/visibility hooks、Rush antenna；API 更新 |
| BotAimImprover | 17e895b | signature 更新、body mode HSR、target selection、API 更新 |
| BotBuy | 11884e0 | delayed timer controller validity、Rush 支持、API 更新 |
| BotRandomizer | 024f314 | signature/catalog 自动维护改造；当前 Windows 两条内嵌 bindings 仍与旧 pin 一致 |
| NadeSystem | 009ce03 | projectile Create signatures 更新 |
| BotHider / BotVision | 12069c2 / 33ab01f | gitlinks 未变 |

单独组件最新 main 尚未完全整合进 umbrella：

- [BotController 4d8441a](https://github.com/XBribo/CS2-Bot-Controller/compare/0ae8f18fd6872a369cb984e0e95e5a352be092fe...4d8441a13fc8f2eeeae3a97bfe1ede928aeb7126)：
  普通 weapon selection 尊重已有 hooks，内部 replay 才用 raw bypass；没有 gamedata 变化。
- [BotRandomizer 7b168a3](https://github.com/ed0ard/CS2-Bot-Randomizer/compare/024f314b782433daf1150eba700a7b381e99baa1...7b168a30b9976f4a1de344d87c72c9bddf8c60da)：
  commit 标题虽为 Update signatures and cosmetic database，实际 diff 只有 catalog 和两份 package 文件；
  `BotRandomizer.cs` 在两 refs 的 blob SHA 都是 `9783a460252980df2617291d43508a98cc340d5b`。
  不能把标题当成 Windows signature 已修复的证据，也不把这两项擅自混入 umbrella pin。

实际旧 build 上 107/107 Windows patterns 唯一匹配：29 条 native Bot gamedata、28 条 installed CSS、
40 条 BotAI patch signatures、4 条 BotState FOV signatures/patches、1 条 aim、2 条 randomizer、3 条 nade bindings。
BotAI **40/40** 和 BotState **2/2** patch 位移处原始字节匹配（含 wildcard），只是只读检查，未写 patch。
完整输入、RVA 与 expected/actual bytes 见 `2026-10-09-compatibility/bot-signature-scan.json`。
这扩大了此前 JSON-only probe 的覆盖，但仍不能证明目标 build、schema offsets 或实际 hook 行为。
BotAI a23f7e2 精确源码（GitHub blobs；临时源码未提交）Release build 通过，0 warnings / 0 errors。
没有对全套 Bot/native runtime 宣称 build 或运行验收通过。

[#172](https://github.com/ed0ard/CS2-Bot-Improver/issues/172) 和
[#181](https://github.com/ed0ard/CS2-Bot-Improver/issues/181) 查询时仍为 open，分别报告 Windows listen-server
stutter 和 FPS/1% low 问题。#172 的日志含 rendering stall 和额外 RayTrace 错误，不能直接归因到本项目组件；
#181 报告关闭 mod 后不发生。结合本项目既有同地图回退对照，performance gate 仍未通过；
upstream 更新没有提供本机受控性能验收证据。

结论：**尚不值得把这套 main 作为可直接进入下一次实机 baseline 的候选包**。
目标 build binary 缺失，静态 target gate 未过；未准备新的 Bot artifact、未安装、未接受新 baseline。
保留现有 `ref=9848ac8`、artifact、manualObservation、`status=candidate`、`testedCs2Build=null`；
新增独立 upstreamCompatibilityReview，避免把审查中的 main 和历史 artifact 身份混在一起。

## 验证与重放

- .NET SDK 10.0.401：Core/UI Release tests **173/173 PASS**。
- Controller Release build：**0 warnings / 0 errors**。
- BotAI a23f7e2 Release build：**0 warnings / 0 errors**；40 条 patch 原始字节静态检查通过。
- gamedata patch apply/replay、JSON 差异范围检查通过；所有实机 binary 保持原样。

静态报告可直接重放。先从报告 `results` 提取输入（下面以 Human 为例），再运行 scanner：

```powershell
$report = Get-Content docs/archive/2026-10-09-compatibility/human-signature-scan.json -Raw | ConvertFrom-Json
$report.results | ConvertTo-Json -Depth 12 | Set-Content temp/human-patterns.json -Encoding utf8NoBOM
python scripts/compatibility/scan-windows-signatures.py --game-root '<CS2>/game' --patterns temp/human-patterns.json --output temp/human-rescan.json
```

scanner 为只读 diagnostic，输出所有重叠匹配，扫描范围只含 PE executable sections。
其成功退出表示扫描执行成功；不表示每个 pattern 唯一或 target build 正确，必须审查报告。
不验证 vtable/offset/schema、native ABI、加载次序、运行时 hook、游戏渲染或性能。
pattern 字符串是上游兼容性证据，未 vendor 完整第三方实现；来源许可证 Human MIT、Bot AGPL-3.0。
