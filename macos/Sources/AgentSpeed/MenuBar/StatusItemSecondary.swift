import AgentSpeedCore
import AppKit

/// The menu bar money block (DESIGN 3) after a tok/s value: `即時 0.43 CN¥/s` over `今日 128.40 CN¥` in the traffic
/// block's 9 pt tabular text, labels and units in the secondary color, numbers in the label color at 85 %. The numbers
/// sit right-aligned in a cell the renderer's width governor sizes (grows at once, shrinks after a delay), so the item
/// never jitters while the numbers change.
struct StatusItemSecondary: Equatable {
    static let size: CGFloat = 9
    static let gap: CGFloat = 8
    static let pitch: CGFloat = 10
    static let labelGap: CGFloat = 3
    static let unitGap: CGFloat = 2.5

    struct Line: Equatable {
        var label: String
        var number: String
        var unit: String
        /// Nothing priced or zero: the number takes the secondary color.
        var dim: Bool
    }

    var live: Line
    var today: Line
    /// Room for the numbers; at least the wider one's drawn width.
    var cell: CGFloat = 0
    var spoken: String

    static var font: NSFont { .monospacedDigitSystemFont(ofSize: size, weight: .medium) }

    @MainActor
    init?(model: AppModel) {
        let settings = model.settings
        guard settings.barSecondary, settings.liveUnit == .tokens, settings.barMode.showsValue else { return nil }
        let l10n = model.l10n
        let cost = model.cost
        let money = StatusItemValue.liveMoney(model)
        live = Line(label: l10n("bar.money.now"),
                    number: money.unknown ? "–" : (money.atLeast ? "≥" : "") + money.number,
                    unit: money.unit,
                    dim: money.unknown || money.value == 0)
        let summary = TodayCostSummary(model: model)
        let unknown = summary.total.isUnknown(requests: summary.requests)
        let amount = cost.amount(microUsd: summary.requests == 0 ? 0 : summary.total.costMicroUsd).number
        let atLeast = summary.requests > 0 && summary.total.unpricedRequests > 0
        today = Line(label: l10n("bar.money.today"),
                     number: unknown ? "–" : (atLeast ? "≥" : "") + amount,
                     unit: cost.symbol,
                     dim: unknown || summary.total.costMicroUsd == 0)
        spoken = l10n("a11y.bar.money", money.spokenAmount, summary.spoken)
        cell = numberWidth
    }

    private static func width(_ text: String) -> CGFloat {
        ceil((text as NSString).size(withAttributes: [.font: font]).width)
    }

    /// Drawn width of the wider number. Digits are tabular, so it only changes with the digit count or format.
    var numberWidth: CGFloat { max(Self.width(live.number), Self.width(today.number)) }

    private var labelWidth: CGFloat { max(Self.width(live.label), Self.width(today.label)) }

    /// The block's width, gap included.
    var width: CGFloat {
        Self.gap + labelWidth + Self.labelGap + cell + Self.unitGap + max(Self.width(live.unit), Self.width(today.unit))
    }

    func draw(at x: CGFloat, height: CGFloat, palette: StatusItemPalette) {
        let font = Self.font
        let first = (height / 2 - Self.pitch / 2 + font.capHeight / 2).rounded()
        let left = x + Self.gap
        let numberRight = left + labelWidth + Self.labelGap + cell
        for (line, baseline) in [(live, first), (today, first + Self.pitch)] {
            func draw(_ text: String, at x: CGFloat, color: RGBColor) {
                NSAttributedString(string: text, attributes: [.font: font, .foregroundColor: color.nsColor])
                    .draw(at: CGPoint(x: x, y: baseline - font.ascender))
            }
            draw(line.label, at: left, color: palette.secondary)
            let numberWidth = (line.number as NSString).size(withAttributes: [.font: font]).width
            draw(line.number, at: numberRight - numberWidth, color: line.dim ? palette.secondary : palette.primary.opacity(0.85))
            draw(line.unit, at: numberRight + Self.unitGap, color: palette.secondary)
        }
    }
}
