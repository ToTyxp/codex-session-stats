// Renders the Agent Speed app icon: the macOS iconset, the Windows ICO and preview PNGs.
// Usage: swift generate-icon.swift <output-dir> <work-dir>   (normally run through generate.sh)
// Visual language: spec/DESIGN.md sections 1 (heat palette) and 2 (gauge geometry).

import CoreGraphics
import Foundation
import ImageIO

// MARK: - Color

let sRGB = CGColorSpace(name: CGColorSpace.sRGB)!

struct Color {
    var r: Double, g: Double, b: Double, a: Double

    init(_ r: Double, _ g: Double, _ b: Double, _ a: Double = 1) {
        (self.r, self.g, self.b, self.a) = (r, g, b, a)
    }

    init(hex: UInt32, alpha: Double = 1) {
        self.init(Double((hex >> 16) & 0xFF) / 255, Double((hex >> 8) & 0xFF) / 255, Double(hex & 0xFF) / 255, alpha)
    }

    static let white = Color(1, 1, 1)
    static let black = Color(0, 0, 0)

    func alpha(_ value: Double) -> Color { Color(r, g, b, value) }
    var cg: CGColor { CGColor(colorSpace: sRGB, components: [r, g, b, a].map { CGFloat($0) })! }
}

enum OKLab {
    static func linear(_ c: Double) -> Double { c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4) }
    static func gamma(_ c: Double) -> Double {
        let v = min(max(c, 0), 1)
        return v <= 0.0031308 ? v * 12.92 : 1.055 * pow(v, 1 / 2.4) - 0.055
    }

    static func lab(_ c: Color) -> (Double, Double, Double) {
        let (r, g, b) = (linear(c.r), linear(c.g), linear(c.b))
        let l = cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b)
        let m = cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b)
        let s = cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b)
        return (0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
                1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
                0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s)
    }

    static func color(_ lab: (Double, Double, Double), alpha: Double) -> Color {
        let (L, A, B) = lab
        let l = pow(L + 0.3963377774 * A + 0.2158037573 * B, 3)
        let m = pow(L - 0.1055613458 * A - 0.0638541728 * B, 3)
        let s = pow(L - 0.0894841775 * A - 1.2914855480 * B, 3)
        return Color(gamma(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s),
                     gamma(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s),
                     gamma(-0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s), alpha)
    }

    static func mix(_ x: Color, _ y: Color, _ t: Double) -> Color {
        let (a, b) = (lab(x), lab(y))
        return color((a.0 + (b.0 - a.0) * t, a.1 + (b.1 - a.1) * t, a.2 + (b.2 - a.2) * t), alpha: x.a + (y.a - x.a) * t)
    }
}

let heatStops: [(Double, Color)] = [
    (0.00, Color(hex: 0x7C8798)), (0.10, Color(hex: 0x2DD4BF)), (0.30, Color(hex: 0x38BDF8)),
    (0.48, Color(hex: 0x818CF8)), (0.64, Color(hex: 0xE879F9)), (0.78, Color(hex: 0xFB7185)),
    (0.90, Color(hex: 0xFB923C)), (1.00, Color(hex: 0xFACC15)),
]
let whiteHot = Color(hex: 0xFFF7D6)

func heat(_ h: Double) -> Color {
    let h = min(max(h, 0), 1)
    for (lo, hi) in zip(heatStops, heatStops.dropFirst()) where h <= hi.0 {
        return OKLab.mix(lo.1, hi.1, (h - lo.0) / (hi.0 - lo.0))
    }
    return heatStops.last!.1
}

func gradient(_ stops: [(CGFloat, Color)]) -> CGGradient {
    CGGradient(colorsSpace: sRGB, colors: stops.map(\.1.cg) as CFArray, locations: stops.map(\.0))!
}

// MARK: - Geometry

