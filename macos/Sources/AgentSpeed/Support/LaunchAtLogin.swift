import Observation
import ServiceManagement

/// Wraps `SMAppService.mainApp` and always reflects the real registration status.
@MainActor
@Observable
final class LaunchAtLogin {
    enum Status: Equatable {
        case enabled
        case disabled
        case requiresApproval
        /// The app is not running from a bundle the system can register (e.g. a debug build).
        case unavailable
    }

    private(set) var status: Status = .disabled
    private(set) var lastErrorFailed = false

    init() {
        refresh()
    }

    var isEnabled: Bool { status == .enabled || status == .requiresApproval }

    func refresh() {
        let next: Status
        if Bundle.main.bundleURL.pathExtension != "app" {
            next = .unavailable
        } else {
            switch SMAppService.mainApp.status {
            case .enabled: next = .enabled
            case .requiresApproval: next = .requiresApproval
            case .notFound: next = .disabled
            case .notRegistered: next = .disabled
            @unknown default: next = .disabled
            }
        }
        if next != status { status = next }
    }

    func setEnabled(_ enabled: Bool) {
        guard status != .unavailable else { return }
        do {
            if enabled {
                try SMAppService.mainApp.register()
            } else {
                try SMAppService.mainApp.unregister()
            }
            lastErrorFailed = false
        } catch {
            lastErrorFailed = true
        }
        refresh()
    }

    func openSystemSettings() {
        SMAppService.openSystemSettingsLoginItems()
    }
}
