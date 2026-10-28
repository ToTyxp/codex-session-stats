# omp (oh-my-pi) — research notes

Evidence for METRICS 4.7. Every rule there was derived by reading the omp source at commit
`3a2758b68f7c8e6bdf22c3a76b86c8675e5e0c9e` (2026-10-21, v18.12.6) and the older npm releases named below
(`@oh-my-pi/pi-coding-agent`, `@oh-my-pi/pi-ai`, read from the npm tarballs or unpkg). No real omp session file was
available when the rules were written. Re-check them against real sessions with
`python3 spec/tools/reference.py --dump-today --omp <sessions folder>` and against `omp stats --json`, which
aggregates the same records (per-file token sums and per-response `output * 1000 / duration` should match).

omp (`can1357/oh-my-pi`, by Stencil Labs, MIT) is a fork of Mario Zechner's pi (`badlogic/pi-mono`). It is not a fork of
any agent Agent Speed already supports, and its files differ from the OpenCode, Codex, Claude, dsh and Gemini CLI
formats.

## Decisions

- **Roots: the default root and every named profile are scanned automatically.** Profiles (`OMP_PROFILE` /
  `PI_PROFILE`) are a built-in omp feature with a predictable folder; skipping them would silently drop usage.
- **`custom-session-files` paths are read only through the user setting.** The markers can point anywhere on disk;
  scanning arbitrary paths would break the fixed, read-only set of roots the app watches.
- **Upstream pi is out of scope.** `~/.pi/agent/sessions` shares the JSONL family but has no `duration`, `ttft` or
  `completedAt`. If it becomes a sibling source later: start = `message.timestamp`, end = entry `timestamp`.
- **`usage.orchestration` is not counted** (a documented approximation). These provider-side tokens are billed and
  included in `totalTokens` but are not part of the conversation, so normalized `input + output` can be lower than
  `totalTokens`.
- **Error and aborted responses count in totals only.** They carry partial or zero output, which would distort speed.
- **Messages whose four token fields are all 0 produce no response.** This matches Antigravity's non-zero rule; the
  draft counted them, which would have inflated request counts with abort placeholders and the synthetic `aborted`
  message that closes a resumed interrupted turn.
- **A missing or non-string `stopReason` counts as `aborted`** (omp's own stats classify it as `error` or `aborted`;
  both are totals-only here).
- **Key = entry `id` + message `timestamp`**, falling back to the entry timestamp when `message.timestamp` is missing
  or 0, the same pair and fallback as omp's stats (`coerceEntryTimestamp`).

Worth revisiting with real data:
- The copied-history rule (`headerMs - 2000`): a pi-era fixture has a header stamped 49 s after its first three
  entries (an aborted first request). Current omp code cannot produce that; if real files show it, those leading
  entries are dropped (lost totals for that exchange only).
- The in-flight start at the input entry or the batch-completing tool result (measured −5 to −7 ms before the next
  `message.timestamp` on pi-era files).
- Whether `usage.orchestration` should count once real files with it are seen.

## Path resolution details

From `packages/utils/src/dirs.ts`:
- `<cfg>` = `$PI_CONFIG_DIR` (a folder name under home) else `.omp`; it also locates the profiles folder
  (`~/<cfg>/profiles/<name>`).
- `$PI_CODING_AGENT_DIR` replaces the agent folder of the default profile only; named profiles ignore it.
- XDG applies on Linux and macOS (not Windows), and only when the agent folder is not overridden. The default profile
  uses `$XDG_DATA_HOME/omp` when that folder exists; a named profile uses `$XDG_DATA_HOME/omp/profiles/<name>` only
  when that profile folder itself exists (the choice is pinned per profile so a profile never moves when the base XDG
  folder appears later). XDG flattens the `agent/` level: `~/.omp/agent/sessions` → `$XDG_DATA_HOME/omp/sessions`.
  `omp config init-xdg` performs the migration.
- Sessions written with `--session-dir` / `--session` or `PI_CODING_AGENT_SESSION_DIR` live anywhere; omp records
  each path as a marker in `<agent dir>/custom-session-files/`.

