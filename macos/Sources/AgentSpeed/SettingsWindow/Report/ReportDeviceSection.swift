import AgentSpeedCore
import SwiftUI

/// `report.device` (DESIGN 11, This device): the registration, the key's fingerprint, Forget (asks first; falls back to
/// deleting the local key only when the service cannot be reached) and Reset after a key mismatch.
struct ReportDeviceSection: View {
    let model: AppModel
    var showsRegistrationName = true

    @State private var confirmsForget = false
    @State private var confirmsReset = false

    var body: some View {
        let l10n = model.l10n
        let report = model.report
        let name = report.identity?.username ?? ""
        let host = report.destinationHost ?? ""
        Section {
            HStack(alignment: .firstTextBaseline, spacing: Spacing.s) {
                Text(showsRegistrationName ? l10n("report.device.detail", name, host) : host)
                    .textSelection(.enabled)
                    .fixedSize(horizontal: false, vertical: true)
                Spacer(minLength: Spacing.xs)
                if let profile = report.identity?.profileURL {
                    ReportLink(title: l10n("report.device.profile"), url: profile)
                }
            }
            if let fingerprint = report.fingerprint {
                LabeledContent {
                    Text(fingerprint)
                        .font(.system(size: 12, design: .monospaced))
                        .foregroundStyle(.secondary)
                        .textSelection(.enabled)
                } label: {
                    RowTitle(l10n("report.device.fingerprint"))
                }
            }
            if report.problem == .keyMismatch {
                HStack(alignment: .firstTextBaseline, spacing: Spacing.s) {
                    Footnote(l10n("report.reset.detail"))
                    Spacer(minLength: Spacing.xs)
                    Button(l10n("report.reset")) { confirmsReset = true }
                }
            }
            forgetRow
        } header: {
            Text(l10n("report.device"))
        }
        .alert(l10n("report.forget.confirm", report.serviceName ?? host), isPresented: $confirmsForget) {
            Button(l10n("report.forget.action"), role: .destructive) { report.forget() }
            Button(l10n("history.cancel"), role: .cancel) {}
        } message: {
            Text(l10n("report.forget.detail"))
        }
        .alert(l10n("report.reset"), isPresented: $confirmsReset) {
            Button(l10n("report.reset"), role: .destructive) { report.resetDevice() }
            Button(l10n("history.cancel"), role: .cancel) {}
        } message: {
            Text(l10n("report.reset.detail"))
        }
    }

    @ViewBuilder
    private var forgetRow: some View {
        let l10n = model.l10n
        let report = model.report
        switch report.forgetState {
        case .failed(let reason):
            VStack(alignment: .leading, spacing: Spacing.xs) {
                ReportInlineMessage(text: l10n("report.forget.failed", l10n(reason.key)))
                    .frame(maxWidth: .infinity, alignment: .leading)
                HStack(alignment: .firstTextBaseline, spacing: Spacing.s) {
                    Footnote(l10n("report.forget.localOnly.detail"))
                    Spacer(minLength: Spacing.xs)
                    if let privacy = report.discovery?.privacy {
                        ReportLink(title: l10n("report.service.privacy"), url: privacy)
                    }
                }
                HStack {
                    Spacer()
                    Button(l10n("history.cancel")) { report.dismissForgetResult() }
                    Button(l10n("report.forget.localOnly"), role: .destructive) {
                        report.forgetLocally()
                        report.dismissForgetResult()
                    }
                }
            }
        default:
            HStack {
                if report.forgetState == .working {
                    ProgressView().controlSize(.small)
                }
                Spacer()
                Button(l10n("report.forget"), role: .destructive) { confirmsForget = true }
                    .buttonStyle(.borderless)
                    .foregroundStyle(.red)
                    .disabled(report.forgetState == .working || !model.settings.networkEnabled)
            }
        }
    }
}

/// `report.others`: identities kept for services that are not selected, each with Forget.
struct ReportOthersSection: View {
    let model: AppModel

    @State private var expanded = false
    @State private var forgetting: ReportIdentity?

    var body: some View {
        let l10n = model.l10n
        let report = model.report
        Section {
            DisclosureGroup(isExpanded: $expanded) {
                ForEach(report.otherIdentities) { identity in
                    HStack(alignment: .firstTextBaseline, spacing: Spacing.s) {
                        VStack(alignment: .leading, spacing: 2) {
                            Text(identity.discovery?.api.authority ?? identity.service.authority)
                            Footnote(identity.username ?? "")
                        }
                        Spacer(minLength: Spacing.xs)
                        Button(l10n("report.forget"), role: .destructive) { forgetting = identity }
                            .buttonStyle(.borderless)
                            .foregroundStyle(.red)
                            .disabled(!model.settings.networkEnabled)
                    }
                    .accessibilityElement(children: .contain)
                }
            } label: {
                RowTitle(l10n("report.others"))
            }
        }
        .alert(l10n("report.forget.confirm", forgetting?.discovery?.name ?? forgetting?.service.authority ?? ""),
               isPresented: Binding(get: { forgetting != nil }, set: { if !$0 { forgetting = nil } })) {
            Button(l10n("report.forget.action"), role: .destructive) {
                if let forgetting { report.forget(forgetting.id) }
                forgetting = nil
            }
            Button(l10n("history.cancel"), role: .cancel) { forgetting = nil }
        } message: {
            Text(l10n("report.forget.detail"))
        }
    }
}
