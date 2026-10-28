#if DEBUG
import AgentSpeedCore
import CoreGraphics
import Foundation
import ImageIO
import UniformTypeIdentifiers

@MainActor
enum ReportProfileHarness {
    private static var failures = 0
    private static var diagnostics: (() -> String?)?

    static func run(first: AppModel, url: String) async -> Bool {
        failures = 0
        await clockSkewPolling()
        diagnostics = { [weak first] in
            first?.report.profile.lastFailure.map { String(describing: $0) }
        }
        let suite = "AgentSpeedProfilePeerE2E"
        UserDefaults.standard.removePersistentDomain(forName: suite)
        let defaults = UserDefaults(suiteName: suite)!
        let peer = AppModel(settings: AppSettings(defaults: defaults),
                            updates: UpdateController(configuration: .current(environment: [:]), store: UpdateStore(defaults: defaults)))
        peer.activate(live: false)
        peer.report.activate()
        defer {
            first.report.profile.closePairing()
            peer.shutdown()
            UserDefaults.standard.removePersistentDomain(forName: suite)
            diagnostics = nil
        }
        let a = first.report, b = peer.report
        guard a.profile.capabilities.profileAvatar && a.profile.capabilities.sharedIdentity else {
            check("server advertises profile extensions", false); return false
        }
        b.selectPreset(.custom); b.commitCustomURL(url)
        guard await wait("peer discovery", { if case .ready = b.serviceState { true } else { false } }) else { return false }
        b.updatePreferences { $0.username = ReportUsername.random() }
        b.setEnabled(true)
        guard await wait("peer consent", { b.consent != nil }) else { return false }
        b.startReporting()
        guard await wait("independent peer registration", { b.isOn }) else { return false }
        let firstId = a.identity?.deviceId, peerId = b.identity?.deviceId
        let firstKey = a.fingerprint, peerKey = b.fingerprint
        check("separate device identities and signing keys", firstId != peerId && firstKey != peerKey && firstKey != nil && peerKey != nil)
        a.profile.refresh(); b.profile.refresh()
        guard await wait("both signed profiles load", { a.profile.snapshot != nil && b.profile.snapshot != nil && !a.profile.busy && !b.profile.busy }) else { return false }
        check("peer begins unbound", b.profile.snapshot?.sharedUserId == nil)
        check("inline name routes unbound device correctly", ReportProfileNameTarget.resolve(a) == .device)
        let deviceName = ReportUsername.random()
        ReportProfileNameTarget.resolve(a).commit(deviceName, report: a)
        guard await wait("inline device name uses original rename API", { a.identity?.username == deviceName && a.renameState == .updated }) else { return false }
        a.setSection(.models, on: false)
        check("model sharing off does not upload profile data", !a.effectiveSections.contains(.models) && a.profile.snapshot?.avatar == nil)
        do { a.profile.preview = try avatar() } catch { check("avatar encoding", false); return false }
        a.profile.upload()
        guard await wait("PNG upload accepted", { a.profile.snapshot?.avatar != nil && !a.profile.busy }) else { return false }
        let avatarHash = a.profile.snapshot?.avatar?.sha256
        check("server PNG downloaded and hash verified", a.profile.avatar?.sha256 == avatarHash)
        a.profile.createIdentity()
        guard await wait("shared identity created", { a.profile.snapshot?.sharedUserId != nil && !a.profile.busy }) else { return false }
        let shared = a.profile.snapshot?.sharedUserId
        a.profile.invite()
        guard await wait("initial invitation created", { a.profile.pairing?.code != nil && !a.profile.busy }) else { return false }
        first.settings.networkEnabled = false
        guard await wait("network off stops pairing and clears code", { a.profile.pairing == nil && !a.profile.canRequest }) else { return false }
        first.settings.networkEnabled = true
        guard await wait("network restored", { a.profile.canRequest }) else { return false }
        a.profile.invite()
        guard await wait("single use invitation created", { a.profile.pairing?.code != nil && !a.profile.busy }), let code = a.profile.pairing?.code else { return false }
        b.profile.join(code)
        guard await wait("peer claim waits for approval", { b.profile.pairing?.state == .pending && !b.profile.busy }) else { return false }
        check("claim does not immediately bind", b.profile.snapshot?.sharedUserId == nil)
        a.profile.checkPairing()
        guard await wait("inviter sees requesting device", { a.profile.pairing?.claimant?.deviceId == peerId && !a.profile.busy }) else { return false }
        a.profile.approve()
        guard await wait("explicit inviter approval binds peer", within: 15, { b.profile.snapshot?.sharedUserId == shared && !b.profile.busy }) else { return false }
        check("shared avatar uses authenticated download", b.profile.avatar?.sha256 == avatarHash)
        check("binding preserves both local keys and IDs", a.identity?.deviceId == firstId && b.identity?.deviceId == peerId && a.fingerprint == firstKey && b.fingerprint == peerKey)
        let registeredName = a.identity?.username
        check("inline name routes bound identity correctly", ReportProfileNameTarget.resolve(a) == .shared)
        ReportProfileNameTarget.resolve(a).commit("Profile Group", report: a)
        guard await wait("shared display name updated", { a.profile.snapshot?.displayName == "Profile Group" && !a.profile.busy }) else { return false }
        b.profile.refresh()
        guard await wait("peer sees shared display name", { b.profile.snapshot?.displayName == "Profile Group" && !b.profile.busy }) else { return false }
        check("shared name keeps registration username", a.identity?.username == registeredName)
        b.profile.unlink()
        guard await wait("unlink removes only this peer", { b.profile.snapshot?.sharedUserId == nil && !b.profile.busy }) else { return false }
        a.profile.refresh()
        guard await wait("inviter stays bound after peer unlink", { a.profile.snapshot?.sharedUserId == shared && a.profile.snapshot?.devices.count == 1 && !a.profile.busy }) else { return false }
        a.profile.removeAvatar()
        guard await wait("avatar removal accepted", { a.profile.snapshot?.avatar == nil && !a.profile.busy }) else { return false }
        b.forget()
        _ = await wait("peer test device cleanup", { b.identity == nil })
        a.forget()
        _ = await wait("first test device cleanup", { a.identity == nil })
        print("PROFILE-E2E \(failures == 0 ? "PASSED" : "FAILED")")
        return failures == 0
    }

