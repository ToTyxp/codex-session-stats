#if DEBUG
import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit

/// `--nameplates-demo DIR` (debug builds): seeds four scattered orbs in a scratch defaults suite over a busy backdrop,
/// drags two of them into a row (plates resolve after the snap settles), hovers one (its plate widens to the chip),
/// splits one (plates fade in after landing, the undo chip stacks beyond the plate), captures each step of that screen
/// region with `screencapture` into DIR, then measures the app's CPU and plate redraws while static with plates and
/// without (printed). Quits when done.
@MainActor
enum NameplatesDemoHarness {
    private static var manager: OrbWindowManager?
    private static var backdrop: NSWindow?
    private static var publishing = true

    static func start(into directory: URL) {
        Task { @MainActor in
            await run(into: directory)
            NSApp.terminate(nil)
        }
    }

    private static func run(into directory: URL) async {
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        guard let screen = NSScreen.screens.first, let display = screen.orbDisplayUUID else { return }
        let suite = "AgentSpeedNameplatesDemo"
        UserDefaults.standard.removePersistentDomain(forName: suite)
        let defaults = UserDefaults(suiteName: suite) ?? .standard
        let region = demoRegion(screen: screen)
        let (document, ids) = seed(screen: screen, display: display, region: region)
        defaults.set(document.data(), forKey: OrbsController.documentKey)
        defaults.set(true, forKey: "orbsVisible")
        let dark = ProcessInfo.processInfo.environment["AGENT_SPEED_DEMO_LIGHT"] != "1"
        defaults.set(dark ? "dark" : "light", forKey: "appearance")

        let model = AppModel(
            settings: AppSettings(defaults: defaults),
            updates: UpdateController(configuration: .current(environment: [:]), store: UpdateStore(defaults: defaults))
        )
        model.settings.language = .zhHant
        model.activate(live: false)
        showBackdrop(region, dark: dark)
        publishing = true
        let publisher = Task { @MainActor in
            var t = 0.0
            while !Task.isCancelled {
                if publishing {
                    let speed = model.settings.redline * (0.45 + 0.2 * sin(t * 0.7))
                    model.store.publishLive(PreviewData.snapshot(speed: speed, redline: model.settings.redline))
                }
                t += 0.5
                try? await Task.sleep(for: .seconds(0.5))
            }
        }
        defer { publisher.cancel() }
        let manager = OrbWindowManager(model: model)
        self.manager = manager
        try? await Task.sleep(for: .seconds(2.5))
        capture(region, "1-scattered", into: directory)

        guard let anchor = manager.window(for: ids[0]), let second = manager.window(for: ids[1]),
              let third = manager.window(for: ids[2]) else { return }
        await drag(second, to: CGPoint(x: anchor.center.x + anchor.radius + second.radius + 20, y: anchor.center.y + 8))
        capture(region, "2-dragging", into: directory)
        second.debugRelease()
        try? await Task.sleep(for: .seconds(1.2))
        await drag(third, to: CGPoint(x: second.center.x + second.radius + third.radius + 18, y: second.center.y - 6))
        third.debugRelease()
        try? await Task.sleep(for: .seconds(1.4))
        capture(region, "3-row-resolved", into: directory)
        if let plate = anchor.nameplate.restFrame {
            let probes = [("center", CGPoint(x: plate.midX, y: plate.midY)), ("past the end", CGPoint(x: plate.maxX + 4, y: plate.midY)),
                          ("rounded corner", CGPoint(x: plate.minX + 1, y: plate.minY + 1))]
            print("plate hit test:", probes.map { "\($0.0) \(anchor.nameplate.contains(screenPoint: $0.1))" }.joined(separator: ", "))
        }

        // The real pointer is elsewhere, so widen the plate as a hover would.
        anchor.nameplate.setExpanded(true)
        try? await Task.sleep(for: .seconds(0.9))
        capture(region, "4-hover-widened", into: directory)
        anchor.nameplate.setExpanded(false)
        try? await Task.sleep(for: .seconds(0.6))

        model.orbs.split(ids[0])
        try? await Task.sleep(for: .seconds(2.2))
        capture(region, "5-split-plates-undo", into: directory)
        model.orbs.dismissUndo()

        publishing = false
        guard ProcessInfo.processInfo.environment["AGENT_SPEED_DEMO_QUICK"] != "1" else {
            manager.shutdown()
            backdrop?.orderOut(nil)
            return
        }
        try? await Task.sleep(for: .seconds(4))
        capture(region, "6-static", into: directory)
        print(await measure("static, plates on", seconds: 30))
        model.settings.orbNames = .never
        try? await Task.sleep(for: .seconds(2))
        capture(region, "7-plates-off", into: directory)
        print(await measure("static, plates off", seconds: 30))

        manager.shutdown()
        backdrop?.orderOut(nil)
    }

