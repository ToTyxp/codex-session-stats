import Darwin
import Foundation

/// Swaps the running bundle for a validated new one on the same volume and relaunches it (UPDATES section 5, macOS).
struct BundleReplacer: Sendable {
    static let stagingName = ".Agent Speed.update"
    private static let quarantine = "com.apple.quarantine"

    /// The running app bundle.
    let target: URL

    /// Fails with `.permission` unless the bundle and its folder can be replaced by this user.
    func checkWritable() throws {
        let manager = FileManager.default
        let parent = target.deletingLastPathComponent()
        guard manager.isWritableFile(atPath: parent.path), manager.isWritableFile(atPath: target.path) else {
            throw UpdateFailure.permission
        }
    }

    /// Stages `newApp` in `<parent>/.Agent Speed.update/`, replaces the target with it in one step, then strips
    /// quarantine. The staging folder is removed whatever happens.
    func replace(with newApp: URL) throws {
        let manager = FileManager.default
        let staging = target.deletingLastPathComponent().appendingPathComponent(Self.stagingName, isDirectory: true)
        try? manager.removeItem(at: staging)
        do {
            try manager.createDirectory(at: staging, withIntermediateDirectories: false, attributes: [.posixPermissions: 0o700])
        } catch {
            throw UpdateFailure.permission
        }
        defer { try? manager.removeItem(at: staging) }

        let staged = staging.appendingPathComponent(target.lastPathComponent, isDirectory: true)
        do {
            try manager.moveItem(at: newApp, to: staged)
            _ = try manager.replaceItemAt(target, withItemAt: staged, backupItemName: nil, options: [.usingNewMetadataOnly])
        } catch {
            throw UpdateFailure.installFailed
        }
        Self.removeQuarantine(under: target)
    }

    /// Starts `/bin/sh` detached: it waits (up to 30 s) for `pid` to exit, then opens a new instance of the target.
    /// The PID and path are positional parameters, never part of the script text.
    func spawnRelaunchHelper(waitingFor pid: Int32) throws {
        let script = #"i=0; while /bin/kill -0 "$1" 2>/dev/null; do i=$((i+1)); [ "$i" -ge 300 ] && exit 1; /bin/sleep 0.1; done; exec /usr/bin/open -n "$2""#
        try ProcessRunner.spawnDetached("/bin/sh", ["-c", script, "agent-speed-relaunch", String(pid), target.path])
    }

    private static func removeQuarantine(under root: URL) {
        removexattr(root.path, quarantine, XATTR_NOFOLLOW)
        guard let walker = FileManager.default.enumerator(at: root, includingPropertiesForKeys: nil) else { return }
        for case let item as URL in walker {
            removexattr(item.path, quarantine, XATTR_NOFOLLOW)
        }
    }
}
