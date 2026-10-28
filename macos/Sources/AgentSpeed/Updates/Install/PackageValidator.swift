import AgentSpeedCore
import Foundation

/// Extracts the release archive and proves it holds the expected, intact app (UPDATES section 5.3, macOS).
struct PackageValidator: Sendable {
    static let maxInfoPlistSize = 1 << 20

    let expectedVersion: SemanticVersion

    /// Vets the archive's entry names, extracts it with `ditto` into a new folder of `workspace`, then validates
    /// the extracted tree, the bundle identity and version, and the code signature. Returns the app's URL.
    func extract(_ archive: URL, in workspace: UpdateWorkspace) async throws -> URL {
        let entries = try ZipDirectory.entries(of: archive)
        try ArchiveLayout.validate(entries, root: UpdateConfiguration.bundleName)

        let payload = try workspace.folder("payload")
        let status = try await ProcessRunner.run("/usr/bin/ditto", ["-x", "-k", "--noqtn", archive.path, payload.path])
        guard status == 0 else { throw UpdateFailure.invalidPackage }

        let app = payload.appendingPathComponent(UpdateConfiguration.bundleName, isDirectory: true)
        try verifyTree(payload, app: app)
        try verifyBundle(app)
        let signature = try await ProcessRunner.run("/usr/bin/codesign", ["--verify", "--deep", "--strict", app.path])
        guard signature == 0 else { throw UpdateFailure.invalidPackage }
        return app
    }

    /// The payload must be exactly one app folder holding only files, folders and symlinks that stay inside it.
    /// Symlink targets may not be absolute or use `..`, so resolving them can only descend into the bundle.
    private func verifyTree(_ payload: URL, app: URL) throws {
        let manager = FileManager.default
        let top = try manager.contentsOfDirectory(atPath: payload.path)
        guard top == [UpdateConfiguration.bundleName] else { throw UpdateFailure.invalidPackage }
        let appValues = try app.resourceValues(forKeys: [.isDirectoryKey, .isSymbolicLinkKey])
        guard appValues.isDirectory == true, appValues.isSymbolicLink != true else { throw UpdateFailure.invalidPackage }

        let keys: [URLResourceKey] = [.isRegularFileKey, .isDirectoryKey, .isSymbolicLinkKey, .fileSizeKey]
        guard let walker = manager.enumerator(at: app, includingPropertiesForKeys: keys, options: [], errorHandler: { _, _ in false }) else {
            throw UpdateFailure.invalidPackage
        }
        var total: Int64 = 0
        for case let item as URL in walker {
            let values = try item.resourceValues(forKeys: Set(keys))
            if values.isSymbolicLink == true {
                let destination = try manager.destinationOfSymbolicLink(atPath: item.path)
                let components = destination.split(separator: "/", omittingEmptySubsequences: false)
                guard !destination.isEmpty, !destination.hasPrefix("/"), !components.contains("..") else {
                    throw UpdateFailure.invalidPackage
                }
            } else if values.isRegularFile == true {
                total += Int64(values.fileSize ?? 0)
                guard total <= Int64(ArchiveLayout.maxExpandedSize) else { throw UpdateFailure.invalidPackage }
            } else if values.isDirectory != true {
                throw UpdateFailure.invalidPackage
            }
        }
    }

    private func verifyBundle(_ app: URL) throws {
        let contents = app.appendingPathComponent("Contents", isDirectory: true)
        let plistURL = contents.appendingPathComponent("Info.plist", isDirectory: false)
        let size = try plistURL.resourceValues(forKeys: [.fileSizeKey, .isRegularFileKey])
        guard size.isRegularFile == true, (size.fileSize ?? .max) <= Self.maxInfoPlistSize else {
            throw UpdateFailure.invalidPackage
        }
        let data = try Data(contentsOf: plistURL)
        guard let info = try PropertyListSerialization.propertyList(from: data, format: nil) as? [String: Any],
              info["CFBundleIdentifier"] as? String == UpdateConfiguration.bundleIdentifier,
              info["CFBundlePackageType"] as? String == "APPL",
              let shortVersion = info["CFBundleShortVersionString"] as? String,
              SemanticVersion(shortVersion) == expectedVersion,
              let executable = info["CFBundleExecutable"] as? String,
              !executable.isEmpty, !executable.contains("/"), executable != "..", executable != "." else {
            throw UpdateFailure.invalidPackage
        }
        let binary = contents.appendingPathComponent("MacOS", isDirectory: true).appendingPathComponent(executable)
        let values = try binary.resourceValues(forKeys: [.isRegularFileKey])
        guard values.isRegularFile == true, FileManager.default.isExecutableFile(atPath: binary.path) else {
            throw UpdateFailure.invalidPackage
        }
    }
}
