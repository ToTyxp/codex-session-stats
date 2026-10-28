import AppKit

/// Owns the menu bar item: a status item that hugs its content, showing the image drawn by `StatusItemArtist`.
///
/// Left click toggles the dashboard; right-click, ⌃-click or ⌥-click opens the quick menu anchored under the item.
/// Clicks act on mouse down like native extras.
@MainActor
final class StatusItemController: NSObject, NSMenuDelegate {
    private static let autosaveName = "AgentSpeedStatusItem"

    private let model: AppModel
    private let item: NSStatusItem
    private var accessibility: StatusItemAccessibility?
    private var renderer: StatusItemRenderer?
    private var isHighlighted = false
    private var isMenuScheduled = false

    init(model: AppModel) {
        self.model = model
        item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        super.init()
        item.autosaveName = Self.autosaveName
        item.isVisible = true
        guard let button = item.button else { return }
        configure(button)
        accessibility = StatusItemAccessibility(model: model, button: button)
    }

    var anchorView: NSView? { item.button }

    /// Shows the item pressed while the dashboard is open.
    func setHighlighted(_ highlighted: Bool) {
        isHighlighted = highlighted
        applyHighlight()
        reapplyHighlightAfterTracking()
    }

    // MARK: Setup

    private func configure(_ button: NSStatusBarButton) {
        button.title = ""
        button.image = nil
        button.imagePosition = .noImage
        button.focusRingType = .none
        button.target = self
        button.action = #selector(handleClick(_:))
        button.sendAction(on: [.leftMouseDown, .rightMouseDown])
        button.setAccessibilityLabel(model.l10n("app.name"))

        button.imageScaling = .scaleNone
        button.imagePosition = .imageOnly
        renderer = StatusItemRenderer(model: model, item: item)
    }

    // MARK: Clicks

    @objc private func handleClick(_ sender: NSStatusBarButton) {
        guard item.menu == nil else { return }
        let event = NSApp.currentEvent
        let isSecondary = event?.type == .rightMouseDown || event?.type == .rightMouseUp
            || !(event?.modifierFlags.intersection([.option, .control]).isEmpty ?? true)
        if isSecondary {
            scheduleQuickMenu()
        } else {
            model.commands?.toggleDashboard()
            reapplyHighlightAfterTracking()
        }
    }

    /// Opens the menu once the button has finished tracking the click, so the menu's own tracking loop never nests
    /// inside the button's.
    private func scheduleQuickMenu() {
        guard !isMenuScheduled else { return }
        isMenuScheduled = true
        RunLoop.main.perform(inModes: [.default]) { [weak self] in
            MainActor.assumeIsolated { self?.showQuickMenu() }
        }
    }

    private func showQuickMenu() {
        isMenuScheduled = false
        guard let button = item.button, item.menu == nil else { return }
        let menu = MenuFactory.quickMenu(model: model)
        menu.delegate = self
        item.menu = menu
        button.performClick(nil)
    }

    func menuDidClose(_ menu: NSMenu) {
        guard item.menu === menu else { return }
        item.menu = nil
        reapplyHighlightAfterTracking()
    }

    // MARK: Highlight

    private func applyHighlight() {
        guard let button = item.button, item.menu == nil, button.isHighlighted != isHighlighted else { return }
        button.highlight(isHighlighted)
    }

    /// The button clears its highlight when mouse tracking ends; restore the wanted state right after.
    private func reapplyHighlightAfterTracking() {
        RunLoop.main.perform(inModes: [.default]) { [weak self] in
            MainActor.assumeIsolated { self?.applyHighlight() }
        }
    }
}
