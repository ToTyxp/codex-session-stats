# WorkBuddy — research notes

Evidence for METRICS 4.9 and the WorkBuddy rows of METRICS 10.5. Written 2026-10-23 from a static reading of both
editions' shipped JavaScript; nothing from either app was executed and no real session file was available. Re-check the
rules on a machine that uses WorkBuddy with
`python3 spec/tools/reference.py --dump-today --workbuddy ~/.workbuddy` (or `--workbuddy-ai ~/.workbuddy-ai`) and the
verification list at the end.

| Edition | Homebrew cask | Version read | DMG sha256 | Bundle id |
| --- | --- | --- | --- | --- |
| International, `WorkBuddy AI` | `workbuddy-ai` | 5.8.4.41366630 | `e5ef3f91…1133` | `com.workbuddy.workbuddy-ai` |
| China, `WorkBuddy` | `workbuddy-cn` | 5.8.3 | `ecd404f1…e626` | `com.tencent.workbuddy.mac` |

## Structure

- An Electron app (`app.asar` → `@genie/workbuddy-desktop`); the main executable is `Contents/MacOS/Electron`, helpers
  are `WorkBuddy AI Helper (…)` / `WorkBuddy Helper (…)`.
- The agent is an embedded CodeBuddy Code CLI (`@tencent-ai/codebuddy-code` 2.161.0) in
  `Contents/Resources/app.asar.unpacked/cli/` (`bin/codebuddy`, `dist/codebuddy-lite-wb.mjs`, `dist/lazy-lite-wb/`).
- The two editions' CLI bundles are byte-identical once a CI build path and a set of UUIDs are normalized; 172 of 175
  desktop `main/*.js` files are identical. The editions differ in `cli/product.json` only: product name, data folder
  (`.workbuddy` / `.workbuddy-ai`), service (workbuddy.cn / workbuddy.ai), Windows executable name (`WorkBuddy` /
  `WorkBuddyAI`) and the built-in model catalog. Both ship the same icon (a white cat over a teal-green rounded square,
  `#0EC8A8` → `#05C890`).

## Data folder

- `resolveWorkbuddyConfigDir()` = `WORKBUDDY_CONFIG_DIR` ‖ `CODEBUDDY_CONFIG_DIR` ‖ `~/<dataFolderName>`; `~` is
  `os.homedir()` (`%USERPROFILE%` on Windows). The main process sets both variables on itself at start-up
  (`configureElectronApp`) and passes them to its children; the installer and `Info.plist` (`LSEnvironment` has only
  `MallocNanoZone`) do not set them.
- Electron's user data is `<data folder>/app`. Older Windows releases used fallback folders
  (`C:\ProgramData\WorkBuddy\users\<hash>\.workbuddy`, `C:\WorkBuddy-env\<hash>\.workbuddy`,
  `%WORKBUDDY_FALLBACK_HOME%\.workbuddy`); 5.8 merges them into the data folder at launch (`discoverFallbackDirs`).
