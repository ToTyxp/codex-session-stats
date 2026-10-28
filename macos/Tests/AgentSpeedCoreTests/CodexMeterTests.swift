import Foundation
import Testing
@testable import AgentSpeedCore

@Suite("Codex per-conversation meter")
struct CodexMeterTests {
    private final class Events {
        var values: [CodexMeterEvent] = []
        var samples: [CodexMeterSample] {
            values.compactMap { if case .sample(let sample) = $0 { sample } else { nil } }
        }
    }

    private let threadA = "11111111-1111-4111-8111-111111111111"
    private let threadB = "22222222-2222-4222-8222-222222222222"
    private let base: Int64 = 1_792_490_400_000

    private func line(_ offset: Int64, _ type: String, _ payload: [String: Any]) throws -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        let json: [String: Any] = [
            "timestamp": formatter.string(from: Date(timeIntervalSince1970: Double(base + offset) / 1000)),
            "type": type, "payload": payload,
        ]
        return String(decoding: try JSONSerialization.data(withJSONObject: json, options: [.sortedKeys]), as: UTF8.self) + "\n"
    }

    private func header(_ thread: String) throws -> String {
        try line(0, "session_meta", ["id": thread])
            + line(1, "turn_context", ["model": "fixture-model"])
    }

    private func request(_ id: String, start: Int64, end: Int64, output: Int = 120, reasoning: Int = 30) throws -> String {
        try line(start, "response_item", ["type": "function_call_output", "output": "PRIVATE_BODY_NEVER_CACHE"])
            + line(start + 10, "response_item", ["type": "reasoning", "encrypted_content": "PRIVATE_BODY_NEVER_CACHE"])
            + usage(id, end: end, output: output, reasoning: reasoning)
    }

    private func usage(_ id: String, end: Int64, output: Int = 120, reasoning: Int = 30) throws -> String {
        try line(end, "token_usage_record", [
            "response_id": id,
            "usage": ["input_tokens": 25, "output_tokens": output, "reasoning_output_tokens": reasoning],
        ])
    }

    private func meter(_ directory: TempDirectory, _ events: Events, host: String = "local") throws -> CodexMeter {
        try CodexMeter(codexHome: directory.file("codex"), cacheFile: directory.file("cache/meter.json"), hostId: host,
                       onEvent: { events.values.append($0) })
    }

    private func drain(_ meter: CodexMeter) {
        while meter.processNext() {}
    }

    @Test func samplesMatchOriginalResponseRecordsAndTimedResponsesForEveryCodexFixture() throws {
        // An independent original parser/reader pass is the reference, including compressed and legacy sessions.
        let expected = try Fixtures.json("parser-expected.json")
        let order = try #require(expected["order"] as? [String]).filter { $0.hasPrefix("codex/") }
        for relative in order {
            let directory = try TempDirectory()
            let fixture = Fixtures.root.appendingPathComponent(relative)
            let target = directory.file("codex/sessions/" + fixture.lastPathComponent)
            try FileManager.default.createDirectory(at: target.deletingLastPathComponent(), withIntermediateDirectories: true)
            try FileManager.default.copyItem(at: fixture, to: target)
            var reader = TranscriptReader(path: target.path)
            var parser = CodexParser(agentId: target.path, fileID: 1)
            var deduper = Deduper()
            var originals: [ResponseRecord] = []
            _ = reader.read { bytes in
                parser.consume(CodexLineExtractor.extract(bytes), deduper: &deduper) { upsert in
                    if upsert.isCompletion, upsert.record.isFinal, upsert.record.usage.output > 0,
                       TimedResponse(upsert.record) != nil { originals.append(upsert.record) }
                }
            }
            let events = Events()
            let meter = try meter(directory, events)
            meter.discover(nowMs: base, includeHistory: true)
            drain(meter)
            #expect(events.samples.count == originals.count, "\(relative)")
            for (sample, record) in zip(events.samples, originals) {
                let timed = try #require(TimedResponse(record))
                #expect(sample.threadId == parser.state.thread?.id)
                #expect(sample.responseId == record.key)
                #expect(sample.model == record.model)
                #expect(sample.outputTokens == record.usage.output)
                #expect(sample.reasoningTokens == record.usage.reasoning)
                #expect(sample.startMs == timed.startMs)
                #expect(sample.endMs == timed.endMs)
                #expect(sample.durationMs == timed.durationMs)
                #expect(sample.rate == SpeedEngine.summary(of: record, project: "").speed)
                #expect(sample.rate == timed.output / (Double(timed.durationMs) / 1000))
            }
        }
    }

    @Test func backgroundReasoningOnlyCompletionAndABARestoreAfterRestart() throws {
        let directory = try TempDirectory()
        try directory.write("codex/sessions/rollout-\(threadA).jsonl", header(threadA) + request("a1", start: 1000, end: 5000))
        try directory.write("codex/sessions/rollout-\(threadB).jsonl", header(threadB) + request("b1", start: 2000, end: 6000))
        let events = Events()
        let meter = try meter(directory, events)
        meter.select(threadId: threadA)
        meter.select(threadId: threadB)
        // A continues to run after switching to B. No assistant text is needed for a measured completion.
        try directory.append("codex/sessions/rollout-\(threadA).jsonl",
                             request("a2-reasoning-only", start: 7000, end: 11000, output: 240, reasoning: 240))
        meter.discover(nowMs: base + 12000)
        drain(meter)
        #expect(meter.cachedSample(threadId: threadA)?.responseId == "a2-reasoning-only")
        #expect(meter.cachedSample(threadId: threadA)?.rate == 60)
        meter.select(threadId: threadA)
        #expect(events.samples.last?.responseId == "a2-reasoning-only")
        #expect(events.samples.last?.outputTokens == 240)
        #expect(events.samples.last?.reasoningTokens == 240)

        let restoredEvents = Events()
        let restored = try self.meter(directory, restoredEvents)
        restored.emitRestoredSamples()
        #expect(Set(restoredEvents.samples.map(\.threadId)) == [threadA, threadB])
        restored.discover(nowMs: base, includeHistory: true)
        drain(restored)
        #expect(restoredEvents.samples.count == 2, "replayed/older history cannot replace restored cache entries")
        // Cache hit still works when the source logs are unavailable: no source scan is required.
        try FileManager.default.removeItem(at: directory.file("codex"))
        restored.select(threadId: threadA)
        restored.select(threadId: threadB)
        restored.select(threadId: threadA)
        #expect(restoredEvents.samples.suffix(3).map(\.responseId) == ["a2-reasoning-only", "b1", "a2-reasoning-only"])
        let text = try String(contentsOf: directory.file("cache/meter.json"), encoding: .utf8)
        #expect(!text.contains("PRIVATE_BODY_NEVER_CACHE"))
        #expect(!text.contains("rollout-"))
    }

    @Test func oldDuplicateInvalidAndIncompleteResponsesNeverReplaceLatest() throws {
        let directory = try TempDirectory()
        let path = "codex/sessions/a.jsonl"
        try directory.write(path, header(threadA) + request("latest", start: 10_000, end: 14_000))
        let events = Events()
        let meter = try meter(directory, events)
        meter.select(threadId: threadA)
        #expect(events.samples.count == 1)
        try directory.append(path,
                             request("older", start: 1000, end: 5000)
                             + request("latest", start: 15_000, end: 19_000)
                             + request("too-long", start: 20_000, end: 20_000 + 15 * 60_000 + 1)
                             + request("backwards", start: 1_100_000, end: 1_000_000)
                             + request("no-output", start: 1_200_000, end: 1_204_000, output: 0, reasoning: 0)
                             + request("impossible-reasoning", start: 1_250_000, end: 1_254_000, output: 120, reasoning: 121)
                             + line(1_300_000, "response_item", ["type": "function_call_output"])
                             + line(1_301_000, "response_item", ["type": "reasoning"]))
        meter.discover(nowMs: base)
        drain(meter)
        #expect(events.samples.count == 1)
        #expect(meter.cachedSample(threadId: threadA)?.responseId == "latest")
        let restored = try self.meter(directory, Events())
        #expect(restored.cachedSample(threadId: threadA)?.responseId == "latest")
    }

    @Test func selectingRestoredCacheImmediatelyEmitsOldValueThenRecoversCompletionMissedWhileStopped() throws {
        let directory = try TempDirectory()
        let path = "codex/sessions/nonstandard-old-conversation.jsonl"
        try directory.write(path, header(threadA) + request("before-stop", start: 1000, end: 5000))
        let original = try meter(directory, Events())
        original.select(threadId: threadA)
        #expect(original.cachedSample(threadId: threadA)?.responseId == "before-stop")
        try directory.append(path, request("while-stopped", start: 6000, end: 10_000, output: 240, reasoning: 240))
        let now = Date()
        try FileManager.default.setAttributes(
            [.modificationDate: now.addingTimeInterval(-Double(CodexMeter.activeWindowMs) / 1000 - 60)],
            ofItemAtPath: directory.file(path).path
        )
        let events = Events()
        let restarted = try meter(directory, events)
        restarted.discover(nowMs: Int64(now.timeIntervalSince1970 * 1000))
        drain(restarted)
        #expect(events.samples.isEmpty, "old metadata is outside the background active window")
        #expect(restarted.cachedSample(threadId: threadA)?.responseId == "before-stop")
        restarted.select(threadId: threadA)
        #expect(events.samples.map(\.responseId) == ["before-stop", "while-stopped"])
        #expect(events.samples.last?.rate == 60)
        #expect(restarted.cachedSample(threadId: threadA)?.responseId == "while-stopped")
    }

    @Test func impossibleReasoningSubsetCannotOccupyRestoredCache() throws {
        let directory = try TempDirectory()
        try directory.write("codex/sessions/a.jsonl", header(threadA) + request("valid", start: 1000, end: 5000))
        let original = try meter(directory, Events())
        original.select(threadId: threadA)
        let cacheURL = directory.file("cache/meter.json")
        var cache = try #require(JSONSerialization.jsonObject(with: Data(contentsOf: cacheURL)) as? [String: Any])
        var samples = try #require(cache["samples"] as? [[String: Any]])
        samples[0]["reasoningTokens"] = 121
        cache["samples"] = samples
        try JSONSerialization.data(withJSONObject: cache).write(to: cacheURL, options: .atomic)
        let events = Events()
        let restored = try meter(directory, events)
        #expect(restored.cachedSample(threadId: threadA) == nil)
        restored.emitRestoredSamples()
        #expect(events.samples.isEmpty)
        restored.select(threadId: threadA)
        #expect(events.samples.map(\.responseId) == ["valid"])
        #expect(restored.cachedSample(threadId: threadA)?.reasoningTokens == 30)
    }

    @Test func partialLineAndTruncateUseUpstreamReaderAndDeduper() throws {
        let directory = try TempDirectory()
        let path = "codex/sessions/rollout-\(threadA).jsonl"
        let complete = try request("first", start: 1000, end: 5000)
        try directory.write(path, header(threadA) + String(complete.dropLast()))
        let events = Events()
        let meter = try meter(directory, events)
        meter.select(threadId: threadA)
        #expect(events.samples.isEmpty, "an unterminated completion waits for its newline")
        try directory.append(path, "\n")
        meter.discover(nowMs: base)
        drain(meter)
        #expect(events.samples.map(\.responseId) == ["first"])
        // Reset the inode/offset, replay a duplicate, then append a new complete response.
        try directory.write(path, "")
        meter.discover(nowMs: base)
        drain(meter)
        let rebuilt = try header(threadA) + complete + request("second", start: 6000, end: 10_000)
        try directory.write(path, rebuilt)
        meter.discover(nowMs: base)
        drain(meter)
        #expect(events.samples.map(\.responseId) == ["first", "second"])
        // Reconstruct request boundaries with an independent full upstream parse, then deduplicate samples.
        var originalParser = CodexParser(agentId: directory.file(path).path, fileID: 1)
        var originalDeduper = Deduper()
        var reference: [ResponseRecord] = []
        forEachLine(in: Data(rebuilt.utf8)) { bytes in
            originalParser.consume(CodexLineExtractor.extract(bytes), deduper: &originalDeduper) { reference.append($0.record) }
        }
        let original = try #require(reference.last)
        let timed = try #require(TimedResponse(original))
        #expect(reference.map(\.key) == ["first", "second"])
        #expect(meter.cachedSample(threadId: threadA)?.startMs == timed.startMs)
        #expect(meter.cachedSample(threadId: threadA)?.durationMs == timed.durationMs)
        #expect(meter.cachedSample(threadId: threadA)?.rate == SpeedEngine.summary(of: original, project: "").speed)
    }

    @Test func filenameCannotSpoofThreadAndHistoricalSelectionDoesNotDrainBackgroundQueue() throws {
        let directory = try TempDirectory()
        let old = Date(timeIntervalSince1970: Double(base - 86_400_000) / 1000)
        // Name A belongs to metadata B. Association must come from parser.state.thread.id.
        try directory.write("codex/sessions/rollout-\(threadA).jsonl", header(threadB) + request("b1", start: 1000, end: 5000), modified: old)
        try directory.write("codex/sessions/unusual-name.jsonl", header(threadA) + request("a1", start: 2000, end: 6000), modified: old)
        try directory.write("codex/sessions/hot.jsonl", header("hot") + request("hot1", start: 3000, end: 7000))
        let events = Events()
        let meter = try meter(directory, events)
        meter.discover(nowMs: Int64(Date().timeIntervalSince1970 * 1000))
        #expect(events.samples.isEmpty, "discovery only inspects metadata")
        meter.select(threadId: threadA)
        #expect(meter.cachedSample(threadId: threadA)?.responseId == "a1")
        #expect(meter.cachedSample(threadId: threadB)?.responseId == "b1")
        #expect(meter.cachedSample(threadId: "hot") == nil, "background queue was not drained before selection")
        drain(meter)
        #expect(meter.cachedSample(threadId: "hot")?.responseId == "hot1")
        meter.select(threadId: threadA, hostId: "remote")
        #expect(events.values.last == .unavailable(CodexMeterUnavailable(hostId: "remote", threadId: threadA, reason: "unsupported-host")))
    }

    @Test func minimumAndMaximumIntervalBoundariesAreExactlyOriginal() throws {
        let directory = try TempDirectory()
        try directory.write("codex/sessions/a.jsonl",
                            header(threadA) + request("tiny", start: 1000, end: 1100, output: 100)
                            + request("max", start: 2000, end: 902_000, output: 900)
                            + request("beyond", start: 903_000, end: 1_803_001))
        let events = Events()
        let meter = try meter(directory, events)
        meter.select(threadId: threadA)
        #expect(events.samples.map(\.responseId) == ["tiny", "max"])
        #expect(events.samples.first?.startMs == base + 600)
        #expect(events.samples.first?.durationMs == 500)
        #expect(events.samples.first?.rate == 200)
        #expect(events.samples.last?.durationMs == 900_000)
        #expect(events.samples.last?.rate == 1)
    }

    @Test func nonstandardCompressedNameUsesBoundedHeaderLookupThenOriginalFullReader() throws {
        let directory = try TempDirectory()
        let old = Date(timeIntervalSince1970: Double(base - 86_400_000) / 1000)
        try directory.write("codex/sessions/unknown-name.jsonl.zst",
                            bytes: ZstdFrames.raw(header(threadA) + request("compressed", start: 1000, end: 5000)))
        try FileManager.default.setAttributes([.modificationDate: old],
                                             ofItemAtPath: directory.file("codex/sessions/unknown-name.jsonl.zst").path)
        let events = Events()
        let meter = try meter(directory, events)
        meter.select(threadId: threadA)
        #expect(events.samples.map(\.responseId) == ["compressed"])
        #expect(events.samples.first?.rate == 30)
    }

    @Test func selectingKnownConversationAlsoReplaysUnassociatedHistoricalFiles() throws {
        let directory = try TempDirectory()
        let old = Date(timeIntervalSince1970: Double(base - 86_400_000) / 1000)
        try directory.write("codex/sessions/rollout-\(threadA).jsonl",
                            header(threadA) + request("known", start: 1000, end: 5000))
        try directory.write("codex/archived_sessions/nonstandard-part.jsonl",
                            header(threadA) + request("missed-history", start: 6000, end: 10_000), modified: old)
        let events = Events()
        let meter = try meter(directory, events)
        meter.discover(nowMs: Int64(Date().timeIntervalSince1970 * 1000))
        drain(meter)
        #expect(meter.cachedSample(threadId: threadA)?.responseId == "known")
        meter.select(threadId: threadA)
        #expect(meter.cachedSample(threadId: threadA)?.responseId == "missed-history")
    }

    @Test func compressedReplayPreservesNextResponseTimingWithoutReemittingHistory() throws {
        let directory = try TempDirectory()
        let path = "codex/sessions/rollout-\(threadA).jsonl.zst"
        let first = try header(threadA) + request("first", start: 1000, end: 5000)
        try directory.write(path, bytes: ZstdFrames.raw(first))
        let events = Events()
        let meter = try meter(directory, events)
        meter.select(threadId: threadA)
        #expect(events.samples.map(\.responseId) == ["first"])
        try directory.write(path, bytes: ZstdFrames.raw(first + request("second", start: 6000, end: 10_000)))
        meter.discover(nowMs: base)
        drain(meter)
        #expect(events.samples.map(\.responseId) == ["first", "second"])
        #expect(events.samples.last?.startMs == base + 6000)
        #expect(events.samples.last?.durationMs == 4000)
        #expect(events.samples.last?.rate == 30)
    }

    @Test func copiedResponseStaysWithFirstThreadWhileNextRequestKeepsItsOwnTiming() throws {
        let directory = try TempDirectory()
        let copied = try request("copied", start: 1000, end: 5000)
        try directory.write("codex/sessions/rollout-\(threadA).jsonl", header(threadA) + copied)
        try directory.write("codex/sessions/rollout-\(threadB).jsonl",
                            header(threadB) + copied + request("b-own", start: 6000, end: 10_000))
        let events = Events()
        let meter = try meter(directory, events)
        meter.select(threadId: threadA)
        meter.select(threadId: threadB)
        #expect(events.samples.filter { $0.responseId == "copied" }.map(\.threadId) == [threadA])
        #expect(meter.cachedSample(threadId: threadB)?.responseId == "b-own")
        #expect(meter.cachedSample(threadId: threadB)?.startMs == base + 6000)
        #expect(meter.cachedSample(threadId: threadB)?.durationMs == 4000)
        #expect(meter.cachedSample(threadId: threadB)?.rate == 30)
    }
}
