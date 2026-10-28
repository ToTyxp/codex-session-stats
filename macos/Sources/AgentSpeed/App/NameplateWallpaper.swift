import AppKit

/// Busy, deterministic wallpapers for legibility previews (y-down points): a night city under light streaks and bokeh
/// (dark), and a pastel paper with soft blobs, ink strokes and bokeh rings (light).
@MainActor
enum NameplateWallpaper {
    static func draw(in context: CGContext, size: CGSize, dark: Bool, seed: UInt64) {
        var random = Random(seed: seed)
        if dark { night(context, size: size, random: &random) } else { paper(context, size: size, random: &random) }
    }

    private struct Random {
        var state: UInt64
        init(seed: UInt64) { state = seed &* 0x9E37_79B9_7F4A_7C15 | 1 }
        mutating func next() -> CGFloat {
            state = state &* 6_364_136_223_846_793_005 &+ 1_442_695_040_888_963_407
            return CGFloat(state >> 40) / CGFloat(1 << 24)
        }
        mutating func range(_ low: CGFloat, _ high: CGFloat) -> CGFloat { low + (high - low) * next() }
    }

    private static func color(_ hex: UInt32, _ alpha: CGFloat = 1) -> CGColor {
        RGBColor(hex: hex, alpha: Double(alpha)).cgColor
    }

    private static func gradient(_ context: CGContext, size: CGSize, _ stops: [(UInt32, CGFloat)], diagonal: Bool) {
        let colors = stops.map { color($0.0) } as CFArray
        let locations = stops.map(\.1)
        guard let gradient = CGGradient(colorsSpace: CGColorSpace(name: CGColorSpace.sRGB), colors: colors, locations: locations) else { return }
        let end = diagonal ? CGPoint(x: size.width, y: size.height) : CGPoint(x: 0, y: size.height)
        context.drawLinearGradient(gradient, start: .zero, end: end, options: [.drawsBeforeStartLocation, .drawsAfterEndLocation])
    }

    private static func night(_ context: CGContext, size: CGSize, random: inout Random) {
        gradient(context, size: size, [(0x2B1660, 0), (0x6E2290, 0.45), (0xC0388E, 0.7), (0x2A1450, 1)], diagonal: true)
        let hues: [UInt32] = [0xFF5FA2, 0xB86BFF, 0x48D4FF, 0xFFB45A, 0xFF4D88]
        for _ in 0..<46 {
            let radius = random.range(8, 46)
            let center = CGPoint(x: random.range(0, size.width), y: random.range(0, size.height * 0.75))
            let hue = hues[Int(random.next() * CGFloat(hues.count)) % hues.count]
            let rect = CGRect(x: center.x - radius, y: center.y - radius, width: 2 * radius, height: 2 * radius)
            context.setFillColor(color(hue, random.range(0.12, 0.3)))
            context.fillEllipse(in: rect)
            context.setStrokeColor(color(hue, 0.55))
            context.setLineWidth(1.2)
            context.strokeEllipse(in: rect.insetBy(dx: 0.6, dy: 0.6))
        }
        context.setLineCap(.round)
        for _ in 0..<34 {
            let start = CGPoint(x: random.range(-200, size.width), y: random.range(0, size.height))
            let length = random.range(120, 420)
            let end = CGPoint(x: start.x + length, y: start.y - length * 0.42)
            context.setStrokeColor(color(random.next() > 0.5 ? 0xFFFFFF : 0x9CF2FF, random.range(0.25, 0.75)))
            context.setLineWidth(random.range(1, 3.2))
            context.move(to: start)
            context.addLine(to: end)
            context.strokePath()
        }
        var x: CGFloat = -10
        while x < size.width {
            let width = random.range(40, 110)
            let height = random.range(size.height * 0.18, size.height * 0.55)
            let rect = CGRect(x: x, y: size.height - height, width: width, height: height)
            context.setFillColor(color(0x11122A, 0.96))
            context.fill(rect)
            for wy in stride(from: rect.minY + 10, to: rect.maxY - 6, by: 9) {
                for wx in stride(from: rect.minX + 6, to: rect.maxX - 6, by: 8) where random.next() > 0.62 {
                    context.setFillColor(color(random.next() > 0.3 ? 0xFFD27A : 0x7FE3FF, random.range(0.5, 0.95)))
                    context.fill(CGRect(x: wx, y: wy, width: 3, height: 3))
                }
            }
            x += width + random.range(2, 14)
        }
    }

    private static func paper(_ context: CGContext, size: CGSize, random: inout Random) {
        gradient(context, size: size, [(0xF7EEE4, 0), (0xEADFF4, 0.5), (0xDDEBF6, 1)], diagonal: true)
        let blobs: [UInt32] = [0xFFB59A, 0xB9A8FF, 0x8FE0C8, 0xFFE08A, 0x9CC8FF]
        for _ in 0..<14 {
            let radius = random.range(60, 180)
            let center = CGPoint(x: random.range(0, size.width), y: random.range(0, size.height))
            let hue = blobs[Int(random.next() * CGFloat(blobs.count)) % blobs.count]
            let colors = [color(hue, 0.55), color(hue, 0)] as CFArray
            guard let gradient = CGGradient(colorsSpace: CGColorSpace(name: CGColorSpace.sRGB), colors: colors, locations: [0, 1]) else { continue }
            context.drawRadialGradient(gradient, startCenter: center, startRadius: 0, endCenter: center, endRadius: radius, options: [])
        }
        context.setLineCap(.round)
        for _ in 0..<26 {
            let start = CGPoint(x: random.range(-100, size.width), y: random.range(0, size.height))
            let length = random.range(80, 360)
            context.setStrokeColor(color(random.next() > 0.5 ? 0x23263A : 0x4A3B8C, random.range(0.15, 0.6)))
            context.setLineWidth(random.range(0.8, 5))
            context.move(to: start)
            context.addLine(to: CGPoint(x: start.x + length, y: start.y + length * random.range(-0.3, 0.3)))
            context.strokePath()
        }
        for _ in 0..<30 {
            let radius = random.range(8, 40)
            let center = CGPoint(x: random.range(0, size.width), y: random.range(0, size.height))
            context.setStrokeColor(color(blobs[Int(random.next() * 5) % 5], 0.8))
            context.setLineWidth(1.5)
            context.strokeEllipse(in: CGRect(x: center.x - radius, y: center.y - radius, width: 2 * radius, height: 2 * radius))
        }
        let band = CGRect(x: 0, y: size.height * 0.62, width: size.width, height: size.height * 0.12)
        context.setFillColor(color(0x2E3350, 0.18))
        context.fill(band)
    }
}
