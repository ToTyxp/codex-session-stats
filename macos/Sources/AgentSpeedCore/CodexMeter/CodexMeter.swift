import Darwin
import Foundation

/// Read-only Codex adapter. The caller serializes calls and controls polling cadence.
/// No LiveMonitor, price loader, reporting, authentication or model client is started here.
public final class CodexMeter {
    private struct SessionKey: Hashable {
        var host: String
        var thread: String
    }

    private struct Cache: Codable {
        var version = 2
        var samples: [CodexMeterSample]
        var stats: [CodexSessionStats]?
    }

    private final class File {
        let path: String
        let id: FileID
        var reader: TranscriptReader
        var parser: CodexParser
        var deduper = Deduper()
        var contributions = CodexSessionContributions()
        var lastReadStat: FileStat?

        init(path: String, id: FileID) {
            self.path = path
            self.id = id
            reader = TranscriptReader(path: path)
            parser = CodexParser(agentId: path, fileID: id)
        }

        func resetParser() {
            parser = CodexParser(agentId: path, fileID: id)
            deduper = Deduper()
            contributions = CodexSessionContributions()
        }
    }

    public let hostId: String
    private let cacheFile: URL
    private let roots: [String]
    private let onEvent: (CodexMeterEvent) -> Void
    private let onDiagnostic: (String) -> Void
    private var indexes: [DirectoryIndex]
    private var stats: [String: FileStat] = [:]
    private var files: [String: File] = [:]
    private var threadPaths: [String: Set<String>] = [:]
    private var pathThreads: [String: String] = [:]
    private var pending: [String] = []
    private var queued: Set<String> = []
    private var sampleDeduper = Deduper()
    private var nextFileId: FileID = 1
    private var latest: [SessionKey: CodexMeterSample] = [:]
    private var totals: [SessionKey: CodexSessionStats] = [:]
    private var selectedThreads: Set<String> = []
    private var coverage: Set<String> = []
    private var needsCoverage: Set<String> = []
    private var rebuilding: Set<String> = []
    /// A cached full snapshot is held until its complete source coverage has been reconstructed.
    private var baselines: Set<String> = []

    /// Startup reads only recently active files; choosing an old conversation explicitly reads its history.
    public static let activeWindowMs: Int64 = 2 * 60_000

    public init(
        codexHome: URL,
        cacheFile: URL,
        hostId: String = "local",
        onEvent: @escaping (CodexMeterEvent) -> Void,
        onDiagnostic: @escaping (String) -> Void = { _ in }
    ) throws {
        self.hostId = hostId
        self.cacheFile = cacheFile.standardizedFileURL
        self.onEvent = onEvent
        self.onDiagnostic = onDiagnostic
        roots = ["sessions", "archived_sessions"].map {
            codexHome.standardizedFileURL.appendingPathComponent($0, isDirectory: true).path
        }
        let destination = cacheFile.resolvingSymlinksInPath().path
        guard !roots.contains(where: {
            let root = URL(fileURLWithPath: $0).resolvingSymlinksInPath().path
            return destination == root || destination.hasPrefix(root + "/")
        }) else {
            throw CocoaError(.fileWriteNoPermission)
        }
        indexes = roots.map { DirectoryIndex(root: $0, names: .codexSession) }
        if FileManager.default.fileExists(atPath: cacheFile.path) {
            do {
                let cache = try JSONDecoder().decode(Cache.self, from: Data(contentsOf: cacheFile))
                if cache.version == 1 || cache.version == 2 {
                    for sample in cache.samples where sample.isValidCacheEntry {
                        let key = SessionKey(host: sample.hostId, thread: Self.threadKey(sample.threadId))
                        if latest[key].map({ $0.endMs < sample.endMs }) ?? true { latest[key] = sample }
                    }
                    for value in cache.stats ?? [] where value.isValidCacheEntry {
                        let key = SessionKey(host: value.hostId, thread: Self.threadKey(value.threadId))
                        totals[key] = value
                        if value.hostId == hostId { baselines.insert(key.thread) }
                    }
                }
            } catch {
                onDiagnostic("cache read failed")
            }
        }
    }

    /// Emit persisted per-conversation values immediately, before any transcript reads.
    public func emitRestoredSamples() {
        for sample in sortedSamples where sample.hostId == hostId { onEvent(.sample(sample)) }
        for value in sortedStats where value.hostId == hostId { onEvent(.stats(value)) }
    }