func point(_ c: CGPoint, _ r: CGFloat, _ degrees: Double) -> CGPoint {
    let a = degrees * .pi / 180
    return CGPoint(x: c.x + r * CGFloat(cos(a)), y: c.y + r * CGFloat(sin(a)))
}

func radians(_ degrees: Double) -> CGFloat { CGFloat(degrees * .pi / 180) }

/// Apple's continuous-curvature rounded rectangle (the iOS 7+ / macOS 11+ icon corner).
func continuousRect(_ rect: CGRect, radius r: CGFloat) -> CGPath {
    let k: [(CGFloat, CGFloat)] = [
        (1.52866483, 0), (1.08849323, 0), (0.86840689, 0), (0.63149399, 0.07491100),
        (0.37282392, 0.16943499), (0.16943499, 0.37282392), (0.07491100, 0.63149399),
        (0, 0.86840689), (0, 1.08849323), (0, 1.52866483),
    ]
    let corners: [(CGPoint, CGVector, CGVector)] = [
        (CGPoint(x: rect.maxX, y: rect.minY), CGVector(dx: -1, dy: 0), CGVector(dx: 0, dy: 1)),
        (CGPoint(x: rect.maxX, y: rect.maxY), CGVector(dx: 0, dy: -1), CGVector(dx: -1, dy: 0)),
        (CGPoint(x: rect.minX, y: rect.maxY), CGVector(dx: 1, dy: 0), CGVector(dx: 0, dy: -1)),
        (CGPoint(x: rect.minX, y: rect.minY), CGVector(dx: 0, dy: 1), CGVector(dx: 1, dy: 0)),
    ]
    let path = CGMutablePath()
    for (index, (p, u, v)) in corners.enumerated() {
        let q = { (i: Int) in CGPoint(x: p.x + r * (k[i].0 * u.dx + k[i].1 * v.dx), y: p.y + r * (k[i].0 * u.dy + k[i].1 * v.dy)) }
        if index == 0 { path.move(to: q(0)) } else { path.addLine(to: q(0)) }
        path.addCurve(to: q(3), control1: q(1), control2: q(2))
        path.addCurve(to: q(6), control1: q(4), control2: q(5))
        path.addCurve(to: q(9), control1: q(7), control2: q(8))
    }
    path.closeSubpath()
    return path
}

/// Annular sector between two angles (degrees, clockwise on screen) with rounded corners.
func sector(_ c: CGPoint, inner ri: CGFloat, outer ro: CGFloat, from a0: Double, to a1: Double, corner rc: CGFloat) -> CGPath {
    let (rin, rout) = (ri + rc, ro - rc)
    let d = { (r: CGFloat) in asin(Double(rc / r)) * 180 / .pi }
    let path = CGMutablePath()
    path.addArc(center: c, radius: rout, startAngle: radians(a0 + d(rout)), endAngle: radians(a1 - d(rout)), clockwise: false)
    path.addArc(center: c, radius: rin, startAngle: radians(a1 - d(rin)), endAngle: radians(a0 + d(rin)), clockwise: true)
    path.closeSubpath()
    guard rc > 0.01 else { return path }
    return path.union(path.copy(strokingWithWidth: rc * 2, lineCap: .round, lineJoin: .round, miterLimit: 10))
}

// MARK: - Layout

enum Platform { case macOS, windows }

/// Level of detail. Small renditions are redrawn with chunkier, higher-contrast geometry instead of being downscaled.
struct Tier {
    var segments: Int               // 0 draws one continuous arc
    var outer: CGFloat              // ring radii as fractions of the body width
    var inner: CGFloat
    var gap: Double                 // degrees, or pixels at mid radius when `gapInPixels`
    var gapInPixels = false
    var segmentCorner: CGFloat = 0  // on the 824 px body grid
    var unlit: Double
    var glow: Double
    var plate = false
    var scale = false
    var comet = false
    var agents = false

