import AgentSpeedCore
import Foundation

/// The dashboard invitation (DESIGN 11) and the setup that follows its one click (REPORTING 3.3, One-click consent):
/// fetch the preset's document, confirm it when the invitation covered it, create the key and register. Reporting
/// that is on but not registered yet is driven from here, and leaves nothing behind when it is turned off.
extension ReportController {
    static let inviteDoneHold: TimeInterval = 2.4
    static let setupRetryInterval: TimeInterval = 3600

    // MARK: - Invitation

    /// `report.invite.accept`: the user's consent for the preset; nothing was requested before it.
    func acceptInvitation() {
        guard invitationOffered, selectedService != nil else { return }
        updatePreferences {
            $0.invite = .accepted
            $0.enabled = true
        }
        problem = nil
        clearSetupMessages()
        consentWanted = false
        inviteJustAccepted = true
        DispatchQueue.main.asyncAfter(deadline: .now() + Self.inviteDoneHold) { [weak self] in
            self?.inviteJustAccepted = false
        }
        continueSetup()
    }

    /// `report.invite.decline` and the card's close button: answered, and nothing else happens.
    func declineInvitation() {
        guard preferences.invite == nil else { return }
        updatePreferences { $0.invite = .declined }
    }

    /// `report.service.review` for `report.status.review`: the consent sheet finishes what the invitation did not
    /// cover, from the document already fetched.
    func reviewInvitation() {
        beginConsent(review: false)
    }

    // MARK: - Setup

    /// The invitation's consent still applies: accepted, for the preset, and no consent sheet asked for instead.
    var canConfirmByInvitation: Bool {
        preferences.invite == .accepted && preferences.preset == .codexRadar && !consentWanted && consent == nil
    }

    /// Moves reporting that is on but not registered one step on: register an existing device, or fetch the document
    /// and confirm it. Runs on accepting, at launch, when the network comes back, on `report.service.retry`, after
    /// the user fixed a name or a key, and from the hourly retry after a passing failure.
    func continueSetup() {
        guard isSettingUp, settings.networkEnabled, problem == nil, consent == nil, !registering,
              let service = selectedService else { return }
        if let identity {
            guard identity.pin != nil, let document = identity.discovery else { return }
            if signer == nil { loadSecrets() }
            reRegister(recheck: false, username: nextUsername(rules: document.usernameRules))
            return
        }
        guard canConfirmByInvitation, serviceState != .checking else { return }
        if case .ready(let document) = serviceState {
            confirmInvitedIfCovered(document, service: service)
        } else {
            checkService()
        }
    }

    /// REPORTING 3.3: the fetched document is stored as confirmed without a sheet only when it adds nothing the
    /// invitation did not say; then the key is created and the device registers.
    func confirmInvitedIfCovered(_ document: ReportDiscovery, service: ReportServiceURL) {
        guard isSettingUp, identity == nil, canConfirmByInvitation, problem == nil, selectedService == service else { return }
        guard document.supportsThisClient else {
            problem = .upgrade
            return
        }
        let name = nextUsername(rules: document.usernameRules)
        let presetDomain = preferences.preset == .codexRadar ? ReportPreset.codexRadarDomain : nil
        if let gap = ReportInvite.gap(document, service: service, username: name, presetDomain: presetDomain) {
            inviteGap = gap
            return
        }
        inviteGap = nil
        let deviceId = ReportCanonical.newUUID()
        let key: any ReportSigner
        do {
            key = try keyStore.createKey(deviceId: deviceId)
        } catch {
            registrationFailure = .key
            return
        }
        let now = Date()
        var identity = ReportIdentity(service: service, deviceId: deviceId, keyCreatedAt: now, lastUsedAt: now)
        identity.pin = ReportConsentPin(document)
        identity.discovery = document
        identity.discoveryFetchedAt = now
        var dropped: [String] = []
        updatePreferences { dropped = $0.upsert(identity) }
        for old in dropped { keyStore.deleteAll(deviceId: old) }
        signer = key
        pendingSigner = nil
        apiKey = nil
        registrationFailure = nil
        reRegister(recheck: false, username: name)
    }

    /// `report.service.retry`: the fetch or registration that failed, again, at once.
    func retry() {
        guard settings.networkEnabled else { return }
        if isSettingUp {
            registrationFailure = nil
            setupTimer?.invalidate()
            setupTimer = nil
            continueSetup()
        } else if consentWanted {
            beginConsent(review: false)
        } else {
            checkService()
        }
    }

    /// At most one automatic registration attempt an hour (REPORTING 7.3).
    func scheduleSetupRetry() {
        setupTimer?.invalidate()
        let timer = Timer(timeInterval: Self.setupRetryInterval, repeats: false) { [weak self] _ in
            MainActor.assumeIsolated { self?.continueSetup() }
        }
        timer.tolerance = 60
        RunLoop.main.add(timer, forMode: .common)
        setupTimer = timer
    }

    /// Turning reporting off while it is being set up: the unregistered device and its keys go, and so do the
    /// setup's messages, so a later visit starts clean.
    func endSetup() {
        setupTimer?.invalidate()
        setupTimer = nil
        consentWanted = false
        clearSetupMessages()
        discardUnregisteredIdentity()
        if serviceIssue != nil { serviceState = .unknown }
        if let problem, [.usernameTaken, .usernameRejected, .apiKey].contains(problem) { self.problem = nil }
    }

    func clearSetupMessages() {
        inviteGap = nil
        registrationFailure = nil
    }

    /// A device id and key that never registered anywhere carry nothing worth keeping.
    func discardUnregisteredIdentity() {
        guard let identity, !identity.isRegistered, !registering else { return }
        keyStore.deleteAll(deviceId: identity.deviceId)
        updatePreferences { $0.remove(identity.id) }
        loadSecrets()
    }

    /// The pane closed: with reporting off, a fetch's message belonged to the attempt that caused it (DESIGN 11,
    /// Service), so it goes.
    func paneClosed() {
        profile.closePairing()
        if consent == nil { consentWanted = false }
        guard !preferences.enabled else { return }
        if serviceIssue != nil { serviceState = .unknown }
        if case .failed = renameState { renameState = .idle }
    }
}