    public func cachedSample(threadId: String, hostId: String? = nil) -> CodexMeterSample? {
        latest[SessionKey(host: hostId ?? self.hostId, thread: Self.threadKey(threadId))]
    }

    public func cachedStats(threadId: String, hostId: String? = nil) -> CodexSessionStats? {
        totals[SessionKey(host: hostId ?? self.hostId, thread: Self.threadKey(threadId))]
    }

    /// DirectoryIndex caches directory listings; this operation stats metadata and queues changed/active files.
    /// It does not scan transcript bodies, so selection can be handled before background reads.
    public func discover(nowMs: Int64, includeHistory: Bool = false) {
        let previousPaths = Set(stats.keys)
        var seen: Set<String> = []
        var additions: [DiscoveredFile] = []
        for index in indexes.indices {
            let candidates = indexes[index].scan()
            for file in TranscriptDiscovery.files(at: candidates) where seen.insert(file.path).inserted {
                let previous = stats.updateValue(file.stat, forKey: file.path)
                if previous != nil, previous != file.stat, let thread = pathThreads[file.path], selectedThreads.contains(thread) {
                    needsCoverage.insert(thread)
                    coverage.remove(thread)
                }
                if includeHistory || (previous == nil && file.stat.modifiedMs >= nowMs - Self.activeWindowMs)
                    || (previous != nil && previous != file.stat) {
                    additions.append(file)
                }
            }
        }
        for path in stats.keys where !seen.contains(path) {
            stats[path] = nil
            files[path] = nil
            removeThreadPath(path)
            queued.remove(path)
        }
        if previousPaths != seen {
            needsCoverage.formUnion(selectedThreads)
            coverage.subtract(selectedThreads)
        }
        pending.removeAll { !seen.contains($0) }
        for file in additions.sorted(by: TranscriptDiscovery.startupOrder) { enqueue(file.path) }
    }

    /// Read at most one queued file using the upstream incremental reader.
    @discardableResult
    public func processNext() -> Bool {
        guard !pending.isEmpty else {
            if let thread = needsCoverage.sorted().first {
                rebuildStats(thread: thread)
                return true
            }
            return false
        }
        let path = pending.removeFirst()
        queued.remove(path)
        read(path)
        return true
    }

    /// A cache hit is emitted synchronously, then its conversation is checked for completions missed while stopped.
    /// A miss prioritizes that conversation's file before background work.
    /// File names are only a lookup hint: all association comes from CodexParser.state.thread.id.
    public func select(threadId: String, hostId: String? = nil) {
        let host = hostId ?? self.hostId
        let thread = Self.threadKey(threadId)
        if let value = cachedStats(threadId: thread, hostId: host) { onEvent(.stats(value)) }
        if let cached = cachedSample(threadId: thread, hostId: host) {
            onEvent(.sample(cached))
        }
        guard host == self.hostId else {
            if cachedSample(threadId: thread, hostId: host) == nil {
                unavailable(host: host, thread: threadId, reason: "unsupported-host")
            }
            return
        }
        discover(nowMs: Clock.milliseconds(Date()))

        selectedThreads.insert(thread)
        rebuildStats(thread: thread)
        if cachedSample(threadId: thread) == nil {
            unavailable(host: host, thread: threadId,
                        reason: threadPaths[thread]?.isEmpty == false ? "no-valid-completed-response" : "session-not-found")
        }
    }

    private func rebuildStats(thread: String) {
        rebuilding.insert(thread)
        needsCoverage.remove(thread)
        var candidates = threadPaths[thread] ?? []
        // Standard rollout names end in the UUID; this avoids reading unrelated history at startup.
        for path in stats.keys {
            let name = (path as NSString).lastPathComponent.lowercased()
            if name.hasSuffix(thread.lowercased() + ".jsonl") || name.hasSuffix(thread.lowercased() + ".jsonl.zst") {
                candidates.insert(path)
            }
        }
        for path in ordered(candidates) { read(path) }

        // A known file does not rule out an additional historical file with a nonstandard name.
        // Associate unknown files through their bounded headers, including compressed sessions.
        var knownHeaders = true
        for path in stats.keys.sorted() where !candidates.contains(path) && pathThreads[path] == nil {
            if let found = headerThread(path) {
                associate(path: path, thread: found)
                if found == thread { read(path) }
            } else { knownHeaders = false }
        }
        rebuilding.remove(thread)
        if knownHeaders, threadPaths[thread]?.isEmpty == false,
           (threadPaths[thread] ?? []).allSatisfy({ files[$0]?.contributions.readComplete == true }) {
            coverage.insert(thread)
            baselines.remove(thread)
        } else {
            coverage.remove(thread)
        }
        publishStats(thread: thread)
    }

