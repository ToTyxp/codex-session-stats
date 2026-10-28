# Antigravity — research notes

Evidence for METRICS 4.8. Written 2026-10-21 from public reverse-engineering projects (commits in the evidence table),
the protobuf descriptors embedded in the `agy` binary (snapshots of agy 1.0.10, 1.3.6 and 1.4.4 published by
agy-reader), Google's own bundled docs, and the local structure of one Mac that has Antigravity installed but **no
conversation data** (the account never got a license: `conversations/` is empty and `conversation_summaries.db` has 0
rows). No real conversation database has been checked by us. Re-check the rules on a machine with real conversations
with `python3 spec/tools/reference.py --dump-today --antigravity <store folder>` and the verification plan below.

Verdict of the research: full live speed is possible for SQLite-era conversations (agy CLI ≥ 1.0.7, desktop app and
IDE 2.x since about June 2026): every model call is a `PLANNER_RESPONSE` row with its own token usage and its own
creation and generation-finished timestamps. Legacy encrypted `.pb` conversations and plaintext brain transcripts give
nothing (no readable usage). The timing interpretation is the main unverified assumption.

## Decisions

- **Only the SQLite conversation format is read** (agy ≥ 1.0.7, desktop app and IDE 2.x). Encrypted `.pb` files and
  `transcript.jsonl` are out of scope and never decrypted: the `.pb` key is embedded in a closed binary (we do not
  extract or ship it), and transcripts carry no token fields.
- **Three stores**: `~/.gemini/antigravity-cli`, `~/.gemini/antigravity`, `~/.gemini/antigravity-ide`
  (`%USERPROFILE%` on Windows). Each product has its own `--app_data_dir`; all three can exist on one machine.
- **`*-backup` stores are never read, not even through the user setting.** `antigravity-backup` is a copy; reading it
  would count every conversation twice if its response ids ever differed, and costs I/O even when dedupe catches it.
- **UUID file-name filter** (`<8-4-4-4-12 hex>.db` directly in `conversations/`): conversation ids are UUIDs; the
  filter keeps stray databases (exporter caches, `conversation_summaries.db` if ever moved) out.
- **`GEMINI_CLI_HOME` is not honoured.** tokscale assumes agy honours it; no source confirms it, and Google documents
  no override for the store folder.
- **`gen_metadata` rows are guarded by the `size` column** (and `length(data)`): rows embed the whole prompt and reach
  hundreds of MB. The query never loads a blob whose `size` exceeds 1 MiB.
- **Planner rows produce a response only at a terminal status.** An open row may carry partial usage; waiting for the
  terminal status keeps every emitted response final (no unfinalized warnings from Antigravity).
