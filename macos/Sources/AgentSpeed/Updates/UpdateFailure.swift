import AgentSpeedCore
import Foundation

/// Why a check or an install failed; each case maps to one user-facing reason.
enum UpdateFailure: Error, Equatable, Sendable {
    case network
    /// HTTP 403 or 429 from GitHub.
    case rateLimited
    /// The feed answered with something that is not a release.
    case unreadableFeed
    /// Size or SHA-256 mismatch.
    case checksum
    /// The app's folder is not writable.
    case permission
    /// The archive or the app inside it failed validation.
    case invalidPackage
    /// Moving the new app into place failed; the current app is unchanged.
    case installFailed
    /// Not running from an app bundle (e.g. `swift run`).
    case notBundled

    var reasonKey: String {
        switch self {
        case .network: "update.failed.network"
        case .rateLimited: "mac.update.failed.rateLimited"
        case .unreadableFeed: "mac.update.failed.feed"
        case .checksum: "update.failed.checksum"
        case .permission: "update.failed.permission"
        case .invalidPackage: "update.failed.invalid"
        case .installFailed: "mac.update.failed.install"
        case .notBundled: "mac.update.failed.unbundled"
        }
    }

    /// Short, non-personal label for the log.
    var logLabel: String {
        switch self {
        case .network: "network"
        case .rateLimited: "rate-limited"
        case .unreadableFeed: "unreadable-feed"
        case .checksum: "checksum"
        case .permission: "permission"
        case .invalidPackage: "invalid-package"
        case .installFailed: "install-failed"
        case .notBundled: "not-bundled"
        }
    }

    init(_ error: any Error) {
        switch error {
        case let failure as UpdateFailure:
            self = failure
        case is ReleaseFeedError, is DecodingError:
            self = .unreadableFeed
        case is ZipDirectoryError, is ArchiveLayoutError:
            self = .invalidPackage
        case is URLError:
            self = .network
        default:
            self = .installFailed
        }
    }
}

extension Localizer {
    /// "Update failed: <reason>".
    func failureText(_ failure: UpdateFailure) -> String {
        self("update.failed", self(failure.reasonKey))
    }
}
