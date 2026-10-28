import AgentSpeedCore
import SwiftUI

/// Settings → API (DESIGN 11): the master switch, the service, the username (always shown) and the API key, what is
/// sent and the status line. Profile management stays in one card; supported services refresh its public data.
struct ReportPane: View {
    let model: AppModel

    var body: some View {
        let report = model.report
        let registered = report.identity?.isRegistered == true
        Form {
            ReportMasterSection(model: model)
            ReportServiceSection(model: model)
            ReportProfileSection(model: model)
            if report.identity != nil, let mode = report.discovery?.apiKeyMode, mode != .none {
                ReportAPIKeySection(model: model, mode: mode)
            }
            ReportSectionsSection(model: model)
            ReportStatusSection(model: model)
        }
        .settingsForm()
        .animation(model.accessibility.reduceMotion ? nil : .easeOut(duration: 0.2), value: registered)
        .onDisappear { report.paneClosed() }
        .sheet(isPresented: Binding(get: { report.consent != nil }, set: { if !$0 { report.cancelConsent() } })) {
            ReportConsentSheet(model: model)
                .appEnvironment(model)
        }
    }
}

/// `report.enabled` with its caption; disabled with the network master switch off.
private struct ReportMasterSection: View {
    let model: AppModel

    var body: some View {
        let l10n = model.l10n
        let report = model.report
        let network = model.settings.networkEnabled
        Section {
            Toggle(isOn: Binding(get: { report.preferences.enabled }, set: { report.setEnabled($0) })) {
                VStack(alignment: .leading, spacing: 2) {
                    RowTitle(l10n("report.enabled"))
                    Footnote(network ? l10n("report.enabled.hint") : l10n("settings.network.offHint"))
                }
            }
            .disabled(!network)
            if report.forgetState == .done {
                ReportInlineMessage(text: l10n("report.forget.done"), isError: false)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .animation(model.accessibility.reduceMotion ? nil : Motion.fade, value: report.forgetState)
    }
}

/// `report.service`: the preset popup, the custom URL and the service card.
private struct ReportServiceSection: View {
    let model: AppModel

    var body: some View {
        let l10n = model.l10n
        let report = model.report
        let reduceMotion = model.accessibility.reduceMotion
        Section {
            PopupRow(title: l10n("report.service"),
                     selection: Binding(get: { report.preferences.preset }, set: { report.selectPreset($0) })) {
                Text(ReportPreset.codexRadarName).tag(ReportPreset.codexRadar)
                Divider()
                Text(l10n("report.service.custom")).tag(ReportPreset.custom)
            }
            if report.preferences.preset == .custom {
                ReportURLField(model: model)
            }
            if let service = report.selectedService, let discovery = report.discovery {
                ReportServiceCard(model: model, service: service, discovery: discovery)
                    .transition(reduceMotion ? .identity : .opacity)
                if report.needsReview {
                    HStack(alignment: .firstTextBaseline, spacing: Spacing.s) {
                        Label(l10n("report.service.changed"), systemImage: "exclamationmark.circle.fill")
                            .symbolRenderingMode(.multicolor)
                            .font(.system(size: 11))
                            .fixedSize(horizontal: false, vertical: true)
                        Spacer(minLength: Spacing.xs)
                        Button(l10n("report.service.review")) { report.beginConsent(review: true) }
                            .disabled(!model.settings.networkEnabled)
                    }
                }
            } else if let service = report.selectedService {
                // Before the first fetch: only what the app itself knows (REPORTING 3.4).
                ReportServiceSummary(model: model, service: service,
                                     name: report.preferences.preset == .codexRadar ? ReportPreset.codexRadarName : nil)
                    .transition(reduceMotion ? .identity : .opacity)
            }
            ReportServiceState(model: model)
        }
        .animation(reduceMotion ? nil : Motion.fade, value: report.discovery?.name)
    }
}

/// `report.url`, validated as the user types (0.5 s debounce) and committed on Return or focus loss.
private struct ReportURLField: View {
    let model: AppModel

    @State private var text = ""
    @State private var problem: ReportURLProblem?
    @State private var loopback = false
    @State private var validation: Task<Void, Never>?
    @FocusState private var focused: Bool

    var body: some View {
        let l10n = model.l10n
        VStack(alignment: .leading, spacing: 4) {
            LabeledContent {
                TextField(l10n("report.url"), text: $text, prompt: Text(l10n("report.url.placeholder")))
                    .labelsHidden()
                    .textFieldStyle(.roundedBorder)
                    .frame(width: 260)
                    .autocorrectionDisabled()
                    .focused($focused)
                    .onSubmit(commit)
                    .accessibilityLabel(l10n("report.url"))
            } label: {
                RowTitle(l10n("report.url"))
            }
            if let problem {
                Label(l10n(problem == .notHTTPS ? "report.url.https" : "report.url.invalid"),
                      systemImage: "exclamationmark.circle.fill")
                    .symbolRenderingMode(.multicolor)
                    .font(.system(size: 11))
                    .frame(maxWidth: .infinity, alignment: .trailing)
            } else if loopback {
                ReportLoopbackNote(model: model)
                    .frame(maxWidth: .infinity, alignment: .trailing)
            }
        }
        .onAppear { text = model.report.preferences.customURL }
        .onChange(of: text) { _, value in debounce(value) }
        .onChange(of: focused) { _, isFocused in if !isFocused { commit() } }
        .animation(Motion.fade, value: problem)
    }

    private func debounce(_ value: String) {
        validation?.cancel()
        validation = Task { @MainActor in
            try? await Task.sleep(for: .milliseconds(500))
            guard !Task.isCancelled else { return }
            validate(value)
        }
    }

    private func validate(_ value: String) {
        guard !value.trimmingCharacters(in: .whitespaces).isEmpty else {
            problem = nil
            loopback = false
            return
        }
        switch ReportServiceURL.parse(value) {
        case .success(let service):
            problem = nil
            loopback = service.isLoopbackHTTP
        case .failure(let failure):
            problem = failure
            loopback = false
        }
    }

    private func commit() {
        validate(text)
        guard problem == nil, !text.isEmpty else { return }
        model.report.commitCustomURL(text)
        if let normalized = try? ReportServiceURL.parse(text).get() { text = normalized.string }
    }
}
