import AgentSpeedCore
import AgentSpeedOrbModel
import SwiftUI

/// Orb and menu bar renders for `--render-previews`.
@MainActor
enum SurfacePreviews {
    struct TierSample {
        let name: String
        let fraction: Double
        let tier: SpeedTier
    }

    static let tiers: [TierSample] = [
        TierSample(name: "idle", fraction: 0, tier: .idle),
        TierSample(name: "cruise", fraction: 0.07, tier: .cruise),
        TierSample(name: "boost", fraction: 0.24, tier: .boost),
        TierSample(name: "overdrive", fraction: 0.52, tier: .overdrive),
        TierSample(name: "redline", fraction: 0.9, tier: .redline),
        TierSample(name: "beyond", fraction: 1.45, tier: .redline),
    ]

    static let sizes: [CGFloat] = [112, 148, 196]

    /// Every style × state × size, light and dark (DESIGN 2a), plus the `led` stills under their historical names.
    static func renderOrbs(model: AppModel, into directory: URL) {
        let redline = model.settings.redline
        for dark in [false, true] {
            let scheme = dark ? "dark" : "light"
            var sheet: [[NSImage]] = []
            for size in sizes {
                var row: [NSImage] = []
                for sample in tiers {
                    let image = orbImage(model: model, size: size, dark: dark, speed: redline * sample.fraction, tier: sample.tier)
                    if let image {
                        PreviewSnapshot.write(image, to: directory.appendingPathComponent("orb-\(sample.name)-\(Int(size))-\(scheme).png"))
                        row.append(pointImage(image))
                    }
                }
                sheet.append(row)
            }
            writeSheet(sheet, dark: dark, to: directory.appendingPathComponent("orb-sheet-\(scheme).png"))

            let extras: [(String, OrbExtra)] = [("peak", .peak), ("hover", .hover), ("pulse", .pulse)]
            for (name, extra) in extras {
                if let image = orbImage(model: model, size: 196, dark: dark, speed: redline * 0.95, tier: .redline, extra: extra) {
                    PreviewSnapshot.write(image, to: directory.appendingPathComponent("orb-\(name)-196-\(scheme).png"))
                }
            }
            for style in OrbStyleKind.allCases {
                renderStyleSheet(model: model, style: style, dark: dark, into: directory)
            }
        }
    }

    /// Columns: idle, cruise, boost, overdrive, redline, beyond the redline, new peak, hover, pulse; rows: sizes.
    static func renderStyleSheet(model: AppModel, style: OrbStyleKind, dark: Bool, into directory: URL) {
        let redline = model.settings.redline
        let columns: [(Double, OrbExtra)] = tiers.map { ($0.fraction, OrbExtra.none) } + [(0.95, .peak), (0.6, .hover), (0.6, .pulse)]
        let rowSizes: [CGFloat] = (style == .minimal ? [64] : [96]) + [148, 240]
        var rows: [[NSImage]] = []
        for size in rowSizes {
            rows.append(columns.compactMap { fraction, extra in
                stageImage(model: model, style: style, size: size, dark: dark, value: redline * fraction, extra: extra)
                    .map(pointImage)
            })
        }
        writeSheet(rows, dark: dark, to: directory.appendingPathComponent("orb-style-\(style.rawValue)-\(dark ? "dark" : "light").png"))
    }

    /// An @2x still as an image of its point size, so sheets are written at 2× and not 4×.
    static func pointImage(_ image: CGImage) -> NSImage {
        NSImage(cgImage: image, size: CGSize(width: CGFloat(image.width) / 2, height: CGFloat(image.height) / 2))
    }

    enum OrbExtra {
        case none, peak, hover, pulse
    }

    /// A still of `style` at `value` after 2.4 s at that speed (and the extra event in flight).
    static func stageImage(model: AppModel, style: OrbStyleKind, size: CGFloat, dark: Bool, value: Double, extra: OrbExtra = .none,
                           level: EffectsLevel = .standard, configure: ((OrbPreviewStage) -> Void)? = nil) -> CGImage? {
        let stage = OrbPreviewStage(model: model, style: style, diameter: size, dark: dark, level: level)
        defer { stage.stop() }
        configure?(stage)
        stage.reset(value: value)
        stage.hovered = extra == .hover
        stage.run(for: 2.4, at: value)
        switch extra {
        case .peak:
            stage.animator.celebrate(at: stage.time)
            stage.run(for: 0.62, at: value)
        case .pulse:
            stage.animatorPulse(speed: value, output: 4000)
            stage.run(for: 0.2, at: value)
        case .hover:
            stage.run(for: 0.6, at: value)
        case .none:
            break
        }
        return stage.image()
    }

