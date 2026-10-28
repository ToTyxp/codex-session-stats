import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit
import Metal
import QuartzCore
import SwiftUI

/// One orb a preview group draws: its feed and style, and where its canvas sits in the group (points, top-left).
struct OrbPreviewSpec: Equatable {
    let id: String
    let feed: OrbFeed
    let style: OrbStyleKind
    let diameter: CGFloat
    /// The orb's canvas (1.5 × the diameter, centered on the disc) in the group's coordinates.
    let frame: CGRect
    /// What the orb may draw into (its card or row).
    let clip: CGRect
    let thumbnail: Bool
    /// The title theme it names levels with; nil = General.
    var theme: TitleTheme?

    static func == (lhs: OrbPreviewSpec, rhs: OrbPreviewSpec) -> Bool {
        lhs.id == rhs.id && lhs.feed === rhs.feed && lhs.style == rhs.style && lhs.diameter == rhs.diameter
            && lhs.frame == rhs.frame && lhs.clip == rhs.clip && lhs.thumbnail == rhs.thumbnail && lhs.theme == rhs.theme
    }
}

/// Several small orbs drawn into one Metal layer with one present per frame: the Settings style cards, or the list
/// thumbnails. Each orb keeps its own animator; the group asks the shared frame clock for the highest class any of
/// them needs, capped, and draws nothing while inactive.
@MainActor
final class OrbPreviewGroup {
    private final class Item {
        let spec: OrbPreviewSpec
        let animator: OrbAnimator
        var content: OrbContent
        var scene = OrbScene(capacity: 1024)
        var loops: [ObservationLoop] = []

        init(spec: OrbPreviewSpec, animator: OrbAnimator, content: OrbContent) {
            self.spec = spec
            self.animator = animator
            self.content = content
        }
    }

    let canvas = OrbGroupCanvasView(frame: .zero)
    var dark: Bool {
        didSet { if dark != oldValue { refreshAll() } }
    }
    var cap: Double {
        didSet { if cap != oldValue { applyRequest() } }
    }
    var isActive = false {
        didSet { if isActive != oldValue { applyRequest() } }
    }

    private weak var model: AppModel?
    private var items: [Item] = []

    init(model: AppModel, dark: Bool, cap: Double) {
        self.model = model
        self.dark = dark
        self.cap = cap
        canvas.count = { [weak self] in self?.items.count ?? 0 }
        canvas.build = { [weak self] index, scale, time in
            guard let self, let model = self.model, self.items.indices.contains(index) else { return nil }
            let item = self.items[index]
            OrbSceneBuilder.build(into: &item.scene, content: item.content, animator: item.animator, model: model,
                                  feed: item.spec.feed, canvasSide: item.spec.frame.width, scale: scale, time: time)
            return (item.scene, item.spec.frame, item.spec.clip)
        }
        canvas.onFrame = { [weak self] in self?.items.forEach { $0.animator.reevaluate() } }
    }

    /// Replaces the orbs (only changed ones are rebuilt).
    func update(_ specs: [OrbPreviewSpec]) {
        guard specs != items.map(\.spec), let model else { return }
        var next: [Item] = []
        for spec in specs {
            if let existing = items.first(where: { $0.spec == spec }) {
                next.append(existing)
                continue
            }
            if let reusable = items.first(where: { $0.spec.id == spec.id && $0.spec.feed === spec.feed && $0.spec.style == spec.style }) {
                let item = Item(spec: spec, animator: reusable.animator, content: reusable.content)
                reusable.loops.forEach { $0.cancel() }
                reusable.loops.removeAll()
                observe(item)
                next.append(item)
                continue
            }
            let animator = OrbAnimator(surface: .preview, style: spec.style)
            animator.attach(model: model, feed: spec.feed, interaction: nil)
            let item = Item(spec: spec, animator: animator, content: content(for: spec))
            observe(item)
            next.append(item)
        }
        for item in items where !next.contains(where: { $0 === item }) {
            item.loops.forEach { $0.cancel() }
            if !next.contains(where: { $0.animator === item.animator }) { item.animator.detach() }
        }
        items = next
        applyRequest()
        canvas.setNeedsFrame()
    }

