import AgentSpeedCore
import AppKit

/// The menu bar traffic block (DESIGN 10, Menu bar and taskbar): after the value, `↓ 1.28 MB/s` over `↑ 64.0 KB/s`
/// in 9 pt tabular text, numbers in the label color at 85 %, arrows and units secondary, in fixed cells so the item's
/// width never changes with the values. It stays visible at `0 B/s`.
struct StatusItemTraffic: Equatable {
    static let size: CGFloat = 9
    static let gap: CGFloat = 8
    static let pitch: CGFloat = 10
    static let arrowGap: CGFloat = 2
    static let unitGap: CGFloat = 2.5

    /// nil reads `–` (no sample yet, or per-agent traffic unavailable).
    var rx: TrafficFormat.Parts?
    var tx: TrafficFormat.Parts?
    var bits: Bool

    static var font: NSFont { .monospacedDigitSystemFont(ofSize: size, weight: .medium) }

    @MainActor
    init?(model: AppModel) {
        let settings = model.settings
        guard settings.trafficEnabled, settings.barTraffic != .off, settings.barMode != .glyph else { return nil }
        let snapshot = model.visibleTraffic
        let rate: TrafficRate? = settings.barTraffic == .machine
            ? snapshot.machineLive
            : snapshot.live(of: nil, includeTools: settings.trafficIncludeTools)
        bits = settings.trafficUnit == .bits
        rx = rate.map { TrafficFormat.rateParts($0.rx, bits: bits) }
        tx = rate.map { TrafficFormat.rateParts($0.tx, bits: bits) }
    }

    /// Widths of the arrow, the `8.88` number cell and the `MB/s` unit cell.
    static func cells(bits: Bool) -> (arrow: CGFloat, number: CGFloat, unit: CGFloat) {
        func width(_ text: String) -> CGFloat { ceil((text as NSString).size(withAttributes: [.font: font]).width) }
        return (width("↓"), width("8.88"), width(bits ? "Mb/s" : "MB/s"))
    }

    /// The block's width, gap included.
    var width: CGFloat {
        let cells = Self.cells(bits: bits)
        return Self.gap + cells.arrow + Self.arrowGap + cells.number + Self.unitGap + cells.unit
    }

    func draw(at x: CGFloat, height: CGFloat, palette: StatusItemPalette) {
        let font = Self.font
        let cells = Self.cells(bits: bits)
        let first = (height / 2 - Self.pitch / 2 + font.capHeight / 2).rounded()
        let left = x + Self.gap
        for (parts, down, baseline) in [(rx, true, first), (tx, false, first + Self.pitch)] {
            func draw(_ text: String, at x: CGFloat, color: RGBColor) {
                NSAttributedString(string: text, attributes: [.font: font, .foregroundColor: color.nsColor])
                    .draw(at: CGPoint(x: x, y: baseline - font.ascender))
            }
            draw(down ? "↓" : "↑", at: left, color: palette.secondary)
            let number = parts?.number ?? "–"
            let numberWidth = (number as NSString).size(withAttributes: [.font: font]).width
            let numberRight = left + cells.arrow + Self.arrowGap + cells.number
            draw(number, at: numberRight - numberWidth, color: parts == nil ? palette.secondary : palette.primary.opacity(0.85))
            if let unit = parts?.unit { draw(unit, at: numberRight + Self.unitGap, color: palette.secondary) }
        }
    }
}