    static func orbImage(model: AppModel, size: CGFloat, dark: Bool, speed: Double, tier: SpeedTier, extra: OrbExtra = .none) -> CGImage? {
        model.store.publishLive(PreviewData.snapshot(speed: speed, redline: model.settings.redline))
        return stageImage(model: model, style: .led, size: size, dark: dark, value: speed, extra: extra)
    }

    static func renderMenuBar(model: AppModel, into directory: URL) {
        let redline = model.settings.redline
        let values: [Double] = [0, 4.2, 45, 364, 1280]
        for dark in [false, true] {
            var images: [NSImage] = []
            for value in values {
                model.store.publishLive(PreviewData.snapshot(speed: value, redline: redline))
                PreviewRenderer.snapGauge(model, to: value, now: Clock.now - 3)
                let state = StatusItemState(model: model, time: Clock.now)
                let size = CGSize(width: 120, height: state.layout.height)
                let image = NSImage(size: size, flipped: true) { rect in
                    (dark ? NSColor(white: 0.16, alpha: 1) : NSColor(white: 0.93, alpha: 1)).setFill()
                    rect.fill()
                    NSColor.systemRed.withAlphaComponent(0.35).setFill()
                    CGRect(x: 10 + state.layout.length, y: 0, width: 1, height: rect.height).fill()
                    NSGraphicsContext.current?.cgContext.translateBy(x: 10, y: 0)
                    StatusItemArtist.draw(state, dark: dark, in: CGRect(origin: .zero, size: CGSize(width: state.layout.length, height: state.layout.height)))
                    return true
                }
                images.append(scaled(image, by: 4))
            }
            writeColumn(images, to: directory.appendingPathComponent("menubar-\(dark ? "dark" : "light").png"))
        }
    }

    private static func scaled(_ image: NSImage, by scale: CGFloat) -> NSImage {
        let size = CGSize(width: image.size.width * scale, height: image.size.height * scale)
        guard let rep = NSBitmapImageRep(
            bitmapDataPlanes: nil, pixelsWide: Int(size.width), pixelsHigh: Int(size.height),
            bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
            colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0
        ) else { return image }
        rep.size = image.size
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
        image.draw(in: CGRect(origin: .zero, size: image.size))
        NSGraphicsContext.restoreGraphicsState()
        let result = NSImage(size: size)
        rep.size = size
        result.addRepresentation(rep)
        return result
    }

    static func writeSheet(_ rows: [[NSImage]], dark: Bool, to url: URL) {
        let cellWidth = rows.flatMap { $0 }.map(\.size.width).max() ?? 0
        let rowHeights = rows.map { $0.map(\.size.height).max() ?? 0 }
        let width = cellWidth * CGFloat(rows.map(\.count).max() ?? 0)
        let height = rowHeights.reduce(0, +)
        guard width > 0, height > 0 else { return }
        let image = NSImage(size: CGSize(width: width, height: height), flipped: true) { _ in
            let background = dark
                ? NSGradient(colors: [NSColor(red: 0.12, green: 0.14, blue: 0.2, alpha: 1), NSColor(red: 0.05, green: 0.05, blue: 0.08, alpha: 1)])
                : NSGradient(colors: [NSColor(red: 0.86, green: 0.89, blue: 0.94, alpha: 1), NSColor(red: 0.72, green: 0.78, blue: 0.86, alpha: 1)])
            background?.draw(in: CGRect(x: 0, y: 0, width: width, height: height), angle: 90)
            var y: CGFloat = 0
            for (index, row) in rows.enumerated() {
                for (column, cell) in row.enumerated() {
                    cell.draw(in: CGRect(x: CGFloat(column) * cellWidth + (cellWidth - cell.size.width) / 2, y: y + (rowHeights[index] - cell.size.height) / 2, width: cell.size.width, height: cell.size.height))
                }
                y += rowHeights[index]
            }
            return true
        }
        if let cg = image.cgImage(forProposedRect: nil, context: nil, hints: nil) {
            PreviewSnapshot.write(cg, to: url)
        }
    }

    static func writeColumn(_ images: [NSImage], to url: URL) {
        let width = images.map(\.size.width).max() ?? 0
        let height = images.reduce(0) { $0 + $1.size.height + 8 }
        guard width > 0 else { return }
        let image = NSImage(size: CGSize(width: width, height: height), flipped: true) { _ in
            NSColor(white: 0.5, alpha: 1).setFill()
            CGRect(x: 0, y: 0, width: width, height: height).fill()
            var y: CGFloat = 0
            for item in images {
                item.draw(in: CGRect(x: 0, y: y, width: item.size.width, height: item.size.height))
                y += item.size.height + 8
            }
            return true
        }
        if let cg = image.cgImage(forProposedRect: nil, context: nil, hints: nil) {
            PreviewSnapshot.write(cg, to: url)
        }
    }
}