    static let micro = Tier(segments: 0, outer: 0.43, inner: 0.27, gap: 1.0, gapInPixels: true, unlit: 0.18, glow: 0)
    static let tiny = Tier(segments: 6, outer: 0.44, inner: 0.22, gap: 1.6, gapInPixels: true, unlit: 0.16, glow: 0.4)
    static let small = Tier(segments: 12, outer: 0.42, inner: 0.265, gap: 1.0, gapInPixels: true, unlit: 0.12, glow: 0.6)
    static let medium = Tier(segments: 18, outer: 0.40, inner: 0.29, gap: 4.4, segmentCorner: 8, unlit: 0.09, glow: 0.7,
                             plate: true, comet: true)
    static let full = Tier(segments: 36, outer: 0.385, inner: 0.30, gap: 2.2, segmentCorner: 5, unlit: 0.08, glow: 1,
                           plate: true, scale: true, comet: true, agents: true)

    static func pick(_ platform: Platform, pixels: Int, points: Int) -> Tier {
        switch platform {
        case .macOS:
            if points <= 16 { return pixels <= 16 ? .micro : .tiny }
            if points <= 32 { return pixels <= 32 ? .small : .medium }
            return .full
        case .windows:
            switch pixels {
            case ...20: return .micro
            case ...24: return .tiny
            case ...40: return .small
            case ...64: return .medium
            default: return .full
            }
        }
    }
}

struct Layout {
    let pixels: Int
    let tier: Tier
    let body: CGRect
    let corner: CGFloat
    let center: CGPoint
    let outer: CGFloat
    let inner: CGFloat
    let gap: Double
    let shadow: (offset: CGFloat, blur: CGFloat, alpha: Double)?

    /// Scale from the 824 px body of the 1024 px master to this rendition.
    var k: CGFloat { body.width / 824 }
    /// A continuous arc reuses the 6-segment layout so every tier lights the same 5/6 of the gauge.
    var segments: Int { max(tier.segments, 6) }
    var lit: Int { segments * 5 / 6 }
    var lead: Int { lit - 1 }
    let startAngle = 135.0
    var pitch: Double { 270 / Double(segments) }
    var litEnd: Double { startAngle + Double(lit) * pitch - gap / 2 }

    func span(_ i: Int) -> (Double, Double) {
        let a = startAngle + Double(i) * pitch
        return (a + gap / 2, a + pitch - gap / 2)
    }

    /// The lit arc is stretched over teal → orange so the icon always shows the full spectrum.
    func heatAt(angle: Double) -> Double {
        let t = (angle - startAngle) / (litEnd - startAngle)
        return 0.1 + 0.85 * min(max(t, 0), 1)
    }

    func heatOf(_ i: Int) -> Double { 0.1 + 0.85 * Double(i) / Double(max(lead, 1)) }

    init(platform: Platform, pixels: Int, points: Int) {
        let size = CGFloat(pixels)
        let tier = Tier.pick(platform, pixels: pixels, points: points)
        self.pixels = pixels
        self.tier = tier

        var margin: CGFloat
        switch platform {
        case .macOS:
            // macOS grid: 824 px body centred on a 1024 px canvas. Small sizes snap to whole pixels.
            margin = size * 100 / 1024
            if pixels <= 16 { margin = 1 } else if pixels <= 64 { margin = margin.rounded() }
        case .windows:
            margin = size * 0.03
            if pixels <= 64 { margin = margin.rounded() }
        }
        body = CGRect(x: margin, y: margin, width: size - 2 * margin, height: size - 2 * margin)
        let u = body.width
        // 0.26 matches the system icon mask of macOS 26+ (continuous corners, fitted to 0.1 px).
        corner = u * (platform == .macOS ? 0.26 : 0.2)
        outer = u * tier.outer
        inner = u * tier.inner
        center = CGPoint(x: body.midX, y: body.midY + u * 0.03)
        gap = tier.gapInPixels ? tier.gap / Double((outer + inner) / 2) * 180 / .pi : tier.gap

        let k = u / 824
        switch platform {
        case .macOS where pixels >= 64: shadow = (12 * k, 20 * k, 0.30)
        case .macOS where pixels >= 32: shadow = (0.5, 1, 0.22)
        case .windows where pixels >= 128: shadow = (u * 3 / 240, u * 6 / 240, 0.25)
        default: shadow = nil
        }
    }
}

