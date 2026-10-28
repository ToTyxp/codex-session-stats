import SwiftUI

/// Settings › About › Network (NETWORK 1, DESIGN 9 About): the master switch for every request the app makes — the
/// update check, the price table and the exchange rates. Off, nothing is requested and costs use the built-in data.
struct NetworkSettingsCard: View {
    let model: AppModel
    @Environment(\.colorSchemeContrast) private var contrast

    var body: some View {
        @Bindable var settings = model.settings
        let l10n = model.l10n
        let shape = RoundedRectangle(cornerRadius: 12, style: .continuous)
        HStack(alignment: .center, spacing: Spacing.s) {
            Image(systemName: settings.networkEnabled ? "network" : "network.slash")
                .font(.system(size: 13, weight: .semibold))
                .foregroundStyle(settings.networkEnabled ? Color.accentColor : Color.secondary)
                .frame(width: 26, height: 26)
                .background(Color.primary.opacity(0.06), in: RoundedRectangle(cornerRadius: 7, style: .continuous))
                .contentTransition(.symbolEffect(.replace))
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                Text(l10n("settings.network"))
                Footnote(l10n(model.report.isOn ? "settings.network.hint.report" : "settings.network.hint"))
            }
            .accessibilityHidden(true)
            Spacer(minLength: Spacing.xs)
            Toggle(l10n("settings.network"), isOn: $settings.networkEnabled)
                .labelsHidden()
                .toggleStyle(.switch)
                .accessibilityHint(l10n(model.report.isOn ? "settings.network.hint.report" : "settings.network.hint"))
        }
        .padding(Spacing.m)
        .background(Color.primary.opacity(0.04), in: shape)
        .overlay {
            shape.strokeBorder(Color.primary.opacity(contrast == .increased ? 0.3 : 0.08), lineWidth: 1)
        }
        .animation(Motion.fade, value: settings.networkEnabled)
    }
}

/// `settings.network.offHint` under a control that the master switch disables.
struct NetworkOffHint: View {
    let model: AppModel

    var body: some View {
        if !model.settings.networkEnabled {
            Label(model.l10n("settings.network.offHint"), systemImage: "network.slash")
                .font(.subheadline)
                .foregroundStyle(.secondary)
                .labelStyle(.titleAndIcon)
                .imageScale(.small)
        }
    }
}
