import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit
import CoreImage

/// One composed desktop for the name-plate previews: a busy wallpaper, real orbs from `OrbPreviewStage`, and their
/// plates placed by the real solver and drawn by the real renderer over a blurred copy of what is behind them (the
/// stand-in for the live `NSVisualEffectView`). Scene points are y-down; the solver and the plates work y-up.
@MainActor
final class NameplatePreviewSheet {
    struct Orb {
        var config: OrbConfig
        /// Center in the sheet (points, y down).
        var center: CGPoint
        var fraction: Double = 0.55
        var unavailable: Set<SourceKind> = []
        var hovered = false
        var increaseContrast = false
        var reduceTransparency = false
        /// The orb's opacity on the desktop (55 % when all its sources are unavailable).
        var alpha: CGFloat = 1
        var showsPlate = true
        var undo: String?
    }

    let model: AppModel
    let size: CGSize
    let dark: Bool
    let canvas: PreviewCanvas
    private(set) var orbs: [Orb] = []

    init?(model: AppModel, size: CGSize, dark: Bool, seed: UInt64) {
        guard let canvas = PreviewCanvas(size: size, background: .black) else { return nil }
        self.model = model
        self.size = size
        self.dark = dark
        self.canvas = canvas
        NameplateWallpaper.draw(in: canvas.context, size: size, dark: dark, seed: seed)
    }

    func add(_ orb: Orb) {
        orbs.append(orb)
    }

    // MARK: Orbs

    func drawOrbs() {
        let redline = model.settings.redline
        for orb in orbs {
            let config = orb.config
            let stage = OrbPreviewStage(model: model, style: config.style, diameter: CGFloat(config.size), dark: dark)
            let known = config.sources.knownSources
            if !known.isEmpty {
                stage.feed.bezel = OrbBezel(arcs: known.map { OrbBezel.Arc(color: $0.tint, available: !orb.unavailable.contains($0)) })
            }
            let live = known.isEmpty ? [SourceKind.codex, .claudeCode] : known.filter { !orb.unavailable.contains($0) }
            stage.feed.set(generating: Array(live.prefix(3)), todayTokens: 1_200_000, agentCount: min(2, live.count))
            if !known.isEmpty, known.allSatisfy({ orb.unavailable.contains($0) }) {
                stage.feed.unavailableTitleKey = "source.notFound"
            }
            stage.increaseContrast = orb.increaseContrast
            stage.reduceTransparency = orb.reduceTransparency
            stage.hovered = orb.hovered
            let value = stage.feed.unavailableTitleKey == nil ? redline * orb.fraction : 0
            stage.reset(value: value)
            stage.run(for: 2.4, at: value)
            if let image = stage.image() {
                let side = stage.canvasSide
                canvas.context.saveGState()
                canvas.context.setAlpha(orb.alpha)
                canvas.draw(image, at: CGPoint(x: orb.center.x - side / 2, y: orb.center.y - side / 2), size: CGSize(width: side, height: side))
                canvas.context.restoreGState()
            }
            stage.stop()
        }
    }

    // MARK: Plates

    private func flipped(_ point: CGPoint) -> CGPoint { CGPoint(x: point.x, y: size.height - point.y) }

    private func disc(_ orb: Orb) -> OrbDisc {
        let scale: CGFloat = orb.hovered && !model.accessibility.reduceMotion ? 1.03 : 1
        return OrbDisc(center: flipped(orb.center), radius: CGFloat(orb.config.size) * scale / 2)
    }

    func content(_ orb: Orb) -> OrbNameplateContent {
        let namer = model.orbs.namer(model.l10n)
        let label = NameplateLabel.make(config: orb.config, namer: namer, unavailable: orb.unavailable)
        return OrbNameplateContent(label: label, metrics: NameplateMetrics(diameter: CGFloat(orb.config.size)), namer: namer)
    }

    func items(_ orb: Orb) -> [NameplateFace.Item] {
        let l10n = model.l10n
        let known = orb.config.sources.knownSources
        guard !known.isEmpty else { return [NameplateFace.Item(dot: .spectrum, text: l10n("orbs.sources.all"), role: .text)] }
        return known.map { source in
            guard orb.unavailable.contains(source) else { return NameplateFace.Item(dot: .source(source), text: l10n(source.titleKey), role: .text) }
            return NameplateFace.Item(dot: .unavailable(source), text: l10n(source.titleKey) + " · " + l10n("source.notFound"), role: .secondary)
        }
    }

