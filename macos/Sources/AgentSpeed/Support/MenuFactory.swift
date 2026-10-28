import AppKit

/// An `NSMenuItem` that runs a closure.
@MainActor
final class ActionMenuItem: NSMenuItem {
    private let handler: @MainActor () -> Void

    init(_ title: String, key: String = "", state: NSControl.StateValue = .off, handler: @escaping @MainActor () -> Void) {
        self.handler = handler
        super.init(title: title, action: #selector(invoke), keyEquivalent: key)
        target = self
        self.state = state
    }

    @available(*, unavailable)
    required init(coder: NSCoder) {
        fatalError("init(coder:) is not supported")
    }

    @objc private func invoke() {
        handler()
    }
}

/// Native menus shared by the orb's context menu and the menu bar quick menu. Built on demand so they are always
/// in the current language and state.
@MainActor
enum MenuFactory {
    /// The orb's context menu (DESIGN 2b, Context menu).
    static func orbContextMenu(model: AppModel, orbId: String) -> NSMenu {
        OrbMenus.contextMenu(model: model, orbId: orbId)
    }

    static func quickMenu(model: AppModel) -> NSMenu {
        let l10n = model.l10n
        let menu = NSMenu()
        menu.autoenablesItems = false
        if let release = model.updates.availableRelease {
            let update = ActionMenuItem(l10n("update.menu", release.version.description)) { [weak model] in
                model?.commands?.showUpdate()
            }
            update.image = accentDot
            menu.addItem(update)
            menu.addItem(.separator())
        }
        menu.addItem(ActionMenuItem(l10n("menu.dashboard")) { [weak model] in model?.commands?.showDashboard() })
        menu.addItem(historyItem(model: model))
        if let report = ReportMenu.item(model: model) {
            menu.addItem(.separator())
            menu.addItem(report)
        }
        menu.addItem(.separator())
        menu.addItem(orbVisibilityItem(model: model))
        OrbMenus.addQuickItems(to: menu, model: model)
        menu.addItem(.separator())
        menu.addItem(effectsItem(model: model))
        let preview = ActionMenuItem(l10n(model.isPreviewing ? "settings.effects.previewing" : "settings.effects.preview")) { [weak model] in
            model?.previewEffects()
        }
        preview.isEnabled = !model.isPreviewing
        menu.addItem(preview)
        menu.addItem(.separator())
        menu.addItem(settingsItem(model: model))
        menu.addItem(ActionMenuItem(l10n("menu.about")) { [weak model] in model?.commands?.showSettings(.about) })
        let check = ActionMenuItem(l10n("mac.update.checkMenu")) { [weak model] in model?.commands?.checkForUpdates() }
        check.isEnabled = !model.updates.isBusy && model.settings.networkEnabled
        if !model.settings.networkEnabled { check.toolTip = l10n("settings.network.offHint") }
        menu.addItem(check)
        menu.addItem(.separator())
        menu.addItem(quitItem(model: model))
        return menu
    }

    /// The small accent dot that marks an available update.
    static var accentDot: NSImage {
        let image = NSImage(size: NSSize(width: 8, height: 8), flipped: false) { rect in
            NSColor.controlAccentColor.setFill()
            NSBezierPath(ovalIn: rect.insetBy(dx: 0.5, dy: 0.5)).fill()
            return true
        }
        image.accessibilityDescription = nil
        return image
    }

    static func effectsItem(model: AppModel) -> NSMenuItem {
        let item = NSMenuItem(title: model.l10n("menu.effects"), action: nil, keyEquivalent: "")
        let submenu = NSMenu()
        for level in EffectsLevel.allCases {
            let state: NSControl.StateValue = model.settings.effects == level ? .on : .off
            submenu.addItem(ActionMenuItem(model.l10n(level.titleKey), state: state) { [weak model] in
                model?.settings.effects = level
            })
        }
        item.submenu = submenu
        return item
    }

    /// The master switch (DESIGN 2b): `menu.showOrb` / `menu.hideOrb` with one orb, the plural with several.
    static func orbVisibilityItem(model: AppModel) -> NSMenuItem {
        let visible = model.orbs.masterVisible
        let plural = model.orbs.orbs.count > 1
        let key = visible ? (plural ? "menu.hideOrbs" : "menu.hideOrb") : (plural ? "menu.showOrbs" : "menu.showOrb")
        return ActionMenuItem(model.l10n(key)) { [weak model] in
            model?.commands?.setOrbsVisible(!visible)
        }
    }

    static func historyItem(model: AppModel) -> NSMenuItem {
        ActionMenuItem(model.l10n("history.open"), key: "y") { [weak model] in model?.commands?.showHistory(source: nil) }
    }

    static func settingsItem(model: AppModel) -> NSMenuItem {
        ActionMenuItem(model.l10n("menu.settings"), key: ",") { [weak model] in model?.commands?.showSettings(nil) }
    }

    static func quitItem(model: AppModel) -> NSMenuItem {
        ActionMenuItem(model.l10n("menu.quit"), key: "q") { [weak model] in model?.commands?.quit() }
    }
}
