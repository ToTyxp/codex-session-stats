import AgentSpeedCore
import AgentSpeedOrbModel
import SwiftUI

/// "Speed titles" over an aligned grid of every title that scales with the redline and the speed it starts at
/// (DESIGN section 5, Redline speed): four columns of three rows where they fit, else three or two, so the twelve
/// titles always fill whole rows. Each column is as wide as its longest entry, and every value reserves the widest
/// text it can take over the slider range, so dragging the redline changes the numbers but never the layout.
/// Hovering or focusing a title highlights its tick on the heat strip.
struct TitleLadderList: View {
    let redline: Double
    let metric: SpeedMetric
    let l10n: Localizer
    @Binding var highlighted: SpeedTitle?
    /// Names the titles (DESIGN 1, Title themes): presets in the current language, custom text as typed.
    var theme: TitleTheme = .preset(.vehicles)

    @State private var hovered: SpeedTitle?
    @FocusState private var focused: SpeedTitle?
    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.colorSchemeContrast) private var contrast

    private static let titleFont = Font.subheadline
    private static let valueFont = Font.subheadline.weight(.semibold).monospacedDigit()
    /// Column counts that divide the twelve titles into full rows, widest first.
    private static let columnChoices = [4, 3, 2]

    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            Text(l10n("title.ladder"))
                .font(Self.titleFont.weight(.semibold))
                .foregroundStyle(.secondary)
                .lineLimit(1)
                .accessibilityAddTraits(.isHeader)
            ViewThatFits(in: .horizontal) {
                ForEach(Self.columnChoices, id: \.self) { columns in
                    grid(columns: columns)
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .accessibilityElement(children: .contain)
        .accessibilityLabel(l10n("title.ladder"))
        .onChange(of: hovered) { publish() }
        .onChange(of: focused) { publish() }
    }

    private func grid(columns: Int) -> some View {
        let titles = SpeedTitle.scaledLadder
        let rows = stride(from: 0, to: titles.count, by: columns).map { Array(titles[$0..<min($0 + columns, titles.count)]) }
        return Grid(alignment: .leading, horizontalSpacing: 20, verticalSpacing: 4) {
            ForEach(rows, id: \.self) { row in
                GridRow {
                    ForEach(row, id: \.self) { title in
                        entry(for: title)
                    }
                }
            }
        }
        .fixedSize(horizontal: true, vertical: false)
    }

    private func publish() {
        let next = hovered ?? focused
        if highlighted != next { highlighted = next }
    }

    private func name(_ title: SpeedTitle) -> String {
        if case .custom(let set) = theme, !set.field(level: title.rawValue).isEmpty { return set.field(level: title.rawValue) }
        return theme.text(level: title.rawValue, localized: { l10n($0) }).capitalized(with: l10n.locale)
    }

    private func entry(for title: SpeedTitle) -> some View {
        let enter = title.enterSpeed(redline: redline)
        let value = TitleLadderValues.text(enter)
        let heat = HeatPalette.heat(forFraction: title.threshold)
        let isHighlighted = highlighted == title
        let valueColor = HeatText.color(heat, dark: colorScheme == .dark, increasedContrast: contrast == .increased)
        let nameStyle: AnyShapeStyle = isHighlighted || contrast == .increased ? AnyShapeStyle(.primary) : AnyShapeStyle(.secondary)
        return HStack(alignment: .firstTextBaseline, spacing: 0) {
            Text(name(title))
                .foregroundStyle(nameStyle)
            Spacer(minLength: 8)
            // The value keeps the width of its widest form, so the columns never shift while the slider moves.
            ZStack(alignment: .trailing) {
                ForEach(TitleLadderValues.shapes(for: title, metric: metric), id: \.self) { shape in
                    Text(verbatim: shape).hidden()
                }
                Text(verbatim: value)
                    .foregroundStyle(valueColor)
            }
            .font(Self.valueFont)
        }
        .font(Self.titleFont)
        .lineLimit(1)
        .background {
            RoundedRectangle(cornerRadius: 4, style: .continuous)
                .fill(Color.primary.opacity(isHighlighted ? 0.07 : 0))
                .padding(.horizontal, -4)
                .padding(.vertical, -1)
        }
        .contentShape(Rectangle())
        .onHover { inside in
            if inside {
                hovered = title
            } else if hovered == title {
                hovered = nil
            }
        }
        .focusable(interactions: .activate)
        .focused($focused, equals: title)
        .focusEffectDisabled()
        .animation(Motion.fade, value: isHighlighted)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("\(name(title)), \(l10n("mac.settings.speedValue", value))")
    }
}

/// The digit patterns a title's threshold takes over the slider range of a metric ("0.0", "00", "0.00k", …). With
/// tabular digits, the widest of them is the widest the value ever gets.
@MainActor
enum TitleLadderValues {
    private static var cache: [String: [String]] = [:]

    static func shapes(for title: SpeedTitle, metric: SpeedMetric) -> [String] {
        let key = "\(metric.rawValue)/\(title.rawValue)"
        if let cached = cache[key] { return cached }
        var shapes: [String] = []
        for redline in redlines(for: metric) {
            let shape = pattern(text(title.enterSpeed(redline: redline)))
            if !shapes.contains(shape) { shapes.append(shape) }
        }
        cache[key] = shapes
        return shapes
    }

    /// Every redline the slider can produce for the metric.
    static func redlines(for metric: SpeedMetric, steps: Int = 400) -> [Double] {
        var values: [Double] = []
        for step in 0...steps {
            let value = SettingsLimits.redline(atPosition: Double(step) / Double(steps), metric: metric)
            if values.last != value { values.append(value) }
        }
        return values
    }

    /// The speed formatter's text, except that a whole number below 10 drops its ".0" ("Bicycle 8", as in the spec).
    static func text(_ value: Double) -> String {
        value >= 1 && value < 10 && value == value.rounded() ? String(Int(value)) : SpeedFormat.speed(value)
    }

    static func pattern(_ text: String) -> String {
        String(text.map { $0.isNumber ? "0" : $0 })
    }
}
