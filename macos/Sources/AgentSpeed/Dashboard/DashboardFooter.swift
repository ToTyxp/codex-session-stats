import AgentSpeedCore
import SwiftUI

/// Floating-orb toggle, metric info, update pill, settings and quit (DESIGN section 4.6, UPDATES section 4).
struct DashboardFooter: View {
    let model: AppModel

    @State private var showsInfo = false

    var body: some View {
        let l10n = model.l10n
        let orbVisible = model.orbs.masterVisible
        let plural = model.orbs.orbs.count > 1
        let orbAction = l10n(orbVisible ? (plural ? "menu.hideOrbs" : "menu.hideOrb") : (plural ? "menu.showOrbs" : "menu.showOrb"))

        HStack(spacing: 2) {
            Button {
                model.commands?.setOrbsVisible(!orbVisible)
            } label: {
                HStack(spacing: 6) {
                    Image(systemName: orbVisible ? "circle.circle.fill" : "circle.circle")
                        .font(.system(size: 13, weight: .regular))
                        .foregroundStyle(orbVisible ? AnyShapeStyle(Color.accentColor) : AnyShapeStyle(.dashboardSecondary))
                        .contentTransition(.symbolEffect(.replace))
                    Text(l10n("settings.orb"))
                        .lineLimit(1)
                }
            }
            .buttonStyle(DashboardPillButtonStyle(isOn: orbVisible))
            .help(orbAction)
            .accessibilityLabel(orbAction)
            .accessibilityAddTraits(orbVisible ? .isSelected : [])

            Button {
                showsInfo.toggle()
            } label: {
                Image(systemName: "info.circle")
            }
            .buttonStyle(DashboardIconButtonStyle(isOn: showsInfo))
            .help(l10n("mac.dashboard.info"))
            .accessibilityLabel(l10n("mac.dashboard.info"))
            .popover(isPresented: $showsInfo, arrowEdge: .top) {
                MetricInfoView(model: model)
            }

            Spacer(minLength: Spacing.xs)

            if let release = model.updates.availableRelease {
                UpdatePill(model: model, release: release)
                    .transition(.opacity.combined(with: .scale(scale: 0.92)))
            }

            if model.report.indicatorShown {
                ReportIndicator(model: model)
                    .transition(.opacity)
            }

            Button {
                model.commands?.showSettings(nil)
            } label: {
                Image(systemName: "gearshape")
            }
            .buttonStyle(DashboardIconButtonStyle())
            .keyboardShortcut(",", modifiers: .command)
            .help(l10n("menu.settings"))
            .accessibilityLabel(l10n("menu.settings"))

            Button {
                model.commands?.quit()
            } label: {
                Image(systemName: "power")
            }
            .buttonStyle(DashboardIconButtonStyle())
            .keyboardShortcut("q", modifiers: .command)
            .help(l10n("menu.quit"))
            .accessibilityLabel(l10n("menu.quit"))
        }
        .animation(model.dashboardMotion, value: model.updates.availableRelease?.tag)
        .animation(model.dashboardMotion, value: model.report.indicatorShown)
    }
}

/// "Update 1.2.0": opens the update window (UPDATES section 4).
private struct UpdatePill: View {
    let model: AppModel
    let release: Release

    var body: some View {
        let l10n = model.l10n
        let version = release.version.description
        Button {
            model.commands?.showUpdate()
        } label: {
            HStack(spacing: 6) {
                Circle()
                    .fill(Color.accentColor)
                    .frame(width: 6, height: 6)
                Text(l10n("update.available.short", version))
                    .monospacedDigit()
                    .lineLimit(1)
            }
        }
        .buttonStyle(DashboardPillButtonStyle(isOn: true))
        .help(l10n("update.menu", version))
        .accessibilityLabel(l10n("update.menu", version))
    }
}

/// The one-paragraph explanation of how speed is measured.
struct MetricInfoView: View {
    let model: AppModel

    var body: some View {
        let l10n = model.l10n
        VStack(alignment: .leading, spacing: Spacing.xs) {
            Text(l10n("mac.dashboard.info"))
                .font(.system(size: 13, weight: .semibold))
            HStack(spacing: 4) {
                Text(l10n("settings.metric"))
                    .foregroundStyle(.dashboardSecondary)
                Text(l10n(model.settings.metric.titleKey))
            }
            .font(.system(size: 11, weight: .medium))
            Text(l10n("metric.info"))
                .font(.system(size: 12))
                .foregroundStyle(.dashboardSecondary)
                .lineSpacing(2.5)
                .fixedSize(horizontal: false, vertical: true)
                .padding(.top, 2)
            // The cost explanation follows as a second paragraph (DESIGN 9).
            Text(l10n("cost.info"))
                .font(.system(size: 12))
                .foregroundStyle(.dashboardSecondary)
                .lineSpacing(2.5)
                .fixedSize(horizontal: false, vertical: true)
                .padding(.top, 4)
        }
        .padding(Spacing.m)
        .frame(width: 300, alignment: .leading)
        .environment(\.locale, model.l10n.locale)
    }
}