    private static func clockSkewPolling() async {
        let suite = "AgentSpeedProfileClockE2E"
        UserDefaults.standard.removePersistentDomain(forName: suite)
        let defaults = UserDefaults(suiteName: suite)!
        let model = AppModel(settings: AppSettings(defaults: defaults),
                             updates: UpdateController(configuration: .current(environment: [:]), store: UpdateStore(defaults: defaults)))
        defer { model.shutdown(); UserDefaults.standard.removePersistentDomain(forName: suite) }
        let report = model.report
        for (correction, ttl) in [(Int64(-120), Int64(30)), (Int64(120), Int64(1))] {
            report.profile.reset()
            report.preview(.reporting, username: "Clock Test")
            var document = report.discovery!
            document.extensions = .init(sharedIdentity: true)
            report.serviceState = .ready(document)
            report.clockCorrection = correction
            let expires = ReportClock.unixMs / 1000 + correction + ttl
            let transport = ProfileClockTransport(expires: ReportInstant.format(unixMs: expires * 1000))
            report.api = ReportAPI(transport: transport, userAgent: report.userAgent)
            report.profile.invite()
            guard await wait("clock fixture invitation loaded", { report.profile.pairing != nil && !report.profile.busy }) else { continue }
            try? await Task.sleep(for: .milliseconds(3600))
            let gets = await transport.gets
            if correction < 0 {
                check("fast local clock keeps valid pairing and polls", report.profile.pairing?.state == .waiting && gets > 0)
            } else {
                check("slow local clock expires pairing on server time", report.profile.pairing?.state == .expired && gets == 0
                      && report.profile.pairing?.code == nil)
            }
        }
    }

    private static func avatar() throws -> ReportAvatarImage {
        let context = CGContext(data: nil, width: 320, height: 180, bitsPerComponent: 8, bytesPerRow: 1280,
                                space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        context.setFillColor(CGColor(red: 0.14, green: 0.4, blue: 0.78, alpha: 1))
        context.fill(CGRect(x: 0, y: 0, width: 320, height: 180))
        let bytes = NSMutableData()
        let destination = CGImageDestinationCreateWithData(bytes, UTType.jpeg.identifier as CFString, 1, nil)!
        CGImageDestinationAddImage(destination, context.makeImage()!, nil)
        guard CGImageDestinationFinalize(destination) else { throw ReportAvatarImage.Failure.invalid }
        return try ReportAvatarImage.prepare(data: bytes as Data)
    }
    @discardableResult
    private static func check(_ name: String, _ condition: Bool) -> Bool {
        if !condition {
            failures += 1
            if let reason = diagnostics?() { print("PROFILE-E2E ERROR " + reason) }
        }
        print("PROFILE-E2E \(condition ? "PASS" : "FAIL") \(name)")
        return condition
    }
    @discardableResult
    private static func wait(_ name: String, within seconds: TimeInterval = 10, _ condition: () -> Bool) async -> Bool {
        let end = Date().addingTimeInterval(seconds)
        while Date() < end {
            if condition() { return check(name, true) }
            try? await Task.sleep(for: .milliseconds(100))
        }
        return check(name, false)
    }
}

private actor ProfileClockTransport: ReportTransport {
    var gets = 0
    let expires: String
    init(expires: String) { self.expires = expires }
    func get(_ url: URL, etag: String?, userAgent: String, maxBytes: Int, timeout: TimeInterval) async throws(ReportTransportError) -> ReportHTTPResponse {
        ReportHTTPResponse(status: 404)
    }
    func send(_ request: ReportRequest, maxBytes: Int, timeout: TimeInterval) async throws(ReportTransportError) -> ReportHTTPResponse {
        if request.method == "GET" { gets += 1 }
        let code = request.method == "POST" ? ",\"code\":\"ABCD-EFGH-JKLM\"" : ""
        let body = Data("""
            {"id":"33333333-3333-4333-8333-333333333333","state":"waiting","expiresAt":"\(expires)"\(code)}
            """.utf8)
        return ReportHTTPResponse(status: request.method == "POST" ? 201 : 200, body: body)
    }
}
#endif
