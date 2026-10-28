import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit
import SwiftUI

/// One floating orb of any style on a simulated clock, deterministic for a given script: the gauge is fed like the
/// engine (a new value every 500 ms), the animator steps 60 times per second, and stills are rendered offscreen
/// (body + dynamic layers). Used by `--render-previews`, `--orb-frames` and `--orb-bench`.
@MainActor
final class OrbPreviewStage {
    let model: AppModel
    let feed = OrbFeed()
    let animator: OrbAnimator
    let interaction = OrbInteraction()
    private(set) var time: TimeInterval
    private(set) var value = 0.0
    var diameter: CGFloat
    var dark: Bool
    var increaseContrast = false
    var reduceTransparency = false
    var isThumbnail = false
    var hovered = false {
        didSet { interaction.isHovered = hovered }
    }
    var language: AppLanguage?
    /// The title theme the orb names its levels with (nil = General).
    var titleTheme: TitleTheme?
    private var nextTick: TimeInterval
    private var scene = OrbScene(capacity: 2048)
    /// The recordings' new-peak bookkeeping (`newPeakRule`).
    var recordedPeak = 0.0
    var lastRecordedPeak: TimeInterval = -.infinity
    private static var bodyCache: [String: CGImage] = [:]

    init(model: AppModel, style: OrbStyleKind, diameter: CGFloat = 148, dark: Bool = true, level: EffectsLevel = .standard,
         reduceMotion: Bool = false, start: TimeInterval = Clock.now, surface: OrbSurface = .floating) {
        self.model = model
        self.diameter = diameter
        self.dark = dark
        time = start
        nextTick = start
        animator = OrbAnimator(surface: surface, style: style)
        animator.overrides = .init(level: level, reduceMotion: reduceMotion)
        feed.gauge.redline = model.settings.redline
        feed.redlineOutput = model.settings.redline(for: .output)
        feed.set(generating: [.codex, .claudeCode, .claudeDesktop], todayTokens: 1_240_000, agentCount: 3)
        animator.attach(model: model, feed: feed, interaction: interaction)
    }

    func stop() {
        animator.detach()
    }

    var gauge: LiveGauge { feed.gauge }

    /// Jumps to `value` in the steady state of its tier and title.
    func reset(value: Double) {
        self.value = value
        gauge.snap(to: value, now: time - 3)
        animator.primeForStill(model: model, gauge: gauge)
        nextTick = time
    }

    var content: OrbContent {
        var content = OrbContentBuilder.content(model: model, feed: feed, diameter: diameter, dark: dark, contrast: increaseContrast,
                                                style: animator.style, showsReadout: true, theme: titleTheme)
        content.isThumbnail = isThumbnail
        content.reduceTransparency = reduceTransparency
        if let language { content.language = language }
        return content
    }

    /// Advances one step of `dt`, feeding `value(t)` at the engine's 500 ms ticks.
    func step(_ dt: TimeInterval = 1.0 / 60, value: (TimeInterval) -> Double) {
        time += dt
        if time >= nextTick {
            self.value = value(time)
            nextTick += 0.5
            if nextTick < time { nextTick = time + 0.5 }
        }
        gauge.ingest(self.value, now: time)
        OrbSceneBuilder.build(into: &scene, content: content, animator: animator, model: model, feed: feed,
                              canvasSide: diameter * OrbView.canvasScale, scale: 2, time: time,
                              contentScale: hovered && !model.accessibility.reduceMotion ? 1.03 : 1)
    }

    func run(for duration: TimeInterval, value: (TimeInterval) -> Double) {
        let end = time + duration
        while time < end - 1e-9 { step(value: value) }
    }

    func run(for duration: TimeInterval, at constant: Double) {
        run(for: duration) { _ in constant }
    }

    func animatorPulse(speed: Double, output: Int) {
        let strength = (log10(max(1, Double(output))) / 3.4).clamped(to: 0.3...1)
        let heat = HeatPalette.heat(value: speed, redline: feed.redlineOutput)
        animator.addPulse(at: time, strength: strength, heat: max(heat, 0.08))
    }

    // MARK: Rendering

    var canvasSide: CGFloat { diameter * OrbView.canvasScale }

    /// The scene of the last step (GPU timing).
    var lastScene: OrbScene { scene }

    /// The current frame: body and dynamic layers at @2x, `canvasSide` points square.
    func image() -> CGImage? {
        guard let dynamic = OrbSnapshot.image(scene, side: canvasSide, scale: 2) else { return nil }
        let side = canvasSide
        guard let canvas = PreviewCanvas(size: CGSize(width: side, height: side), background: .clear) else { return nil }
        canvas.context.clear(CGRect(x: 0, y: 0, width: side, height: side))
        let scale: CGFloat = hovered && !model.accessibility.reduceMotion ? 1.03 : 1
        if let body = bodyImage() {
            let size = side * scale
            canvas.draw(body, at: CGPoint(x: (side - size) / 2, y: (side - size) / 2), size: CGSize(width: size, height: size))
        }
        canvas.draw(dynamic, at: .zero)
        return canvas.image
    }

    private func bodyImage() -> CGImage? {
        let style = animator.style
        let rim = feed.bezel == nil
        let key = "\(diameter)-\(dark)-\(style.rawValue)-\(rim)-\(increaseContrast)-\(reduceTransparency)"
        if let cached = Self.bodyCache[key] { return cached }
        let body = OrbBody(diameter: diameter, canvasSide: canvasSide, tokens: dark ? .dark : .light, opacity: 1, solid: true,
                           increaseContrast: increaseContrast, style: style, rim: rim)
        let renderer = ImageRenderer(content: body.environment(\.colorScheme, dark ? .dark : .light))
        renderer.scale = 2
        renderer.isOpaque = false
        guard let image = renderer.cgImage else { return nil }
        if Self.bodyCache.count > 64 { Self.bodyCache.removeAll() }
        Self.bodyCache[key] = image
        return image
    }
}

/// The designer's prototype tour (seconds, tok/s at R = 400), sampled like the engine: a new value every 0.5 s, log-eased
/// ramps and a little organic noise.
enum OrbTour {
    static let keys: [(Double, Double)] = [
        (0, 0), (4, 3), (7, 12), (10, 30), (13, 62), (16, 110), (19, 180), (22, 260), (25, 330), (28, 430),
        (31, 580), (34, 780), (38, 620), (41, 240), (44, 60), (47, 8), (49, 0), (62, 0),
    ]
    static let length = 62.0

    static func value(at t: Double, redline: Double = 400) -> Double {
        let t = (t / 0.5).rounded(.down) * 0.5
        var v = keys[keys.count - 1].1
        for index in 0..<(keys.count - 1) where t >= keys[index].0 && t < keys[index + 1].0 {
            let ramp = min(1.2, keys[index + 1].0 - keys[index].0)
            let previous = index > 0 ? keys[index - 1].1 : keys[index].1
            let current = keys[index].1
            let u = min(1, (t - keys[index].0) / ramp)
            let eased = u * u * (3 - 2 * u)
            v = previous > 0 && current > 0 ? exp(log(previous) + (log(current) - log(previous)) * eased) : previous + (current - previous) * eased
            break
        }
        guard v > 0 else { return 0 }
        let noise = 0.035 * sin(t * 2.1) + 0.025 * sin(t * 5.3 + 1) + 0.015 * sin(t * 11.7 + 2)
        return v * (1 + noise) * redline / 400
    }
}
