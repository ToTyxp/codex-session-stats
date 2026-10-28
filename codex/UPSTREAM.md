# 来源

- Agent Speed：https://github.com/zoefix/agent-speed ，v1.4.1，提交 `a94a67fcc78e5d8eea5f82d8d5e737bccf994e2c`。Codex 日志提取、响应去重、增量文件读取及均速计算直接复用其核心代码。MIT 许可见 `LICENSE-Agent-Speed`。
- Codex Monitor：https://github.com/KevinKE93/Codex-Monitor ，提交 `56b2ea3602cf8395bdc9d0513a8088f8e38d657e`。`codex_cdp.py`、端口/App 发现和正常重启脚本来自该项目；MIT 许可见 `LICENSE-Codex-Monitor`。
- DeepSeek Harness Desktop 2.0.17：`@deepseek-ai/dsh-client-ui-chat` 的 `StatsPills`、`stat-dialog` 与 token/缓存命中格式化，以及 `dsh-client-ui-primitives` 的速度表、数据库图标。来源为本机已安装发行包；适配为轻量 DOM 组件，不加载 DSH 运行时。MIT 许可见 `LICENSE-DeepSeek-Harness`。
- `speed_badge.js`、统计记录桥接及按会话缓存接入为本次集成修改；保留原测速、动画、会话及多窗口识别逻辑。
