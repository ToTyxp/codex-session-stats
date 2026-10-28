# ZCode, Kimi Code CLI and Grok Build

## Integration contract

The adapters add source records to the existing measurement engine. The output/input token definitions,
end-to-end duration, 500 ms minimum, 15-minute maximum, rolling window, estimates and pricing equations are unchanged.
Missing numeric fields continue to follow METRICS 3.2. Display names come from `spec/strings.json`; protocol ids remain
independent. In particular, DSH continues to use `deepseek`, and new ids append as `zcode`, `kimi`, `grokBuild`.

Native batch records carry `requestCount` (default 1), so a Grok batch of several calls is not reported as one call.
That count is used by today's totals, daily/model history, unpriced-request totals and the report ledger. The
`--dump-today` diagnostic summary's `counts` retains its original meaning of emitted record rows; each row exposes
its own `requestCount`.

## ZCode

The default store is `~/.zcode/cli/db/db.sqlite`. `ZCODE_DATA_BASE_DIR` changes the home base (the store remains below
`.zcode/cli/db`), while the Agent Speed setting may select the database, its containing folder or the data directory.
Both platforms read native `session` and `model_usage` tables, with `message` and tool `part` metadata for phase.
They neither migrate nor write the source database, credentials, configuration or transcript.

`model_usage` is the canonical billing/usage fact, including background title and compaction calls. Its input is
already cache-inclusive. `id` distinguishes attempts; `logical_request_id` alone must not collapse separately
recorded attempts. Usage written after midnight must not be lost because its start was on the preceding day.

Primary evidence:

