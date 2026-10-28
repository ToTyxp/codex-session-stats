import AgentSpeedCore
import Foundation
import os

/// Where updates come from and what a valid update looks like (UPDATES sections 1 and 5).
struct UpdateConfiguration: Sendable {
    static let bundleIdentifier = "io.github.zoefix.agentspeed"
    static let bundleName = "Agent Speed.app"
    static let assetName = "AgentSpeed-macOS.zip"
    static let checksumAssetName = "SHA256SUMS.txt"
    static let maxAssetSize: Int64 = 300 << 20
    static let releaseFeedURL = URL(string: "https://api.github.com/repos/zoefix/agent-speed/releases/latest")!
    static let releasesPageURL = URL(string: "https://github.com/zoefix/agent-speed/releases/latest")!
    /// Development hook: a feed URL that replaces the GitHub API and lifts the host allow-list. Only the local
    /// user can set it, by launching the executable with this variable.
    static let feedOverrideVariable = "AGENT_SPEED_UPDATE_FEED"

    let feedURL: URL
    let policy: UpdateURLPolicy
    let currentVersion: String

    var userAgent: String { "AgentSpeed/\(currentVersion) (macOS)" }

    /// An unbundled build (`swift run`) has no marketing version and counts as development (`0.0.0`).
    static var bundleVersion: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0.0.0"
    }

    static func current(environment: [String: String] = ProcessInfo.processInfo.environment) -> UpdateConfiguration {
        let version = bundleVersion
        if let override = environment[feedOverrideVariable], !override.isEmpty {
            if let url = URL(string: override), UpdateURLPolicy.testing.permits(url) {
                UpdateLog.logger.notice("Update feed override active (\(url.scheme ?? "", privacy: .public)://\(url.host() ?? "", privacy: .public))")
                return UpdateConfiguration(feedURL: url, policy: .testing, currentVersion: version)
            }
            UpdateLog.logger.error("Ignoring \(feedOverrideVariable, privacy: .public): not an http(s) URL")
        }
        return UpdateConfiguration(feedURL: releaseFeedURL, policy: .standard, currentVersion: version)
    }

    /// The release page to open for "View on GitHub", never a URL outside the policy.
    func pageURL(for release: Release?) -> URL {
        if let url = release?.pageURL, policy.permits(url) { return url }
        return Self.releasesPageURL
    }
}

enum UpdateLog {
    static let logger = Logger(subsystem: UpdateConfiguration.bundleIdentifier, category: "updates")
}
