import AppKit
import CoreText

/// A bitmap for composing preview sheets and video frames: images and single-line labels in points (y down, @2x).
@MainActor
final class PreviewCanvas {
    let size: CGSize
    let scale: CGFloat
    let context: CGContext

    init?(size: CGSize, scale: CGFloat = 2, background: NSColor) {
        self.size = size
        self.scale = scale
        guard let context = CGContext(
            data: nil,
            width: Int((size.width * scale).rounded()),
            height: Int((size.height * scale).rounded()),
            bitsPerComponent: 8,
            bytesPerRow: 0,
            space: CGColorSpace(name: CGColorSpace.sRGB) ?? CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGImageAlphaInfo.premultipliedFirst.rawValue | CGBitmapInfo.byteOrder32Little.rawValue
        ) else { return nil }
        self.context = context
        context.translateBy(x: 0, y: size.height * scale)
        context.scaleBy(x: scale, y: -scale)
        context.setFillColor(background.cgColor)
        context.fill(CGRect(origin: .zero, size: size))
    }

    /// Draws `image` (pixels at `scale`) with its top-left corner at `origin`.
    func draw(_ image: CGImage, at origin: CGPoint, size: CGSize? = nil) {
        let size = size ?? CGSize(width: CGFloat(image.width) / scale, height: CGFloat(image.height) / scale)
        context.saveGState()
        context.translateBy(x: origin.x, y: origin.y + size.height)
        context.scaleBy(x: 1, y: -1)
        context.draw(image, in: CGRect(origin: .zero, size: size))
        context.restoreGState()
    }

    enum Alignment { case left, center }

    /// One line of text with its baseline at `y`.
    func text(_ string: String, at point: CGPoint, size: CGFloat, weight: NSFont.Weight = .regular, color: NSColor,
              alignment: Alignment = .left, monospacedDigits: Bool = false) {
        var font = NSFont.systemFont(ofSize: size, weight: weight)
        if monospacedDigits { font = NSFont.monospacedDigitSystemFont(ofSize: size, weight: weight) }
        let attributed = NSAttributedString(string: string, attributes: [.font: font, .foregroundColor: color])
        let line = CTLineCreateWithAttributedString(attributed)
        let width = CTLineGetTypographicBounds(line, nil, nil, nil)
        let x = alignment == .center ? point.x - CGFloat(width) / 2 : point.x
        context.saveGState()
        context.textMatrix = .identity
        context.translateBy(x: x, y: point.y)
        context.scaleBy(x: 1, y: -1)
        context.textPosition = .zero
        CTLineDraw(line, context)
        context.restoreGState()
    }

    func fill(_ rect: CGRect, color: NSColor) {
        context.setFillColor(color.cgColor)
        context.fill(rect)
    }

    var image: CGImage? { context.makeImage() }

    func write(to url: URL) {
        guard let image else { return }
        PreviewSnapshot.write(image, to: url)
    }
}
