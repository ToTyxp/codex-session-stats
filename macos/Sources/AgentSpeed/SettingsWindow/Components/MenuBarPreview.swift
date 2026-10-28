import AgentSpeedCore
import SwiftUI

/// The right end of a menu bar over a soft wallpaper, showing the status item as the current options draw it.
struct MenuBarPreview: View {
    let model: AppModel
    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.colorSchemeContrast) private var contrast

    private static let barHeight: CGFloat = 24
    private static let height: CGFloat = 58
    private static let cornerRadius: CGFloat = 10
    /// Sample speed as a fraction of the redline: high enough to show the heat color.
    private static let sampleFraction = 0.5

    var body: some View {
        let settings = model.settings
        let dark = colorScheme == .dark
        let value = settings.redline * Self.sampleFraction
        let shape = RoundedRectangle(cornerRadius: Self.cornerRadius, style: .continuous)
        ZStack(alignment: .top) {
            PreviewWallpaper(dark: dark)
            HStack(spacing: 14) {
                Spacer(minLength: 0)
                StatusItemSample(
                    mode: settings.barMode,
                    colored: settings.barColoredGlyph,
                    showsUnit: settings.barShowsUnit,
                    value: value,
                    heat: HeatPalette.heat(value: value, redline: settings.redline),
                    unit: model.l10n("unit.tokS"),
                    dark: dark
                )
                Group {
                    Image(systemName: "wifi")
                    Image(systemName: "battery.75percent")
                    Image(systemName: "switch.2")
                    Text(clock)
                }
                .foregroundStyle(.primary.opacity(0.85))
            }
            .font(.system(size: 13))
            .padding(.horizontal, Spacing.s)
            .frame(height: Self.barHeight)
            .frame(maxWidth: .infinity)
            .background { barBackground(dark: dark) }
            .animation(model.accessibility.reduceMotion ? nil : Motion.layout, value: settings.barMode)
            .animation(Motion.fade, value: settings.barShowsUnit)
            .animation(Motion.fade, value: settings.barColoredGlyph)
        }
        .frame(height: Self.height)
        .clipShape(shape)
        .overlay {
            shape.strokeBorder(Color.primary.opacity(contrast == .increased ? 0.32 : 0.1), lineWidth: 1)
        }
        .padding(.vertical, Spacing.xxs)
        .accessibilityHidden(true)
    }

    private func barBackground(dark: Bool) -> some View {
        let opaque = model.accessibility.reduceTransparency
        let fill: Color = dark
            ? (opaque ? Color(white: 0.16) : Color.black.opacity(0.32))
            : (opaque ? Color(white: 0.95) : Color.white.opacity(0.62))
        return fill
            .overlay(alignment: .bottom) {
                Rectangle().fill(Color.primary.opacity(0.08)).frame(height: 1)
            }
            .mask {
                LinearGradient(
                    stops: [.init(color: .clear, location: 0), .init(color: .black, location: 0.42)],
                    startPoint: .leading,
                    endPoint: .trailing
                )
            }
    }

    /// The classic 9:41 on a Tuesday, in the app's language.
    private var clock: String {
        var components = DateComponents()
        components.year = 2026
        components.month = 1
        components.day = 6
        components.hour = 9
        components.minute = 41
        let date = Calendar(identifier: .gregorian).date(from: components) ?? Date()
        return date.formatted(Date.FormatStyle().weekday(.abbreviated).hour().minute().locale(model.l10n.locale))
    }
}

/// The status item content: gauge glyph, the value tight against it, and the unit.
private struct StatusItemSample: View {
    let mode: BarDisplayMode
    let colored: Bool
    let showsUnit: Bool
    let value: Double
    let heat: Double
    let unit: String
    let dark: Bool

    var body: some View {
        HStack(spacing: StatusItemLayout.glyphGap) {
            if mode.showsGlyph {
                GaugeGlyph(
                    fraction: heat,
                    fill: colored ? HeatPalette.color(heat, dark: dark).color : Color.primary,
                    track: Color.primary.opacity(0.25),
                    lineWidth: StatusItemLayout.glyphLineWidth
                )
                .frame(width: StatusItemLayout.glyphDiameter, height: StatusItemLayout.glyphDiameter)
                .transition(.opacity)
            }
            if mode.showsValue {
                HStack(alignment: .firstTextBaseline, spacing: StatusItemLayout.unitGap) {
                    Text(SpeedFormat.speed(value))
                        .font(Font(StatusItemLayout.valueFont as CTFont))
                    if showsUnit {
                        Text(unit)
                            .font(Font(StatusItemLayout.unitFont as CTFont))
                            .foregroundStyle(.secondary)
                            .transition(.opacity)
                    }
                }
                .transition(.opacity)
            }
        }
        .padding(.horizontal, 6)
        .frame(height: 22)
        .background(Color.primary.opacity(0.07), in: RoundedRectangle(cornerRadius: 5, style: .continuous))
    }
}

private struct PreviewWallpaper: View {
    let dark: Bool

    var body: some View {
        ZStack {
            dark ? Color(red: 0.075, green: 0.085, blue: 0.125) : Color(red: 0.91, green: 0.93, blue: 0.97)
            RadialGradient(
                colors: [HeatPalette.color(0.12).color.opacity(dark ? 0.38 : 0.32), .clear],
                center: UnitPoint(x: 0.12, y: 1.25),
                startRadius: 0,
                endRadius: 260
            )
            RadialGradient(
                colors: [HeatPalette.color(0.6).color.opacity(dark ? 0.34 : 0.26), .clear],
                center: UnitPoint(x: 0.82, y: 1.4),
                startRadius: 0,
                endRadius: 300
            )
        }
    }
}
