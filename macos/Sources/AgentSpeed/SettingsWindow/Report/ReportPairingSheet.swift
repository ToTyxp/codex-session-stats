import AgentSpeedCore
import SwiftUI

struct ReportPairingJoinSheet: View {
    let model: AppModel
    @Environment(\.dismiss) private var dismiss
    @State private var code = ""

    var body: some View {
        let profile = model.report.profile
        let l10n = model.l10n
        VStack(alignment: .leading, spacing: 18) {
            Text(l10n("profile.pairing.join")).font(.title2.weight(.semibold))
            Text(l10n("profile.pairing.note")).font(.callout).foregroundStyle(.secondary)
            TextField(l10n("profile.pairing.code"), text: $code, prompt: Text("XXXX-XXXX-XXXX"))
                .font(.title3.monospaced())
                .textFieldStyle(.roundedBorder)
                .autocorrectionDisabled()
                .onChange(of: code) { _, value in if value.count > 32 { code = String(value.prefix(32)) } }
            if let key = profile.failureKey { ReportInlineMessage(text: l10n(key)) }
            HStack {
                Spacer()
                Button(l10n("history.cancel")) { code = ""; dismiss() }
                Button(l10n("profile.pairing.join")) { profile.join(code); code = "" }
                    .buttonStyle(.borderedProminent)
                    .disabled(!profile.canRequest || ReportPairingSnapshot.normalizedCode(code) == nil)
            }
        }
        .padding(26)
        .frame(width: 440)
        .onChange(of: profile.pairing?.id) { _, id in if id != nil { code = ""; dismiss() } }
        .onDisappear { code = "" }
    }
}

struct ReportPairingSheet: View {
    let model: AppModel
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        let profile = model.report.profile
        let l10n = model.l10n
        VStack(alignment: .leading, spacing: 18) {
            Text(l10n(profile.pairingIsInvitation ? "profile.pairing.invite" : "profile.pairing.join"))
                .font(.title2.weight(.semibold))
            if let pairing = profile.pairing {
                if let code = pairing.code, pairing.state == .waiting || pairing.state == .pending {
                    Text(code).font(.system(size: 28, weight: .medium, design: .monospaced))
                        .textSelection(.enabled)
                        .accessibilityLabel(l10n("profile.pairing.code") + ": " + code)
                }
                Text(l10n(profile.pairingIsInvitation && pairing.state == .pending ? "profile.pairing.approve" : stateKey(pairing.state)))
                    .font(.callout).foregroundStyle(.secondary)
                Text(pairing.expiresAt, style: .time)
                    .font(.caption.monospacedDigit()).foregroundStyle(.tertiary)
                if let claimant = pairing.claimant {
                    HStack(alignment: .top) {
                        Image(systemName: claimant.platform == "macOS" ? "laptopcomputer" : "pc")
                            .font(.title2)
                        VStack(alignment: .leading, spacing: 4) {
                            Text(claimant.platform).fontWeight(.medium)
                            Text(claimant.deviceId).font(.caption.monospaced()).textSelection(.enabled)
                        }
                    }
                    .padding(12)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(.primary.opacity(0.045), in: RoundedRectangle(cornerRadius: 10))
                }
                if let key = profile.failureKey { ReportInlineMessage(text: l10n(key)) }
                HStack {
                    Button(l10n("history.cancel")) { profile.closePairing(); dismiss() }
                    Spacer()
                    if pairing.state == .waiting || pairing.state == .pending {
                        Button(l10n("profile.pairing.cancel")) { profile.cancelPairing() }
                            .disabled(!profile.canRequest)
                        if profile.pairingIsInvitation, pairing.state == .pending {
                            Button(l10n("profile.pairing.approve")) { profile.approve() }
                                .buttonStyle(.borderedProminent)
                                .disabled(!profile.canRequest)
                        } else {
                            Button(l10n("profile.pairing.check")) { profile.checkPairing() }
                                .disabled(!profile.canRequest)
                        }
                    }
                }
            }
        }
        .padding(26)
        .frame(width: 480)
        .onDisappear { profile.closePairing() }
    }

    private func stateKey(_ state: ReportPairingSnapshot.State) -> String {
        switch state {
        case .waiting: "profile.pairing.waiting"
        case .pending: "profile.pairing.pending"
        case .approved: "profile.pairing.approved"
        case .cancelled, .expired: "profile.pairing.expired"
        }
    }
}
