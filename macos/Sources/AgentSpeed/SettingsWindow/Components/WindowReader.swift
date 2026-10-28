import AppKit
import SwiftUI

/// Weak handle to the window hosting a SwiftUI view, for presenting sheets on it.
@MainActor
final class HostWindow {
    weak var window: NSWindow?
}

/// Records the hosting window into a `HostWindow` whenever the view moves between windows.
struct WindowReader: NSViewRepresentable {
    let host: HostWindow

    func makeNSView(context: Context) -> ReaderView {
        let view = ReaderView()
        view.host = host
        return view
    }

    func updateNSView(_ view: ReaderView, context: Context) {
        view.host = host
        host.window = view.window
    }

    final class ReaderView: NSView {
        var host: HostWindow?

        override func viewDidMoveToWindow() {
            super.viewDidMoveToWindow()
            host?.window = window
        }
    }
}
