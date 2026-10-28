#if DEBUG
import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit

/// `--orbs-demo DIR` (debug builds): seeds three floating orbs (all sources / Claude Code + Claude Desktop / Codex) in a
/// scratch defaults suite, shows them on screen over a neutral backdrop, then drags one against another, splits and
/// merges, and captures only that screen region with `screencapture` into DIR. Quits when done.
@MainActor
enum OrbsDemoHarness {
    private static var manager: OrbWindowManager?
    private static var backdrop: NSWindow?

    static func start(into directory: URL) {
        Task { @MainActor in
            await run(into: directory)
            NSApp.terminate(nil)
        }
    }

    private static func run(into directory: URL) async {
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        guard let screen = NSScreen.screens.first, let display = screen.orbDisplayUUID else { return }
        let suite = "AgentSpeedOrbsDemo"
        UserDefaults.standard.removePersistentDomain(forName: suite)
        let defaults = UserDefaults(suiteName: suite) ?? .standard
        defaults.set(seed(screen: screen, display: display).data(), forKey: OrbsController.documentKey)
        defaults.set(true, forKey: "orbsVisible")
        if ProcessInfo.processInfo.environment["AGENT_SPEED_DEMO_LIGHT"] == "1" { defaults.set("light", forKey: "appearance") }
        else { defaults.set("dark", forKey: "appearance") }

        let model = AppModel(
            settings: AppSettings(defaults: defaults),
            updates: UpdateController(configuration: .current(environment: [:]), store: UpdateStore(defaults: defaults))
        )
        model.settings.language = .zhHant
        model.activate(live: false)
        let region = demoRegion(screen: screen)
        showBackdrop(region, dark: model.settings.appearance != .light)
        let publisher = Task { @MainActor in
            var t = 0.0
            while !Task.isCancelled {
                let speed = model.settings.redline * (0.55 + 0.25 * sin(t * 0.7))
                model.store.publishLive(PreviewData.snapshot(speed: speed, redline: model.settings.redline))
                t += 0.5
                try? await Task.sleep(for: .seconds(0.5))
            }
        }
        defer { publisher.cancel() }
        let manager = OrbWindowManager(model: model)
        self.manager = manager
        try? await Task.sleep(for: .seconds(3))
        capture(region, "1-three-orbs", into: directory)

        let ids = model.orbs.orbs.map(\.id)
        guard ids.count == 3, let mover = manager.window(for: ids[2]), let target = manager.window(for: ids[1]) else { return }
        // Drag the Codex orb toward the left of the pair orb: guides and magnetic pull, then release.
        let start = mover.center
        let goal = CGPoint(x: target.center.x - target.radius - mover.radius - 22, y: target.center.y - 6)
        for step in 1...24 {
            let u = CGFloat(step) / 24
            mover.debugDrag(to: CGPoint(x: start.x + (goal.x - start.x) * u, y: start.y + (goal.y - start.y) * u))
            try? await Task.sleep(for: .seconds(1.0 / 60))
        }
        try? await Task.sleep(for: .seconds(0.25))
        capture(region, "2-dragging", into: directory)
        mover.debugRelease()
        try? await Task.sleep(for: .seconds(1.0))
        capture(region, "3-snapped", into: directory)

        model.orbs.split(ids[1])
        try? await Task.sleep(for: .seconds(0.22))
        capture(region, "4-split-motion", into: directory)
        try? await Task.sleep(for: .seconds(1.1))
        capture(region, "5-split-chips-undo", into: directory)

        model.orbs.merge(keeping: ids[0])
        try? await Task.sleep(for: .seconds(0.15))
        capture(region, "6-merge-motion", into: directory)
        try? await Task.sleep(for: .seconds(1.2))
        capture(region, "7-merged", into: directory)

        model.orbs.undo()
        try? await Task.sleep(for: .seconds(1.2))
        capture(region, "8-undo", into: directory)
        model.orbs.highlightedOrb = model.orbs.orbs.last?.id
        try? await Task.sleep(for: .seconds(0.5))
        capture(region, "9-highlight", into: directory)

        try? report(model: model, manager: manager).write(to: directory.appendingPathComponent("orbs-demo.txt"), atomically: true, encoding: .utf8)
        manager.shutdown()
        backdrop?.orderOut(nil)
    }

