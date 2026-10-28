import AppKit

/// The app has no main menu, so the settings window handles the standard key equivalents itself.
@MainActor
final class SettingsWindow: NSWindow {
    var onQuit: (@MainActor () -> Void)?

    override func performKeyEquivalent(with event: NSEvent) -> Bool {
        let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
        guard flags == .command, let key = event.charactersIgnoringModifiers?.lowercased() else {
            return super.performKeyEquivalent(with: event)
        }
        switch key {
        case "w":
            performClose(nil)
            return true
        case "q":
            onQuit?()
            return true
        case "c":
            if NSApp.sendAction(#selector(NSText.copy(_:)), to: nil, from: self) { return true }
        case "a":
            if NSApp.sendAction(#selector(NSText.selectAll(_:)), to: nil, from: self) { return true }
        default:
            break
        }
        return super.performKeyEquivalent(with: event)
    }
}