    func stop() {
        for item in items {
            item.loops.forEach { $0.cancel() }
            item.animator.detach()
        }
        items.removeAll()
        canvas.request = .none
        canvas.count = nil
        canvas.build = nil
        canvas.onFrame = nil
    }

    private func observe(_ item: Item) {
        item.loops.append(ObservationLoop { [weak self, weak item] in
            guard let self, let item else { return }
            item.content = self.content(for: item.spec)
            self.canvas.setNeedsFrame()
        })
        item.loops.append(ObservationLoop { [weak self, weak item] in
            _ = item?.animator.frameClass
            self?.applyRequest()
        })
    }

    private func content(for spec: OrbPreviewSpec) -> OrbContent {
        guard let model else { return .placeholder(diameter: spec.diameter, dark: dark) }
        var content = OrbContentBuilder.content(model: model, feed: spec.feed, diameter: spec.diameter, dark: dark, contrast: false,
                                                style: spec.style, showsReadout: true, theme: spec.theme)
        content.isThumbnail = spec.thumbnail
        _ = (spec.feed.gauge.title, spec.feed.gauge.previousTitle)
        return content
    }

    private func refreshAll() {
        for item in items { item.content = content(for: item.spec) }
        canvas.setNeedsFrame()
    }

    private func applyRequest() {
        let frameClass = isActive ? items.map(\.animator.frameClass).max() ?? .none : .none
        canvas.request = OrbFrameRequest(frameClass: frameClass, cap: cap, countsForCrowd: false)
    }
}

/// The Metal layer of a preview group, driven by the shared frame clock (and drawn once on demand when static).
@MainActor
final class OrbGroupCanvasView: NSView, OrbFrameClient {
    var count: (() -> Int)?
    var build: ((Int, CGFloat, TimeInterval) -> (scene: OrbScene, frame: CGRect, clip: CGRect)?)?
    var onFrame: (() -> Void)?
    var request: OrbFrameRequest = .none {
        didSet { if request != oldValue { OrbFrameClock.shared.update() } }
    }

