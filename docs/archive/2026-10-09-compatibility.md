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

## 验证与重放

- .NET SDK 10.0.401：Core/UI Release tests **173/173 PASS**。
- Controller Release build：**0 warnings / 0 errors**。
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
