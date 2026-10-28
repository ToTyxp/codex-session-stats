import AgentSpeedCore
import SwiftUI

@MainActor
enum ReportProfileNameTarget: Equatable {
    case registration, device, shared, loading

    static func resolve(_ report: ReportController) -> Self {
        guard report.identity?.isRegistered == true else { return .registration }
        if let snapshot = report.profile.snapshot { return snapshot.sharedUserId == nil ? .device : .shared }
        let caps = report.profile.capabilities
        return caps.profileAvatar || caps.sharedIdentity ? .loading : .device
    }

    func commit(_ text: String, report: ReportController) {
        switch self {
        case .registration: report.commitUsername(text)
        case .device:
            if report.settings.networkEnabled { report.commitUsername(text) }
        case .shared:
            if report.profile.capabilities.sharedIdentity && report.profile.canRequest { report.profile.rename(text) }
        case .loading: break
        }
    }
}

struct ReportProfileNameEditor: View {
    let model: AppModel
    @State private var text = ""
    @FocusState private var focused: Bool

    private var target: ReportProfileNameTarget { .resolve(model.report) }
    private var currentName: String {
        target == .shared ? model.report.profile.snapshot?.displayName ?? model.report.fieldUsername : model.report.fieldUsername
    }
    private var changed: Bool { ReportUsername.normalized(text) != currentName }
    private var canEdit: Bool {
        let report = model.report
        guard target != .loading, !report.registering, report.renameState != .working else { return false }
        return target == .registration || (model.settings.networkEnabled && (target != .shared || (report.profile.capabilities.sharedIdentity && report.profile.canRequest)))
    }

    var body: some View {
        let l10n = model.l10n
        let report = model.report
        let label = l10n(target == .shared ? "profile.displayName" : "report.username")
        VStack(alignment: .leading, spacing: 7) {
            Text(label).font(.caption).foregroundStyle(.secondary)
            TextField(label, text: $text)
                .labelsHidden()
                .textFieldStyle(.roundedBorder)
                .multilineTextAlignment(.leading)
                .autocorrectionDisabled()
                .focused($focused)
                .disabled(!canEdit)
                .onSubmit(commit)
                .onExitCommand { restore() }
                .accessibilityLabel(label)
                .accessibilityHint(l10n("report.username.public"))
            HStack(spacing: 10) {
                Button(l10n("profile.save")) { commit() }
                    .disabled(!canEdit || !changed || problem != nil)
                if changed {
                    Button(l10n("history.cancel")) { restore() }
                }
                Button {
                    text = ReportUsername.random()
                    commit()
                } label: {
                    Image(systemName: "dice")
                }
                .buttonStyle(.borderless)
                .help(l10n("report.username.random"))
                .accessibilityLabel(l10n("report.username.random"))
                .disabled(!canEdit)
                Spacer(minLength: 0)
                if target == .loading || report.renameState == .working {
                    ProgressView().controlSize(.small)
                }
            }
            if let problem {
                ReportInlineMessage(text: problem)
            } else if target != .shared {
                switch report.renameState {
                case .updated: ReportInlineMessage(text: l10n("report.username.updated"), isError: false)
                case .failed(let key): ReportInlineMessage(text: l10n(key))
                case .working, .idle: EmptyView()
                }
            }
        }
        .onAppear { restore() }
        .onChange(of: currentName) { old, name in if !focused && text == old { text = name } }
        .onChange(of: target) { _, _ in if !focused { restore() } }
        .onChange(of: focused) { _, value in if !value && target == .registration { commit() } }
    }

    private var problem: String? {
        guard !text.isEmpty else { return nil }
        return ReportUsernameField.problem(text, rules: model.report.usernameRules, l10n: model.l10n)
    }
    private func restore() { text = currentName }
    private func commit() {
        guard canEdit, changed, problem == nil, !text.isEmpty else { return }
        target.commit(text, report: model.report)
    }
}
