import AgentSpeedCore
import AppKit

/// The quick menu's live reporting item (DESIGN 11, Menu): only while reporting is on, with a status dot and a
/// submenu to pause, resume, open the leaderboard or the settings.
@MainActor
enum ReportMenu {
    static func item(model: AppModel) -> NSMenuItem? {
        let report = model.report
        guard report.isOn else { return nil }
        let l10n = model.l10n
        let paused = report.isPaused
        let title = paused ? l10n("report.menu.paused") : l10n("report.menu.on", report.serviceName ?? report.destinationHost ?? "")
        let item = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        item.image = dot(report.status.tone)
        let submenu = NSMenu()
        submenu.autoenablesItems = false
        if paused {
            submenu.addItem(ActionMenuItem(l10n("report.resume")) { [weak report] in report?.resume() })
        } else {
            submenu.addItem(ActionMenuItem(l10n("report.pause.hour")) { [weak report] in report?.pause(for: 3600) })
            submenu.addItem(ActionMenuItem(l10n("report.pause.indefinite")) { [weak report] in report?.pause(for: nil) })
        }
        if let leaderboard = report.discovery?.leaderboard, leaderboard.scheme?.lowercased() == "https" {
            submenu.addItem(.separator())
            submenu.addItem(ActionMenuItem(l10n("report.service.leaderboard")) { NSWorkspace.shared.open(leaderboard) })
        }
        submenu.addItem(.separator())
        submenu.addItem(ActionMenuItem(l10n("report.menu.settings")) { [weak model] in model?.commands?.showSettings(.report) })
        item.submenu = submenu
        return item
    }

    /// A 7 pt dot in the status color; its meaning is always in the item's text.
    static func dot(_ tone: ReportStatus.Tone) -> NSImage {
        let color: NSColor = switch tone {
        case .green: .systemGreen
        case .gray: .tertiaryLabelColor
        case .amber: .systemOrange
        case .red: .systemRed
        }
        let image = NSImage(size: NSSize(width: 8, height: 8), flipped: false) { rect in
            color.setFill()
            NSBezierPath(ovalIn: rect.insetBy(dx: 0.5, dy: 0.5)).fill()
            return true
        }
        image.accessibilityDescription = nil
        return image
    }
}
