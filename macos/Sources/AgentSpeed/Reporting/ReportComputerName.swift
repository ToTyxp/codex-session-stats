import Foundation
import SystemConfiguration

/// The computer's name from System Settings → General → Sharing (DESIGN 11, Default username). The username field,
/// the invitation and the dashboard indicator read it while they render, so it is asked for at most once a minute.
@MainActor
enum ReportComputerName {
    private static var cached: (name: String?, at: Date)?

    static var current: String? {
        if let cached, Date().timeIntervalSince(cached.at) < 60 { return cached.name }
        let name = read()
        cached = (name, Date())
        return name
    }

    private static func read() -> String? {
        #if DEBUG
        if let name = ProcessInfo.processInfo.environment["AGENT_SPEED_COMPUTER_NAME"] { return name }
        #endif
        guard let name = SCDynamicStoreCopyComputerName(nil, nil) as String?, !name.isEmpty else { return nil }
        return name
    }
}
