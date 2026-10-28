import AgentSpeedCore
import Foundation
import os

/// One real price-table round and one exchange-rate round (NETWORK 3, 4), for diagnostics: each request's host,
/// status, size and time, then what the round concluded. Files go to `directory`, never to the app's own store.
enum NetworkSync {
    static let userAgent = "AgentSpeed/0.0.0 (macOS)"

    static func prices(directory: URL) async -> String {
        let transport = RecordingTransport()
        let store = PriceTableStore(appDataDirectory: directory)
        let sync = PriceTableSync(store: store, transport: transport, userAgent: userAgent)
        let started = Date()
        let result = await sync.run(currentVersion: PriceBook.bundled.version)
        var lines = ["price table (bundled v\(PriceBook.bundled.version)):"] + transport.lines
        lines.append("  outcome: \(result.outcome) in \(milliseconds(since: started)) ms")
        if let version = result.state.version {
            lines.append("  downloaded table: v\(version), updated \(result.state.updated ?? "?"), from \(result.state.url ?? "?")")
        }
        if let table = result.table ?? store.loadTable() {
            lines.append("  stored copy: v\(table.version), \(table.models.count) models, \(table.data.count) bytes")
        }
        return lines.joined(separator: "\n")
    }

    static func rates(directory: URL, currency: String) async -> String {
        let transport = RecordingTransport()
        let sync = ExchangeRateSync(store: ExchangeRateStore(appDataDirectory: directory), transport: transport, userAgent: userAgent)
        let started = Date()
        let outcome = await sync.run(selected: currency)
        var lines = ["exchange rates (selected \(currency)):"] + transport.lines
        switch outcome {
        case .updated(let set), .unchanged(let set):
            let date = Date(timeIntervalSince1970: TimeInterval(set.dateMs) / 1000).formatted(.iso8601)
            let rate = set.rates[currency].map { "\($0)" } ?? "missing"
            lines.append("  accepted from \(set.provider): \(set.rates.count) currencies, dated \(date), 1 USD = \(rate) \(currency)"
                + (set.needsAttribution ? " (attribution required)" : ""))
        case .failed(let reason):
            lines.append("  failed: \(reason)")
        }
        lines.append("  round took \(milliseconds(since: started)) ms")
        return lines.joined(separator: "\n")
    }

    private static func milliseconds(since date: Date) -> Int {
        Int((Date().timeIntervalSince(date) * 1000).rounded())
    }
}

/// `URLSessionTransport` with a line per request.
private final class RecordingTransport: HTTPTransport, Sendable {
    private let base = URLSessionTransport()
    private let recorded = OSAllocatedUnfairLock<[String]>(initialState: [])

    var lines: [String] { recorded.withLock { $0 } }

    func get(_ request: HTTPFetchRequest) async throws -> HTTPFetchResponse {
        let started = Date()
        let host = request.url.host() ?? "?"
        let conditional = request.ifNoneMatch == nil ? "" : " (If-None-Match)"
        do {
            let response = try await base.get(request)
            let ms = Int((Date().timeIntervalSince(started) * 1000).rounded())
            recorded.withLock {
                $0.append("  GET \(request.url.absoluteString)\(conditional) → \(response.status), \(response.body.count) bytes, \(ms) ms"
                    + (response.etag.map { ", ETag \($0)" } ?? "") + " [\(host)]")
            }
            return response
        } catch {
            let ms = Int((Date().timeIntervalSince(started) * 1000).rounded())
            let reason = (error as? HTTPFetchError)?.reason ?? "\(error)"
            recorded.withLock { $0.append("  GET \(request.url.absoluteString)\(conditional) → failed: \(reason), \(ms) ms") }
            throw error
        }
    }
}