// MARK: - Drawing helpers

extension CGContext {
    func with(_ body: () -> Void) {
        saveGState()
        body()
        restoreGState()
    }

    func fill(_ path: CGPath, _ color: Color) {
        setFillColor(color.cg)
        addPath(path)
        fillPath()
    }

    func clip(to path: CGPath) {
        addPath(path)
        clip()
    }

    /// Draws only the blurred halo of `path` (the shape itself is rendered off-canvas).
    func glow(_ path: CGPath, _ color: Color, blur: CGFloat, mode: CGBlendMode = .plusLighter) {
        with {
            setBlendMode(mode)
            let offset: CGFloat = 20000
            translateBy(x: offset, y: 0)
            setShadow(offset: CGSize(width: -offset, height: 0), blur: blur, color: color.cg)
            setFillColor(Color.black.cg)
            addPath(path)
            fillPath()
        }
    }

    /// Fills `path` with a sweep of colors around `center`; aliased wedges inside an antialiased clip tile without seams.
    func sweep(_ path: CGPath, center: CGPoint, radius: CGFloat, from a0: Double, to a1: Double, color: (Double) -> Color) {
        with {
            clip(to: path)
            setShouldAntialias(false)
            let steps = max(Int((a1 - a0) * 2), 1)
            for s in 0..<steps {
                let b0 = a0 + (a1 - a0) * Double(s) / Double(steps)
                let b1 = a0 + (a1 - a0) * Double(s + 1) / Double(steps)
                let wedge = CGMutablePath()
                wedge.move(to: center)
                wedge.addLine(to: point(center, radius, b0))
                wedge.addLine(to: point(center, radius, b1))
                wedge.closeSubpath()
                fill(wedge, color((b0 + b1) / 2))
            }
        }
    }
}

func circle(_ c: CGPoint, _ r: CGFloat) -> CGPath {
    CGPath(ellipseIn: CGRect(x: c.x - r, y: c.y - r, width: r * 2, height: r * 2), transform: nil)
}

// MARK: - Icon

func render(_ L: Layout) -> CGImage {
    let size = L.pixels
    let ctx = CGContext(data: nil, width: size, height: size, bitsPerComponent: 32, bytesPerRow: 0, space: sRGB,
                        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue | CGBitmapInfo.floatComponents.rawValue
                            | CGBitmapInfo.byteOrder32Little.rawValue)!
    ctx.translateBy(x: 0, y: CGFloat(size))
    ctx.scaleBy(x: 1, y: -1)
    ctx.setShouldAntialias(true)
    ctx.setAllowsAntialiasing(true)
    ctx.interpolationQuality = .high

    let body = continuousRect(L.body, radius: L.corner)
    if let s = L.shadow {
        ctx.with {
            ctx.setShadow(offset: CGSize(width: 0, height: -s.offset), blur: s.blur, color: Color.black.alpha(s.alpha).cg)
            ctx.fill(body, Color(hex: 0x090B10))
        }
    }
    ctx.with {
        ctx.clip(to: body)
        drawGlass(ctx, L)
        if L.tier.plate { drawPlate(ctx, L) }
        if L.tier.scale { drawScale(ctx, L) }
        if L.tier.segments == 0 { drawContinuousGauge(ctx, L) } else { drawGauge(ctx, L) }
        if L.tier.comet { drawComet(ctx, L) }
        if L.tier.agents && L.pixels >= 256 { drawAgents(ctx, L) }
    }
    drawRim(ctx, L, body)
    return quantize(ctx, seed: UInt64(size))
}

