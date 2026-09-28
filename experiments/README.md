# Experiments

这里放尚未具备 production authority 的最小 runtime probe。

实验的目标是 **回答一个具体技术问题**，不是提前搭完整产品。

规则：

- 一个 experiment 尽量验证一个假设；
- 不因为实验代码已经复杂就直接提升为 production；
- 不在 experiment 中顺手实现 Panel、preset 全功能或 match lifecycle；
- 实验需要的临时 upstream clone / logs 放 `temp/`，不要 vendor 进来；
- 自动化测试与真实游戏观察分开报告；
- 成功的机制应在理解边界后重新提炼到正式模块，而不是简单把整个实验目录改名；
- 失败实验应保留必要 Git 历史即可，不持续叠加 fallback 形成第二个 legacy runtime。

适合这里验证的问题包括：

- existing gun cosmetic projection；
- USP-S/P2000 与 M4 loadout identity；
- knife entity creation / replacement；
- knife model vs animation vs skin；
- glove；
- music kit；
- 当前 CS2 build 的 compatibility binding。