    /// The context and quick menus and every orb's accessibility text, for review.
    private static func report(model: AppModel, manager: OrbWindowManager) -> String {
        func dump(_ menu: NSMenu, indent: String = "") -> String {
            menu.items.map { item in
                if item.isSeparatorItem { return indent + "—" }
                let state = item.state == .on ? "✓ " : ""
                let enabled = item.isEnabled ? "" : " (disabled\(item.toolTip.map { ": " + $0 } ?? ""))"
                let line = indent + state + (item.attributedTitle?.string ?? item.title) + enabled
                return item.submenu.map { line + "\n" + dump($0, indent: indent + "    ") } ?? line
            }.joined(separator: "\n")
        }
        var text = "Quick menu:\n" + dump(MenuFactory.quickMenu(model: model)) + "\n\n"
        for orb in model.orbs.orbs {
            guard let window = manager.window(for: orb.id) else { continue }
            text += "Orb \(model.orbs.displayName(of: orb.id)):\n"
            text += "  a11y label: \(window.host.accessibilityLabel() ?? "")\n"
            text += "  a11y value: \(window.host.accessibilityValue() as? String ?? "")\n"
            text += "  a11y help: \(window.host.accessibilityHelp() ?? "")\n"
            text += "  a11y actions: \((window.host.accessibilityCustomActions() ?? []).map(\.name).joined(separator: ", "))\n"
            text += dump(MenuFactory.orbContextMenu(model: model, orbId: orb.id), indent: "  ") + "\n\n"
        }
        return text
    }

    /// Three orbs near the top-right corner of the main display.
    private static func seed(screen: NSScreen, display: String) -> OrbsDocument {
        let visible = screen.visibleFrame
        let all = OrbConfig(id: UUID().uuidString, sources: .all, style: .led, size: 148)
        let pair = OrbConfig(id: UUID().uuidString, sources: .explicit([.claudeCode, .claudeDesktop]), style: .needle, size: 148)
        let codex = OrbConfig(id: UUID().uuidString, sources: .explicit([.codex]), style: .liquid, size: 112)
        var document = OrbsDocument(orbs: [all, pair, codex])
        let arrangement = OrbsController.arrangementKey
        let a = OrbSnap.defaultCenter(radius: 74, in: visible)
        let b = CGPoint(x: a.x - 148 - 12, y: a.y)
        let c = CGPoint(x: a.x - 40, y: a.y - 74 - 56 - 70)
        for (orb, center) in [(all, a), (pair, b), (codex, c)] {
            let record = OrbPositionRecord(center: center, radius: CGFloat(orb.size) / 2, visible: visible)
            document.setEntry(OrbLayoutEntry(display: display, record: record), orb: orb.id, arrangement: arrangement)
        }
        return document
    }

    private static func demoRegion(screen: NSScreen) -> CGRect {
        let visible = screen.visibleFrame
        return CGRect(x: visible.maxX - 760, y: visible.maxY - 470, width: 760, height: 470).intersection(visible)
    }

    private static func showBackdrop(_ region: CGRect, dark: Bool) {
        let window = NSWindow(contentRect: region, styleMask: [.borderless], backing: .buffered, defer: false)
        window.isReleasedWhenClosed = false
        window.ignoresMouseEvents = true
        window.level = .floating
        window.backgroundColor = dark ? NSColor(srgbRed: 0.16, green: 0.2, blue: 0.27, alpha: 1)
            : NSColor(srgbRed: 0.78, green: 0.83, blue: 0.9, alpha: 1)
        window.orderFrontRegardless()
        backdrop = window
    }

    private static func capture(_ rect: CGRect, _ name: String, into directory: URL) {
        guard let screen = NSScreen.screens.first else { return }
        let top = screen.frame.maxY - rect.maxY
        let region = "\(Int(rect.minX)),\(Int(top)),\(Int(rect.width)),\(Int(rect.height))"
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/sbin/screencapture")
        process.arguments = ["-x", "-R", region, directory.appendingPathComponent("orbs-\(name).png").path]
        try? process.run()
        process.waitUntilExit()
    }
}
#endif