    private let encoder = OrbGPU.shared.map(OrbEncoder.init)
    private var pendingDraw = false
    nonisolated(unsafe) private var screenObserver: NSObjectProtocol?
    private let pass: MTLRenderPassDescriptor = {
        let pass = MTLRenderPassDescriptor()
        pass.colorAttachments[0].loadAction = .clear
        pass.colorAttachments[0].storeAction = .store
        pass.colorAttachments[0].clearColor = MTLClearColor(red: 0, green: 0, blue: 0, alpha: 0)
        return pass
    }()
    /// The scenes of the frame being drawn, reused frame after frame.
    private var frameItems: [(scene: OrbScene, frame: CGRect, clip: CGRect)] = []

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        layerContentsRedrawPolicy = .never
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("init(coder:) is not supported")
    }

    override func makeBackingLayer() -> CALayer {
        let layer = CAMetalLayer()
        layer.device = OrbGPU.shared?.device
        layer.pixelFormat = OrbGPU.pixelFormat
        layer.framebufferOnly = true
        layer.isOpaque = false
        layer.colorspace = CGColorSpace(name: CGColorSpace.sRGB)
        layer.maximumDrawableCount = 3
        layer.allowsNextDrawableTimeout = true
        return layer
    }

    override var isOpaque: Bool { false }
    override func hitTest(_ point: NSPoint) -> NSView? { nil }

    var frameRequest: OrbFrameRequest {
        guard window != nil, !isHiddenOrHasHiddenAncestor else { return .none }
        return request
    }

    var frameScreen: NSScreen? { window?.screen }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        if let screenObserver { NotificationCenter.default.removeObserver(screenObserver) }
        screenObserver = nil
        if let window {
            OrbFrameClock.shared.register(self)
            screenObserver = NotificationCenter.default.addObserver(
                forName: NSWindow.didChangeScreenNotification, object: window, queue: .main
            ) { [weak self] _ in
                MainActor.assumeIsolated {
                    self?.updateDrawableSize()
                    OrbFrameClock.shared.update()
                }
            }
            updateDrawableSize()
            setNeedsFrame()
        } else {
            OrbFrameClock.shared.unregister(self)
        }
    }

    override func viewDidChangeBackingProperties() {
        super.viewDidChangeBackingProperties()
        updateDrawableSize()
        setNeedsFrame()
    }

    override func setFrameSize(_ newSize: NSSize) {
        super.setFrameSize(newSize)
        updateDrawableSize()
        setNeedsFrame()
    }

    private var backingScale: CGFloat { window?.backingScaleFactor ?? 2 }

    private func updateDrawableSize() {
        guard let layer = layer as? CAMetalLayer else { return }
        let scale = backingScale
        layer.contentsScale = scale
        let size = CGSize(width: (bounds.width * scale).rounded(), height: (bounds.height * scale).rounded())
        if layer.drawableSize != size, size.width > 0, size.height > 0 { layer.drawableSize = size }
    }

    /// Draws one frame soon (static content changed); coalesced within the run loop turn.
    func setNeedsFrame() {
        guard !pendingDraw else { return }
        pendingDraw = true
        DispatchQueue.main.async { [weak self] in
            MainActor.assumeIsolated {
                guard let self else { return }
                self.pendingDraw = false
                guard self.window != nil else { return }
                self.renderFrame(at: Clock.now)
            }
        }
    }

    func renderFrame(at time: TimeInterval) {
        guard let encoder, let count = count?(), count > 0, let build, bounds.width > 0,
              let layer = layer as? CAMetalLayer, let drawable = layer.nextDrawable() else { return }
        let scale = backingScale
        frameItems.removeAll(keepingCapacity: true)
        for index in 0..<count {
            if let item = build(index, scale, time) { frameItems.append(item) }
        }
        pass.colorAttachments[0].texture = drawable.texture
        encoder.render(count: frameItems.count, size: bounds.size, scale: scale, pass: pass, present: drawable) { index in
            let item = frameItems[index]
            return (item.scene, item.frame, item.clip)
        }
        pass.colorAttachments[0].texture = nil
        frameItems.removeAll(keepingCapacity: true)
        onFrame?()
    }

    deinit {
        if let screenObserver { NotificationCenter.default.removeObserver(screenObserver) }
    }
}

/// SwiftUI host of a preview group: the orbs of `specs`, positioned in this view's coordinate space.
struct OrbPreviewGroupView: NSViewRepresentable {
    let model: AppModel
    let specs: [OrbPreviewSpec]
    let cap: Double
    let active: Bool

    @Environment(\.colorScheme) private var colorScheme

    final class Coordinator {
        var group: OrbPreviewGroup?
    }

    func makeCoordinator() -> Coordinator { Coordinator() }

    func makeNSView(context: Context) -> OrbGroupCanvasView {
        let group = OrbPreviewGroup(model: model, dark: colorScheme == .dark, cap: cap)
        context.coordinator.group = group
        group.update(specs)
        group.isActive = active
        return group.canvas
    }

    func updateNSView(_ view: OrbGroupCanvasView, context: Context) {
        guard let group = context.coordinator.group else { return }
        group.dark = colorScheme == .dark
        group.cap = cap
        group.update(specs)
        group.isActive = active
    }

    static func dismantleNSView(_ view: OrbGroupCanvasView, coordinator: Coordinator) {
        coordinator.group?.stop()
        coordinator.group = nil
    }
}