- **Error, cancelled and interrupted planner responses count in totals only**, the same principle as omp.
- **Token cap 10 000 000 per field** (the draft used agentsview's 2 000 000). agentsview's cap protects a heuristic
  walker that guesses field positions; with exact field numbers the cap only has to reject garbage and values that
  would overflow `Int64`, and 2 M is close to real context sizes (input plus cache reads of 1–2 M-token models).
- **Older databases are not opened at startup**; the first time one changes it is read completely (METRICS 5.4). This
  avoids opening hundreds of databases at launch just to learn their highest `idx`; their old rows predate today, so
  reading them late changes no live value.
- **Model resolution is simplified** to `gen_metadata.step_indices` (then `response_model`, then
  `model_display_name`), then the executor's `model_name`. A join on `chat_model.usage.response_id` = the step's
  `model_usage.response_id` (what tokscale does) is an equivalent alternative if `step_indices` turns out to be missing
  in some versions.
- **The phase is a function of the latest row** (highest `idx`) plus executor termination, so it does not depend on
  how reads are batched and both platforms reach the same state.

Worth revisiting with real data:
- The meaning of `created_at` / `finished_generating_at` on planner rows (inferred from field names, Windsurf-era
  semantics and one timing sample).
- Whether a `GENERATING` planner row appears before its stream ends (verification step 4).
- Whether `steps.metadata.model_usage` equals `gen_metadata.chat_model.usage` for the same `response_id`.
- Whether a fork's `CortexTrajectoryMetadata.created_at` is new (the copied-history rule assumes so).
- Whether `executor_metadata` rows are written only when a run ends, and which `termination_reason` a normal end uses
  (`NO_TOOL_CALL` = 4 is expected).
- Whether the `gen_metadata.size` column equals `length(data)`.

## Surfaces and stores

| Surface | Store (`<g>` = `~/.gemini`, `%USERPROFILE%\.gemini` on Windows) | Notes |
| --- | --- | --- |
| Antigravity CLI (`agy`, Go binary, successor of Gemini CLI since 2026-06-18) | `<g>/antigravity-cli` | settings in `settings.json`, prompt log `history.jsonl` |
| Antigravity 2.0 desktop app (Electron), IDE extensions (VS Code, JetBrains, Zed, Visual Studio, Xcode), original Antigravity IDE 1.x | `<g>/antigravity` | `conversation_summaries.db`, `antigravity_state.pbtxt` |
| Antigravity IDE (VS Code fork, separate download since 2.0) | `<g>/antigravity-ide` | language server started with `--app_data_dir antigravity-ide` |
| Backup copy | `<g>/antigravity-backup` | a copy; never read |

The store directory is the language server's `--app_data_dir` relative to `<g>`. `~/.gemini/config/` belongs to the
Antigravity app.

| Era | Conversation file | Readable usage |
| --- | --- | --- |
| Antigravity IDE 1.x (Nov 2025 – mid 2026), early agy releases (the oldest audited SQLite schema is agy 1.0.7) | `conversations/<uuid>.pb`, `implicit/<uuid>.pb`: AES-encrypted protobuf (AES-256-GCM for agy) | No (key embedded in the closed binary) |
| agy 1.0.7 – 1.4.4 (current), every surface since ~June 2026 | `conversations/<uuid>.db`: SQLite (WAL), protobuf blobs | Yes |
| Forks in agy 1.2.17 | both `<uuid>.pb` (at fork time) and `<uuid>.db` (once the fork continues) | the `.db` |
| Plaintext transcript (newer builds) | `brain/<uuid>/.system_generated/logs/transcript.jsonl` | No token fields |

Other files that are not sessions: `*.db-wal`, `*.db-shm`, `implicit/`, `*.trajectory.json` (sidecars written by the
third-party `agy-reader`), `brain/`, `annotations/`, `log/`. Real users have hundreds of conversation databases (one
audit: 381); some machines hold byte copies of the same conversations in `antigravity` and `antigravity-ide`.

## Database

Schema of agy 1.0.7 – 1.4.4 (`PRAGMA user_version = 1`; fingerprint unchanged; the IDE writes the same schema,
verified at 2.2.1):

```
trajectory_meta(trajectory_id TEXT, cascade_id TEXT, trajectory_type INTEGER, source INTEGER)
steps(idx INTEGER, step_type INTEGER, status INTEGER, has_subtrajectory INTEGER, metadata BLOB, error_details BLOB,
      permissions BLOB, task_details BLOB, render_info BLOB, step_payload BLOB, step_format INTEGER)
gen_metadata(idx INTEGER, data BLOB, size INTEGER)
executor_metadata(idx INTEGER, data BLOB)
parent_references(idx INTEGER, data BLOB)
trajectory_metadata_blob(id TEXT, data BLOB)
battle_mode_infos(idx INTEGER, data BLOB)
```

The database is the `gemini_coder.Trajectory` message split into tables: `steps` rows are its steps (`metadata` =
`CortexStepMetadata`), `gen_metadata` rows are `CortexStepGeneratorMetadata` (one per model generation; `idx` is the
generation index), `executor_metadata` rows are `ExecutorMetadata`, `trajectory_metadata_blob` is
`CortexTrajectoryMetadata`. `trajectory_meta.cascade_id` equals the conversation id (the file name);
`trajectory_meta.source` 1 = desktop app / IDE (`CASCADE_CLIENT`), 17 = CLI, 21 = Gemini app. agy-reader's offline
export from the database matched the daemon's own JSON for 381 real conversations, so the database holds everything
the RPC returns. The language server writes the WAL continuously while a conversation is open.

Protobuf details beyond the fields METRICS uses (agy 1.4.4 descriptors): `CortexStepMetadata` also has
`step_generation_version` 21, `source` 3, `tool_call` 4, `model_cost` 10 (float, fixed32), `generator_model` 11,
`retry_infos` 28 (each with its own `usage`), `sensitivity_level` 39; `ModelUsageStats` also has `model` 1 (enum,
placeholder values), `api_provider` 6, `response_header` 8 (map), `provider_assigned_message_id` 12, `service_tier`
13, modality token details 14–16, and the deprecated `cache_write_tokens` 4; `ChatModelMetadata` has
`chat_start_metadata` 9, `time_to_first_token` 11 and `streaming_duration` 12 (`Duration`), `retries` 14,
`retry_infos` 17, `response_model_full` 22; `CortexTrajectoryMetadata` has `root_conversation_id` 6,
`mendel_experiment_ids` 15 (packed), `is_battle_mode_fork` 16; `ExecutorMetadata.termination_reason` values: 1 ERROR,
2 USER_CANCELED, 3 MAX_INVOCATIONS, 4 NO_TOOL_CALL, 6 MAX_FORCED_INVOCATIONS, 7 EARLY_CONTINUE, 8 TERMINAL_STEP_TYPE,
9 TERMINAL_CUSTOM_HOOK, 10 INJECTED_RESPONSE. Step types seen in the enum include 5 CODE_ACTION, 8 VIEW_FILE,
9 LIST_DIRECTORY, 14 USER_INPUT, 15 PLANNER_RESPONSE, 17 ERROR_MESSAGE, 21 RUN_COMMAND, 23 CHECKPOINT,
28 COMMAND_STATUS, 98 CONVERSATION_HISTORY, 100 SEND_COMMAND_INPUT, 101 SYSTEM_MESSAGE, 127 INVOKE_SUBAGENT.

Real sanitized evidence (agy 1.2.16): agy reported 24 426 input, 48 836 cache-read, 1 571 output (1 346 thinking),
total 25 997 = input + output, so `input_tokens` excludes cache reads. The two steps whose completion times the audit
report calls the first and the final response took `created_at → completed_at` = 1.65 s and 2.36 s for short
`gemini-3.8-flash-high` answers, which fits the timing model; that these steps are `PLANNER_RESPONSE` rows is
inferred (the report does not name step types).

## How other readers use the data

- **tokscale** reads token usage from `gen_metadata` (`chat_model.usage`) and dates each generation from the `steps`
  table: `metadata.#1` (`created_at`) joined on `metadata.#9.#11` (`model_usage.response_id`) or on
  `metadata.#20.#3` (generation index = `gen_metadata.idx`). agy ≤ 1.1.17 also wrote `chat_start_metadata.created_at`
  (`chatModel.#9.#4`); 1.1.18 dropped it. Its source comment swaps fields 9 and 10 of `ModelUsageStats`; the
  descriptor says 9 = thinking, 10 = response.
- **agentsview** also reads usage from `gen_metadata` (all rows, no size guard) and validates it with a heuristic
  walker: model enum in [1000, 5000), every token field ≤ 2 000 000 (`maxPlausibleTokens`) and input + output ≤
  2 000 000, to reject decoy blocks in legacy records. Its notes: `output_tokens` includes thinking in 550/550 blocks
  checked against sidecar ground truth; `stepIndices[0]` is always a `PLANNER_RESPONSE` step and no planner step is
  claimed by two generations (26 sidecars, 1 839 generations).
- **nervous-bus**: `output_tokens == thinking_output_tokens + response_output_tokens` on 14 654 of 14 654 real rows.
- **tokentelemetry**: "since June 2026 every surface keeps `conversations/<id>.db`"; `gen_metadata` rows reach
  hundreds of MB.
- Steps metadata in agy 1.2.17 sidecars carries `modelUsage` (`inputTokens`, `outputTokens`, `cacheReadTokens`,
  `thinkingOutputTokens`, `responseOutputTokens`, `responseId`, `messageId`), `createdAt`, `finishedGeneratingAt`,
  `lastCompletedChunkAt`, `completedAt`, `viewableAt`, `startedAt` and `retryInfos[].usage`.

## Relationship to Gemini CLI (METRICS 4.6)

Both live under `~/.gemini`, but 4.6 only matches `<root>/<projectDir>/chats/<file>` below `~/.gemini/tmp` (and
`~/.cache/.gemini/tmp`), so no Antigravity file is read by it and nothing is counted twice. The token mapping differs
(Gemini `promptTokenCount` includes cached input; Antigravity `input_tokens` does not). Gemini CLI stopped serving
personal accounts on 2026-06-18; `agy` replaces it.

## Not used

- Logs (`<store>/log/cli-*.log`, `~/Library/Logs/Antigravity/language_server.log`) are glog lines
  (`I1013 19:13:51.698198 51 server.go:1631] …`, no year) about server lifecycle and backend calls; no source shows
  per-request usage or latency in them.
- The Gemini macOS app's Antigravity integration (`~/Library/Application Support/com.google.GeminiMacOS/Antigravity/
  client_storage.enc`, encrypted; trajectory source `GEMINI_APP` = 21): where its conversations are stored is unknown.
- The Python SDK's runtime.
- `conversation_summaries.db` (local Mac, app v2.23.1, language server v2.22.0): 21 columns including `status`,
  `not_fully_idle`, `parent_conversation_id`, `nesting_depth`, `agent_name`, and no token columns.

## Verification plan

On a machine with real conversations (macOS and Windows):
1. `sqlite3 'file:<db>?immutable=1'`: the schema matches the 7 tables above.
2. Decode every `PLANNER_RESPONSE` row: `output_tokens == thinking_output_tokens + response_output_tokens`; per
   conversation, token sums equal the `gen_metadata` usage sums and agy's own `/usage` or headless result.
3. Timing: for each response, `finished_generating_at - created_at` is within 15 % of `time_to_first_token +
   streaming_duration` when present; no negative intervals.
4. While a long answer streams, poll the database: does a row with status 8 appear before the stream ends?
5. Fork a conversation and open it under two stores: no response counted twice; the fork's trajectory `created_at` is
   new.
6. Compare `python3 spec/tools/reference.py --dump-today --antigravity <store>` with both platform probes.

## Evidence

| Rule | Source |
| --- | --- |
| Store directory per product (`antigravity-cli/`, `antigravity/`, `antigravity-ide/`) | Google-bundled doc `~/.gemini/antigravity/builtin/skills/agy-customizations/docs/hooks.md` 160–165 (local) |
| Product surfaces (CLI, 2.0 app, IDE, SDK); CLI settings path | bundled `builtin/skills/antigravity_guide/references/{app,cli,ide,sdk}.md` (local); https://antigravity.google/docs/install?tab=cli |
| `--app_data_dir`, Windows paths `%USERPROFILE%\.gemini\antigravity*\conversations` | https://github.com/google-antigravity/antigravity-cli/issues/1042; codeburn `docs/providers/antigravity.md` at `3472885629c41725b40c19c0780ecce148b067bf` |
| Default roots, `antigravity-backup` excluded, IDE byte copies | agentsview `becfc353371d35939e26986d50e2c4fb20f4616b` `internal/parser/types.go` 965–995; nervous-bus `ab9d876a` `adapters/reflex-recorder/agy_transcripts.py` 1–18 |
| SQLite tables and columns | agy-reader `53a4f069b70c0de05c29a6bef6c1f8bf5fe0e8c9` `internal/testfixture/source.go` 59–71; `COMPATIBILITY.md` 9–12, 26–32 |
| Database = `gemini_coder.Trajectory`; offline export equals daemon JSON (381 conversations) | agy-reader `artifacts/proto-snapshots/agy-1.4.4-337dcde66844/proto/third_party/gemini_coder/proto/trajectory.proto` 37; `docs/compatibility/agy-1.4.4-offline-export.md` 9–24 |
| Field numbers (`CortexStepMetadata`, `ModelUsageStats`, `ChatModelMetadata`, `ChatStartMetadata`, `CortexStepGeneratorMetadata`, `ExecutorMetadata`, `CortexTrajectoryMetadata`, enums) | agy-reader agy-1.4.4 snapshot `cortex_pb/cortex.proto` 883, 1016, 1114, 1178, 1236, 4993, 6998, 7172, 7324; `codeium_common_pb/codeium_common.proto` 2420; same numbers in `artifacts/historical-schema-recovery/1.0.10-proto` |
| `ExecutorTerminationReason` values; `CascadePlannerConfig.model_name` 28 | agy-1.4.4 snapshot `cortex_pb/cortex.proto` 7848–7868, 3106 |
| Planner rows carry `model_usage` and timestamps in real databases | tokscale `f913ad2a9085fe9f50901255d1fae9c2fa263f45` `crates/tokscale-core/src/sessions/antigravity_cli.rs` 466–520; https://github.com/junhoyeo/tokscale/issues/1184; agy-reader `docs/compatibility/agy-1.2.17-sidecar-shape.tsv` 1508–1567 |
| `output == thinking + response` (14 654 rows); 9 = thinking, 10 = response | nervous-bus `agy_transcripts.py` 34–66; descriptor above (tokscale's comment swaps 9 and 10) |
| `input_tokens` excludes cache reads; real timing sample | agy-reader `docs/compatibility/agy-1.2.16-recheck-evidence.json` 24–50, 102–108; `agy-1.2.16-recheck.md` 35–40, 114–116 |
| Usage block semantics, step indices, executor model, oversized-value guard | agentsview `internal/parser/antigravity.go` 307–334, 664–760; `internal/parser/antigravity_cli.go` 1088–1145 |
| agentsview heuristic cap (`maxPlausibleTokens` = 2 000 000), model enum range, usage read from `gen_metadata` | agentsview `internal/parser/antigravity.go` 499–523, 664–705, 739–860 |
| agy ≤ 1.1.17 `chat_start_metadata.created_at`; gone in 1.1.18 | tokscale `antigravity_cli.rs` 20–40, 330–345; issue #1184 |
| Schema unchanged agy 1.0.7 – 1.4.4; IDE same schema at 2.2.1 | agentsview `internal/parser/antigravity_version.go` 12–45; agy-reader `COMPATIBILITY.md` 26–32 |
| Legacy `.pb`: AES-256-GCM with a fixed key embedded in agy; `.db` + `.pb` after a fork | agy-reader `internal/completeness/legacy.go` 19–26, 80–91; `README.md` 176–186; agentsview `internal/parser/antigravity_crypto.go` 12–35 |
| "Since June 2026 every surface keeps `conversations/<id>.db`"; rows up to hundreds of MB | tokentelemetry `b637b1ae12799639bcb119019617093f3d770483` `backend/antigravity_usage.py` 1–30, 38–42 |
| Brain transcript format (no tokens) | agentsview `internal/parser/antigravity_brain_transcript.go` 16–41 |
| Local Mac structure: `conversation_summaries.db` schema (21 columns incl. `status`, `not_fully_idle`, `parent_conversation_id`, `nesting_depth`, `agent_name`, no token columns), app v2.23.1, language server v2.22.0, glog log format | read-only inspection on 2026-10-21; no conversation data present |
| Gemini CLI retired for personal accounts 2026-06-18; Antigravity 2.0 + CLI launched 2026-05-19 | https://thenewstack.io/gemini-cli-antigravity-replacement/; https://techcrunch.com/2026/05/19/google-launches-antigravity-2-0-with-an-updated-desktop-app-and-cli-tool/ |