func drawGlass(_ ctx: CGContext, _ L: Layout) {
    let b = L.body
    ctx.drawLinearGradient(gradient([(0, Color(hex: 0x1B2030)), (1, Color(hex: 0x090B10))]),
                           start: CGPoint(x: b.midX, y: b.minY), end: CGPoint(x: b.midX, y: b.maxY), options: [])

    let lens = CGPoint(x: b.minX + b.width * 0.26, y: b.minY + b.height * 0.16)
    ctx.drawRadialGradient(gradient([(0, Color.white.alpha(0.075)), (0.45, Color.white.alpha(0.03)), (1, Color.white.alpha(0))]),
                           startCenter: lens, startRadius: 0, endCenter: lens, endRadius: b.width * 0.78, options: [])

    let vignette = CGPoint(x: b.midX, y: b.midY - b.height * 0.06)
    ctx.drawRadialGradient(gradient([(0, Color.black.alpha(0)), (0.62, Color.black.alpha(0)), (1, Color.black.alpha(0.42))]),
                           startCenter: vignette, startRadius: 0, endCenter: vignette, endRadius: b.width * 0.78,
                           options: [.drawsAfterEndLocation])
}

/// Recessed dial face inside the gauge: darker at the top, a soft inner shadow and a faint lower bevel.
func drawPlate(_ ctx: CGContext, _ L: Layout) {
    let (c, k) = (L.center, L.k)
    let r = L.inner - 36 * k
    let plate = circle(c, r)
    ctx.with {
        ctx.clip(to: plate)
        ctx.drawLinearGradient(gradient([(0, Color(hex: 0x06080C, alpha: 0.75)), (1, Color(hex: 0x171C28, alpha: 0.75))]),
                               start: CGPoint(x: c.x, y: c.y - r), end: CGPoint(x: c.x, y: c.y + r), options: [])
        let surround = CGMutablePath()
        surround.addRect(CGRect(x: c.x - r * 2, y: c.y - r * 2, width: r * 4, height: r * 4))
        surround.addPath(plate)
        ctx.setShadow(offset: CGSize(width: 0, height: -10 * k), blur: 28 * k, color: Color.black.alpha(0.6).cg)
        ctx.addPath(surround)
        ctx.setFillColor(Color.black.cg)
        ctx.fillPath(using: .evenOdd)
    }
    ctx.with {
        ctx.addPath(plate)
        ctx.setLineWidth(max(2 * k, 0.5))
        ctx.replacePathWithStrokedPath()
        ctx.clip()
        ctx.drawLinearGradient(gradient([(0, Color.white.alpha(0)), (0.55, Color.white.alpha(0.02)), (1, Color.white.alpha(0.12))]),
                               start: CGPoint(x: c.x, y: c.y - r), end: CGPoint(x: c.x, y: c.y + r), options: [])
    }
}

/// Fine inner scale with the tier thresholds of spec §1 (boost, overdrive, redline).
func drawScale(_ ctx: CGContext, _ L: Layout) {
    let (c, k) = (L.center, L.k)
    let r = L.inner - 22 * k
    let arc = CGMutablePath()
    arc.addArc(center: c, radius: r, startAngle: radians(L.startAngle), endAngle: radians(L.startAngle + 270), clockwise: false)
    ctx.with {
        ctx.addPath(arc)
        ctx.setLineWidth(2 * k)
        ctx.setLineCap(.round)
        ctx.setStrokeColor(Color.white.alpha(0.07).cg)
        ctx.strokePath()
    }
    for v in [0.15, 0.375, 0.75] {
        ctx.fill(circle(point(c, r, L.startAngle + 270 * pow(v, 0.6)), 4.5 * k), Color.white.alpha(0.22))
    }
}

