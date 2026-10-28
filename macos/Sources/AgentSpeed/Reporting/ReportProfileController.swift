import AgentSpeedCore
import Foundation
import Observation

@MainActor
@Observable
final class ReportProfileController {
    var snapshot: ReportProfileSnapshot?
    var avatar: ReportAvatarImage?
    var preview: ReportAvatarImage?
    var pairing: ReportPairingSnapshot?
    var pairingIsInvitation = false
    var busy = false
    var failureKey: String?
    var lastFailure: ReportProfileError?
    var retryAt: Date?
    @ObservationIgnored weak var owner: ReportController?
    @ObservationIgnored private var generation = 0
    @ObservationIgnored private var task: Task<Void, Never>?
    @ObservationIgnored private var poll: Task<Void, Never>?

    var capabilities: ReportExtensions { owner?.discovery?.extensions ?? .none }
    var canRequest: Bool {
        guard let owner else { return false }
        return owner.settings.networkEnabled && owner.identity?.isRegistered == true && !owner.needsReview
            && !owner.isDiscoveryStale && owner.signer != nil && !busy && (retryAt.map { $0 <= Date() } ?? true)
    }

    func choose(_ file: URL) {
        do { preview = try ReportAvatarImage.prepare(file: file); failureKey = nil }
        catch { failureKey = "profile.avatar.invalid" }
    }
    func discardPreview() { preview = nil; failureKey = nil }
    func refresh() { perform(.profile) }
    func upload() { if let preview { perform(.uploadAvatar(preview)) } }
    func removeAvatar() { perform(.removeAvatar) }
    func createIdentity() { perform(.createIdentity) }
    func rename(_ name: String) { perform(.rename(name)) }
    func unlink() { perform(.unlinkIdentity) }
    func invite() { perform(.createPairing) }
    func join(_ code: String) { perform(.claimPairing(code)) }
    func approve() { if pairingIsInvitation, let pairing { perform(.approvePairing(pairing.id)) } }
    func checkPairing() { if let pairing { perform(.pairing(pairing.id)) } }
    func cancelPairing() { if let pairing { perform(.cancelPairing(pairing.id)) } }

    func reset() {
        generation += 1
        task?.cancel(); task = nil
        closePairing()
        snapshot = nil; avatar = nil; preview = nil; failureKey = nil; lastFailure = nil; busy = false; retryAt = nil
    }
    func closePairing() {
        generation += 1
        task?.cancel(); task = nil; busy = false
        poll?.cancel(); poll = nil
        pairing = nil; pairingIsInvitation = false
    }
    func networkOff() { generation += 1; task?.cancel(); task = nil; busy = false; closePairing() }

    private func perform(_ operation: ReportProfileOperation, polling: Bool = false) {
        guard canRequest, let owner, let identity = owner.identity, let document = owner.discovery, let signer = owner.signer else { return }
        let origin = identity.id, device = identity.deviceId, current = generation
        let builder = ReportRequestBuilder(userAgent: owner.userAgent, deviceId: device, apiKey: owner.apiKey)
        let api = owner.api, hash = snapshot?.avatar?.sha256
        let timestamp = ReportClock.unixMs / 1000 + owner.clockCorrection
        busy = true
        if !polling { failureKey = nil }
        task = Task { [weak self, weak owner] in
            let result = await api.profile(operation, document: document, builder: builder, signer: signer, timestamp: timestamp, avatarHash: hash)
            guard let self, let owner, !Task.isCancelled, current == self.generation,
                  owner.identity?.id == origin, owner.identity?.deviceId == device else { return }
            self.busy = false; self.task = nil
            switch result {
            case .failure(let error):
                self.lastFailure = error
                self.failureKey = error == .unsupported ? "profile.unavailable" : "profile.failed"
                if case .rejected(let status, _, let retry) = error, status == 429 || retry != nil {
                    let seconds = min(max(retry ?? 60, 2), 3600)
                    self.retryAt = Date().addingTimeInterval(Double(seconds))
                    Task { [weak self] in
                        try? await Task.sleep(for: .seconds(seconds))
                        guard let self, current == self.generation else { return }
                        self.retryAt = nil
                    }
                }
                self.poll?.cancel(); self.poll = nil
            case .success(let value):
                self.failureKey = nil; self.lastFailure = nil; self.retryAt = nil
                switch value {
                case .profile(let snapshot):
                    self.snapshot = snapshot
                    switch operation {
                    case .uploadAvatar: self.avatar = self.preview; self.preview = nil
                    case .removeAvatar: self.avatar = nil; self.preview = nil
                    case .unlinkIdentity: self.closePairing(); self.avatar = nil
                    default: break
                    }
                    if snapshot.avatar == nil { self.avatar = nil }
                    else if self.capabilities.profileAvatar, self.preview == nil,
                            self.avatar?.sha256 != snapshot.avatar?.sha256 { self.perform(.avatar) }
                case .avatar(let image): self.avatar = image
                case .avatarMissing: self.avatar = nil
                case .cancelled: self.closePairing()
                case .pairing(var pairing):
                    switch operation {
                    case .createPairing: self.pairingIsInvitation = true
                    case .claimPairing: self.pairingIsInvitation = false
                    default: pairing.code = self.pairing?.code
                    }
                    self.pairing = pairing
                    if pairing.state == .approved {
                        self.poll?.cancel(); self.poll = nil
                        self.refresh()
                    } else if pairing.state == .waiting || pairing.state == .pending {
                        self.startPolling()
                    } else { self.poll?.cancel(); self.poll = nil; self.pairing?.code = nil }
                }
            }
        }
    }

    private func startPolling() {
        guard poll == nil, let expires = pairing?.expiresAt else { return }
        poll = Task { [weak self] in
            while !Task.isCancelled {
                do { try await Task.sleep(for: .seconds(3)) } catch { return }
                guard let self, let pairing = self.pairing else { return }
                let correctedNow = Date().addingTimeInterval(TimeInterval(self.owner?.clockCorrection ?? 0))
                guard correctedNow < expires else {
                    self.pairing?.state = .expired; self.pairing?.code = nil; self.poll = nil
                    return
                }
                guard self.owner?.settings.networkEnabled == true else { self.closePairing(); return }
                self.perform(.pairing(pairing.id), polling: true)
            }
        }
    }
}
