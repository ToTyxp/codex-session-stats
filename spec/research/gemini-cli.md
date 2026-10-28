# Gemini CLI — research notes

Evidence for METRICS 4.6. Every rule there was derived by reading the Gemini CLI source at commit
`c1740d29301a73a47dc3501c1aecc7eb00c0ee58` (2026-10-21, v0.66.0-preview.0) and the release tags named below. No real
Gemini CLI session files were available when the rules were written; re-check them against real sessions with
`python3 spec/tools/reference.py --dump-today --gemini <tmp folder>` when possible.

Decisions worth revisiting with real data:
- `tokens.tool` (server-side tool results) is counted as input, so normalized `input + output == tokens.total`.
- The cancel marker is the exact UI string `Request cancelled.`; a future wording change would leave the phase
  `generating` until the 300-second staleness rule.

## Evidence

Gemini CLI at `c1740d29301a73a47dc3501c1aecc7eb00c0ee58` unless a tag is named.

| Rule | Source |
| --- | --- |
| Record and token types | `packages/core/src/services/chatRecordingTypes.ts` 12, 19–26, 44–104, 123–165 |
| Record classification and replay (`$rewindTo`, `$patch`, message, `$set`, header) | `packages/core/src/services/chatRecordingService.ts` 79–104, 304–549 |
| File naming, sub-agent directory, header, legacy `.json` → `.jsonl` migration | `chatRecordingService.ts` 711–887 (732–756, 779–823, 856–865) |
| Append, atomic rewrite, `$set` | `chatRecordingService.ts` 942–1035 |
| Full re-append of a message, creation timestamp | `chatRecordingService.ts` 1037–1077 |
| Gemini message gets queued tokens/thoughts/model, then `$set.lastUpdated` | `chatRecordingService.ts` 1079–1110 |
| Tokens from `usageMetadata`; late attach to a tokenless last message | `chatRecordingService.ts` 1137–1166 |
| Tool calls attached by re-append (or a new message) | `chatRecordingService.ts` 1168–1237 |
| `$rewindTo` on `/rewind`; `$rewindTo`/`$patch` on history rollback | `chatRecordingService.ts` 1377–1420, 1559–1589 |
| User record and synthetic tool-result `user` record before the request | `packages/core/src/core/geminiChat.ts` 544–656 |
| Retries; rollback of aborted/failed requests | `geminiChat.ts` 664–846 (809–842) |
| Model actually used (`lastModelToUse`) | `geminiChat.ts` 1180–1240 |
| Usage buffered per chunk, validation, then tokens + gemini record at stream end | `geminiChat.ts` 1479–1794 (1584–1587, 1698–1793) |
| Tool call timestamp = completion time | `geminiChat.ts` 1811–1841 |
| Compression/masking/routing run before `turn.run` | `packages/core/src/core/client.ts` 644–837 (719, 729, 808–811, 833) |
| Tool calls recorded, then submitted; all-cancelled path | `packages/cli/src/ui/hooks/useGeminiStream.ts` 364–400, 2242–2326 |
| `error` item on request failure | `useGeminiStream.ts` 1346–1360, 1970–1990 |
| `info`/`warning`/`error` records from the UI; `Request cancelled.` on cancel | `packages/cli/src/ui/hooks/useHistoryManager.ts` 89–124; `useGeminiStream.ts` 746, 1013–1021, 2259–2264 |
| Sub-agent session id and main-session parent id | `packages/core/src/agents/local-executor.ts` 145–157, 330 |
| Sub-agent records `complete_task`, `$set.summary` on success | `local-executor.ts` 1294, 1309, 810–827 |
| Session summary appended to the previous session only | `packages/core/src/services/sessionSummaryUtils.ts` 416–449, 503 |
| `GEMINI_CLI_HOME`, project hash | `packages/core/src/utils/paths.ts` 13, 22–28, 318–320 |
| Filename sanitizing | `packages/core/src/utils/fileUtils.ts` 778–780 |
| Runtime dir (seatbelt `~/.cache/.gemini`), `tmp`, project temp dir, hash→slug migration | `packages/core/src/config/storage.ts` 54–60, 92–108, 195–197, 230–234, 277–324 |
| Slugs, `.project_root`, Windows lowercasing | `packages/core/src/config/projectRegistry.ts` 24, 95–101, 304–422 |
| Docker sandbox mounts an ephemeral `.gemini` | `packages/cli/src/utils/sandbox.ts` 527–571 |
| `usageMetadata` semantics (`total` = prompt + candidates + toolUsePrompt + thoughts) | `@google/genai` 1.30.0 `dist/genai.d.ts` 3205–3222 |
| Legacy `.json`: pretty-printed in-place rewrite, sub-agent `<id>.json` | tag v0.38.0 `chatRecordingService.ts` 212, 525–572 |
| ≤ v0.31 gemini record only with text; tool-only message created at tool completion | v0.31.0 `geminiChat.ts` 905; v0.32.0 909; v0.10.0 `chatRecordingService.ts` 300–338 |
| JSONL since v0.39 (commit f7449135); thoughts and per-chunk usage during the stream, record before validation | v0.39.0 `geminiChat.ts` 897–979 |
| Synthetic tool-result `user` record since v0.44 | v0.44.0 `geminiChat.ts` 445 |
| Buffered usage since v0.54; failure rollback since v0.54; abort rollback since v0.57 | v0.54.0 `geminiChat.ts` 1292, 1468, 671; v0.57.0 `geminiChat.ts` (`rollback(originalLength)`) |
| `$set.messages` checkpoints (v0.44 – v0.63) | v0.63.0 `chatRecordingService.ts` 955–1031 |
| `$patch` since v0.64 (commit d1cc08a8) | `chatRecordingService.ts` 1422–1644 |
