import AppKit
import SwiftUI

/// The compact update window (UPDATES section 4): sized to its content and anchored at its top edge, so the
/// buttons, progress and error states swap in place.
@MainActor
final class UpdateWindowController: NSObject, NSWindowDelegate {
    static let width: CGFloat = 460

    private let model: AppModel
    private var window: NSWindow?
    private var contentHeight: CGFloat = 0
    private var titleLoop: ObservationLoop?

    init(model: AppModel) {
        self.model = model
    }

    func show() {
        let window = ensureWindow()
        if !window.isVisible {
            window.contentView?.layoutSubtreeIfNeeded()
            fit(animated: false)
            window.center()
        }
        NSApp.activate()
        window.makeKeyAndOrderFront(nil)
        if !NSApp.isActive { window.orderFrontRegardless() }
    }

    func close() {
        window?.close()
    }

    private func ensureWindow() -> NSWindow {
        if let window { return window }
        let root = UpdateView(model: model) { [weak self] in
            self?.close()
        }
        .onGeometryChange(for: CGFloat.self) { proxy in
            proxy.size.height
        } action: { [weak self] height in
            self?.contentDidChange(height: height)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .appEnvironment(model)

        let host = NSHostingController(rootView: root)
        host.sizingOptions = []
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: Self.width, height: 240),
            styleMask: [.titled, .closable, .fullSizeContentView],
            backing: .buffered,
            defer: true
        )
        window.contentViewController = host
        window.titlebarAppearsTransparent = true
        window.titleVisibility = .hidden
        window.isMovableByWindowBackground = true
        window.isReleasedWhenClosed = false
        window.isRestorable = false
        window.tabbingMode = .disallowed
        window.collectionBehavior = [.moveToActiveSpace, .fullScreenAuxiliary]
        window.standardWindowButton(.miniaturizeButton)?.isHidden = true
        window.standardWindowButton(.zoomButton)?.isHidden = true
        window.delegate = self
        self.window = window
        titleLoop = ObservationLoop { [weak self] in
            guard let self, let window = self.window else { return }
            let title = self.model.l10n("mac.update.windowTitle")
            if window.title != title { window.title = title }
        }
        return window
    }

    private func contentDidChange(height: CGFloat) {
        guard height > 0, abs(height - contentHeight) >= 0.5 else { return }
        contentHeight = height
        DispatchQueue.main.async { [weak self] in self?.fit(animated: true) }
    }

    /// Resizes the window to the content height, keeping the top edge where it is.
    private func fit(animated: Bool) {
        guard let window, contentHeight > 0 else { return }
        let target = window.frameRect(forContentRect: NSRect(x: 0, y: 0, width: Self.width, height: contentHeight.rounded(.up)))
        var frame = window.frame
        guard abs(frame.height - target.height) >= 0.5 || abs(frame.width - target.width) >= 0.5 else { return }
        frame.origin.y += frame.height - target.height
        frame.size = target.size
        if let visible = window.screen?.visibleFrame, frame.minY < visible.minY {
            frame.origin.y = min(visible.minY, visible.maxY - frame.height)
        }
        guard animated, window.isVisible, !model.accessibility.reduceMotion else {
            window.setFrame(frame, display: true)
            return
        }
        NSAnimationContext.runAnimationGroup { context in
            context.duration = 0.22
            context.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
            window.animator().setFrame(frame, display: true)
        }
    }
}
