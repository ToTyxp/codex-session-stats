import AgentSpeedCore
import AgentSpeedOrbModel
import SwiftUI

/// An orb drawn by the real renderer for Settings (DESIGN 5): a list thumbnail or a style card. Live on the shared frame
/// clock at a capped rate while `active`, a still frame in previews (`\.orbStillTime`).
struct OrbPreviewView: View {
    let model: AppModel
    let feed: OrbFeed
    let style: OrbStyleKind
    let diameter: CGFloat
    /// List thumbnails draw only the value and a 1 px bezel (DESIGN 2a, 2b).
    var thumbnail = false
    /// Frames per second at most.
    let cap: Double
    let active: Bool
    /// False when a preview group draws the dynamic layers over this view (only the body is drawn here).
    var dynamic = true
    /// The title theme it names levels with; nil = General.
    var theme: TitleTheme?

    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.orbStillTime) private var stillTime

    private var canvasSide: CGFloat { (diameter * OrbView.canvasScale).rounded(.up) }

    var body: some View {
        let dark = colorScheme == .dark
        let contrast = model.accessibility.increaseContrast
        ZStack {
            OrbBody(
                diameter: diameter,
                canvasSide: canvasSide,
                tokens: dark ? .dark : .light,
                opacity: 1,
                solid: true,
                increaseContrast: contrast,
                style: style,
                rim: feed.bezel == nil
            )
            .equatable()
            .drawingGroup()
            if let stillTime {
                if let image = OrbPreviewStill.image(model: model, feed: feed, style: style, diameter: diameter, dark: dark,
                                                     thumbnail: thumbnail, time: stillTime, theme: theme) {
                    Image(decorative: image, scale: 2)
                }
            } else if dynamic {
                OrbPreviewCanvas(model: model, feed: feed, style: style, diameter: diameter, dark: dark,
                                 thumbnail: thumbnail, cap: cap, active: active, theme: theme)
            }
        }
        .frame(width: canvasSide, height: canvasSide)
        .allowsHitTesting(false)
        .accessibilityHidden(true)
    }
}

/// The live canvas: one presenter per preview, its rate capped and paused while the pane is not visible.
private struct OrbPreviewCanvas: NSViewRepresentable {
    let model: AppModel
    let feed: OrbFeed
    let style: OrbStyleKind
    let diameter: CGFloat
    let dark: Bool
    let thumbnail: Bool
    let cap: Double
    let active: Bool
    let theme: TitleTheme?

    final class Coordinator {
        var presenter: OrbPresenter?
        var theme: TitleTheme?
    }

    func makeCoordinator() -> Coordinator { Coordinator() }

    func makeNSView(context: Context) -> OrbCanvasView {
        let canvas = OrbCanvasView(frame: .zero)
        let presenter = OrbPresenter(model: model, feed: feed, surface: .preview, style: style, diameter: diameter, dark: dark,
                                     interaction: nil, canvas: canvas)
        let thumbnail = thumbnail
        presenter.contentAdjustment = { content in content.isThumbnail = thumbnail }
        presenter.cap = cap
        presenter.isActive = active
        if let theme { presenter.titleTheme = { theme } }
        context.coordinator.theme = theme
        context.coordinator.presenter = presenter
        return canvas
    }

    func updateNSView(_ canvas: OrbCanvasView, context: Context) {
        guard let presenter = context.coordinator.presenter else { return }
        presenter.setStyle(style)
        presenter.diameter = diameter
        presenter.dark = dark
        if presenter.cap != cap { presenter.cap = cap }
        presenter.isActive = active
        if context.coordinator.theme != theme {
            context.coordinator.theme = theme
            presenter.titleTheme = theme.map { value in { value } }
        }
    }

    static func dismantleNSView(_ canvas: OrbCanvasView, coordinator: Coordinator) {
        coordinator.presenter?.stop()
        coordinator.presenter = nil
    }
}

/// Still frames of a Settings preview: the current state of `feed`, settled, rendered offscreen.
@MainActor
enum OrbPreviewStill {
    private static var scene = OrbScene(capacity: 1024)

    static func image(model: AppModel, feed: OrbFeed, style: OrbStyleKind, diameter: CGFloat, dark: Bool, thumbnail: Bool,
                      time: TimeInterval, theme: TitleTheme? = nil) -> CGImage? {
        let animator = OrbAnimator(surface: .preview, style: style)
        animator.primeForStill(model: model, gauge: feed.gauge)
        var content = OrbContentBuilder.content(model: model, feed: feed, diameter: diameter, dark: dark,
                                                contrast: model.accessibility.increaseContrast, style: style, showsReadout: true,
                                                theme: theme)
        content.isThumbnail = thumbnail
        let side = (diameter * OrbView.canvasScale).rounded(.up)
        let start = time + 1
        animator.prewarm(from: start, to: start + 1.5) { step in
            OrbSceneBuilder.build(into: &scene, content: content, animator: animator, model: model, feed: feed,
                                  canvasSide: side, scale: 2, time: step)
        }
        return OrbStill.image(content: content, animator: animator, model: model, feed: feed, canvasSide: side,
                              time: start + 1.5 + 1.0 / 60)
    }
}
