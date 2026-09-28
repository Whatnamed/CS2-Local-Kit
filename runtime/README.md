# Runtime locks

此目录保存外部 runtime component 的 machine-readable pin。

原则：

- lock 是当前 ref/hash 状态的事实来源；
- Markdown 文档不复制精确版本作为长期事实；
- `candidate` 表示尚未完成本机实机验收；
- 只有完成相关自动化与真实 CS2 验收后才能标记为 `accepted`；
- upstream 最新版本不会自动覆盖 accepted pin；
- 第三方二进制和完整源码默认不提交到本目录。

当前第一个外部 component 是 CS2-Bot-Improver。
