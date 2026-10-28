import AgentSpeedCore
import SwiftUI

/// The dashboard footer's quiet sign that live reporting is on — also while it is being set up (DESIGN 11, Dashboard
/// footer): static, secondary, 40 % while paused, an amber dot when the user must act; a click opens Settings → API.
struct ReportIndicator: View {
    let model: AppModel

    var body: some View {
        let l10n = model.l10n
        let report = model.report
        let attention = report.status.needsUser
        let paused = report.isPaused
        let label = attention
            ? l10n("report.indicator.attention")
            : paused ? l10n("report.menu.paused")
            : l10n("report.indicator", report.fieldUsername, report.destinationHost ?? "")
        Button {
            model.commands?.showSettings(.report)
        } label: {
            Image(systemName: "dot.radiowaves.left.and.right")
                .foregroundStyle(.dashboardSecondary)
                .opacity(paused && !attention ? 0.4 : 1)
                .overlay(alignment: .topTrailing) {
                    if attention {
                        Circle()
                            .fill(Color.orange)
                            .frame(width: 5, height: 5)
                            .overlay { Circle().strokeBorder(Color(nsColor: .windowBackgroundColor), lineWidth: 1) }
                            .offset(x: 2, y: -1)
                            .accessibilityHidden(true)
                    }
                }
        }
        .buttonStyle(DashboardIconButtonStyle())
        .help(label)
        .accessibilityLabel(label)
    }
}
