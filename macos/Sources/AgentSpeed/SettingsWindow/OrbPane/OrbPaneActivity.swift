import AppKit
import SwiftUI

/// Whether the floating orb pane can be seen right now (its window open, on screen, not miniaturized or covered, and
/// this pane selected) and whether its window is key. Thumbnails and style cards draw only while it is visible.
struct OrbPaneActivity: Equatable {
    var visible = false
    var key = false
}

/// Reports `OrbPaneActivity` from the AppKit window the pane lives in. The tab controller takes a pane out of the
/// window when another pane is selected, which this sees as leaving the window.
struct OrbPaneActivityReader: NSViewRepresentable {
    @Binding var activity: OrbPaneActivity

    func makeNSView(context: Context) -> ReaderView {
        let view = ReaderView()
        view.onChange = { activity in
            DispatchQueue.main.async { if self.activity != activity { self.activity = activity } }
        }
        return view
    }

    func updateNSView(_ view: ReaderView, context: Context) {}

    final class ReaderView: NSView {
        var onChange: ((OrbPaneActivity) -> Void)?
        nonisolated(unsafe) private var tokens: [NSObjectProtocol] = []

        override func viewDidMoveToWindow() {
            super.viewDidMoveToWindow()
            tokens.forEach(NotificationCenter.default.removeObserver)
            tokens.removeAll()
            if let window {
                let names: [Notification.Name] = [
                    NSWindow.didChangeOcclusionStateNotification, NSWindow.didMiniaturizeNotification,
                    NSWindow.didDeminiaturizeNotification, NSWindow.willCloseNotification,
                    NSWindow.didBecomeKeyNotification, NSWindow.didResignKeyNotification,
                ]
                for name in names {
                    let closing = name == NSWindow.willCloseNotification
                    tokens.append(NotificationCenter.default.addObserver(forName: name, object: window, queue: .main) { [weak self] _ in
                        MainActor.assumeIsolated { self?.report(closing: closing) }
                    })
                }
            }
            report(closing: false)
        }

        override func hitTest(_ point: NSPoint) -> NSView? { nil }

        private func report(closing: Bool) {
            guard let window, !closing else {
                onChange?(OrbPaneActivity())
                return
            }
            let visible = window.isVisible && !window.isMiniaturized && window.occlusionState.contains(.visible)
            onChange?(OrbPaneActivity(visible: visible, key: window.isKeyWindow))
        }

        deinit {
            tokens.forEach(NotificationCenter.default.removeObserver)
        }
    }
}