    private func enqueue(_ path: String) {
        if queued.insert(path).inserted { pending.append(path) }
    }

    private func read(_ path: String) {
        guard let stat = FileStat.lstat(path), stat.isRegularFile else {
            files[path]?.contributions.readComplete = false
            if let thread = pathThreads[path] { coverage.remove(thread) }
            return
        }
        let file: File
        if let existing = files[path] {
            file = existing
        } else {
            file = File(path: path, id: nextFileId)
            nextFileId &+= 1
            files[path] = file
        }
        // Same-size in-place replacement is not an append; rebuild its contributions and request boundaries.
        if let previous = file.lastReadStat, previous.identity == stat.identity,
           previous.size == stat.size, previous.modifiedNs != stat.modifiedNs {
            file.reader = TranscriptReader(path: path)
            file.resetParser()
        }
        let result = file.reader.read(
            onReset: { file.resetParser() },
            onMalformed: { file.parser.noteMalformedLine(); file.contributions.malformed = true }
        ) { bytes in
            var upserts: [ResponseUpsert] = []
            // Replays must advance every original request boundary before output deduplication.
            // A global parser deduper would skip historical completions after reset or file migration,
            // leaving the parser's old request open and corrupting the next response's duration.
            let extraction = CodexLineExtractor.extract(bytes)
            file.parser.consume(extraction, deduper: &file.deduper) { upserts.append($0) }
            file.contributions.consume(extraction, state: file.parser.state)
            guard let id = file.parser.state.thread?.id else { return }
            let thread = Self.threadKey(id)
            associate(path: path, thread: thread)
            for upsert in upserts {
                if let sample = CodexMeterSample(hostId: hostId, threadId: thread, upsert: upsert) { accept(sample) }
            }
        }
        file.contributions.readComplete = !result.failed && !result.oversized && !result.partialHistory
            && !file.contributions.malformed && file.parser.state.thread?.id != nil
        if let stat = result.stat { stats[path] = stat; file.lastReadStat = stat }
        if result.failed { onDiagnostic("transcript read failed") }
        if result.didReset, file.parser.state.thread?.id == nil {
            if let thread = pathThreads[path] { needsCoverage.insert(thread); coverage.remove(thread) }
            removeThreadPath(path)
        }
        if let thread = pathThreads[path], !rebuilding.contains(thread), !needsCoverage.contains(thread) {
            if selectedThreads.contains(thread), !coverage.contains(thread) {
                needsCoverage.insert(thread)
            } else { publishStats(thread: thread) }
        }
    }

    private func accept(_ sample: CodexMeterSample) {
        guard sampleDeduper.claim(sample.responseId, for: 0) == .new else { return }
        let key = SessionKey(host: sample.hostId, thread: sample.threadId)
        if let previous = latest[key] {
            guard sample.responseId != previous.responseId, sample.endMs > previous.endMs else { return }
        }
        latest[key] = sample
        persist()
        onEvent(.sample(sample))
    }

    private func publishStats(thread: String) {
        guard !rebuilding.contains(thread), !needsCoverage.contains(thread) else { return }
        let paths = threadPaths[thread] ?? []
        let key = SessionKey(host: hostId, thread: thread)
        let previous = totals[key]
        let complete = coverage.contains(thread) && !paths.isEmpty
            && paths.allSatisfy { files[$0]?.contributions.readComplete == true }
        var responses: [String: CodexSessionContributions.Completion] = [:]
        for path in paths.sorted() {
            for (id, value) in files[path]?.contributions.responses ?? [:] {
                if responses[id].map({ $0.endMs > value.endMs }) ?? false { continue }
                responses[id] = value
            }
        }
        guard !responses.isEmpty || complete || previous != nil else { return }
        let turns = responses.values.compactMap(\.turnId)
        var value = CodexSessionStats(hostId: hostId, threadId: thread,
                                     turns: turns.count == responses.count ? Set(turns).count : nil,
                                     steps: responses.count, inputTokens: 0, cachedInputTokens: 0, outputTokens: 0,
                                     updatedAtMs: previous?.updatedAtMs ?? 0, complete: complete)
        for response in responses.values {
            value.inputTokens = value.inputTokens.saturatingAdd(response.usage.input)
            value.cachedInputTokens = value.cachedInputTokens.saturatingAdd(response.usage.cachedInput)
            value.outputTokens = value.outputTokens.saturatingAdd(response.usage.output)
        }
        // A failed or unfinished rebuild must not publish a lower partial sum over a saved full snapshot.
        if let previous, !complete, baselines.contains(thread) || previous.complete {
            value = previous
            value.complete = false
            baselines.insert(thread)
        }
        guard value != previous else { return }
        value.updatedAtMs = max(Clock.milliseconds(Date()), (previous?.updatedAtMs ?? 0) + 1)
        totals[key] = value
        persist()
        onEvent(.stats(value))
    }

