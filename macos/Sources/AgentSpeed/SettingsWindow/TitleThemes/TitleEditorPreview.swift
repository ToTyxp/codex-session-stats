import AgentSpeedCore
import AgentSpeedOrbModel
import SwiftUI

/// The scripted feeds of the editor's live previews (DESIGN 5, Title themes editor): they show the focused row's level
/// at 1.05 × its threshold (idle: idle), with the real transition when the level changes; level 13 with no focus.
@MainActor
@Observable
final class TitleEditorPreviewFeeds {
    let hero = OrbFeed()
    let orb = OrbFeed()
    private(set) var level: SpeedTitle = .rocket
    /// Frames per second: the style cards' rate while a level change plays, the calmer steady rate once it settled.
    private(set) var rate = TitleEditorPreview.steadyRate
    @ObservationIgnored private var settleToken = 0

    init() {
        for feed in [hero, orb] {
            feed.set(generating: [.claudeCode, .codex], todayTokens: 1_240_000, agentCount: 2)
        }
    }

    /// Shows `level` at the current redline; `animated` false jumps there without a transition.
    func show(_ level: SpeedTitle, redline: Double, animated: Bool) {
        let value = level == .idle ? 0 : level.enterSpeed(redline: redline) * 1.05
        let now = Clock.now
        for feed in [hero, orb] {
            if feed.gauge.redline != redline { feed.gauge.redline = redline }
            feed.redlineOutput = redline
            if animated {
                feed.gauge.show(level, value: value, now: now)
            } else {
                feed.gauge.snap(to: value, title: level, now: now - 3)
            }
        }
        self.level = level
        if animated { playTransition() }
    }

    private func playTransition() {
        if rate != TitleEditorPreview.transitionRate { rate = TitleEditorPreview.transitionRate }
        settleToken &+= 1
        let token = settleToken
        DispatchQueue.main.asyncAfter(deadline: .now() + TitleEditorPreview.transitionTime) { [weak self] in
            guard let self, self.settleToken == token else { return }
            self.rate = TitleEditorPreview.steadyRate
        }
    }
}

/// The preview column: the dashboard hero at 112 pt (title badge, or the vehicle for a Vehicles-based set) and a
/// floating orb at 96 pt in the style of the orb selected in the 懸浮窗 pane, both Standard effects and only while
/// visible: at the style cards' rate while a level change plays, at a calmer rate once settled (both ≤ 30 fps, within
/// the pane's 3 % budget).
struct TitleEditorPreview: View {
    let model: AppModel
    let feeds: TitleEditorPreviewFeeds
    let theme: TitleTheme
    let active: Bool

    static let heroDiameter: CGFloat = 112
    static let orbDiameter: CGFloat = 96
    static let transitionRate = OrbStyleCards.cardRate
    static let steadyRate = 15.0
    /// The longest transition a level change plays (the new-peak flash).
    static let transitionTime = OrbAnimator.peakDuration + 0.4

    @Environment(\.orbStillTime) private var stillTime

    private var orbStyle: OrbStyleKind {
        let orbs = model.orbs
        return orbs.orb(orbs.settingsSelection ?? "")?.style ?? orbs.orbs.first?.style ?? .led
    }

    var body: some View {
        let l10n = model.l10n
        let title = model.titles.text(feeds.level, in: theme, l10n: l10n)
        VStack(alignment: .leading, spacing: Spacing.xs) {
            Text(l10n("titles.custom.preview"))
                .font(.system(size: 11, weight: .semibold))
                .foregroundStyle(.secondary)
            card(caption: l10n("mac.titles.preview.dashboard")) {
                Group {
                    if active || stillTime != nil {
                        OrbView(model: model, feed: feeds.hero, diameter: Self.heroDiameter, surface: .embedded, frameCap: Int(feeds.rate))
                    } else {
                        Color.clear
                    }
                }
                .frame(width: Self.heroDiameter, height: Self.heroDiameter)
                .padding(.vertical, 22)
            }
            card(caption: l10n("orbs.row.detail", l10n("settings.orb"), Int(Self.orbDiameter))) {
                OrbPreviewView(model: model, feed: feeds.orb, style: orbStyle, diameter: Self.orbDiameter, cap: feeds.rate,
                               active: active, theme: theme)
                    .frame(width: Self.orbDiameter * 1.3, height: Self.orbDiameter * 1.3)
            }
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(l10n("titles.custom.preview"))
        .accessibilityValue(title)
        .accessibilityAddTraits(.isImage)
    }

    private func card<Content: View>(caption: String, @ViewBuilder content: () -> Content) -> some View {
        VStack(spacing: Spacing.xxs) {
            content()
            Text(caption)
                .font(.system(size: 11))
                .foregroundStyle(.secondary)
                .lineLimit(1)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, Spacing.xs)
        .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill(Color.primary.opacity(0.045)))
        .clipShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
    }
}
