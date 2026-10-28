import Foundation
import Testing
@testable import AgentSpeedCore

@Suite("Codex cumulative session stats")
struct CodexSessionStatsTests {
    private let a = "11111111-1111-4111-8111-111111111111"
    private let b = "22222222-2222-4222-8222-222222222222"
    private let base: Int64 = 1_792_490_400_000
    private final class Events {
        var values: [CodexMeterEvent] = []
        var stats: [CodexSessionStats] { values.compactMap { if case .stats(let x) = $0 { x } else { nil } } }
    }
    private func line(_ offset: Int64, _ type: String, _ payload: [String: Any]) throws -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return String(decoding: try JSONSerialization.data(withJSONObject: [
            "timestamp": formatter.string(from: Date(timeIntervalSince1970: Double(base + offset) / 1000)),
            "type": type, "payload": payload,
        ], options: [.sortedKeys]), as: UTF8.self) + "\n"
    }
    private func header(_ thread: String, at: Int64 = 0) throws -> String {
        try line(at, "session_meta", ["id": thread])
    }
    private func usage(_ id: String?, at: Int64, turn: String? = nil, thread: String? = nil,
                       input: Int = 100, cached: Int = 80, output: Int = 20) throws -> String {
        var payload: [String: Any] = ["usage": ["input_tokens": input, "cached_input_tokens": cached,
                                                       "output_tokens": output, "reasoning_output_tokens": output]]
        if let id { payload["response_id"] = id }
        if let turn { payload["turn_id"] = turn }
        if let thread { payload["thread_id"] = thread }
        return try line(at, "token_usage_record", payload)
    }
    private func meter(_ dir: TempDirectory, _ events: Events = Events()) throws -> CodexMeter {
        try CodexMeter(codexHome: dir.file("codex"), cacheFile: dir.file("cache/meter.json"),
                       onEvent: { events.values.append($0) })
    }
    private func drain(_ meter: CodexMeter) { while meter.processNext() {} }
    private func refresh(_ meter: CodexMeter) { meter.discover(nowMs: base, includeHistory: true); drain(meter) }

    @Test func countsCompletedCallsIndependentlyOfSpeedAndTurnBoundaries() throws {
        let dir = try TempDirectory()
        try dir.write("codex/sessions/a.jsonl", header(a)
                      + line(100, "event_msg", ["type": "task_started", "turn_id": "turn-a"])
                      + usage("no-start", at: 1000, output: 0)
                      + usage("second", at: 2_000_000)
                      + line(2_001_000, "event_msg", ["type": "task_complete", "turn_id": "turn-a"])
                      + line(2_002_000, "event_msg", ["type": "task_started", "turn_id": "unfinished-turn"]))
        let m = try meter(dir)
        m.select(threadId: a)
        let value = try #require(m.cachedStats(threadId: a))
        #expect(value.steps == 2 && value.turns == 1)
        #expect(value.inputTokens == 200 && value.cachedInputTokens == 160 && value.outputTokens == 20)
        #expect(value.complete)
        #expect(m.cachedSample(threadId: a) == nil)
    }

    @Test func scopeExcludesForeignCopiedHistoryAndChildrenAndMissingTurnRemainsNull() throws {
        let dir = try TempDirectory()
        try dir.write("codex/sessions/a.jsonl", header(a, at: 10000)
                      + usage("ancestor", at: 1000, turn: "old", thread: a)
                      + usage("foreign", at: 11000, turn: "wrong", thread: b)
                      + usage("own", at: 12000, thread: a))
        try dir.write("codex/sessions/child.jsonl", line(10000, "session_meta", ["id": b, "parent_thread_id": a])
                      + usage("child", at: 12000, turn: "child-turn", thread: b))
        let m = try meter(dir)
        m.select(threadId: a)
        let value = try #require(m.cachedStats(threadId: a))
        #expect(value.steps == 1 && value.turns == nil && value.inputTokens == 100)
        let json = try #require(JSONSerialization.jsonObject(with: JSONEncoder().encode(CodexMeterEvent.stats(value))) as? [String: Any])
        #expect(json["turns"] is NSNull)
        #expect(json["source"] as? String == "agent-speed/1.4.1")
    }

    @Test func lateCompletionRevisionPersistsAndDuplicateReplayDoesNotAdvanceIt() throws {
        let dir = try TempDirectory()
        let path = "codex/sessions/a.jsonl"
        let initial = try header(a) + usage("newest", at: 10000, turn: "one")
        try dir.write(path, initial)
        let m = try meter(dir)
        m.select(threadId: a)
        let before = try #require(m.cachedStats(threadId: a))
        try dir.append(path, usage("late-old", at: 1000, turn: "two"))
        refresh(m)
        let after = try #require(m.cachedStats(threadId: a))
        #expect(after.steps == 2 && after.turns == 2 && after.updatedAtMs > before.updatedAtMs)
        try dir.append(path, usage("newest", at: 10001, turn: "one"))
        refresh(m)
        #expect(m.cachedStats(threadId: a)?.updatedAtMs == after.updatedAtMs)
        let restarted = try meter(dir)
        #expect(restarted.cachedStats(threadId: a) == after)
        restarted.select(threadId: a)
        #expect(restarted.cachedStats(threadId: a) == after)
    }

    @Test func cacheBackfillPublishesWholeTotalsAndV1RemainsReadable() throws {
        let dir = try TempDirectory()
        let active = "codex/sessions/a.jsonl"
        let archived = "codex/archived_sessions/old.jsonl"
        try dir.write(active, header(a) + usage("first", at: 1000, turn: "one"))
        try dir.write(archived, header(a) + usage("second", at: 2000, turn: "two"))
        let m = try meter(dir)
        m.select(threadId: a)
        let previous = try #require(m.cachedStats(threadId: a))
        try dir.append(archived, usage("third", at: 3000, turn: "three"))
        let events = Events()
        let restarted = try meter(dir, events)
        restarted.select(threadId: a)
        #expect(events.stats.map(\.steps) == [2, 3])
        #expect(events.stats.first == previous)
        #expect(events.stats.last?.complete == true)
        try dir.write("cache/meter.json", "{\"version\":1,\"samples\":[]}")
        let v1 = try meter(dir)
        #expect(v1.cachedStats(threadId: a) == nil)
        v1.select(threadId: a)
        #expect(v1.cachedStats(threadId: a)?.steps == 3)
    }

    @Test func partialMalformedMissingIdAndUnavailableNeverClaimCompleteOrFalseZero() throws {
        let dir = try TempDirectory()
        let path = "codex/sessions/a.jsonl"
        try dir.write(path, header(a) + usage("good", at: 1000, turn: "one"))
        let m = try meter(dir)
        m.select(threadId: a)
        try dir.append(path, String(try usage("partial", at: 2000, turn: "one").dropLast()))
        refresh(m)
        #expect(m.cachedStats(threadId: a)?.steps == 1)
        #expect(m.cachedStats(threadId: a)?.complete == false)
        try dir.append(path, "\n")
        refresh(m)
        #expect(m.cachedStats(threadId: a)?.steps == 2)
        #expect(m.cachedStats(threadId: a)?.complete == true)
        try dir.append(path, usage(nil, at: 3000) + "malformed\n")
        refresh(m)
        #expect(m.cachedStats(threadId: a)?.steps == 2)
        #expect(m.cachedStats(threadId: a)?.complete == false)
        try FileManager.default.removeItem(at: dir.file("codex"))
        let restarted = try meter(dir)
        restarted.select(threadId: a)
        #expect(restarted.cachedStats(threadId: a)?.steps == 2)
        #expect(restarted.cachedStats(threadId: a)?.complete == false)
        #expect(restarted.cachedStats(threadId: b) == nil)
    }

    @Test func truncateCompressedRewriteAndArchiveMigrationReplaceContributions() throws {
        let dir = try TempDirectory()
        let path = "codex/sessions/a.jsonl.zst"
        let first = try header(a) + usage("one", at: 1000, turn: "one") + usage("two", at: 2000, turn: "two")
        try dir.write(path, bytes: ZstdFrames.raw(first))
        let m = try meter(dir)
        m.select(threadId: a)
        #expect(m.cachedStats(threadId: a)?.steps == 2)
        try dir.write(path, bytes: ZstdFrames.raw(try header(a) + usage("replacement", at: 3000, turn: "new")))
        refresh(m)
        #expect(m.cachedStats(threadId: a)?.steps == 1)
        #expect(m.cachedStats(threadId: a)?.inputTokens == 100)
        let archived = dir.file("codex/archived_sessions/moved.jsonl.zst")
        try FileManager.default.createDirectory(at: archived.deletingLastPathComponent(), withIntermediateDirectories: true)
        try FileManager.default.moveItem(at: dir.file(path), to: archived)
        refresh(m)
        #expect(m.cachedStats(threadId: a)?.steps == 1)
        #expect(m.cachedStats(threadId: a)?.complete == true)
        let plain = "codex/sessions/a.jsonl"
        try dir.write(plain, header(a) + usage("replacement", at: 3000, turn: "new") + usage("extra", at: 4000, turn: "new"))
        refresh(m)
        #expect(m.cachedStats(threadId: a)?.steps == 2)
        try dir.write(plain, header(a))
        refresh(m)
        #expect(m.cachedStats(threadId: a)?.steps == 1)
    }

    @Test func dedupHasNoSpeedWindowAndDualWriteUsesOneCompletion() throws {
        var contributions = CodexSessionContributions()
        var parser = CodexParser(agentId: "test", fileID: 1)
        var deduper = Deduper(capacity: 2)
        func consume(_ text: String) {
            forEachLine(in: Data(text.utf8)) { bytes in
                let extraction = CodexLineExtractor.extract(bytes)
                parser.consume(extraction, deduper: &deduper) { _ in }
                contributions.consume(extraction, state: parser.state)
            }
        }
        consume(try header(a))
        let legacy = try line(1000, "event_msg", ["type": "token_count", "info": [
            "total_token_usage": ["input_tokens": 100, "cached_input_tokens": 80, "output_tokens": 20, "reasoning_output_tokens": 20, "total_tokens": 120]]])
        consume(legacy)
        consume(try usage("first", at: 1001, turn: "one"))
        consume(legacy)
        for i in 0..<20 { consume(try usage("r\(i)", at: Int64(2000 + i), turn: "one")) }
        consume(try usage("first", at: 3000, turn: "one"))
        #expect(contributions.responses.count == 21)
        #expect(!contributions.malformed)
    }

    @Test func invalidCountersAndEmptyUsageCannotBecomeCredibleZeroTokenCalls() throws {
        let invalid: [[String: Any]] = [
            [:], ["total_tokens": 100], ["input_tokens": -1, "output_tokens": 0],
            ["input_tokens": "100", "output_tokens": 0],
            ["input_tokens": 100, "output_tokens": NSNull()],
            ["input_tokens": 100, "output_tokens": 0, "cached_input_tokens": -1],
            ["input_tokens": 100, "output_tokens": 0, "reasoning_output_tokens": -0.5],
            ["input_tokens": 0.5, "output_tokens": 0],
            ["input_tokens": 100, "output_tokens": 1.5],
            ["input_tokens": TokenCount.ceiling + 1, "output_tokens": 0],
        ]
        for bad in invalid {
            let dir = try TempDirectory()
            try dir.write("codex/sessions/a.jsonl", header(a) + usage("good", at: 1000, turn: "one")
                          + line(2000, "token_usage_record", ["response_id": "invalid", "turn_id": "false-turn", "usage": bad]))
            let m = try meter(dir)
            m.select(threadId: a)
            let value = try #require(m.cachedStats(threadId: a))
            #expect(value.steps == 1 && value.turns == 1 && value.inputTokens == 100 && value.outputTokens == 20)
            #expect(!value.complete)
        }
    }

    @Test func foreignUsageCannotChangeOwnLegacyAccountingOrCreateTurns() throws {
        let dir = try TempDirectory()
        func legacy(_ thread: String, total: Int, at: Int64) throws -> String {
            try line(at, "event_msg", ["type": "token_count", "thread_id": thread, "info": [
                "total_token_usage": ["input_tokens": total, "output_tokens": total / 5, "total_tokens": total + total / 5]]])
        }
        try dir.write("codex/sessions/a.jsonl", header(a)
                      + legacy(b, total: 1000, at: 500)
                      + legacy(a, total: 100, at: 1000)
                      + usage("foreign-modern", at: 1500, turn: "foreign-turn", thread: b, input: -1)
                      + legacy(b, total: 2000, at: 1800)
                      + legacy(a, total: 200, at: 2000))
        let m = try meter(dir)
        m.select(threadId: a)
        let value = try #require(m.cachedStats(threadId: a))
        #expect(value.steps == 2 && value.turns == nil)
        #expect(value.inputTokens == 200 && value.outputTokens == 40)
        #expect(value.complete)
    }

    @Test func invalidLegacyCountsCannotPassThroughNormalizedParserRecords() throws {
        let dir = try TempDirectory()
        try dir.write("codex/sessions/a.jsonl", header(a)
                      + line(1000, "event_msg", ["type": "token_count", "info": [
                        "total_token_usage": ["input_tokens": 100, "output_tokens": 20, "total_tokens": 120]]])
                      + line(2000, "event_msg", ["type": "token_count", "info": [
                        "total_token_usage": ["input_tokens": 200, "output_tokens": 40, "total_tokens": 240],
                        "last_token_usage": ["input_tokens": -1, "output_tokens": 20]]]))
        let m = try meter(dir)
        m.select(threadId: a)
        let value = try #require(m.cachedStats(threadId: a))
        #expect(value.steps == 1 && value.inputTokens == 100 && value.outputTokens == 20)
        #expect(!value.complete)
    }

    @Test func duplicateResponseCannotBorrowAnUnrelatedCurrentTurn() throws {
        let dir = try TempDirectory()
        try dir.write("codex/sessions/a.jsonl", header(a) + usage("unknown-turn", at: 1000)
                      + line(1500, "event_msg", ["type": "task_started", "turn_id": "known-turn"])
                      + usage("second", at: 2000)
                      + usage("unknown-turn", at: 2500))
        let m = try meter(dir)
        m.select(threadId: a)
        let value = try #require(m.cachedStats(threadId: a))
        #expect(value.steps == 2 && value.turns == nil)
        #expect(value.inputTokens == 200 && value.complete)
    }
}