    /// Places every plate with the solver over the whole sheet (its usable area) and draws them; hovered orbs show
    /// their plate widened to the chip's content.
    func drawPlates(area: CGRect? = nil) {
        let mode = OrbNamesMode.auto
        let requests = orbs.enumerated().map { index, orb in
            let content = content(orb)
            let wants = orb.showsPlate && NameplateVisibility.isDrawn(orb.config, mode: mode, shownOrbs: max(2, orbs.count), isShown: true)
            return NameplateRequest(id: "\(index)", disc: disc(orb), wantsPlate: wants) { content.restWidth(budget: $0) }
        }
        let slots = NameplateLayout.solve(requests, visible: area ?? CGRect(origin: .zero, size: size))
        let backdrop = blurredBackdrop()
        for (index, orb) in orbs.enumerated() {
            guard let slot = slots["\(index)"] else { continue }
            let content = content(orb)
            let face = content.collapsed(budget: slot.budget).face
            let tokens = NameplateTokens(dark: dark, reduceTransparency: orb.reduceTransparency, increaseContrast: orb.increaseContrast)
            var plate = slot.side.rect(around: disc(orb), metrics: content.metrics, width: face.width)
            var scene = NameplateScene(plate: plate, metrics: content.metrics, tokens: tokens, opacity: orb.alpha)
            if orb.hovered, let wide = content.expanded(items(orb), comparedTo: face) {
                plate = slot.side.rect(around: disc(orb), metrics: content.metrics, width: wide.width)
                scene.plate = plate
                scene.add(wide, originX: plate.minX, alpha: 1)
            } else {
                scene.add(face, originX: plate.minX, alpha: 1)
            }
            drawPlate(scene, backdrop: backdrop)
            if let undo = orb.undo {
                drawUndo(undo, below: plate, side: slot.side, metrics: content.metrics, tokens: tokens, backdrop: backdrop)
            }
        }
    }

    private func drawUndo(_ message: String, below plate: CGRect, side: NameplateSide, metrics: NameplateMetrics,
                          tokens: NameplateTokens, backdrop: CGImage?) {
        let typesetter = NameplateTypesetter.shared
        let face = NameplateFace.expanded(
            [NameplateFace.Item(dot: nil, text: message, role: .text), NameplateFace.Item(dot: nil, text: model.l10n("orbs.undo"), role: .accent)],
            metrics: metrics
        ) { typesetter.width($0, size: metrics.fontSize, tracking: $1) }
        let gap = OrbNameplateUndoChip.stackGap
        let y = side == .above ? plate.maxY + gap : plate.minY - gap - metrics.height
        let rect = CGRect(x: plate.midX - face.width / 2, y: y, width: face.width, height: metrics.height)
        var scene = NameplateScene(plate: rect, metrics: metrics, tokens: tokens)
        scene.add(face, originX: rect.minX, alpha: 1)
        drawPlate(scene, backdrop: backdrop)
    }

    private func drawPlate(_ scene: NameplateScene, backdrop: CGImage?) {
        let context = canvas.context
        context.saveGState()
        context.translateBy(x: 0, y: size.height)
        context.scaleBy(x: 1, y: -1)
        if !scene.tokens.solid, let backdrop {
            context.saveGState()
            context.setAlpha(scene.opacity)
            context.addPath(NameplateRenderer.capsule(scene.plate))
            context.clip()
            context.draw(backdrop, in: CGRect(origin: .zero, size: size))
            context.restoreGState()
        }
        NameplateRenderer.draw(scene, in: context)
        context.restoreGState()
    }

    /// What is behind the plates, blurred and slightly saturated like the desktop material.
    private func blurredBackdrop() -> CGImage? {
        guard let image = canvas.image else { return nil }
        let input = CIImage(cgImage: image).clampedToExtent()
        let blurred = input.applyingFilter("CIGaussianBlur", parameters: [kCIInputRadiusKey: 24 * canvas.scale])
            .applyingFilter("CIColorControls", parameters: [kCIInputSaturationKey: 1.4])
            .cropped(to: CGRect(x: 0, y: 0, width: image.width, height: image.height))
        return CIContext(options: [.workingColorSpace: CGColorSpace(name: CGColorSpace.sRGB) as Any])
            .createCGImage(blurred, from: blurred.extent)
    }

    // MARK: Captions

    /// A caption on a band, centered at `x` with its baseline at `y` (y down).
    func caption(_ text: String, x: CGFloat, y: CGFloat, width: CGFloat) {
        let band = CGRect(x: x - width / 2, y: y - 15, width: width, height: 21)
        canvas.fill(band, color: dark ? NSColor(white: 0.06, alpha: 0.86) : NSColor(white: 0.98, alpha: 0.9))
        canvas.text(text, at: CGPoint(x: x, y: y), size: 11, weight: .semibold,
                    color: dark ? NSColor(white: 1, alpha: 0.82) : NSColor(white: 0, alpha: 0.72), alignment: .center)
    }

    func write(to url: URL) {
        guard let image = canvas.image else { return }
        PreviewSnapshot.write(image, to: url)
    }
}
