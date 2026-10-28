import AgentSpeedCore
import AppKit
import SwiftUI
import UniformTypeIdentifiers

struct ReportAvatarImageView: View {
    let model: AppModel

    var body: some View {
        let profile = model.report.profile
        Group {
            if let image = (profile.preview ?? profile.avatar).flatMap({ NSImage(data: $0.png) }) {
                Image(nsImage: image).resizable().scaledToFill()
            } else {
                Image(systemName: "person.crop.circle.fill")
                    .resizable().foregroundStyle(.tertiary)
            }
        }
        .frame(width: 76, height: 76)
        .clipShape(Circle())
        .overlay(Circle().strokeBorder(profile.preview == nil ? Color.primary.opacity(0.12) : Color.accentColor.opacity(0.7), lineWidth: 1))
        .accessibilityLabel(model.l10n("profile.title"))
    }
}

struct ReportAvatarActions: View {
    let model: AppModel

    var body: some View {
        let profile = model.report.profile
        let l10n = model.l10n
        VStack(alignment: .leading, spacing: 7) {
            ViewThatFits(in: .horizontal) {
                HStack(spacing: 10) { buttons }
                VStack(alignment: .leading, spacing: 8) { buttons }
            }
            Footnote(l10n(profile.preview == nil ? "profile.avatar.note" : "profile.avatar.pending"))
        }
    }

    @ViewBuilder
    private var buttons: some View {
        let profile = model.report.profile
        let l10n = model.l10n
        Button(l10n(profile.avatar == nil && profile.preview == nil ? "profile.avatar.choose" : "profile.avatar.change")) {
            chooseAvatar()
        }
        .disabled(profile.busy)
        if profile.preview != nil {
            Button(l10n("profile.avatar.save")) { profile.upload() }
                .buttonStyle(.borderedProminent)
                .disabled(!profile.canRequest)
            Button(l10n("history.cancel")) { profile.discardPreview() }
                .disabled(profile.busy)
        } else if profile.snapshot?.avatar != nil || profile.avatar != nil {
            Button(l10n("profile.avatar.remove")) { profile.removeAvatar() }
                .disabled(!profile.canRequest)
        }
    }

    private func chooseAvatar() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.png, .jpeg]
        panel.canChooseDirectories = false
        panel.allowsMultipleSelection = false
        panel.begin { response in
            MainActor.assumeIsolated {
                guard response == .OK, let url = panel.url else { return }
                let scoped = url.startAccessingSecurityScopedResource()
                defer { if scoped { url.stopAccessingSecurityScopedResource() } }
                model.report.profile.choose(url)
            }
        }
    }
}