- [Session storage and read-only analysis](https://github.com/zai-org/ZCode/blob/main/apps/zcode-cli/docs/usage/06-sessions.md)
- [Canonical usage observability](https://github.com/zai-org/ZCode/blob/main/apps/zcode-cli/docs/design/v2/usage-observability.md)
- [Runtime write points and token/timing fields](https://github.com/zai-org/ZCode/blob/main/apps/zcode-cli/packages/core/src/runtime/methods/usage-observability.ts)
- [Native SQLite schema](https://github.com/zai-org/ZCode/blob/main/apps/zcode-cli/packages/adapters/src/storage/session-store/schema.baseline.sql)

## Kimi Code CLI

This adapter targets the current TypeScript v2 store under `~/.kimi-code/sessions`, including per-agent journals.
`KIMI_CODE_HOME` replaces `.kimi-code`; an explicit Agent Speed setting replaces discovery. Old Python `kimi-cli`
storage is a different format and is not relabeled as supported.

The journal contains both context events and their machine/history mirrors. A single provider/message id must own
one response across `context.append_message`, `context.append_loop_event` / `step.end`, and the nested
`agent.message.appended` or `human.agent.message.appended` shape. Separate `usage.record`, context-length counters
and parent subagent summaries are not extra model calls. A real `llm.request` start includes time to first token;
the persisted first-token latency plus stream duration is a fallback when that start is absent.

Primary evidence:

- [Native data locations](https://github.com/MoonshotAI/kimi-code/blob/main/docs/en/configuration/data-locations.md)
- [Wire record shape and millisecond clock](https://github.com/MoonshotAI/kimi-code/blob/main/packages/agent-core-v2/src/wire/record.ts)
- [Step-end usage and timing projection](https://github.com/MoonshotAI/kimi-code/blob/main/packages/agent-core-v2/src/agent/loop/loopService.ts)
- [Nested history message usage](https://github.com/MoonshotAI/kimi-code/blob/main/packages/agent-core-v2/src/human/agent/historySchema.ts)

## Grok Build: released CLI verification

The official macOS arm64 CLI was downloaded from the URL selected by the official installer, without installing it
on PATH. The stable channel returned **1.0.70 (`f50904eaee6c`)**. An unmodified binary ran a headless request with an
isolated `GROK_HOME` and a local synthetic Chat Completions server; it exited successfully. No real xAI account or
paid model inference was used. Evidence is in `artifacts/issue8-grok-cli/verification.json`, with minimal native
`updates.jsonl`/`summary.json` records and each platform core's actual probe result.

The ordinary store, without sampling enabled, persisted a `turn_completed` event with:

```json
{
  "usage": {
    "inputTokens": 1200,
    "outputTokens": 200,
    "cachedReadTokens": 300,
    "reasoningTokens": 40,
    "modelCalls": 1,
    "apiDurationMs": 1206,
    "modelUsage": {
      "agent-speed-probe": {
        "inputTokens": 1200,
        "outputTokens": 200,
        "cachedReadTokens": 300,
        "reasoningTokens": 40,
        "modelCalls": 1,
        "apiDurationMs": 1206
      }
    }
  }
}
```

Thus sampling logs are not required. The per-model native batch supplies the engine's tokens, duration and actual
call count. The same calls also appear in `usage.json`; adding that file would double-count. Buffered
`response_started` and `response_completed` messages are not reliably in the ordinary journal, so fixture support
must not depend on those transient events.

Parent ledgers fold child usage. Sessions explicitly marked `subagent` or `subagent_fork` with a parent id therefore
provide activity and parent links while the parent batch is counted once. Normal forked sessions keep their own new
prompt/model keys, and copied keys are deduplicated. This policy also avoids retracting a parent after its batch has
already entered the append-only report ledger. If an upstream incomplete batch omits late background usage, the
adapter cannot reconstruct the missing usage from that batch.

The upstream batch does not provide separate call timelines. Its API duration is placed into one interval ending
at completion and passed through the unchanged engine; concurrency inside that batch cannot be reconstructed.
Pricing uses the unchanged price book on the batch; input-tier selection and rounding may differ from a sum of
individual-call bills. These are source-data limits, not a new speed or pricing formula.

If `summary.json` is temporarily unavailable, the live adapter waits before counting numeric batches, keeping at most
256 pending batches. A summary-only update retries them: main-session batches are emitted, while already-folded
child batches are dropped. History can replay the full journal when metadata is restored. Source session counts
exclude subagents; the agent tree continues to show their activity, including parents in a different workspace group.

Primary evidence:

- [Official installer](https://x.ai/cli/install.sh)
- [Native session layout](https://github.com/xai-org/grok-build/blob/main/crates/codegen/xai-grok-pager/docs/user-guide/17-sessions.md)
- [Persisted envelope](https://github.com/xai-org/grok-build/blob/main/crates/codegen/xai-grok-shell/src/session/storage/mod.rs)
- [PromptUsage fields](https://github.com/xai-org/grok-build/blob/main/crates/codegen/xai-grok-shell/src/extensions/notification.rs)
- [Buffered xAI notifications do not follow the persisted ACP rail](https://github.com/xai-org/grok-build/blob/main/crates/codegen/xai-grok-shell/src/session/acp_session_impl/updates.rs)
- [Child usage folding](https://github.com/xai-org/grok-build/blob/main/crates/codegen/xai-chat-state/src/usage.rs)

## Executable checks

`python3 spec/tools/harness_fixtures.py` regenerates synthetic native-format records and independent expectations in
`spec/fixtures/harness-expected.json`. Both platform test suites consume that same data. The cases include classic,
machine-mirror and step-end Kimi records, subagents, fork copies, ZCode running/completed rows, and Grok native
multi-model/multi-call batches alongside misleading cumulative companion totals.

Run `python3 spec/tools/reference.py --check-fixtures`, `swift test` in `macos`, and the Windows Core and Presentation
test projects with .NET 10. Tests cover source preservation, incremental updates, restart persistence, folder
overrides, identity/parent links and aggregate counts. A successful cross-build does not verify Windows guest UI
interaction; actual xAI, ZCode and Kimi model sessions require their corresponding real runtime environments.
