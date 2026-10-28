import AgentSpeedCore
import SwiftUI

/// `report.status` (DESIGN 11, Status): one line with a dot that always has text, the rank when the service gives one,
/// and Pause or Resume. Changes are announced politely, at most once a minute, except when the user must act.
struct ReportStatusSection: View {
    let model: AppModel

    @State private var lastAnnouncement = Date.distantPast
    @State private var announced: ReportStatus?

    var body: some View {
        let l10n = model.l10n
        let report = model.report
        Section {
            TimelineView(.periodic(from: .now, by: 1)) { context in
                let status = report.status
                HStack(alignment: .firstTextBaseline, spacing: Spacing.s) {
                    Circle()
                        .fill(status.color)
                        .frame(width: 8, height: 8)
                        .alignmentGuide(.firstTextBaseline) { $0[VerticalAlignment.center] + 4 }
                        .accessibilityHidden(true)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(status.text(l10n, now: context.date))
                            .monospacedDigit()
                            .fixedSize(horizontal: false, vertical: true)
                            .contentTransition(.opacity)
                        if let detail = status.detail {
                            Footnote(l10n(detail.key))
                        }
                        if let rank = report.rank, report.isOn {
                            Footnote(l10n("report.status.rank", rank))
                        }
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .accessibilityElement(children: .combine)
                    .accessibilityAddTraits(.updatesFrequently)
                    if report.isOn {
                        ReportPauseControl(model: model)
                    } else if status == .review {
                        Button(l10n("report.service.review")) { report.reviewInvitation() }
                            .controlSize(.small)
                            .fixedSize()
                    } else if Self.offersRetry(status) {
                        Button(l10n("report.service.retry")) { report.retry() }
                            .controlSize(.small)
                            .fixedSize()
                    }
                }
                .onChange(of: Self.kind(status)) { _, _ in announce(status, now: context.date) }
            }
        } header: {
            Text(l10n("report.status"))
        }
    }

    /// Setting up after the invitation: a failed fetch or registration can be tried again at once.
    private static func offersRetry(_ status: ReportStatus) -> Bool {
        switch status {
        case .service, .registerFailed: true
        default: false
        }
    }

    /// The status without its changing times, so the announcement fires on real changes only.
    private static func kind(_ status: ReportStatus) -> String {
        switch status {
        case .reporting: "reporting"
        case .paused: "paused"
        case .rateLimited: "rateLimited"
        case .retrying: "retrying"
        default: "\(status)"
        }
    }

    private func announce(_ status: ReportStatus, now: Date) {
        guard status != announced else { return }
        guard status.needsUser || now.timeIntervalSince(lastAnnouncement) >= 60 else { return }
        announced = status
        lastAnnouncement = now
        AccessibilityNotification.Announcement(status.text(model.l10n, now: now)).post()
    }
}

/// `report.pause` as a pull-down (1 hour, until resumed), or `report.resume` while paused.
struct ReportPauseControl: View {
    let model: AppModel

    var body: some View {
        let l10n = model.l10n
        let report = model.report
        if report.isPaused {
            Button(l10n("report.resume")) { report.resume() }
                .buttonStyle(.borderless)
                .font(.system(size: 12))
        } else {
            Menu(l10n("report.pause")) {
                Button(l10n("report.pause.hour")) { report.pause(for: 3600) }
                Button(l10n("report.pause.indefinite")) { report.pause(for: nil) }
            }
            .menuStyle(.borderlessButton)
            .font(.system(size: 12))
            .fixedSize()
        }
    }
}
