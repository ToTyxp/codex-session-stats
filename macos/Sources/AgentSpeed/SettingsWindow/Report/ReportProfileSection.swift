import AgentSpeedCore
import SwiftUI

struct ReportProfileSection: View {
    let model: AppModel
    var loadsProfileOnAppearance = true
    @State private var showsDevices = false

    private var profileLoadKey: String {
        let report = model.report
        let caps = report.profile.capabilities
        return [report.identity?.id ?? "", report.identity?.deviceId ?? "", String(report.identity?.isRegistered == true), String(caps.profileAvatar),
                String(caps.sharedIdentity), String(model.settings.networkEnabled)].joined(separator: "/")
    }

    var body: some View {
        let report = model.report
        let profile = report.profile
        let l10n = model.l10n
        let registered = report.identity?.isRegistered == true
        Section {
            VStack(alignment: .leading, spacing: 14) {
                HStack(alignment: .center, spacing: 18) {
                    ReportAvatarImageView(model: model)
                    ReportProfileNameEditor(model: model)
                        .frame(maxWidth: .infinity, alignment: .leading)
                }
                if registered && profile.capabilities.profileAvatar {
                    ReportAvatarActions(model: model)
                } else {
                    Footnote(l10n("report.username.public"))
                }
                if let key = profile.failureKey, registered {
                    ReportInlineMessage(text: l10n(key))
                }
                if registered || !report.otherIdentities.isEmpty {
                    Divider().opacity(0.6)
                    HStack(spacing: 10) {
                        if profile.busy { ProgressView().controlSize(.small) }
                        if registered && (profile.capabilities.profileAvatar || profile.capabilities.sharedIdentity) {
                            Button(l10n("profile.refresh")) { profile.refresh() }
                                .buttonStyle(.borderless)
                                .disabled(!profile.canRequest)
                        }
                        Spacer(minLength: 0)
                        Button(l10n("profile.manageDevices")) { showsDevices = true }
                    }
                }
            }
            .padding(.vertical, 4)
        } header: {
            Text(l10n("profile.title"))
        }
        .sheet(isPresented: $showsDevices) {
            ReportDeviceManagementSheet(model: model)
                .appEnvironment(model)
        }
        .task(id: profileLoadKey) {
            guard loadsProfileOnAppearance, registered,
                  profile.capabilities.profileAvatar || profile.capabilities.sharedIdentity,
                  profile.canRequest else { return }
            profile.refresh()
        }
        .onDisappear { profile.closePairing() }
    }
}
