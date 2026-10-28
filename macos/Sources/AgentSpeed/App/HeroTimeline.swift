import AgentSpeedCore
import AgentSpeedHero
import AppKit
import SwiftUI

/// Drives the real gauge, embedded orb animator and hero vehicle on a simulated clock, 60 steps per second, and renders
/// stills of the dashboard hero (previews and the frame harness). Deterministic for a given script.
@MainActor
final class HeroTimeline {
    static let orbDiameter = DashboardLayout.orbDiameter

    let model: AppModel
    let animator = OrbAnimator(surface: .embedded)
    private var scene = OrbScene(capacity: 1024)
    private(set) var time: TimeInterval
    private(set) var value = 0.0
    var dark = true
    var increaseContrast = false {
        didSet { hero?.overrides.increaseContrast = increaseContrast ? true : nil }
    }

    init(model: AppModel, start: TimeInterval = Clock.now) {
        self.model = model
        time = start
    }

    var hero: HeroVehicleDriver? { animator.hero }

    /// Publishes sample data at `value`, snaps the gauge to it and restarts the hero from its steady state.
    func reset(value: Double) {
        self.value = value
        let snapshot = PreviewData.snapshot(speed: value, redline: model.settings.redline)
        model.store.publishLive(snapshot)
        model.gauge.redline = model.settings.redline
        model.gauge.snap(to: value, now: time)
        hero?.reset()
        animator.badge?.reset()
        step()
    }

    /// Runs to `end`, feeding `value(t)` to the gauge on every step.
    func run(until end: TimeInterval, value: (TimeInterval) -> Double) {
        while time < end - 1e-9 {
            time = min(end, time + 1.0 / 60)
            self.value = value(time)
            model.gauge.ingest(self.value, now: time)
            step()
        }
    }

    func run(for duration: TimeInterval, at value: Double) {
        run(until: time + duration) { _ in value }
    }

    /// Runs until the title changes (or `limit` passes) at a constant value; returns the change time.
    @discardableResult
    func runUntilTitleChange(at value: Double, limit: TimeInterval = 5) -> TimeInterval? {
        let start = model.gauge.title
        let end = time + limit
        while time < end {
            run(until: time + 1.0 / 60) { _ in value }
            if model.gauge.title != start { return model.gauge.titleChangedAt }
        }
        return nil
    }

    private func step() {
        OrbSceneBuilder.build(into: &scene, content: content(), animator: animator, model: model, feed: model.mainFeed,
                              canvasSide: Self.orbDiameter * OrbView.canvasScale, scale: 2, time: time)
    }

    func content() -> OrbContent {
        let l10n = model.l10n
        let snapshot = model.store.snapshot
        return OrbContent(
            diameter: Self.orbDiameter,
            tokens: dark ? .dark : .light,
            increaseContrast: increaseContrast,
            unit: l10n("unit.tokS"),
            titleLabel: model.titles.label(model.gauge.title, in: model.titles.general, l10n: l10n),
            previousTitleLabel: model.titles.label(model.gauge.previousTitle, in: model.titles.general, l10n: l10n),
            infoLine: "",
            peakLabel: l10n("peak.new"),
            latinTracking: l10n.language.usesLatinCase,
            language: l10n.language,
            generating: snapshot.agents.filter { $0.phase == .generating }.map(\.source),
            showsReadout: false
        )
    }

    /// The embedded orb at the current time with room for its aura (`cell` points square, @2x).
    func orbImage(cell: CGFloat = 140) -> CGImage? {
        render(orbView().padding((cell - Self.orbDiameter) / 2))
    }

    func orbView() -> some View {
        OrbView(model: model, diameter: Self.orbDiameter, surface: .embedded, animator: animator, time: time)
            .appEnvironment(model)
            .environment(\.colorScheme, dark ? .dark : .light)
    }

    func render<V: View>(_ view: V) -> CGImage? {
        let renderer = ImageRenderer(content: view)
        renderer.scale = 2
        renderer.isOpaque = false
        return renderer.cgImage
    }
}
