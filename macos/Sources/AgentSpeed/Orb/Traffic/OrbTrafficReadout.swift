import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit

/// The orb's traffic readout (DESIGN 10, Floating orb readout): two small lines, download over upload, each an arrow,
/// a number right-aligned in a cell as wide as `8.88` and a unit left-aligned in a cell as wide as `MB/s`, sharing
/// their left edge and centered on the orb. Characters are replaced in their cells once per sample (no animation);
/// the block fades by the quiet rule and steps aside for the new-peak pill. Drawn from the shared glyph atlas, so
/// drawing never allocates once the digits of a size exist.
@MainActor
final class OrbTrafficReadout {
    /// The readout fades out (and back) around the new-peak pill (DESIGN 10).
    static let pillFade: TimeInterval = 0.15

    let textCache: OrbTextCache

    init(textCache: OrbTextCache = .shared) {
        self.textCache = textCache
    }

    /// Colors of the two kinds of text: number, and arrow plus unit.
    struct Palette {
        var number: RGBColor
        var secondary: RGBColor
        var shadow: RGBColor?
        var shadowRadius: CGFloat = 0

        /// DESIGN 10: number at 80 % of the value token, arrow and unit in the secondary token; Increase Contrast 92 / 70 %.
        static func standard(dark: Bool, increaseContrast: Bool) -> Palette {
            let ink = dark ? RGBColor.white : RGBColor(hex: 0x0B0D12)
            if increaseContrast { return Palette(number: ink.opacity(0.92), secondary: ink.opacity(0.70)) }
            return Palette(number: ink.opacity(dark ? 0.78 : 0.76), secondary: (dark ? RGBColor.white : .black).opacity(0.50))
        }

        /// Under the liquid surface (2a.3 layer 10 split rule).
        static func underLiquid(dark: Bool, diameter s: CGFloat, increaseContrast: Bool) -> Palette {
            if dark {
                return Palette(number: .white.opacity(increaseContrast ? 0.97 : 0.94), secondary: .white.opacity(increaseContrast ? 0.80 : 0.72),
                               shadow: .black.opacity(0.55), shadowRadius: 0.015 * s)
            }
            let ink = RGBColor(hex: 0x0B0D12)
            return Palette(number: ink.opacity(increaseContrast ? 0.94 : 0.88), secondary: ink.opacity(increaseContrast ? 0.72 : 0.62))
        }
    }

    private func numberStyle(_ t: CGFloat) -> OrbTextStyle {
        OrbTextStyle(size: t, weight: .semibold, monospacedDigits: true, padding: max(1, 0.03 * t))
    }

    private func unitStyle(_ t: CGFloat) -> OrbTextStyle {
        OrbTextStyle(size: max(OrbReadoutBuilder.textFloor * OrbTrafficPlacement.unitScale, OrbTrafficPlacement.unitScale * t),
                     weight: .medium, padding: max(1, 0.03 * t))
    }

    // MARK: Drawing

    /// The block's opacity at the frame: the quiet rule, around the new-peak pill (where the pill shares the area).
    func alpha(_ reading: OrbTrafficReading, _ frame: OrbFrame, yieldsToPill: Bool) -> Double {
        var alpha = reading.alpha(at: frame.time, reduceMotion: frame.effects.reduceMotion)
        if yieldsToPill, frame.effects.celebrate, frame.peakAge >= 0 {
            let age = frame.peakAge
            let fade = frame.effects.reduceMotion ? 0.001 : Self.pillFade
            let total = OrbReadoutBuilder.peakDuration
            if age < total {
                alpha *= 1 - min(1, age / fade)
            } else if age < total + fade {
                alpha *= (age - total) / fade
            }
        }
        return alpha
    }

    /// The readout stack's shift at the frame: follows the block's quiet-rule fade (eased), so the value glides down as
    /// the block appears and back when it has faded; never moves for the new-peak pill.
    func stackShift(_ frame: OrbFrame, placement: OrbTrafficPlacement, layout: OrbReadoutLayout) -> CGFloat {
        guard let reading = frame.content.traffic, !frame.unavailable,
              OrbTrafficMetrics.fits(placement, diameter: frame.s, bits: reading.bits) else { return 0 }
        let shown = reading.alpha(at: frame.time, reduceMotion: frame.effects.reduceMotion)
        return placement.stackShift(layout: layout, diameter: frame.s) * CGFloat(Easing.smoothstep(0, 1, shown))
    }

    func add(into scene: inout OrbScene, _ frame: OrbFrame, placement: OrbTrafficPlacement, palette: Palette,
             clip: OrbTextClip = .none, yieldsToPill: Bool = true) {
        guard let reading = frame.content.traffic, !frame.unavailable else { return }
        let alpha = alpha(reading, frame, yieldsToPill: yieldsToPill) * frame.alpha
        guard alpha > 0.01 else { return }
        let s = frame.s
        let scale = scene.backingScale
        let t = placement.textSize(diameter: s)
        let cells = OrbTrafficMetrics.cells(size: t, bits: reading.bits)
        let width = OrbTrafficMetrics.lineWidth(size: t, bits: reading.bits)
        guard width <= placement.fit * s else { return }
        let left = frame.center.x - width / 2
        let lines = placement.baselines(center: frame.center.y + frame.readoutShift, diameter: s)
        let gap = OrbTrafficPlacement.gap * t
        let numberRight = left + OrbTrafficPlacement.arrowWidth * t + gap + cells.number
        for (parts, baseline, down) in [(reading.rx, lines.down, true), (reading.tx, lines.up, false)] {
            if let arrow = textCache.arrowTexture(down: down, size: t, scale: scale) {
                scene.add(OrbTextDraw(arrow, x: left, baseline: baseline, color: palette.secondary.opacity(alpha),
                                      glow: palette.shadow?.opacity(alpha), glowRadius: palette.shadowRadius, clip: clip))
            }
            var advance: CGFloat = 0
            for character in parts.number {
                advance += textCache.texture(character, style: numberStyle(t), language: .en, scale: scale)?.advance ?? 0
            }
            var x = numberRight - advance
            for character in parts.number {
                guard let glyph = textCache.texture(character, style: numberStyle(t), language: .en, scale: scale) else { continue }
                scene.add(OrbTextDraw(glyph, x: x, baseline: baseline, color: palette.number.opacity(alpha),
                                      glow: palette.shadow?.opacity(alpha), glowRadius: palette.shadowRadius, clip: clip))
                x += glyph.advance
            }
            if let unit = textCache.texture(parts.unit, style: unitStyle(t), language: .en, scale: scale) {
                scene.add(OrbTextDraw(unit, x: numberRight + gap, baseline: baseline, color: palette.secondary.opacity(alpha),
                                      glow: palette.shadow?.opacity(alpha), glowRadius: palette.shadowRadius, clip: clip))
            }
        }
    }
}