func drawGauge(_ ctx: CGContext, _ L: Layout) {
    let (c, k, tier) = (L.center, L.k, L.tier)
    let corner = tier.segmentCorner > 0 ? max(tier.segmentCorner * k, 0.6) : 0
    let paths = (0..<L.segments).map { i -> CGPath in
        let (a0, a1) = L.span(i)
        return sector(c, inner: L.inner, outer: L.outer, from: a0, to: a1, corner: corner)
    }

    for i in L.lit..<L.segments { ctx.fill(paths[i], Color.white.alpha(tier.unlit)) }

    let detailed = tier.segments >= 18
    for i in 0..<L.lead where tier.glow > 0 {
        let color = heat(L.heatOf(i))
        if detailed {
            ctx.glow(paths[i], color.alpha(0.30 * tier.glow), blur: 110 * k)
            ctx.glow(paths[i], color.alpha(0.55 * tier.glow), blur: 34 * k)
        } else {
            ctx.glow(paths[i], color.alpha(0.5 * tier.glow), blur: max(2, 40 * k))
        }
    }

    for i in 0..<L.lead {
        let color = heat(L.heatOf(i))
        guard detailed else { ctx.fill(paths[i], color); continue }
        let mid = (L.span(i).0 + L.span(i).1) / 2
        ctx.with {
            ctx.clip(to: paths[i])
            ctx.drawLinearGradient(gradient([(0, OKLab.mix(color, .black, 0.12)), (0.6, color), (1, OKLab.mix(color, whiteHot, 0.18))]),
                                   start: point(c, L.inner, mid), end: point(c, L.outer, mid), options: [])
        }
    }

    let lead = paths[L.lead]
    if detailed {
        ctx.glow(lead, heat(L.heatOf(L.lead)).alpha(0.85 * tier.glow), blur: 90 * k)
        ctx.glow(lead, whiteHot.alpha(0.9 * tier.glow), blur: 26 * k)
    } else if tier.glow > 0 {
        ctx.glow(lead, whiteHot.alpha(0.7 * tier.glow), blur: max(2, 30 * k))
    }
    ctx.fill(lead, whiteHot)
}

/// 16 px rendition: one crisp arc sweeping through the palette into a white-hot tip.
func drawContinuousGauge(_ ctx: CGContext, _ L: Layout) {
    let c = L.center
    let start = L.startAngle + L.gap / 2
    let end = L.litEnd
    let lit = sector(c, inner: L.inner, outer: L.outer, from: start, to: end, corner: 0)
    let rest = sector(c, inner: L.inner, outer: L.outer, from: end + L.gap, to: L.startAngle + 270 - L.gap / 2, corner: 0)
    ctx.fill(rest, Color.white.alpha(L.tier.unlit))
    let tip = end - 30
    ctx.sweep(lit, center: c, radius: L.outer * 2, from: start, to: end) { a in
        a < tip ? heat(L.heatAt(angle: a)) : OKLab.mix(heat(L.heatAt(angle: tip)), whiteHot, min(1, (a - tip) / 16))
    }
}

