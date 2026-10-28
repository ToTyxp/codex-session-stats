import AgentSpeedCore
import SwiftUI

struct ReportDeviceManagementSheet: View {
    let model: AppModel
    @Environment(\.dismiss) private var dismiss
    @State private var confirmsUnlink = false
    @State private var showsJoin = false

    var body: some View {
        let report = model.report
        let profile = report.profile
        let l10n = model.l10n
        VStack(spacing: 0) {
            HStack {
                Text(l10n("profile.deviceManagement")).font(.title2.weight(.semibold))
                Spacer()
                Button { dismiss() } label: { Image(systemName: "xmark") }
                    .buttonStyle(.borderless)
                    .accessibilityLabel(l10n("history.cancel"))
            }
            .padding(24)
            Form {
                if report.identity?.isRegistered == true {
                    if profile.capabilities.sharedIdentity { sharedDevices }
                    else { Section { Footnote(l10n("profile.unavailable")) } }
                    ReportDeviceSection(model: model, showsRegistrationName: false)
                }
                if !report.otherIdentities.isEmpty { ReportOthersSection(model: model) }
                if let key = profile.failureKey { Section { ReportInlineMessage(text: l10n(key)) } }
            }
            .formStyle(.grouped)
        }
        .frame(width: 600, height: 540)
        .sheet(isPresented: $showsJoin) {
            ReportPairingJoinSheet(model: model).appEnvironment(model)
        }
        .sheet(isPresented: Binding(get: { profile.pairing != nil && !showsJoin }, set: { if !$0 { profile.closePairing() } })) {
            ReportPairingSheet(model: model).appEnvironment(model)
        }
        .alert(l10n("profile.identity.unlink"), isPresented: $confirmsUnlink) {
            Button(l10n("profile.identity.unlink"), role: .destructive) { profile.unlink() }
            Button(l10n("history.cancel"), role: .cancel) {}
        } message: { Text(l10n("profile.identity.unlinkNote")) }
        .onDisappear { profile.closePairing() }
    }

    @ViewBuilder
    private var sharedDevices: some View {
        let profile = model.report.profile
        let l10n = model.l10n
        Section {
            if let snapshot = profile.snapshot {
                if snapshot.sharedUserId != nil {
                    Label(l10n("profile.identity.bound"), systemImage: "checkmark.shield")
                        .foregroundStyle(.secondary)
                    HStack {
                        Button(l10n("profile.pairing.invite")) { profile.invite() }
                        Spacer()
                        Button(l10n("profile.identity.unlink")) { confirmsUnlink = true }
                    }
                    .disabled(!profile.canRequest)
                    ForEach(snapshot.devices) { device in
                        HStack(spacing: 10) {
                            Image(systemName: device.platform == "macOS" ? "laptopcomputer" : "pc")
                            VStack(alignment: .leading, spacing: 3) {
                                Text(device.platform)
                                Text(device.deviceId).font(.caption.monospaced()).foregroundStyle(.secondary)
                                    .textSelection(.enabled)
                            }
                            Spacer()
                            Circle().fill(device.online ? .green : .secondary).frame(width: 6, height: 6)
                                .accessibilityHidden(true)
                        }
                    }
                } else {
                    Footnote(l10n("profile.identity.unbound"))
                    HStack {
                        Button(l10n("profile.identity.create")) { profile.createIdentity() }
                        Spacer()
                        Button(l10n("profile.pairing.join")) { showsJoin = true }
                    }
                    .disabled(!profile.canRequest)
                }
            } else {
                HStack {
                    Footnote(l10n("profile.refresh"))
                    Spacer()
                    Button(l10n("profile.refresh")) { profile.refresh() }.disabled(!profile.canRequest)
                }
            }
        } header: { Text(l10n("profile.devices")) }
    }
}
