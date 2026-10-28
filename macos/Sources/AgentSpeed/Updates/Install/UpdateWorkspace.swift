import Foundation

/// A fresh folder only this user can read (0700) inside the per-user temporary directory, for one install attempt.
struct UpdateWorkspace: Sendable {
    let url: URL

    static func create() throws -> UpdateWorkspace {
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent(prefix + UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(
            at: url,
            withIntermediateDirectories: false,
            attributes: [.posixPermissions: 0o700]
        )
        return UpdateWorkspace(url: url)
    }

    func file(_ name: String) -> URL {
        url.appendingPathComponent(name, isDirectory: false)
    }

    func folder(_ name: String) throws -> URL {
        let folder = url.appendingPathComponent(name, isDirectory: true)
        try FileManager.default.createDirectory(
            at: folder,
            withIntermediateDirectories: false,
            attributes: [.posixPermissions: 0o700]
        )
        return folder
    }

    func remove() {
        try? FileManager.default.removeItem(at: url)
    }

    /// Removes workspaces left behind by an install that was interrupted (e.g. the app quit mid-download).
    /// Recent ones are kept, since another running copy may still be using them.
    static func removeStale(olderThan age: TimeInterval = 60 * 60) {
        DispatchQueue.global(qos: .utility).async {
            let manager = FileManager.default
            let base = manager.temporaryDirectory
            guard let names = try? manager.contentsOfDirectory(atPath: base.path) else { return }
            let cutoff = Date().addingTimeInterval(-age)
            for name in names where name.hasPrefix(prefix) {
                let url = base.appendingPathComponent(name, isDirectory: true)
                let values = try? url.resourceValues(forKeys: [.contentModificationDateKey, .isSymbolicLinkKey])
                guard values?.isSymbolicLink != true, let modified = values?.contentModificationDate, modified < cutoff else { continue }
                try? manager.removeItem(at: url)
            }
        }
    }

    private static let prefix = "AgentSpeed-Update-"
}
