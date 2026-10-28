import AppKit

/// A main menu that is never shown (the app is a menu bar agent) but gives every window the standard editing and
/// window shortcuts (⌘C, ⌘V, ⌘A, ⌘Z, ⌘W, ⌘Q) plus Settings (⌘,) and Usage History (⌘Y).
@MainActor
enum MainMenu {
    static func install(model: AppModel) {
        let l10n = model.l10n
        let main = NSMenu()

        let appItem = NSMenuItem()
        let appMenu = NSMenu()
        appMenu.addItem(withTitle: l10n("menu.settings"), action: #selector(AppDelegate.openSettingsFromMenu(_:)), keyEquivalent: ",")
        appMenu.addItem(withTitle: l10n("history.open"), action: #selector(AppDelegate.openHistoryFromMenu(_:)), keyEquivalent: "y")
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: l10n("menu.quit"), action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        appItem.submenu = appMenu
        main.addItem(appItem)

        let editItem = NSMenuItem()
        let edit = NSMenu(title: "Edit")
        edit.addItem(withTitle: "Undo", action: Selector(("undo:")), keyEquivalent: "z")
        let redo = edit.addItem(withTitle: "Redo", action: Selector(("redo:")), keyEquivalent: "z")
        redo.keyEquivalentModifierMask = [.command, .shift]
        edit.addItem(.separator())
        edit.addItem(withTitle: "Cut", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        edit.addItem(withTitle: "Copy", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        edit.addItem(withTitle: "Paste", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        edit.addItem(withTitle: "Select All", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        editItem.submenu = edit
        main.addItem(editItem)

        let windowItem = NSMenuItem()
        let window = NSMenu(title: "Window")
        window.addItem(withTitle: "Close", action: #selector(NSWindow.performClose(_:)), keyEquivalent: "w")
        window.addItem(withTitle: "Minimize", action: #selector(NSWindow.performMiniaturize(_:)), keyEquivalent: "m")
        windowItem.submenu = window
        main.addItem(windowItem)

        NSApp.mainMenu = main
    }
}