    private var sortedStats: [CodexSessionStats] {
        totals.values.sorted { ($0.hostId, $0.threadId) < ($1.hostId, $1.threadId) }
    }

    private func persist() {
        do {
            let encoder = JSONEncoder()
            encoder.outputFormatting = [.sortedKeys]
            let data = try encoder.encode(Cache(samples: sortedSamples, stats: sortedStats))
            try FileManager.default.createDirectory(at: cacheFile.deletingLastPathComponent(), withIntermediateDirectories: true)
            try data.write(to: cacheFile, options: [.atomic])
            try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: cacheFile.path)
        } catch { onDiagnostic("cache write failed") }
    }

    private var sortedSamples: [CodexMeterSample] {
        latest.values.sorted { ($0.hostId, $0.threadId) < ($1.hostId, $1.threadId) }
    }

    private func ordered(_ paths: Set<String>) -> [String] {
        paths.sorted {
            guard let lhs = stats[$0], let rhs = stats[$1] else { return $0 < $1 }
            return TranscriptDiscovery.startupOrder(DiscoveredFile(path: $0, stat: lhs), DiscoveredFile(path: $1, stat: rhs))
        }
    }

    private func associate(path: String, thread: String) {
        if pathThreads[path] == thread { return }
        removeThreadPath(path)
        pathThreads[path] = thread
        threadPaths[thread, default: []].insert(path)
    }

    private func removeThreadPath(_ path: String) {
        guard let thread = pathThreads.removeValue(forKey: path) else { return }
        threadPaths[thread]?.remove(path)
        if threadPaths[thread]?.isEmpty == true { threadPaths[thread] = nil }
    }

    private func unavailable(host: String, thread: String, reason: String) {
        onEvent(.unavailable(CodexMeterUnavailable(hostId: host, threadId: thread, reason: reason)))
    }

    private static func threadKey(_ id: String) -> String {
        UUID(uuidString: id).map { $0.uuidString.lowercased() } ?? id
    }

    /// Codex writes session_meta first. Read only that bounded line; decode it with the original parser.
    private func headerThread(_ path: String) -> String? {
        if TranscriptReader.isCompressedStream(path) {
            var reader = CompressedStreamReader(path: path, maxLineBytes: 64 << 10)
            var parser = CodexParser(agentId: path, fileID: 0)
            var headerDeduper = Deduper(capacity: 1)
            _ = reader.read(firstLineOnly: true, onReset: {}, onMalformed: {}) { bytes in
                parser.consume(CodexLineExtractor.extract(bytes), deduper: &headerDeduper, emit: { _ in })
            }
            return parser.state.thread?.id.map(Self.threadKey)
        }
        let descriptor = Darwin.open(path, O_RDONLY | O_NOFOLLOW | O_CLOEXEC | O_NONBLOCK)
        guard descriptor >= 0 else { return nil }
        defer { Darwin.close(descriptor) }
        var buffer = [UInt8](repeating: 0, count: 64 << 10)
        let count = Darwin.read(descriptor, &buffer, buffer.count)
        guard count > 0, let newline = buffer[..<count].firstIndex(of: 0x0A) else { return nil }
        var parser = CodexParser(agentId: path, fileID: 0)
        var headerDeduper = Deduper(capacity: 1)
        buffer.withUnsafeBytes {
            parser.consume(CodexLineExtractor.extract(UnsafeRawBufferPointer(rebasing: $0[..<newline])),
                           deduper: &headerDeduper, emit: { _ in })
        }
        return parser.state.thread?.id.map(Self.threadKey)
    }
}
