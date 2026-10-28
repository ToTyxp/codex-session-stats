import AgentSpeedCore
import CryptoKit
import Foundation
import Security

/// Device keys and API keys of live reporting (DESIGN 11, Storage). With a Secure Enclave the signing key is created
/// inside it and only its encrypted handle is stored (`<deviceId>.sekey`, mode 0600); the API key is sealed with
/// AES-GCM under a key derived from a Secure Enclave key agreement, so no Keychain item (and no password prompt after
/// an update of the ad-hoc signed app) is involved. Without a Secure Enclave both live in Keychain items, "this device
/// only". Nothing here is ever logged.
struct ReportKeyStore: Sendable {
    enum Slot: String, Sendable {
        case current, pending
    }

    enum Failure: Error, Equatable {
        case unavailable
        case unreadable
    }

    static let keychainService = "io.github.zoefix.agentspeed.report"
    static let sealSalt = Data("AgentSpeed report".utf8)
    static let sealInfo = Data("api-key".utf8)

    let directory: URL
    let usesSecureEnclave: Bool

    init(directory: URL, usesSecureEnclave: Bool = SecureEnclave.isAvailable) {
        self.directory = directory
        self.usesSecureEnclave = usesSecureEnclave
    }

    /// `<app data>/report`.
    static func standard(appData: URL) -> ReportKeyStore {
        ReportKeyStore(directory: appData.appendingPathComponent("report", isDirectory: true))
    }

    // MARK: - Device keys

    /// Creates a key in `slot`, replacing what was there.
    func createKey(deviceId: String, slot: Slot = .current) throws -> any ReportSigner {
        guard Self.isSafe(deviceId) else { throw Failure.unavailable }
        if usesSecureEnclave {
            let key = try SecureEnclave.P256.Signing.PrivateKey(accessControl: try Self.accessControl())
            try write(key.dataRepresentation, to: file(deviceId, slot == .current ? "sekey" : "pending.sekey"))
            return try SecureEnclaveReportSigner(key: key)
        }
        let key = P256.Signing.PrivateKey()
        try Keychain.set(key.rawRepresentation, account: account(deviceId, slot == .current ? "key" : "pending.key"))
        return SoftwareReportSigner(key: key)
    }

    func loadKey(deviceId: String, slot: Slot = .current) -> (any ReportSigner)? {
        guard Self.isSafe(deviceId) else { return nil }
        if usesSecureEnclave {
            guard let handle = try? Data(contentsOf: file(deviceId, slot == .current ? "sekey" : "pending.sekey")),
                  let key = try? SecureEnclave.P256.Signing.PrivateKey(dataRepresentation: handle) else { return nil }
            return try? SecureEnclaveReportSigner(key: key)
        }
        guard let raw = Keychain.get(account: account(deviceId, slot == .current ? "key" : "pending.key")),
              let key = try? P256.Signing.PrivateKey(rawRepresentation: raw) else { return nil }
        return SoftwareReportSigner(key: key)
    }

    /// The pending key becomes the current one (REPORTING 4.5).
    func promotePending(deviceId: String) throws {
        guard Self.isSafe(deviceId) else { return }
        if usesSecureEnclave {
            let pending = file(deviceId, "pending.sekey")
            let current = file(deviceId, "sekey")
            guard FileManager.default.fileExists(atPath: pending.path) else { return }
            guard rename(pending.path, current.path) == 0 else { throw Failure.unreadable }
            return
        }
        guard let raw = Keychain.get(account: account(deviceId, "pending.key")) else { return }
        try Keychain.set(raw, account: account(deviceId, "key"))
        Keychain.delete(account: account(deviceId, "pending.key"))
    }

    func deletePending(deviceId: String) {
        guard Self.isSafe(deviceId) else { return }
        try? FileManager.default.removeItem(at: file(deviceId, "pending.sekey"))
        if !usesSecureEnclave { Keychain.delete(account: account(deviceId, "pending.key")) }
    }

    /// *Forget*, *Reset* and a locally forgotten identity: every key and the API key.
    func deleteAll(deviceId: String) {
        guard Self.isSafe(deviceId) else { return }
        for suffix in ["sekey", "pending.sekey", "seagree", "apikey"] {
            try? FileManager.default.removeItem(at: file(deviceId, suffix))
        }
        // A Mac with a Secure Enclave never wrote Keychain items; touching them could only ask for a password.
        guard !usesSecureEnclave else { return }
        for suffix in ["key", "pending.key", "apikey"] {
            Keychain.delete(account: account(deviceId, suffix))
        }
    }

    // MARK: - API keys

    func saveAPIKey(_ apiKey: String, deviceId: String) throws {
        guard Self.isSafe(deviceId) else { throw Failure.unavailable }
        let plain = Data(apiKey.utf8)
        guard usesSecureEnclave else {
            try Keychain.set(plain, account: account(deviceId, "apikey"))
            return
        }
        let agreement = try agreementKey(deviceId: deviceId, create: true)
        let ephemeral = P256.KeyAgreement.PrivateKey()
        let key = try Self.sealingKey(try ephemeral.sharedSecretFromKeyAgreement(with: agreement.publicKey))
        let sealed = try AES.GCM.seal(plain, using: key)
        guard let combined = sealed.combined else { throw Failure.unreadable }
        try write(ephemeral.publicKey.x963Representation + combined, to: file(deviceId, "apikey"))
    }

