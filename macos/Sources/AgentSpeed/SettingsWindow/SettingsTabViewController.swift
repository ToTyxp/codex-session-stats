import AppKit
import SwiftUI

/// Toolbar-style tabs, one hosting controller per pane. Owns the window height: each pane reports its natural
/// height and the window follows it, animated and anchored at the top edge.
@MainActor
final class SettingsTabViewController: NSTabViewController {
    var onSelect: (@MainActor (SettingsPane) -> Void)?

    private let model: AppModel
    private var heights: [SettingsPane: CGFloat] = [:]
    private var resizePending = false
    private var titleLoop: ObservationLoop?
    /// Room kept between a pane that fills the screen and the screen's edges.
    private static let screenMargin: CGFloat = 16

    init(model: AppModel) {
        self.model = model
        super.init(nibName: nil, bundle: nil)
        tabStyle = .toolbar
        transitionOptions = [.crossfade, .allowUserInteraction]
        canPropagateSelectedChildViewControllerTitle = true
        for pane in SettingsPane.allCases {
            let root = SettingsPaneHost(pane: pane, model: model) { [weak self] height in
                self?.pane(pane, didReportHeight: height)
            }
            let host = NSHostingController(rootView: root)
            host.sizingOptions = []
            let item = NSTabViewItem(viewController: host)
            item.identifier = pane.rawValue
            item.image = NSImage(systemSymbolName: pane.symbol, accessibilityDescription: nil)
            addTabViewItem(item)
        }
        titleLoop = ObservationLoop { [weak self] in self?.applyTitles() }
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("init(coder:) is not supported")
    }

    var selectedPane: SettingsPane {
        let panes = SettingsPane.allCases
        return panes.indices.contains(selectedTabViewItemIndex) ? panes[selectedTabViewItemIndex] : .general
    }

    func select(_ pane: SettingsPane) {
        guard let index = SettingsPane.allCases.firstIndex(of: pane), index != selectedTabViewItemIndex else { return }
        selectedTabViewItemIndex = index
    }

    /// Localized tab labels and window title; re-run whenever the language changes.
    func applyTitles() {
        let l10n = model.l10n
        for (pane, item) in zip(SettingsPane.allCases, tabViewItems) {
            let title = l10n(pane.titleKey)
            let label = l10n(pane.tabTitleKey)
            if item.label != label { item.label = label }
            if item.viewController?.title != title { item.viewController?.title = title }
        }
        let title = l10n(selectedPane.titleKey)
        if isViewLoaded, let window = view.window, window.title != title { window.title = title }
    }

    override func tabView(_ tabView: NSTabView, didSelect tabViewItem: NSTabViewItem?) {
        super.tabView(tabView, didSelect: tabViewItem)
        applyTitles()
        onSelect?(selectedPane)
        scheduleResize()
    }

    /// Sizes the window so its content area matches the selected pane, keeping the top edge in place.
    func fitWindowToSelectedPane(animated: Bool) {
        guard isViewLoaded, let window = view.window, let height = heights[selectedPane] else { return }
        var target = NSSize(width: SettingsPaneView.width(for: selectedPane), height: height.rounded(.up))
        if let visible = (window.screen ?? NSScreen.main)?.visibleFrame {
            let chrome = window.frame.height - view.frame.height
            target.height = min(target.height, (visible.height - chrome - Self.screenMargin).rounded(.down))
        }
        let current = view.frame.size
        let deltaHeight = target.height - current.height
        let deltaWidth = target.width - current.width
        guard abs(deltaHeight) >= 0.5 || abs(deltaWidth) >= 0.5 else { return }

        var frame = window.frame
        frame.size.height += deltaHeight
        frame.size.width += deltaWidth
        frame.origin.y -= deltaHeight
        if let visible = window.screen?.visibleFrame, frame.minY < visible.minY {
            frame.origin.y = min(visible.minY, visible.maxY - frame.height)
        }

        guard animated, window.isVisible, !model.accessibility.reduceMotion else {
            window.setFrame(frame, display: true)
            return
        }
        NSAnimationContext.runAnimationGroup { context in
            context.duration = min(max(window.animationResizeTime(frame), 0.16), 0.28)
            context.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
            window.animator().setFrame(frame, display: true)
        }
    }

    private func pane(_ pane: SettingsPane, didReportHeight height: CGFloat) {
        guard height > 0, heights[pane] != height else { return }
        heights[pane] = height
        if pane == selectedPane { scheduleResize() }
    }

    /// Coalesces the height reports of one layout pass into a single window resize.
    private func scheduleResize() {
        guard !resizePending else { return }
        resizePending = true
        DispatchQueue.main.async { [weak self] in
            guard let self else { return }
            self.resizePending = false
            self.fitWindowToSelectedPane(animated: true)
        }
    }
}
