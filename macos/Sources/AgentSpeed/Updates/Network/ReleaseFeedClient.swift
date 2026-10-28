import AgentSpeedCore
import Foundation

/// Fetches the latest published release (UPDATES section 1).
struct ReleaseFeedClient: Sendable {
    let configuration: UpdateConfiguration

    /// Throws `ReleaseFeedError.notPublished` / `.unsupportedTag` for releases that are never offered, and
    /// `UpdateFailure` for everything else.
    @concurrent
    func fetchLatest() async throws -> Release {
        let http = UpdateHTTP(configuration: configuration)
        let data: Data
        do {
            data = try await http.data(for: http.feedRequest(), limit: Int64(ReleaseFeed.maxBytes))
        } catch let error as HTTPTransferError {
            switch error {
            case .status(403), .status(429): throw UpdateFailure.rateLimited
            case .tooLarge: throw UpdateFailure.unreadableFeed
            case .status, .disallowedURL, .sizeMismatch, .write: throw UpdateFailure.network
            }
        } catch is CancellationError {
            throw CancellationError()
        } catch {
            throw UpdateFailure.network
        }
        do {
            return try ReleaseFeed.decode(data)
        } catch let error as ReleaseFeedError where error != .malformed {
            throw error
        } catch {
            throw UpdateFailure.unreadableFeed
        }
    }
}