    func loadAPIKey(deviceId: String) -> String? {
        guard Self.isSafe(deviceId) else { return nil }
        guard usesSecureEnclave else {
            return Keychain.get(account: account(deviceId, "apikey")).flatMap { String(data: $0, encoding: .utf8) }
        }
        guard let stored = try? Data(contentsOf: file(deviceId, "apikey")), stored.count > 65,
              let agreement = try? agreementKey(deviceId: deviceId, create: false),
              let ephemeral = try? P256.KeyAgreement.PublicKey(x963Representation: stored.prefix(65)),
              let secret = try? agreement.sharedSecretFromKeyAgreement(with: ephemeral),
              let key = try? Self.sealingKey(secret),
              let box = try? AES.GCM.SealedBox(combined: stored.dropFirst(65)),
              let plain = try? AES.GCM.open(box, using: key) else { return nil }
        return String(data: plain, encoding: .utf8)
    }

    func deleteAPIKey(deviceId: String) {
        guard Self.isSafe(deviceId) else { return }
        try? FileManager.default.removeItem(at: file(deviceId, "apikey"))
        try? FileManager.default.removeItem(at: file(deviceId, "seagree"))
        if !usesSecureEnclave { Keychain.delete(account: account(deviceId, "apikey")) }
    }

    // MARK: - Helpers

    private func agreementKey(deviceId: String, create: Bool) throws -> SecureEnclave.P256.KeyAgreement.PrivateKey {
        let url = file(deviceId, "seagree")
        if let handle = try? Data(contentsOf: url),
           let key = try? SecureEnclave.P256.KeyAgreement.PrivateKey(dataRepresentation: handle) {
            return key
        }
        guard create else { throw Failure.unreadable }
        let key = try SecureEnclave.P256.KeyAgreement.PrivateKey(accessControl: try Self.accessControl())
        try write(key.dataRepresentation, to: url)
        return key
    }

    private static func sealingKey(_ secret: SharedSecret) throws -> SymmetricKey {
        secret.hkdfDerivedSymmetricKey(using: SHA256.self, salt: sealSalt, sharedInfo: sealInfo, outputByteCount: 32)
    }

    /// Usable from the first unlock on (agents keep working behind a locked screen), never leaving this Mac, and with
    /// no user-presence flag, so signing never asks.
    private static func accessControl() throws -> SecAccessControl {
        var error: Unmanaged<CFError>?
        guard let control = SecAccessControlCreateWithFlags(
            nil, kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly, .privateKeyUsage, &error) else {
            throw Failure.unavailable
        }
        return control
    }

    private func file(_ deviceId: String, _ suffix: String) -> URL {
        directory.appendingPathComponent("\(deviceId).\(suffix)")
    }

    private func account(_ deviceId: String, _ suffix: String) -> String { "\(deviceId).\(suffix)" }

    /// Device ids are our own lower-case UUIDs; anything else never becomes a path.
    private static func isSafe(_ deviceId: String) -> Bool { ReportCanonical.isDeviceId(deviceId) }

    /// Mode 0600 in a 0700 folder, written to a temporary file and renamed.
    private func write(_ data: Data, to url: URL) throws {
        let manager = FileManager.default
        try manager.createDirectory(at: directory, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
        let temporary = directory.appendingPathComponent(".\(UUID().uuidString).tmp")
        guard manager.createFile(atPath: temporary.path, contents: data, attributes: [.posixPermissions: 0o600]) else {
            throw Failure.unavailable
        }
        guard rename(temporary.path, url.path) == 0 else {
            try? manager.removeItem(at: temporary)
            throw Failure.unavailable
        }
    }
}

/// A key inside the Secure Enclave: signing happens there; only the public key is known outside.
struct SecureEnclaveReportSigner: ReportSigner {
    let key: SecureEnclave.P256.Signing.PrivateKey
    let publicKey: ReportPublicKey

    init(key: SecureEnclave.P256.Signing.PrivateKey) throws {
        guard let publicKey = ReportPublicKey(raw: key.publicKey.rawRepresentation) else { throw ReportKeyStore.Failure.unreadable }
        self.key = key
        self.publicKey = publicKey
    }

    func signature(for message: Data) throws -> Data {
        try key.signature(for: message).rawRepresentation
    }
}

/// Generic-password items of the fallback (Macs without a Secure Enclave): this device only, never synchronized.
private enum Keychain {
    static func set(_ data: Data, account: String) throws {
        delete(account: account)
        let query: [CFString: Any] = [
            kSecClass: kSecClassGenericPassword,
            kSecAttrService: ReportKeyStore.keychainService,
            kSecAttrAccount: account,
            kSecAttrAccessible: kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly,
            kSecAttrSynchronizable: false,
            kSecValueData: data,
        ]
        guard SecItemAdd(query as CFDictionary, nil) == errSecSuccess else { throw ReportKeyStore.Failure.unavailable }
    }

    static func get(account: String) -> Data? {
        let query: [CFString: Any] = [
            kSecClass: kSecClassGenericPassword,
            kSecAttrService: ReportKeyStore.keychainService,
            kSecAttrAccount: account,
            kSecReturnData: true,
            kSecMatchLimit: kSecMatchLimitOne,
        ]
        var result: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &result) == errSecSuccess else { return nil }
        return result as? Data
    }

    static func delete(account: String) {
        let query: [CFString: Any] = [
            kSecClass: kSecClassGenericPassword,
            kSecAttrService: ReportKeyStore.keychainService,
            kSecAttrAccount: account,
        ]
        SecItemDelete(query as CFDictionary)
    }
}
