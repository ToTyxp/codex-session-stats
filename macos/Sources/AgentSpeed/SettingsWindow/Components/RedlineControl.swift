import AgentSpeedCore
import AgentSpeedOrbModel
import SwiftUI

/// Redline of the selected metric: title and live value, a full-width slider, and the heat strip it calibrates with a
/// tick where each speed title begins. The slider moves along a 0…1 position: linear for output speed, logarithmic
/// for total throughput.
struct RedlineControl: View {
    /// The redline being edited (the General one, or a floating orb's own) and the metric it belongs to.
    let redline: Binding<Double>
    let metric: SpeedMetric
    let l10n: Localizer
    var reduceMotion = false
    /// The title hovered or focused in the ladder below; its tick is emphasized.
    var highlightedTitle: SpeedTitle?
    /// The row title; the General redline's by default.
    var title: String?

    /// The General redline of the selected metric.
    init(settings: AppSettings, l10n: Localizer, reduceMotion: Bool = false, highlightedTitle: SpeedTitle? = nil) {
        self.init(
            redline: Binding(get: { settings.redline }, set: { settings.redline = $0 }),
            metric: settings.metric,
            l10n: l10n,
            reduceMotion: reduceMotion,
            highlightedTitle: highlightedTitle
        )
    }

    init(redline: Binding<Double>, metric: SpeedMetric, l10n: Localizer, reduceMotion: Bool = false,
         highlightedTitle: SpeedTitle? = nil, title: String? = nil) {
        self.title = title
        self.redline = redline
        self.metric = metric
        self.l10n = l10n
        self.reduceMotion = reduceMotion
        self.highlightedTitle = highlightedTitle
    }

    var body: some View {
        let title = self.title ?? l10n("settings.redline")
        let valueText = l10n("mac.settings.speedValue", SpeedFormat.speed(redline.wrappedValue))
        VStack(alignment: .leading, spacing: Spacing.xs) {
            HStack(alignment: .firstTextBaseline) {
                RowTitle(title)
                Spacer(minLength: Spacing.s)
                ValueText(valueText)
                    .contentTransition(.numericText())
                    .accessibilityHidden(true)
            }
            Slider(value: position, in: 0...1) {
                Text(title)
            }
            .labelsHidden()
            .accessibilityValue(Text(valueText))
            .accessibilityAdjustableAction { direction in
                adjust(direction)
            }
            RedlineScale(highlighted: highlightedTitle)
                .padding(.top, Spacing.xxs)
        }
        .padding(.vertical, Spacing.xxs)
        .animation(reduceMotion ? nil : Motion.layout, value: metric)
    }

    private var position: Binding<Double> {
        let redline = redline
        let metric = metric
        return Binding(
            get: { SettingsLimits.position(ofRedline: redline.wrappedValue, metric: metric) },
            set: { redline.wrappedValue = SettingsLimits.redline(atPosition: $0, metric: metric) }
        )
    }

    /// One VoiceOver adjustment moves 1/40 of the slider (about 50 tok/s for output, about 12 % for total).
    private func adjust(_ direction: AccessibilityAdjustmentDirection) {
        let sign: Double
        switch direction {
        case .increment: sign = 1
        case .decrement: sign = -1
        @unknown default: return
        }
        let current = redline.wrappedValue
        let position = SettingsLimits.position(ofRedline: current, metric: metric)
        var next = SettingsLimits.redline(atPosition: position + sign / 40, metric: metric)
        if next == current {
            let step = SettingsLimits.redlineStep(at: current, metric: metric)
            next = SettingsLimits.snapRedline(current + sign * step, metric: metric)
        }
        redline.wrappedValue = next
    }
}

/// The orb's 36-segment gauge unrolled: the heat spectrum from idle to redline, with a thin tick where each speed
/// title begins at `(t / R)^0.6`. The ticks stay put while the redline moves; only the values in the ladder change.
struct RedlineScale: View {
    var highlighted: SpeedTitle?

    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.colorSchemeContrast) private var contrast

    private static let segments = 36
    private static let gap: CGFloat = 2
    private static let barHeight: CGFloat = 5
    private static let tickArea: CGFloat = 9
    private static let tickHeight: CGFloat = 5
    private static let tickSpacing: CGFloat = 2

    var body: some View {
        let dark = colorScheme == .dark
        let increasedContrast = contrast == .increased
        GeometryReader { proxy in
            let width = proxy.size.width
            ZStack(alignment: .topLeading) {
                Canvas { context, size in
                    let count = Self.segments
                    let segment = (size.width - Self.gap * CGFloat(count - 1)) / CGFloat(count)
                    let top = size.height - Self.barHeight
                    for index in 0..<count {
                        let heat = Double(index) / Double(count - 1)
                        let rect = CGRect(x: CGFloat(index) * (segment + Self.gap), y: top, width: segment, height: Self.barHeight)
                        context.fill(
                            Path(roundedRect: rect, cornerRadius: 1.5, style: .continuous),
                            with: .color(HeatPalette.color(heat, dark: dark).color)
                        )
                    }
                }
                ForEach(SpeedTitle.scaledLadder, id: \.self) { title in
                    tick(title, width: width, dark: dark, increasedContrast: increasedContrast)
                }
            }
        }
        .frame(height: Self.tickArea + Self.tickSpacing + Self.barHeight)
        .animation(Motion.fade, value: highlighted)
        .accessibilityHidden(true)
    }

    private func tick(_ title: SpeedTitle, width: CGFloat, dark: Bool, increasedContrast: Bool) -> some View {
        let heat = HeatPalette.heat(forFraction: title.threshold)
        let isHighlighted = title == highlighted
        let tickWidth: CGFloat = isHighlighted ? 2 : 1
        let height = isHighlighted ? Self.tickArea : Self.tickHeight
        let x = (heat * width).clamped(to: tickWidth...max(tickWidth, width - tickWidth))
        let color = DashboardPalette.heatGraphic(heat, dark: dark, increasedContrast: increasedContrast).color
        return Capsule()
            .fill(color)
            .frame(width: tickWidth, height: height)
            .opacity(highlighted == nil || isHighlighted ? 1 : 0.4)
            .offset(x: x - tickWidth / 2, y: Self.tickArea - height)
    }
}

/// Under the redline row: the hint, then every speed title with the speed it starts at for the current redline.
struct RedlineFooter: View {
    let settings: AppSettings
    let l10n: Localizer
    @Binding var highlightedTitle: SpeedTitle?
    var theme: TitleTheme = .preset(.vehicles)

    var body: some View {
        VStack(alignment: .leading, spacing: Spacing.xs - 2) {
            Footnote(l10n("settings.redline.hint"))
            TitleLadderList(
                redline: settings.redline,
                metric: settings.metric,
                l10n: l10n,
                highlighted: $highlightedTitle,
                theme: theme
            )
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}
