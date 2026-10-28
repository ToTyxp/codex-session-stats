import Foundation

/// A completed Codex response, using Agent Speed 1.4.1's measured response interval.
/// `outputTokens` already includes `reasoningTokens`; they must never be added together.
public struct CodexMeterSample: Codable, Equatable, Sendable {
    public let hostId: String
    public let threadId: String
    public let responseId: String
    public let model: String?
    public let outputTokens: Int
    public let reasoningTokens: Int
    /// Effective start from TimedResponse, including its 500 ms minimum interval.
    public let startMs: Int64
    public let endMs: Int64
    public let durationMs: Int64
    public let rate: Double

    public var type: String { "sample" }
    public var metric: String { "response-wall" }
    public var source: String { "agent-speed/1.4.1" }

    init?(hostId: String, threadId: String, upsert: ResponseUpsert) {
        let record = upsert.record
        guard upsert.isCompletion, record.isFinal, record.source == .codex,
              record.usage.output > 0, record.usage.reasoning <= record.usage.output,
              let timed = TimedResponse(record),
              let speed = SpeedEngine.summary(of: record, project: "").speed else { return nil }
        self.hostId = hostId
        self.threadId = threadId
        responseId = record.key
        model = record.model
        outputTokens = record.usage.output
        reasoningTokens = record.usage.reasoning
        startMs = timed.startMs
        endMs = timed.endMs
        durationMs = timed.durationMs
        rate = speed
    }

    /// Cached metadata is validated through the same upstream interval and summary code before reuse.
    var isValidCacheEntry: Bool {
        guard !hostId.isEmpty, !threadId.isEmpty, !responseId.isEmpty,
              startMs >= 0, endMs >= startMs,
              outputTokens > 0, outputTokens <= TokenCount.ceiling,
              reasoningTokens >= 0, reasoningTokens <= outputTokens,
              rate.isFinite else { return false }
        let record = ResponseRecord(
            key: responseId, source: .codex, agentId: "", model: model,
            startMs: startMs, endMs: endMs,
            usage: TokenUsage(output: outputTokens, reasoning: reasoningTokens)
        )
        guard let timed = TimedResponse(record), timed.startMs == startMs, timed.durationMs == durationMs,
              let expected = SpeedEngine.summary(of: record, project: "").speed else { return false }
        return rate == expected
    }

    enum CodingKeys: String, CodingKey {
        case hostId, threadId, responseId, model, outputTokens, reasoningTokens
        case startMs, endMs, durationMs, rate
        case type, metric, source
    }

    public init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        hostId = try values.decode(String.self, forKey: .hostId)
        threadId = try values.decode(String.self, forKey: .threadId)
        responseId = try values.decode(String.self, forKey: .responseId)
        model = try values.decodeIfPresent(String.self, forKey: .model)
        outputTokens = try values.decode(Int.self, forKey: .outputTokens)
        reasoningTokens = try values.decode(Int.self, forKey: .reasoningTokens)
        startMs = try values.decode(Int64.self, forKey: .startMs)
        endMs = try values.decode(Int64.self, forKey: .endMs)
        durationMs = try values.decode(Int64.self, forKey: .durationMs)
        rate = try values.decode(Double.self, forKey: .rate)
    }

    public func encode(to encoder: Encoder) throws {
        var values = encoder.container(keyedBy: CodingKeys.self)
        try values.encode(type, forKey: .type)
        try values.encode(metric, forKey: .metric)
        try values.encode(source, forKey: .source)
        try values.encode(hostId, forKey: .hostId)
        try values.encode(threadId, forKey: .threadId)
        try values.encode(responseId, forKey: .responseId)
        try values.encode(model, forKey: .model)
        try values.encode(outputTokens, forKey: .outputTokens)
        try values.encode(reasoningTokens, forKey: .reasoningTokens)
        try values.encode(startMs, forKey: .startMs)
        try values.encode(endMs, forKey: .endMs)
        try values.encode(durationMs, forKey: .durationMs)
        try values.encode(rate, forKey: .rate)
    }
}

public struct CodexMeterUnavailable: Encodable, Equatable, Sendable {
    public let type = "unavailable"
    public let hostId: String
    public let threadId: String
    public let reason: String
}

public enum CodexMeterEvent: Encodable, Equatable, Sendable {
    case sample(CodexMeterSample)
    case stats(CodexSessionStats)
    case unavailable(CodexMeterUnavailable)

    public func encode(to encoder: Encoder) throws {
        switch self {
        case .sample(let sample): try sample.encode(to: encoder)
        case .stats(let value): try value.encode(to: encoder)
        case .unavailable(let value): try value.encode(to: encoder)
        }
    }
}