func drawComet(_ ctx: CGContext, _ L: Layout) {
    let (c, k) = (L.center, L.k)
    let detailed = L.tier.segments >= 36
    let head = L.litEnd + 1.0
    let length: Double = detailed ? 38 : 30
    let tail = head - length
    let radius = L.outer + (detailed ? 30 * k : 36 * k)
    let width = max(detailed ? 8 * k : 10 * k, 0.7)

    let steps = 120
    let shape = CGMutablePath()
    var inner: [CGPoint] = []
    for s in 0...steps {
        let t = Double(s) / Double(steps)
        let w = width * CGFloat(pow(t, 1.2))
        let a = tail + length * t
        let p = point(c, radius + w, a)
        if s == 0 { shape.move(to: p) } else { shape.addLine(to: p) }
        inner.append(point(c, radius - w, a))
    }
    shape.addArc(center: point(c, radius, head), radius: width, startAngle: radians(head), endAngle: radians(head + 180), clockwise: false)
    for p in inner.reversed() { shape.addLine(to: p) }
    shape.closeSubpath()

    // A linear gradient along the chord stands in for a conic sweep: arc samples project monotonically onto it.
    let from = point(c, radius, tail), to = point(c, radius, head)
    let axis = CGVector(dx: to.x - from.x, dy: to.y - from.y)
    let len2 = axis.dx * axis.dx + axis.dy * axis.dy
    let stops = (0...24).map { i -> (CGFloat, Color) in
        let t = Double(i) / 24
        let p = point(c, radius, tail + length * t)
        let s = ((p.x - from.x) * axis.dx + (p.y - from.y) * axis.dy) / len2
        let color = OKLab.mix(heat(0.82 + 0.16 * t), whiteHot, pow(t, 4))
        return (min(max(s, 0), 1), color.alpha(pow(t, 1.5)))
    }

    ctx.glow(shape, heat(0.92).alpha(0.55), blur: 24 * k)
    ctx.with {
        ctx.setBlendMode(.plusLighter)
        ctx.clip(to: shape)
        ctx.drawLinearGradient(gradient(stops + [(1, whiteHot)]), start: from, end: to, options: [.drawsAfterEndLocation])
    }

    let dot = circle(point(c, radius, head), width * 1.4)
    ctx.glow(dot, heat(0.95).alpha(0.9), blur: 64 * k)
    ctx.glow(dot, whiteHot, blur: 18 * k)
    ctx.with {
        ctx.setBlendMode(.plusLighter)
        ctx.fill(dot, whiteHot)
    }
}

/// The agent dots of the orb, one per source color, resting in the gap at the bottom of the gauge.
func drawAgents(_ ctx: CGContext, _ L: Layout) {
    let (c, k) = (L.center, L.k)
    let plateR = L.inner - 36 * k
    let y = c.y + plateR + 34 * k
    let colors = [Color(hex: 0x34D399), Color(hex: 0xFF8A65), Color(hex: 0xB39DFF)]
    for (i, color) in colors.enumerated() {
        let dot = circle(CGPoint(x: c.x + CGFloat(i - 1) * 40 * k, y: y), 8 * k)
        ctx.glow(dot, color.alpha(0.7), blur: 22 * k)
        ctx.fill(dot, color)
    }
}

func drawRim(_ ctx: CGContext, _ L: Layout, _ body: CGPath) {
    let b = L.body
    let width = max(L.pixels <= 16 ? 0.7 : 1, 2 * L.k)
    ctx.with {
        ctx.clip(to: body)
        ctx.addPath(body)
        ctx.setLineWidth(width * 2)
        ctx.replacePathWithStrokedPath()
        ctx.clip()
        ctx.drawLinearGradient(gradient([(0, Color.white.alpha(0.20)), (0.5, Color.white.alpha(0.06)), (1, Color.white.alpha(0.04))]),
                               start: CGPoint(x: b.midX, y: b.minY), end: CGPoint(x: b.midX, y: b.maxY), options: [])
    }
}

// MARK: - Output

/// Converts the float canvas to 8-bit straight alpha with a light dither to avoid banding in the dark gradients.
func quantize(_ ctx: CGContext, seed: UInt64) -> CGImage {
    let (w, h) = (ctx.width, ctx.height)
    let src = ctx.data!.assumingMemoryBound(to: Float.self)
    let stride = ctx.bytesPerRow / 4
    var out = [UInt8](repeating: 0, count: w * h * 4)
    var state = seed &* 0x9E3779B97F4A7C15 | 1
    func noise() -> Float {
        state ^= state << 13; state ^= state >> 7; state ^= state << 17
        return Float(state % 1024) / 1024 - 0.5
    }
    for y in 0..<h {
        for x in 0..<w {
            let p = src + y * stride + x * 4
            let a = min(max(p[3], 0), 1)
            let o = (y * w + x) * 4
            out[o + 3] = UInt8((a * 255).rounded())
            guard a > 0 else { continue }
            for ch in 0..<3 {
                let v = min(max(p[ch] / a, 0), 1) * 255 + noise()
                out[o + ch] = UInt8(min(max(v.rounded(), 0), 255))
            }
        }
    }
    let provider = CGDataProvider(data: Data(out) as CFData)!
    return CGImage(width: w, height: h, bitsPerComponent: 8, bitsPerPixel: 32, bytesPerRow: w * 4, space: sRGB,
                   bitmapInfo: CGBitmapInfo(rawValue: CGImageAlphaInfo.last.rawValue), provider: provider,
                   decode: nil, shouldInterpolate: true, intent: .defaultIntent)!
}

