# Agent Speed：Codex 内显示

基于 Agent Speed v1.4.1，在 Codex 输入框圆角边框外下方显示 DSH 式会话统计栏：`3 轮 103 步 · 247 tok/s` 和 `9M tok · 缓存命中 98%`。原生输入框与权限、Plan、Goal、模型按钮不改动；窄窗口单行省略，点击查看完整明细。

计量直接使用上游 CodexParser、Deduper、TranscriptReader 和 ResponseTiming：输出 token 已包含推理，耗时包含首 token 等待。沿用上游最短 500ms、最长 15 分钟的有效区间规则。每次有输出的有效调用完成后更新；等待、执行工具、新一轮开始时保留最后结果。它不是服务端纯 decode 速度。

每个会话独立保存结果，切回或重启后恢复。无缓存时回读该会话最近的有效完成调用；无有效数据显示横线。多个窗口各自跟随当前会话，不使用全局预测值或其他会话的数字。计量依赖该 host 的日志在本机可读取；无法读取的会话不会套用本机其他会话的值。

轮数按有已完成调用的执行 `turn_id` 去重；步数按完成模型调用去重，工具不另计步。累计 token 为输入＋输出，缓存已包含在输入中，推理已包含在输出中；缓存命中率为累计缓存输入÷累计输入。只计当前会话自身，不合并子智能体。统计独立于测速资格，所以零输出或没有有效耗时的合法用量仍计入总量。缺轮次标识时轮数显示横线；历史不完整会在详情说明。

两组均可点击展开详情，点击外部或按 Escape 关闭。会话详情解释轮步口径并显示最近调用的模型、耗时、输出、完成时间和速度；Token 详情显示精确总量、未缓存输入、缓存读取和输出。没有 TTFT 或工具耗时数据时不填造。数值动画尊重系统“减少动态效果”。

## 构建和安装

```sh
./scripts/setup.sh
python3 scripts/manage_live_speed.py install
python3 scripts/manage_live_speed.py status
```

Swift 6.0+ 和 Python 3.10+。Python 仅用标准库，无分词器依赖。

使用自定义 `CODEX_HOME` 时，安装命令会将它传给后台服务；后续升级会保留该配置。

运行目录：`~/Library/Application Support/CodexLiveSpeed`。仅一套 LaunchAgent：`local.codex.live-speed`。计量缓存为 `runtime/completed-calls.json`，兼容旧速度缓存；只存标识、模型和数字，不保存正文。历史回读重建统计后整体替换，不能在已恢复总量上再累加。

原生 JSONL 接口来自 `AgentSpeedCodexMeter`：原 `sample` 事件保持测速契约，新增 `stats` 事件包含 `hostId/threadId/turns/steps/inputTokens/cachedInputTokens/outputTokens/updatedAtMs/complete/source`。Python 路由将它作为显示快照的 `stats` 字段，与最近速度一起传给对应窗口。

保留 CDP 临时界面注入与自动重连。官方 App 文件和签名不修改。初次启用 CDP 仍可能需要一次正常退出/重开；后续升级测速器只重启自己的后台任务。

```sh
python3 scripts/manage_live_speed.py stop
python3 scripts/manage_live_speed.py uninstall
```

卸载会停止后台任务、清掉注入控件并移除 LaunchAgent；运行目录保留以便恢复。原始上游许可及界面代码来源见 `UPSTREAM.md`。
