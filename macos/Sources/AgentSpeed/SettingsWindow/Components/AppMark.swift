import AgentSpeedCore
import AppKit
import SwiftUI

/// The app icon, or a drawn gauge mark when running unbundled (where the system shows a generic icon).
struct AppMark: View {
    var size: CGFloat = 96

    var body: some View {
        Group {
            if Bundle.main.bundleURL.pathExtension == "app" {
                Image(nsImage: NSApp.applicationIconImage)
                    .resizable()
                    .interpolation(.high)
            } else {
                GaugeMark()
            }
        }
        .frame(width: size, height: size)
        .accessibilityHidden(true)
    }
}

/// The icon's motif: a smoked tile with the 36-segment heat gauge lit to the redline zone and three source dots.
private struct GaugeMark: View {
    private static let segments = 36
    private static let lit = 28
    private static let tileTop = RGBColor(hex: 0x282D3C)
    private static let tileBottom = RGBColor(hex: 0x0B0E15)
    /// The three dots baked into the bundled app icon (assets/icon), not every source the app reads.
    private static let iconSources: [SourceKind] = [.codex, .claudeCode, .claudeDesktop]

    var body: some View {
        Canvas { context, size in
            let side = min(size.width, size.height)
            let inset = side * 0.09
            let tileRect = CGRect(x: inset, y: inset, width: side - inset * 2, height: side - inset * 2)
            let tile = Path(roundedRect: tileRect, cornerRadius: tileRect.width * 0.225, style: .continuous)

            var shadowed = context
            shadowed.addFilter(.shadow(color: .black.opacity(0.3), radius: side * 0.03, x: 0, y: side * 0.015))
            shadowed.fill(tile, with: .linearGradient(
                Gradient(colors: [Self.tileTop.color, Self.tileBottom.color]),
                startPoint: CGPoint(x: tileRect.midX, y: tileRect.minY),
                endPoint: CGPoint(x: tileRect.midX, y: tileRect.maxY)
            ))
            context.stroke(tile, with: .color(.white.opacity(0.12)), lineWidth: 0.75)

            let center = CGPoint(x: tileRect.midX, y: tileRect.midY)
            let outer = tileRect.width * 0.39
            let inner = tileRect.width * 0.29
            let well = Path(ellipseIn: CGRect(x: center.x - inner * 0.86, y: center.y - inner * 0.86, width: inner * 1.72, height: inner * 1.72))
            context.fill(well, with: .color(.black.opacity(0.28)))
            context.stroke(well, with: .color(.white.opacity(0.06)), lineWidth: 0.75)

            let span = 270.0 / Double(Self.segments)
            let width = (span - 2.2) * .pi / 180 * (inner + outer) / 2
            let style = StrokeStyle(lineWidth: width, lineCap: .round)
            for index in 0..<Self.segments {
                let segment = Self.segment(index: index, center: center, inner: inner, outer: outer, span: span)
                let heat = 0.1 + 0.9 * Double(index) / Double(Self.segments - 1)
                if index == Self.lit - 1 {
                    context.drawLayer { glow in
                        glow.addFilter(.blur(radius: side * 0.03))
                        glow.stroke(segment, with: .color(HeatPalette.amberHot.color), style: StrokeStyle(lineWidth: width * 2.2, lineCap: .round))
                    }
                    context.stroke(segment, with: .color(HeatPalette.whiteHot.color), style: style)
                } else if index < Self.lit {
                    context.stroke(segment, with: .color(HeatPalette.color(heat).color), style: style)
                } else {
                    context.stroke(segment, with: .color(.white.opacity(0.09)), style: style)
                }
            }

            let dot = side * 0.034
            let dotsY = center.y + outer * 0.98
            for (offset, kind) in zip([-1.0, 0, 1], Self.iconSources) {
                let x = center.x + CGFloat(offset) * dot * 2.6
                let rect = CGRect(x: x - dot / 2, y: dotsY - dot / 2, width: dot, height: dot)
                context.fill(Path(ellipseIn: rect), with: .color(kind.tint.color))
            }
        }
    }

    /// One radial LED segment; angles run clockwise from 135° (bottom-left) in y-down coordinates.
    private static func segment(index: Int, center: CGPoint, inner: CGFloat, outer: CGFloat, span: Double) -> Path {
        let radians = (135 + (Double(index) + 0.5) * span) * .pi / 180
        let direction = CGPoint(x: cos(radians), y: sin(radians))
        var path = Path()
        path.move(to: CGPoint(x: center.x + direction.x * inner, y: center.y + direction.y * inner))
        path.addLine(to: CGPoint(x: center.x + direction.x * outer, y: center.y + direction.y * outer))
        return path
    }
}
