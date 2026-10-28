import AgentSpeedCore
import SwiftUI

struct GeneralPane: View {
    let model: AppModel

    /// The speed title hovered or focused in the ladder under the redline; its tick on the heat strip lights up.
    @State private var highlightedTitle: SpeedTitle?
    /// Whether the pane can be seen (the title editor's previews run only then).
    @State private var activity = OrbPaneActivity()

    var body: some View {
        @Bindable var settings = model.settings
        let l10n = model.l10n
        Form {
            Section {
                SegmentedRow(
                    title: l10n("settings.appearance"),
                    selection: $settings.appearance,
                    count: AppearanceMode.allCases.count
                ) {
                    ForEach(AppearanceMode.allCases) { mode in
                        Text(l10n(mode.titleKey)).tag(mode)
                    }
                }

                Picker(l10n("settings.language"), selection: $settings.language) {
                    Text(systemLanguageTitle).tag(LanguagePreference.system)
                    Divider()
                    ForEach(LanguagePreference.allCases.filter { $0 != .system }) { language in
                        Text(l10n(language.titleKey)).tag(language)
                    }
                }
                .pickerStyle(.menu)

                LaunchAtLoginRow(launchAtLogin: model.launchAtLogin, l10n: l10n)
            }

            Section {
                Picker(l10n("settings.metric"), selection: $settings.metric) {
                    ForEach(SpeedMetric.allCases) { metric in
                        Text(l10n(metric.titleKey)).tag(metric)
                    }
                }
                .pickerStyle(.menu)

                RedlineControl(
                    settings: settings,
                    l10n: l10n,
                    reduceMotion: model.accessibility.reduceMotion,
                    highlightedTitle: highlightedTitle
                )
            } footer: {
                RedlineFooter(settings: settings, l10n: l10n, highlightedTitle: $highlightedTitle, theme: model.titles.general)
            }

            TitleThemeSection(model: model, active: activity.visible)
        }
        .settingsForm()
        .background(OrbPaneActivityReader(activity: $activity))
        .animation(model.accessibility.reduceMotion ? .easeOut(duration: 0.2) : .spring(response: 0.35, dampingFraction: 0.86),
                   value: model.titles.document.selected)
        .onAppear { model.launchAtLogin.refresh() }
    }

    /// "System (English)": names the language the System choice resolves to right now.
    private var systemLanguageTitle: String {
        let resolved: LanguagePreference = switch AppLanguage.system {
        case .zhHant: .zhHant
        case .zhHans: .zhHans
        case .en: .en
        case .ja: .ja
        }
        return model.l10n("mac.settings.language.systemValue", model.l10n(resolved.titleKey))
    }
}

private struct LaunchAtLoginRow: View {
    let launchAtLogin: LaunchAtLogin
    let l10n: Localizer

    var body: some View {
        let title = l10n("settings.launchAtLogin")
        let status = launchAtLogin.status
        LabeledContent {
            Toggle(title, isOn: Binding(
                get: { launchAtLogin.isEnabled },
                set: { launchAtLogin.setEnabled($0) }
            ))
            .labelsHidden()
            .toggleStyle(.switch)
            .disabled(status == .unavailable)
        } label: {
            VStack(alignment: .leading, spacing: Spacing.xxs) {
                Text(title)
                switch status {
                case .unavailable:
                    Footnote(l10n("mac.launchAtLogin.unavailable"))
                case .requiresApproval:
                    Footnote(l10n("mac.launchAtLogin.requiresApproval"))
                    Button(l10n("mac.launchAtLogin.openSettings")) {
                        launchAtLogin.openSystemSettings()
                    }
                    .buttonStyle(.link)
                    .font(.subheadline)
                case .enabled, .disabled:
                    EmptyView()
                }
                if launchAtLogin.lastErrorFailed {
                    Notice(text: l10n("mac.launchAtLogin.failed"), symbol: "exclamationmark.triangle.fill", tint: .orange)
                }
            }
        }
    }
}
