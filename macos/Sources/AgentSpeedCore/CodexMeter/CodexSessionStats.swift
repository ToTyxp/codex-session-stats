import Foundation

/// Completed calls belonging to one conversation. Input includes cache reads; output includes reasoning.
public struct CodexSessionStats: Codable, Equatable, Sendable {
    public let hostId: String
    public let threadId: String
    public var turns: Int?
    public var steps: Int
    public var inputTokens: Int
    public var cachedInputTokens: Int
    public var outputTokens: Int
    public var updatedAtMs: Int64
    public var complete: Bool
    public var type: String { "stats" }
    public var source: String { "agent-speed/1.4.1" }

    var isValidCacheEntry: Bool {
        !hostId.isEmpty && !threadId.isEmpty && steps >= 0 && inputTokens >= 0 && cachedInputTokens >= 0
            && cachedInputTokens <= inputTokens && outputTokens >= 0 && updatedAtMs >= 0
            && (turns.map { $0 >= 0 && $0 <= steps } ?? true)
    }

    enum CodingKeys: String, CodingKey {
        case type, hostId, threadId, turns, steps, inputTokens, cachedInputTokens, outputTokens, updatedAtMs, complete, source
    }

    public func encode(to encoder: Encoder) throws {
        var values = encoder.container(keyedBy: CodingKeys.self)
        try values.encode(type, forKey: .type)
        try values.encode(hostId, forKey: .hostId)
        try values.encode(threadId, forKey: .threadId)
        try values.encode(turns, forKey: .turns)
        try values.encode(steps, forKey: .steps)
        try values.encode(inputTokens, forKey: .inputTokens)
        try values.encode(cachedInputTokens, forKey: .cachedInputTokens)
        try values.encode(outputTokens, forKey: .outputTokens)
        try values.encode(updatedAtMs, forKey: .updatedAtMs)
        try values.encode(complete, forKey: .complete)
        try values.encode(source, forKey: .source)
    }
}

extension CodexSessionStats {
    public init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        hostId = try values.decode(String.self, forKey: .hostId)
        threadId = try values.decode(String.self, forKey: .threadId)
        turns = try values.decodeIfPresent(Int.self, forKey: .turns)
        steps = try values.decode(Int.self, forKey: .steps)
        inputTokens = try values.decode(Int.self, forKey: .inputTokens)
        cachedInputTokens = try values.decode(Int.self, forKey: .cachedInputTokens)
        outputTokens = try values.decode(Int.self, forKey: .outputTokens)
        updatedAtMs = try values.decode(Int64.self, forKey: .updatedAtMs)
        complete = try values.decode(Bool.self, forKey: .complete)
    }
}

/// Unbounded, per-file response contributions. Reset/replay replaces this value, never adds another total.
/// This consumes the same extracted lines as the speed parser, before speed interval/output validation.
struct CodexSessionContributions {
    struct Completion: Equatable {
        var turnId: String?
        var usage: TokenUsage
        var endMs: Int64
    }
    var responses: [String: Completion] = [:]
    var malformed = false
    var readComplete = false
    private var turnId: String?
    private var lastLegacyKey: String?
    private var sawModern = false
    private var previousLegacyTotals: CodexRawUsage?

    mutating func consume(_ extraction: LineExtraction<CodexLine>, state: AgentState) {
        guard case .line(let line) = extraction else {
            if case .malformed = extraction { malformed = true }
            return
        }
        guard let thread = state.thread, let own = thread.id else { return }
        // Copied parent history predates this session; explicit foreign thread IDs are excluded below too.
        if let timestamp = line.timestampMs, timestamp < thread.createdMs { return }
        if line.startsTurn { turnId = line.turnId }
        else if case .turnContext = line.kind, let id = line.turnId { turnId = id }
        switch line.kind {
        case .turnEnded:
            turnId = nil
            lastLegacyKey = nil
        case .input, .output:
            lastLegacyKey = nil
        case .modernUsage(let responseId, let threadId, let usage):
            guard threadId.map({ $0.lowercased() == own.lowercased() }) ?? true else { return }
            sawModern = true
            guard let responseId, line.hasUsage, usage.validForStats, usage.hasTokenCounts else { malformed = true; return }
            let tokens = usage.normalized
            guard tokens.cachedInput <= tokens.input, tokens.reasoning <= tokens.output else { malformed = true; return }
            // Some versions dual-write legacy first. An adjacent matching completion has the same request boundary.
            if let key = lastLegacyKey, responses[key]?.usage == tokens { responses[key] = nil }
            lastLegacyKey = nil
            let previous = responses[responseId]
            // Replayed IDs keep their original attribution, including an unknown turn.
            // The turn active at replay time belongs to a different request.
            let responseTurn: String?
            if let explicitTurn = line.turnId { responseTurn = explicitTurn }
            else if let previous { responseTurn = previous.turnId }
            else { responseTurn = turnId }
            responses[responseId] = Completion(turnId: responseTurn,
                                               usage: tokens, endMs: line.timestampMs ?? 0)
        case .legacyUsage(let threadId, let info):
            guard !sawModern, threadId.map({ $0.lowercased() == own.lowercased() }) ?? true else { return }
            // Keep own-thread totals here: the upstream speed parser also sees foreign usage records,
            // whose modern/legacy state must not suppress or change this conversation's accounting.
            guard let info else { return }
            guard let total = info.total, total.validForStats, total.hasTokenCounts,
                  info.last.map({ $0.validForStats && $0.hasTokenCounts }) ?? true else { malformed = true; return }
            let raw: CodexRawUsage
            if let previousLegacyTotals {
                let (delta, negative) = total.delta(since: previousLegacyTotals)
                if delta.total == 0 { return }
                if negative {
                    guard let last = info.last else { malformed = true; return }
                    raw = last
                } else { raw = delta }
            } else { raw = info.last ?? total }
            previousLegacyTotals = total
            let tokens = raw.normalized
            guard tokens.cachedInput <= tokens.input, tokens.reasoning <= tokens.output else { malformed = true; return }
            let key = "codex-legacy:\(own.lowercased()):\(total.total)"
            responses[key] = Completion(turnId: nil, usage: tokens, endMs: line.timestampMs ?? 0)
            lastLegacyKey = key
        default:
            break
        }
    }
}