Layout details:
- Bucket: the canonicalized working directory (symlink aliases share a bucket), encoded as `-<relative>` under home,
  `-tmp-<relative>` under the temp dir, `--<absolute>--` otherwise; separators and `:` become `-`. 17.2.5 – 17.2.8 wrote
  `<scope>-<basename>-<sha256(canonical cwd)>`; 17.2.9 (#7397) migrates those, and older `--<home-encoded>-*--`
  spellings, back. The project name comes from the header `cwd`, never from the bucket.
- File name: `<YYYY-MM-DDTHH-MM-SS-mmmZ>_<sessionId>.jsonl`, the session creation time in UTC with `:` and `.`
  replaced by `-`; `sessionId` is a UUIDv7.
- Entry ids are the last 8 hex digits of a UUID (a full Snowflake id on collision exhaustion; not fixed width) and are
  unique only within one file. Imports use longer ids (`claude-<uuid>-…`).
- Title slot (16.3+): `{"type":"title","v":1,"title",…,"updatedAt","pad"}`, exactly 256 bytes including the newline,
  overwritten in place; a read that races it sees one malformed line.
- Writes: `appendFileSync` single lines without `fsync`, or a full rewrite through a temp file renamed over the
  transcript (Windows first moves the old file aside as `<name>.jsonl.<id>.bak`). The session lease never locks the
  transcript (Linux socket, Windows mutex, macOS sidecar `flock`).
- Lazy persistence: a new session has no file until its first assistant message.
- GC: `omp gc --compress` archives sessions as `.jsonl.gz` under `<agent dir>/archive/sessions`, stages copies in
  `.omp-gc-compress-*` beside the originals, and on APFS replaces the transcript's inode.
- Outside the root: collab guest replicas (`~/.omp/collab/<roomId>.jsonl`, copies of someone else's session), `omp
  render` working copies (OS temp), sub-agents of `--no-session` runs (OS temp).
- Session header `version` has been 3 since 3.0; omp migrates older files on load and rewrites them on the next write.
  omp updates itself (near-daily releases), so files written today come almost entirely from 18.x.

## Measurements on pi-era fixtures

`packages/coding-agent/test/fixtures/large-session.jsonl` and `before-compaction.jsonl` (pi-era sessions, Nov 2025):
- median `entry timestamp − message.timestamp` of assistant entries 4.7 – 5.2 s (the entry is written at the stream
  end); input entry − next `message.timestamp` −5 to −7 ms (the request starts right after the input entry).
- METRICS 4.7 rules applied to `large-session.jsonl`: 452 responses, all keys unique, 431 valid intervals, 3 leading
  entries dropped as copies (the 49 s header case above).
- `before-compaction.jsonl` is a branched version-1 file (`branchedFrom`, no entry ids, header after 1001 of 1002
  entries); it contributes nothing, as intended.

## Evidence

omp at `3a2758b68f7c8e6bdf22c3a76b86c8675e5e0c9e` (https://github.com/can1357/oh-my-pi) unless a release is named.

| Rule | Source |
| --- | --- |
| Fork of pi, Stencil Labs, MIT | `README.md` 22, 569, 585; `package.json` 1–6 |
| Sessions layout, header, entry base, taxonomy, migrations | `docs/session.md` 37–133, 435–459 |
| Root, `PI_CONFIG_DIR`, `PI_CODING_AGENT_DIR`, profiles, XDG on Linux and macOS (not Windows) | `packages/utils/src/dirs.ts` 28–29, 115–123, 328–337, 345–441, 997–1000; `docs/environment-variables.md` 561–578 |
| Bucket encoding, legacy and hashed bucket migration | `packages/coding-agent/src/session/session-paths.ts` 46–118, 296–310; `docs/session.md` 45–47 |
| File name `<timestamp>_<uuidv7>.jsonl`, entry id = last 8 hex of a UUID, ISO entry timestamp | `session-manager.ts` 139–160, 2049, 2070–2077; `session-migrations.ts` 5–11 |
| Title slot (256 bytes, in-place overwrite) | `session-entries.ts` 16–20; `session-title-slot.ts` 22–39; `session-storage.ts` 1008–1025 |
| Append vs full rewrite (temp + rename, Windows move-aside `.bak`) | `session-manager.ts` 1622–1700, 1814–1900; `session-storage.ts` 962–1006, 1171–1230 |
| Lazy persistence until the first assistant message | `session-manager.ts` 1828–1835, 3165–3180; `docs/session.md` 525–530 |
| `AssistantMessage` fields (`timestamp`, `duration`, `ttft`, `completedAt`, `responseId`, `stopReason`) | `packages/ai/src/types.ts` 1268–1356 |
| `Usage` semantics (non-cached input, output incl. thinking, `reasoningTokens` subset, orchestration) | `packages/catalog/src/types.ts` 209–270 |
| Start stamp and duration at stream start/end; retries keep both | `packages/ai/src/providers/anthropic.ts` 2226–2239, 2797–2811, 3952–3971, 4022–4023; `openai-completions.ts` 805–809, 1812–1813; `openai-shared.ts` 4026–4044; `openai-responses.ts` 1270–1295 |
| Providers without `duration` (GitLab Duo) | `packages/ai/src/providers/gitlab-duo-workflow.ts` 2240–2254 (no `duration` assignment in the file); comment in `agent-session.ts` 4283–4287 |
| OpenAI-family and Gemini usage normalization | `openai-shared.ts` 494–511; `google-shared.ts` 533–557 |
| `completedAt` stamp at `message_end`; live meter uses `timestamp` → `timestamp + duration` | `agent-session.ts` 4283–4290, 4330–4345 |
| Message persistence at `message_end`, in order; refusals and empty error turns not persisted | `agent-session.ts` 3740–3752, 3875–3905, 3967–4002; `turn-persistence.ts` 42–62 |
| Input messages emitted at turn start; tool results flushed in call order | `packages/agent/src/agent-loop.ts` 1141–1146, 1432–1442, 3442–3452 |
| Error/aborted placeholders; tool calls run under `stop`; `length`; terminal tool result; `pause_turn` | `agent-loop.ts` 1540–1580, 1590–1610, 1706–1715, 1718–1730 |
| `model_usage` entry and writers | `session-entries.ts` 104–116; `session-manager.ts` 3466–3490; `sdk.ts` 2296–2310 |
| `session_exit`, `tool_execution_start` custom entries | `docs/session.md` 322–333 |
| Fork, branch, move-off, forkFrom copy entries verbatim with a new header | `session-manager.ts` 1263–1288, 1993–2056, 2445–2486, 3913–3990, 4054–4120 |
| omp's own cross-file dedupe on `(entry_id, timestamp)` | `packages/stats/src/db.ts` 866–900, 1228–1246 |
| omp's own speed formula and record selection | `packages/stats/README.md` (Metrics); `packages/stats/src/parser.ts` 37–55, 227–344; `packages/stats/src/rollup.ts` 101–102 |
| Sub-agent, advisor and seance file locations; parent session path in sub-agent header | `session/sub-sessions.ts` 1–6; `task/executor.ts` 4188–4191, 4512–4529; `advisor/transcript-recorder.ts` 148–168; `docs/session-operations-export-share-fork-resume.md` 61, 409 |
| Retry policy (no record at retry start; discarded turns rewritten) | `docs/non-compaction-retry-policy.md`; `session-manager.ts` 3855–3884; `turn-recovery.ts` 1026, 1276 |
| No usage on compaction entries | `session-entries.ts` 144–168 |
| Foreign imports (`/resume @claude`, `@codex`) keep original timestamps; Claude usage kept, Codex empty | `session/foreign-session-import.ts` 1–52; `session/claude-session-store.ts` 273–289, 396–465; `session/codex-session-store.ts` 52–60, 160–170; `session-manager.ts` 2706–2728; `docs/session-operations-export-share-fork-resume.md` 36, 433 |
| GC: archive root, compress scratch dirs, inode replacement on APFS | `docs/session.md` 582–593; `cli/gc-cli.ts` 335–337 |
| Collab replicas under `~/.omp/collab` | `docs/collab.md` 196 |
| Session lease never locks the transcript (Linux socket, Windows mutex, macOS sidecar `flock`) | `docs/session.md` 540–541 |
| `.pi` root and session v2 in 1.337.0; `.omp` from 3.0 | npm `pi-coding-agent@1.337.0` `src/config.ts` 106, `src/core/session-manager.ts` 25; `@3.37.1` `src/config.ts` 13–14 |
| `duration`/`ttft` from 6.9.69 | npm `pi-ai@6.9.0` (absent), `@6.9.69` `src/types.ts` |
| Title slot from 16.3.0 | unpkg `pi-coding-agent@16.2.0` / `@16.3.0` `src/session/session-entries.ts` |
| `model_usage` and `completedAt` from 18.1.0 | unpkg `pi-coding-agent@18.0.0` / `@18.1.0` `src/session/session-entries.ts`, `src/session/agent-session.ts` |
| Request start precedes the input entry by ~5 ms; assistant entry ≈ stream end | measured on `packages/coding-agent/test/fixtures/large-session.jsonl` and `before-compaction.jsonl` (pi-era sessions; median `entry − message.timestamp` 4.7–5.2 s, input entry − next `message.timestamp` −5 to −7 ms) |
| Rules applied to those fixtures | `large-session.jsonl`: 452 responses, all keys unique, 431 valid intervals, 3 leading entries dropped as copies. `before-compaction.jsonl`: a branched pi-era version-1 file (`branchedFrom`, no entry ids, header after 1001 of 1002 entries) contributes nothing, as intended |
| Stats key timestamp falls back to the entry timestamp when `message.timestamp` is missing or 0; missing `stopReason` → `error`/`aborted` | `packages/stats/src/parser.ts` 227–303 (`extractStats`, 297), 338–344 (`coerceEntryTimestamp`) |