func pngData(_ image: CGImage) -> Data {
    let data = NSMutableData()
    let dest = CGImageDestinationCreateWithData(data, "public.png" as CFString, 1, nil)!
    CGImageDestinationAddImage(dest, image, nil)
    guard CGImageDestinationFinalize(dest) else { fatalError("PNG encoding failed") }
    return data as Data
}

func write(_ data: Data, to url: URL) {
    do { try data.write(to: url, options: .atomic) } catch { fatalError("Cannot write \(url.path): \(error)") }
}

/// Multi-resolution ICO with PNG-compressed entries (supported since Windows Vista).
func icoData(_ entries: [(Int, Data)]) -> Data {
    var out = Data()
    func u16(_ v: Int) { out.append(contentsOf: [UInt8(v & 0xFF), UInt8((v >> 8) & 0xFF)]) }
    func u32(_ v: Int) { u16(v & 0xFFFF); u16((v >> 16) & 0xFFFF) }
    u16(0); u16(1); u16(entries.count)
    var offset = 6 + 16 * entries.count
    for (size, png) in entries {
        out.append(UInt8(size >= 256 ? 0 : size))
        out.append(UInt8(size >= 256 ? 0 : size))
        out.append(0); out.append(0)
        u16(1); u16(32)
        u32(png.count); u32(offset)
        offset += png.count
    }
    for (_, png) in entries { out.append(png) }
    return out
}

let args = CommandLine.arguments.dropFirst()
guard args.count == 2 else {
    FileHandle.standardError.write("usage: swift generate-icon.swift <output-dir> <work-dir>\n".data(using: .utf8)!)
    exit(2)
}
let outputDir = URL(fileURLWithPath: args[args.startIndex], isDirectory: true)
let workDir = URL(fileURLWithPath: args[args.startIndex + 1], isDirectory: true)
let iconset = workDir.appendingPathComponent("AgentSpeed.iconset", isDirectory: true)
let windowsDir = workDir.appendingPathComponent("windows", isDirectory: true)
for dir in [outputDir, iconset, windowsDir] {
    try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
}

for points in [16, 32, 128, 256, 512] {
    for scale in [1, 2] {
        let png = pngData(render(Layout(platform: .macOS, pixels: points * scale, points: points)))
        let name = scale == 1 ? "icon_\(points)x\(points).png" : "icon_\(points)x\(points)@2x.png"
        write(png, to: iconset.appendingPathComponent(name))
        if points == 512 && scale == 2 { write(png, to: outputDir.appendingPathComponent("icon-1024.png")) }
    }
}

var icoEntries: [(Int, Data)] = []
for size in [16, 20, 24, 32, 40, 48, 64, 256] {
    let png = pngData(render(Layout(platform: .windows, pixels: size, points: size)))
    write(png, to: windowsDir.appendingPathComponent("icon_\(size).png"))
    icoEntries.append((size, png))
    if size == 256 { write(png, to: outputDir.appendingPathComponent("icon-windows-256.png")) }
}
write(icoData(icoEntries), to: outputDir.appendingPathComponent("AgentSpeed.ico"))
print("Rendered iconset → \(iconset.path)")
print("Rendered ICO     → \(outputDir.appendingPathComponent("AgentSpeed.ico").path)")
