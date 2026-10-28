import Foundation

/// Update bookkeeping persisted across launches (not user settings).
@MainActor
final class UpdateStore {
    private let defaults: UserDefaults

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
    }

    /// Time of the last successful check.
    var lastCheck: Date? {
        get { defaults.object(forKey: Key.lastCheck.rawValue) as? Date }
        set { set(newValue, .lastCheck) }
    }

    /// Tag the user chose to skip; automatic checks never offer it again.
    var skippedTag: String? {
        get { defaults.string(forKey: Key.skippedTag.rawValue) }
        set { set(newValue, .skippedTag) }
    }

    /// Tag that already got its one system notification.
    var notifiedTag: String? {
        get { defaults.string(forKey: Key.notifiedTag.rawValue) }
        set { set(newValue, .notifiedTag) }
    }

    /// Version that was just installed; the relaunched app shows `update.done` once when it matches.
    var installedVersion: String? {
        get { defaults.string(forKey: Key.installedVersion.rawValue) }
        set { set(newValue, .installedVersion) }
    }

    /// Version that ran the install, for the log of the relaunched instance.
    var previousVersion: String? {
        get { defaults.string(forKey: Key.previousVersion.rawValue) }
        set { set(newValue, .previousVersion) }
    }

    private func set(_ value: Any?, _ key: Key) {
        if let value { defaults.set(value, forKey: key.rawValue) } else { defaults.removeObject(forKey: key.rawValue) }
    }

    private enum Key: String {
        case lastCheck = "update.lastCheck"
        case skippedTag = "update.skippedTag"
        case notifiedTag = "update.notifiedTag"
        case installedVersion = "update.installedVersion"
        case previousVersion = "update.previousVersion"
    }
}
