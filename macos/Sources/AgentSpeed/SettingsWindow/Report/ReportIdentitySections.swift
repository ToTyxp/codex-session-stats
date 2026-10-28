import AgentSpeedCore
import SwiftUI

/// A username field with the dice button and the public caption, used by the pane and the consent sheet.
struct ReportUsernameField: View {
    let model: AppModel
    @Binding var text: String
    let rules: ReportUsernameRules
    var onSubmit: () -> Void = {}
    var onCancel: (() -> Void)?
    var focused: FocusState<Bool>.Binding?

    var body: some View {
        let l10n = model.l10n
        LabeledContent {
            HStack(spacing: 6) {
                field
                    .labelsHidden()
                    .textFieldStyle(.roundedBorder)
                    .frame(width: 210)
                    .autocorrectionDisabled()
                    .onSubmit(onSubmit)
                    .onExitCommand { onCancel?() }
                    .accessibilityLabel(l10n("report.username"))
                    .accessibilityHint(l10n("report.username.public"))
                Button {
                    text = ReportUsername.random()
                    onSubmit()
                } label: {
                    Image(systemName: "dice")
                        .font(.system(size: 13))
                        .frame(width: 22, height: 22)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.borderless)
                .help(l10n("report.username.random"))
                .accessibilityLabel(l10n("report.username.random"))
            }
        } label: {
            VStack(alignment: .leading, spacing: 2) {
                RowTitle(l10n("report.username"))
                Footnote(l10n("report.username.public"))
            }
        }
    }

    @ViewBuilder
    private var field: some View {
        let base = TextField(model.l10n("report.username"), text: $text)
        if let focused { base.focused(focused) } else { base }
    }

    /// The localized message for the field's current text, or nil when it passes the service's rules.
    static func problem(_ text: String, rules: ReportUsernameRules, l10n: Localizer) -> String? {
        let name = ReportUsername.normalized(text)
        guard let problem = ReportUsername.validate(name, rules: rules) else { return nil }
        let key = ReportController.usernameKey(problem, name: name, rules: rules)
        return key == "report.username.invalid.length" ? l10n(key, rules.minLength, rules.maxLength) : l10n(key)
    }
}

/// A one-line inline message under a field: errors in red with a symbol, results in the secondary color.
struct ReportInlineMessage: View {
    let text: String
    var isError = true

    var body: some View {
        Label {
            Text(text).fixedSize(horizontal: false, vertical: true)
        } icon: {
            Image(systemName: isError ? "exclamationmark.circle.fill" : "checkmark.circle.fill")
                .foregroundStyle(isError ? Color.red : Color.green)
        }
        .font(.system(size: 11))
        .foregroundStyle(isError ? AnyShapeStyle(.primary) : AnyShapeStyle(.secondary))
        .imageScale(.small)
        .frame(maxWidth: .infinity, alignment: .trailing)
        .transition(.opacity)
    }
}

/// `report.apiKey` (REPORTING 5.6): a secure field, or the saved key as `••••` and its last 4 characters with Clear.
struct ReportAPIKeySection: View {
    let model: AppModel
    let mode: ReportAPIKeyMode

    @State private var text = ""
    @State private var rejected = false

    var body: some View {
        let l10n = model.l10n
        let report = model.report
        Section {
            LabeledContent {
                if let masked = report.maskedAPIKey {
                    HStack(spacing: Spacing.s) {
                        Text(masked)
                            .font(.system(size: 12, design: .monospaced))
                            .foregroundStyle(.secondary)
                            .accessibilityLabel(l10n("report.apiKey"))
                        Button(l10n("report.apiKey.clear")) { report.clearAPIKey() }
                            .buttonStyle(.borderless)
                            .font(.system(size: 12))
                    }
                } else {
                    SecureField(text: $text, prompt: Text(mode == .optional ? l10n("report.apiKey.optional") : "")) {
                        Text(l10n("report.apiKey"))
                    }
                    .labelsHidden()
                    .textFieldStyle(.roundedBorder)
                    .frame(width: 210)
                    .onSubmit(save)
                }
            } label: {
                VStack(alignment: .leading, spacing: 2) {
                    RowTitle(l10n("report.apiKey"))
                    if mode == .required { Footnote(l10n("report.apiKey.required")) }
                }
            }
            if rejected {
                ReportInlineMessage(text: l10n("report.status.apiKey"))
            }
            HStack(alignment: .firstTextBaseline) {
                Footnote(l10n("report.apiKey.hint"))
                Spacer(minLength: Spacing.xs)
                if let url = report.discovery?.apiKeyURL {
                    ReportLink(title: l10n("report.apiKey.get"), url: url)
                }
            }
        }
    }

    private func save() {
        rejected = !model.report.setAPIKey(text)
        if !rejected { text = "" }
    }
}