    private static func drag(_ window: OrbWindowController, to goal: CGPoint) async {
        let start = window.center
        for step in 1...30 {
            let u = CGFloat(step) / 30
            window.debugDrag(to: CGPoint(x: start.x + (goal.x - start.x) * u, y: start.y + (goal.y - start.y) * u))
            try? await Task.sleep(for: .seconds(1.0 / 60))
        }
        try? await Task.sleep(for: .seconds(0.3))
    }

    private static func measure(_ name: String, seconds: Double) async -> String {
        let draws = NameplateCanvasView.draws
        let cpu = cpuTime()
        let wall = Date()
        try? await Task.sleep(for: .seconds(seconds))
        let elapsed = Date().timeIntervalSince(wall)
        let used = cpuTime() - cpu
        return String(format: "%@: app CPU %.2f %% of one core over %.0f s, plate redraws %d", name, used / elapsed * 100, elapsed,
                      NameplateCanvasView.draws - draws)
    }

    private static func cpuTime() -> Double {
        var usage = rusage()
        getrusage(RUSAGE_SELF, &usage)
        func seconds(_ t: timeval) -> Double { Double(t.tv_sec) + Double(t.tv_usec) / 1e6 }
        return seconds(usage.ru_utime) + seconds(usage.ru_stime)
    }

    /// Four orbs scattered in the region: a pair (needle), Codex (liquid), all sources (led) and a custom name (reactor).
    private static func seed(screen: NSScreen, display: String, region: CGRect) -> (OrbsDocument, [String]) {
        let visible = screen.visibleFrame
        let orbs = [
            OrbConfig(id: UUID().uuidString, sources: .explicit([.claudeCode, .claudeDesktop]), style: .needle, size: 148),
            OrbConfig(id: UUID().uuidString, sources: .explicit([.codex]), style: .liquid, size: 148),
            OrbConfig(id: UUID().uuidString, name: "夜間批次", sources: .explicit([.gemini, .deepseek]), style: .reactor, size: 148),
            OrbConfig(id: UUID().uuidString, sources: .all, style: .led, size: 112),
        ]
        var document = OrbsDocument(orbs: orbs)
        let arrangement = OrbsController.arrangementKey
        let centers = [
            CGPoint(x: region.minX + 120, y: region.maxY - 130),
            CGPoint(x: region.minX + 300, y: region.minY + 120),
            CGPoint(x: region.minX + 560, y: region.minY + 170),
            CGPoint(x: region.maxX - 110, y: region.maxY - 150),
        ]
        for (orb, center) in zip(orbs, centers) {
            let record = OrbPositionRecord(center: center, radius: CGFloat(orb.size) / 2, visible: visible)
            document.setEntry(OrbLayoutEntry(display: display, record: record), orb: orb.id, arrangement: arrangement)
        }
        return (document, orbs.map(\.id))
    }

    private static func demoRegion(screen: NSScreen) -> CGRect {
        let visible = screen.visibleFrame
        return CGRect(x: visible.maxX - 900, y: visible.maxY - 520, width: 900, height: 520).intersection(visible)
    }

    private static func showBackdrop(_ region: CGRect, dark: Bool) {
        let window = NSWindow(contentRect: region, styleMask: [.borderless], backing: .buffered, defer: false)
        window.isReleasedWhenClosed = false
        window.ignoresMouseEvents = true
        window.level = .floating
        if let canvas = PreviewCanvas(size: region.size, background: .black) {
            NameplateWallpaper.draw(in: canvas.context, size: region.size, dark: dark, seed: 7)
            if let image = canvas.image {
                let view = NSImageView(frame: CGRect(origin: .zero, size: region.size))
                view.image = NSImage(cgImage: image, size: region.size)
                view.imageScaling = .scaleAxesIndependently
                window.contentView = view
            }
        }
        window.orderFrontRegardless()
        backdrop = window
    }

    private static func capture(_ rect: CGRect, _ name: String, into directory: URL) {
        guard let screen = NSScreen.screens.first else { return }
        let top = screen.frame.maxY - rect.maxY
        let region = "\(Int(rect.minX)),\(Int(top)),\(Int(rect.width)),\(Int(rect.height))"
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/sbin/screencapture")
        process.arguments = ["-x", "-t", "jpg", "-R", region, directory.appendingPathComponent("live-\(name).jpg").path]
        try? process.run()
        process.waitUntilExit()
    }
}
#endif
