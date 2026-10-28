import AgentSpeedCore
import Foundation

/// Everything one menu bar frame shows. Colors are resolved when drawing, against the menu bar's own appearance.
struct StatusItemState: Equatable {
    var layout: StatusItemLayout
    var fraction: Double
    var heat: Double
    var fillOpacity: Double
    /// Glow strength around the fill, 0...1.
    var glow: Double
    var colored: Bool
    var value: String
    var idle: Bool
    var increasedContrast: Bool
    /// The money block (DESIGN 3), when shown.
    var secondary: StatusItemSecondary?
    /// The traffic block (DESIGN 10), when shown.
    var traffic: StatusItemTraffic?

    /// Half-pixel steps along the ring at 2x, so sub-pixel spring motion produces no redraws.
    static let fractionSteps = 132.0
    static let flickerRate = 6.0
    static let glowFade: TimeInterval = 0.35

    /// `valueWidth` is the room the governor reserves for the value (nil: exactly the current text).
    @MainActor
    init(model: AppModel, time: TimeInterval, valueWidth: CGFloat? = nil, secondaryWidth: CGFloat? = nil) {
        let gauge = model.gauge
        let h = gauge.heat(at: time)
        let (text, unit) = StatusItemValue.current(model)
        layout = StatusItemLayout(model: model, unit: unit, valueWidth: valueWidth ?? StatusItemLayout.valueWidth(of: text))
        fraction = (h * Self.fractionSteps).rounded() / Self.fractionSteps
        heat = h
        fillOpacity = Self.flickers(model) && gauge.tier == .redline ? (Self.flicker(at: time) * 20).rounded() / 20 : 1
        colored = model.settings.barColoredGlyph
        glow = colored ? (Self.glow(gauge: gauge, at: time) * 20).rounded() / 20 : 0
        value = text
        idle = gauge.tier == .idle
        increasedContrast = model.accessibility.increaseContrast
        secondary = StatusItemSecondary(model: model)
        if let secondaryWidth, let width = secondary?.numberWidth { secondary?.cell = max(secondaryWidth, width) }
        layout.secondaryWidth = secondary?.width ?? 0
        traffic = StatusItemTraffic(model: model)
        layout.trafficWidth = traffic?.width ?? 0
    }

    /// Whether the redline flicker may run (DESIGN section 3; off with Reduce Motion or effects off).
    @MainActor
    static func flickers(_ model: AppModel) -> Bool {
        !model.accessibility.reduceMotion && model.settings.effects != .off
    }

    /// Frames are needed while the springs move, a glow fades, or the redline flicker runs.
    @MainActor
    static func needsFrames(_ model: AppModel, at time: TimeInterval) -> Bool {
        let gauge = model.gauge
        if flickers(model) && gauge.tier == .redline { return true }
        if time - gauge.tierChangedAt < glowFade { return true }
        return !gauge.isSettled(at: time)
    }

    /// Smooth random opacity between 85 % and 100 % at 6 Hz.
    private static func flicker(at time: TimeInterval) -> Double {
        let phase = time * flickerRate
        let index = Int(phase.rounded(.down))
        let blend = Easing.smoothstep(0, 1, phase - Double(index))
        let a = Noise.hash(index, 17), b = Noise.hash(index + 1, 17)
        return 0.85 + 0.15 * (a + (b - a) * blend)
    }

    /// Fades the glow in and out across a tier change into or out of overdrive.
    @MainActor
    private static func glow(gauge: LiveGauge, at time: TimeInterval) -> Double {
        let now = gauge.tier >= .overdrive ? 1.0 : 0
        let before = gauge.previousTier >= .overdrive ? 1.0 : 0
        guard now != before else { return now }
        let progress = Easing.easeOut((time - gauge.tierChangedAt) / glowFade)
        return before + (now - before) * progress
    }
}