- Other content: `workbuddy.db` (SQLite: sessions, context usage, cloud conversations), `traces/<pid>/*.jsonl`
  (OpenTelemetry spans, possibly encrypted), `sessions/<pid>.json` (process registry), `usage-data/`, `audit-log/`,
  `logs/`, `models.json` (the user's own models), `settings.json`. Only credential fields are encrypted at rest.

## Session files

- `SessionStore` / `LocalJsonlSessionStore`: `<data>/projects/<slug>/<sessionId>.jsonl`; `<slug>` = the working
  directory with `/`, `\` and `:` replaced by `-`, leading/trailing `-` removed and runs collapsed (over 255 bytes:
  cut to 180 bytes plus `-<djb2 base36>`).
- Sub-agents: `<slug>/<parentRuntimeSessionId>/subagents/agent-<uuid>.jsonl`. This version never writes
  `agent-<id>.meta.json` (it only reads it as legacy metadata, or deletes it). Not transcripts: `<sid>/tool-results/`,
  `<sid>/subagents/workflows/wf_<id>/journal.jsonl`, `<sid>.meta.json`.
- Cloud mirrors: `projects/cloud/user-<conversationId>/<conversationId>.jsonl` (desktop `CloudPersonalCbcWriter`),
  rebuilt from the service; all records of a request share `finishTimestamp ?? completedAt`, usage is per request and
  reconciliation rewrites the file.
- Writes: `open(path, "a")`, one line per `addHistory`; a failed append is truncated back or the file removed. The CLI's
  30-day cleanup is disabled by the app (`CODEBUDDY_DISABLE_SESSION_HISTORY_CLEANUP=1`); the desktop's data-retention
  setting defaults to keeping everything (30/60/90 days optional).

## Line format (`transformItemForSaveAsync`)

Keys in order: `id, parentId, logicalParentId, timestamp, type, role`, then the item. `timestamp` is `Date.now()` (a
number). Added: `cwd` (every line except `summary` and `topic`; `session-meta` is written directly without it),
`sessionId` (the runtime session id, on every non-custom line), `message.usage` on message and function_call lines
that carry `providerData.usage`, and `_meta` (trace context).

| Line | Fields read by the parser | Other fields |
| --- | --- | --- |
| user `message` | `role`, `providerData.skipRun` | `content`, `conversationRequestId`, `agent`, `isMeta`, `startsNewUserRequest`, `queueItemId`, `createdAt` |
| assistant `message` | `status`, `providerData.{messageId, model, requestModelId, usage, rawUsage, agent, skipRun, error, isPartialAborted}` | `content`, `requestModelName`, `stepSeq`, `traceId`, `conversationRequestId` |
| `function_call` | `callId`, the same `providerData` fields | `name`, `arguments` |
| `function_call_result` | `callId`, `status`, `providerData.skipRun` | `output`, `providerData.toolResult` (with `subAgent.sessionId` for sub-agent tasks), the producing call's `messageId`, `model`, `requestModelId` |
| `reasoning` | `providerData.messageId` (never usage) | `content: []`, `rawContent` |
| `model-usage` | `providerData.{messageId, model, requestModelId, usage, rawUsage, agent}` | `conversationRequestId`, `stepSeq`, `requestModelName`, `traceId` |

Never read: `session-meta` (`meta` with `codebuddy.ai/mainAgent`, `codebuddy.ai/hostKind`, `multitaskMode`,
`codebuddy.ai/a2aContexts`), `credit-usage` (`credit`, `source`, `stageId`, `messageId?`), `summary`, `ai-title`,
`custom-title`, `topic`, `file-history-snapshot`, `goal-*`, `resend-fork-notice` (`skipRun`), `turn-metrics`
(`durationMs`, `tokenDelta`; written only for interactive TUI sessions, never by WorkBuddy's `--serve` runtime),
`acp-terminal-state` (recognized but never written).

System-made items default to `skipRun: true`: `createUserMessage`, `createAssistantMessage` (interruptions and errors,
with `status: "incomplete"` and `providerData.error`), `createHookMessage` (assistant role, hook output),
content-filter notices. Tool results default to `skipRun: false`; rejected, cancelled and filled-in results set
`skipRun: true`. A tool whose output is a JSON error gets `status: "incomplete"` without `skipRun` and the turn
continues. The Stop hook's continuation and the error-recovery reminder are user lines with `isMeta: true` and no
`skipRun`: a request follows them.

## Usage

- `handleStreamRunResult` stamps every output item of a call with `messageId` (32 hex, one per
  `getStreamedResponse`, sent as `X-Request-ID`), `model` (the stream chunk's model, else the requested id),
  `requestModelId`, `requestModelName`, `stepSeq`, `traceId`; the last output item also gets `usage` (agents-core
  `Usage`: `inputTokens`, `outputTokens`, `totalTokens`, `inputTokensDetails[{cached_tokens}]`,
  `outputTokensDetails[{reasoning_tokens}]`) and usually `rawUsage` (the chunk's OpenAI-style usage: `prompt_tokens`,
  `completion_tokens`, `prompt_tokens_details.cached_tokens`, `completion_tokens_details.reasoning_tokens`, and
  depending on the model `cache_read_input_tokens`, `cache_creation_input_tokens`, `prompt_cache_hit_tokens`,
  `prompt_cache_miss_tokens`, `credit`).
- `model-usage` is built at `response_done` (`timestamp: Date.now()` then) and appended in the run's `finally` block
  when no output item already carries both `usage` and `rawUsage` for that `messageId`: for calls without output items
  and for calls whose raw usage arrived after their last item was written. Its `rawUsage` falls back to the agents-core
  usage when the chunk had none.
- Input convention: the CLI's cost code computes net input as `inputTokens − cacheRead − cacheWrite` (clamped at 0);
  `getCurrentContextUsage` treats `inputTokens` as including cache when `rawUsage` has OpenAI fields.
  `getCacheReadTokens` = first positive of `cache_read_input_tokens`, `cacheReadInputTokens`, Σ
  `inputTokensDetails[].cached_tokens`, `prompt_tokens_details.cached_tokens`, `prompt_cache_hit_tokens`;
  `getCacheCreationTokens` = first positive of `cache_creation_input_tokens`, `cacheCreationInputTokens`,
  `prompt_cache_write_tokens`, `prompt_cache_miss_tokens` — the last is DeepSeek's uncached input, which Agent Speed
  does not count as a cache write.
- Compaction runs as the `compact` agent in the same file (`providerData.agent == "compact"`). Pre-message compaction
  (interceptor priority 1650) runs before the History step (1400) that appends the user line.
- Not in any transcript: the host runtime (`--serve --no-session-persistence`: prompt enhancement, replays) and title
  calls.

## Timing

- No request start, first-token time or per-call duration is persisted (`SseStreamMonitor`, `firstTokenAt`, `ttft`,
  `lastRequestStartTime` live in memory and telemetry).
- User line: timestamp at `ensureHistoryItemTimestamp`, appended after `UserPromptSubmit` hooks and a checkpoint;
  further interceptors (memory context and others) run before the HTTP request.
- Output items: agents-core emits run items after `response_done` (`processModelResponse`, then
  `streamStepItemsToRunResult`); each is appended with its own `addHistory`, milliseconds apart.
- Tool results: `executeFunctionToolCalls` awaits all tools of a step (`Promise.all`); results are stamped when
  consumed, so the next request can already be on its way.
- Errors: an error notice, then for retryable errors (network, stream timeout, parse errors, model behaviour) an
  `isMeta` error-recovery user line and a new call with a new `messageId`; the OpenAI client has `maxRetries: 0`, so
  failed attempts write no usage unless `response_done` arrived. Interruptions write partial drafts
  (`status: "incomplete"`, `isPartialAborted: true`, no usage), then "Interrupted by user"; with tools pending,
  `skipRun` results instead.
- No end-of-turn line: the turn ends with the final assistant message, possibly followed by hook output, `ai-title`
  and end-of-run `model-usage` lines. A tool waiting for permission writes nothing until the step's results.

## Forks and sub-agents

- Forks copy the source's lines with new `id`s and remapped `parentId`s, keeping `timestamp`, `providerData` and
  `cwd`; the root fork drops `sessionId`. Its `session-meta`, if any, follows the copy and has only `meta`
  (`forkedAt`, `forkedFrom` exist in memory only).
- A sub-agent session keeps `agentName`, `parentSessionId`, `subagentRootSessionId` and `taskName` in memory only.
  Its lines carry the sub-agent's runtime `sessionId` (not its file name) and `providerData.agent`. The parent's Agent
  tool result has `providerData.toolResult.subAgent = {sessionId: "agent-<uuid>", afterId, lastId, descriptor}`.

## Models

- International catalog (30 entries): slots `default-model` (Default), `fast-model`, `balanced-model`,
  `primary-model`, `deep-model`; `deepseek-v4.1-flash`, `deepseek-v4.1-flash-sg`, `gpt-6-astra`, `gpt-5.6-sol`,
  `gpt-5.6-terra`, `gpt-5.6-luna`, `gpt-5.5`, `gpt-5.4`, `gemini-3.5-flash`, `glm-5.3`, `glm-5.3-flash`, `glm-5.2`,
  `kimi-k3`, `kimi-k2.8-preview`, `kimi-k2.6`, `hy3`, `hy4-preview`, plus image and video models.
- China catalog (58 entries): `auto` (default), `default`, DeepSeek V4 and V3 variants (`deepseek-v4-pro`,
  `deepseek-v4-flash`, `deepseek-v4.1-flash`, `deepseek-v3-2-volc`, `deepseek-v3-1`, `-volc`, `-lkeap`),
  `minimax-m2.5`, `minimax-m2.7`, `minimax-m3-pay`, GLM 4.6 – 5.3 (`glm-5.3-flashx`, `glm-5.0`, `glm-5.0-turbo`,
  `glm-5v-turbo`, `glm-4.6v`, …), `kimi-k3-2`, Kimi K2.x, `hy3`, `hy3-x`, `hy4-preview`, `hy4-preview-x`,
  `hy3-preview`, Hunyuan models, `default-1.1` ("Claude-3.7-Sonnet"), `default-1.2` ("Claude-4.0-Sonnet") and legacy
  completion models.
- `AUTO_MODEL_IDS = {auto, auto-chat, fast-model, balanced-model, deep-model}`; ids starting with `default-model` are
  placeholder slots; with `EnableAutoModelTiers`, `auto` expands into the tiers. The service resolves them; whether
  the stream then reports the chosen model is not visible in the code. The catalog is replaced at run time by remote
  configuration (`/v3/config`), and no entry has an upstream id. `vendor` is an opaque code.
- Suffixes are not explained in the code. Reading: `-volc` Volcano Engine, `-lkeap` Tencent Cloud LKEAP, `-sg` the
  Singapore region, `-taiji` Tencent's Taiji platform (hosting); `-x`, `-pay` other billing channels; `-1`, `-2`
  deployment slots (the international `kimi-k3` lists `kimi-k3-1` as related).
- `custom-local:<id>`: models from the user's `models.json` when the `CustomModelIdPrefix` feature is on (the original
  id becomes an alias, the prefix is stripped from the request). A `custom:` prefix constant is treated as user-owned;
  how it is built was not found.
- Prices added for Agent Speed (2026-10-23): `hy3` ($0.132 / $0.033 cached / $0.528) and `hy4-preview` ($0.834 /
  $0.042 / $2.501) from Tencent Cloud's TokenHub price list (https://www.tencentcloud.com/document/product/1300/78937,
  API ids from https://www-sg.tencentcloud.com/document/product/1300/80632); `glm-4.6v` ($0.3 / $0.05 / $0.9) from
  Z.ai. Not added: GLM-5-Turbo, GLM-5V-Turbo, Kimi K2.8 Preview and DeepSeek V3.x — their only USD prices are Tencent's
  resale prices, not the providers' own.

## Processes

- macOS: main `Contents/MacOS/Electron`; a daemon, the same executable with `ELECTRON_RUN_AS_NODE=1` running
  `app.asar/main/daemon-app-server-entry.js --stdio`, which owns the CLI processes; a prewarm pool
  `Electron --optimize-for-size --max-semi-space-size=2 <unpacked>/cli/bin/codebuddy --prewarm --prewarm-id
  wb-pool-<ms>-<hex>`, each later activated over IPC as `--serve --session-id <conversationId> --port 0` with an
  unchanged command line; a pool miss starts `Electron …/cli/bin/codebuddy --serve --session-id <id>`; the fallback
  `stdio-cold` path uses argv0 `wb-acp:<conversationId>` and `--acp`. The CLI makes the model calls itself (axios via
  the OpenAI SDK).
- Windows: `WorkBuddyAI.exe` / `WorkBuddy.exe`, the workers the same executable with the same arguments. Installs are
  per-user NSIS, legacy Inno or a plain archive; the default folder is not in the bundle.

## Decisions

- **Two sources, one parser**, ids `workbuddy` and `workbuddyAi` (camel case like `costApi`), names as the apps call
  themselves, the China edition first. A source is detected when its data folder exists.
- **Environment variables are not honoured** (both editions read the same names, `CODEBUDDY_CONFIG_DIR` also belongs
  to the standalone CLI, and the app sets them only for itself); a user setting replaces the root instead.
- **Fixed-depth discovery** (`<slug>/<file>`, `<slug>/<dir>/subagents/<file>`) excludes tool results and workflow
  journals by shape; `cloud/` is excluded by name.
- **Cloud conversations are not read**: no timing, request-level usage, rewritten files.
- **Timing is inferred** from the latest input line; `endMs` is the first line with the call's usage.
- **`skipRun` decides the phase**: a `skipRun` user line starts nothing, a `skipRun` result that completes the step ends
  the turn, a `skipRun` assistant notice ends it only when it reports an interruption or error (`status` incomplete or
  an `error` object). Hook output during tools changes nothing.
- **Tokens take the larger of `usage` and `rawUsage`** for each field, never DeepSeek misses as cache writes, and never
  the derived `message.usage`.
- **Copied history is removed by `messageId` deduplication only**; a fork has no creation time to compare with.
- **Routing slots stay unpriced**; hosting suffixes of DeepSeek ids price as DeepSeek; other suffixes stay unpriced.
- **Colors** (DESIGN 1): the brand teal is nearly Codex mint, so the editions take a darker jade and an ocean blue
  from the same family.

## Verify with real data

1. Per file, the token sums of `--dump-today` against WorkBuddy's own usage view or `workbuddy.db` `session_usage`.
2. Timing: a short answer's interval (user line → usage line) against a stopwatch; tool steps (last result → usage
   line). Note whether user lines come before or after a long `UserPromptSubmit` hook.
3. `providerData.model` for `auto` / `default-model` calls: is the chosen model named? Which ids appear with the
   suffixes, and what do `-pay`, `-x`, `-2` serve?
4. `rawUsage` of an Anthropic model (if any): does `prompt_tokens` include `cache_creation_input_tokens`?
5. A sub-agent: is its folder named after the main session file?
6. A compaction: is there a `compact` user line before the compaction response? A manual compaction: what ends it?
7. Processes: the CLI's command line on macOS and Windows (`ps`, Process Explorer), its parent, and the Windows install
   folder.
