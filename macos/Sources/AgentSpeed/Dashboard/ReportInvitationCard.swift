import AgentSpeedCore
import SwiftUI

/// The dashboard invitation (DESIGN 11, Dashboard invitation): offered once, at the top of the dashboard, for the
/// built-in preset. It names the service, its host and the public username from what the app itself knows — nothing
/// is fetched or created before `report.invite.accept` — and offers the preview. After the click it shows
/// `report.invite.done` for a moment before the dashboard collapses it.
struct ReportInvitationCard: View {
    let model: AppModel

    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.colorSchemeContrast) private var contrast

    private static let corner: CGFloat = 12
    private static let padding: CGFloat = 12

    var body: some View {
        let l10n = model.l10n
        let done = model.report.inviteJustAccepted
        let shape = RoundedRectangle(cornerRadius: Self.corner, style: .continuous)
        Group {
            if done {
                doneLine
                    .transition(.opacity)
            } else {
                offer
                    .transition(.opacity)
            }
        }
        .padding(Self.padding)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background { shape.fill(Color.accentColor.opacity(colorScheme == .dark ? 0.14 : 0.08)) }
        .overlay {
            shape.strokeBorder(Color.accentColor.opacity(contrast == .increased ? 0.4 : 0.2), lineWidth: 0.5)
        }
        .overlay(alignment: .topTrailing) {
            if !done { closeButton }
        }
        .animation(model.dashboardResizeMotion ?? Motion.fade, value: done)
        .accessibilityElement(children: .contain)
        .accessibilityLabel(l10n("report.invite.title"))
    }

    // MARK: - Offer

    private var offer: some View {
        let l10n = model.l10n
        let report = model.report
        let host = ReportPreset.codexRadarDomain ?? report.selectedService?.authority ?? ""
        return VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .center, spacing: Spacing.xs) {
                badge
                Text(l10n("report.invite.title"))
                    .font(.system(size: 13, weight: .semibold))
                    .fixedSize(horizontal: false, vertical: true)
                    .accessibilityAddTraits(.isHeader)
            }
            .padding(.trailing, 20)
            Text(l10n("report.invite.body", ReportPreset.codexRadarName, host))
                .font(.system(size: 12))
                .foregroundStyle(contrast == .increased ? AnyShapeStyle(.primary) : AnyShapeStyle(.secondary))
                .lineSpacing(1.5)
                .lineLimit(4)
                .fixedSize(horizontal: false, vertical: true)
                .padding(.top, 8)
            Text(usernameLine)
                .font(.system(size: 11))
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
                .padding(.top, 4)
            buttons
                .padding(.top, 10)
        }
    }

    /// The broadcast glyph on an accent badge, as in the consent sheet's header.
    private var badge: some View {
        Image(systemName: "dot.radiowaves.left.and.right")
            .font(.system(size: 10.5, weight: .semibold))
            .foregroundStyle(.white)
            .frame(width: 22, height: 22)
            .background(Color.accentColor.gradient, in: RoundedRectangle(cornerRadius: 6, style: .continuous))
            .accessibilityHidden(true)
    }

    /// `report.invite.username` with the name in the label color, isolated so a right-to-left name cannot reorder
    /// the sentence around it.
    private var usernameLine: AttributedString {
        let name = "\u{2068}\(model.report.fieldUsername)\u{2069}"
        var line = AttributedString(model.l10n("report.invite.username", name))
        if let range = line.range(of: name) {
            line[range].foregroundColor = .primary
            line[range].font = .system(size: 11, weight: .medium)
        }
        return line
    }

    private var buttons: some View {
        let l10n = model.l10n
        let report = model.report
        return HStack(spacing: Spacing.xs) {
            Button {
                model.commands?.showReportPreview()
            } label: {
                HStack(spacing: 3) {
                    Image(systemName: "eye")
                        .font(.system(size: 10.5, weight: .medium))
                    Text(l10n("report.invite.preview"))
                }
                .font(.system(size: 12))
                .foregroundStyle(Color.accentColor)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .help(l10n("report.preview.title"))
            .accessibilityLabel(l10n("report.invite.preview"))
            Spacer(minLength: Spacing.xs)
            Button(l10n("report.invite.decline")) {
                report.declineInvitation()
            }
            .controlSize(.small)
            .buttonStyle(.bordered)
            .fixedSize()
            Button(l10n("report.invite.accept")) {
                report.acceptInvitation()
                AccessibilityNotification.Announcement(l10n("report.invite.done")).post()
            }
            .controlSize(.small)
            .buttonStyle(.borderedProminent)
            .fixedSize()
        }
    }

    /// Same as `report.invite.decline`: answered, nothing else happens.
    private var closeButton: some View {
        let l10n = model.l10n
        return Button {
            model.report.declineInvitation()
        } label: {
            Image(systemName: "xmark")
                .font(.system(size: 9, weight: .bold))
                .frame(width: 18, height: 18)
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .foregroundStyle(.dashboardSecondary)
        .help(l10n("report.invite.decline"))
        .accessibilityLabel(l10n("report.invite.decline"))
        .padding(.top, 8)
        .padding(.trailing, 8)
    }

    // MARK: - Done

    private var doneLine: some View {
        HStack(alignment: .firstTextBaseline, spacing: 6) {
            Image(systemName: "checkmark.circle.fill")
                .font(.system(size: 12, weight: .semibold))
                .foregroundStyle(Color.accentColor)
                .accessibilityHidden(true)
            Text(model.l10n("report.invite.done"))
                .font(.system(size: 12, weight: .medium))
                .fixedSize(horizontal: false, vertical: true)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}
