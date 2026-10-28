#if DEBUG
import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit

/// `--screenshots DIR` group `orbpane` (debug builds): opens the real Settings window on the floating orb pane with three
/// orbs whose speeds change every 500 ms, measures the app's CPU and the orb frames drawn per second for 10 s, then the
/// same with another pane selected and with the window closed (the previews must draw nothing then), and captures the
/// pane in light and dark. Writes `DIR/orb-pane-cpu.txt`.
@MainActor
enum OrbPaneHarness {
    static func run(model: AppModel, into directory: URL) async {
        let orbs = model.orbs
        if orbs.orbs.count == 1, let first = orbs.orbs.first?.id, let pair = orbs.add(copying: first), let single = orbs.add(copying: pair) {
            orbs.setSources(.explicit([.claudeCode, .claudeDesktop]), of: pair)
            orbs.setStyle(.needle, of: pair)
            orbs.setSources(.explicit([.codex]), of: single)
            orbs.setStyle(.liquid, of: single)
            orbs.setStyle(.reactor, of: first)
            orbs.settingsSelection = pair
            orbs.dismissUndo()
        }
        let redline = model.settings.redline
        var tick = 0
        let feeder = Timer(timeInterval: 0.5, repeats: true) { _ in
            MainActor.assumeIsolated {
                tick += 1
                let speed = redline * (0.35 + 0.25 * sin(Double(tick) * 0.7))
                model.store.publishLive(PreviewData.snapshot(speed: speed, redline: redline))
            }
        }
        RunLoop.main.add(feeder, forMode: .common)
        defer { feeder.invalidate() }

        let settings = SettingsWindowController(model: model)
        var report = ""
        NSApp.appearance = NSAppearance(named: .darkAqua)
        settings.show(pane: .orb)
        let window = NSApp.windows.first { $0 is SettingsWindow }
        await ScreenshotHarness.bringForward(window)
        try? await Task.sleep(for: .seconds(2))
        canvases(in: window?.contentView).forEach { $0.resetStats() }
        report += await measure("orb pane visible (3 orbs, 5 animated style cards)", seconds: 10)
        for canvas in canvases(in: window?.contentView) {
            report += String(format: "  canvas %.0f pt: %.1f frames/s, %.0f µs CPU per frame\n", canvas.bounds.width,
                             Double(canvas.framesDrawn) / 10, canvas.framesDrawn > 0 ? Double(canvas.cpuNanoseconds) / Double(canvas.framesDrawn) / 1000 : 0)
        }
        settings.show(pane: .general)
        try? await Task.sleep(for: .seconds(1.5))
        report += await measure("general pane selected", seconds: 5)
        window?.orderOut(nil)
        try? await Task.sleep(for: .seconds(1.5))
        report += await measure("settings window closed", seconds: 5)

        for dark in [false, true] {
            NSApp.appearance = NSAppearance(named: dark ? .darkAqua : .aqua)
            settings.show(pane: .orb)
            await ScreenshotHarness.bringForward(window)
            try? await Task.sleep(for: .seconds(1.6))
            ScreenshotHarness.capture(NSApp.windows.first { $0 is SettingsWindow && $0.isVisible }, "settings-orb-live", dark: dark,
                                      into: directory)
        }
        report += await keyboardCheck(window: NSApp.windows.first { $0 is SettingsWindow && $0.isVisible }, orbs: orbs)
        window?.orderOut(nil)
        try? report.write(to: directory.appendingPathComponent("orb-pane-cpu.txt"), atomically: true, encoding: .utf8)
        print(report, terminator: "")
    }

    private static func measure(_ name: String, seconds: Double) async -> String {
        let frames = OrbFrameClock.shared.framesRendered
        let cpu = cpuTime()
        let wall = Date()
        try? await Task.sleep(for: .seconds(seconds))
        let elapsed = Date().timeIntervalSince(wall)
        let used = cpuTime() - cpu
        let drawn = OrbFrameClock.shared.framesRendered - frames
        return String(format: "%@: app CPU %.2f %% of one core, orb frames %.1f/s\n", name, used / elapsed * 100,
                      Double(drawn) / elapsed)
    }

    /// Clicks the first orb row, then presses ↓ and ⌘D through the window: the selection must move to the second orb and
    /// a fourth orb must appear (then it is removed again with ⌫).
    private static func keyboardCheck(window: NSWindow?, orbs: OrbsController) async -> String {
        guard let window, let list = groups(in: window.contentView).max(by: { $0.bounds.height < $1.bounds.height }) else {
            return "keyboard: no list\n"
        }
        let rowPoint = list.convert(NSPoint(x: 120, y: list.isFlipped ? 26 : list.bounds.height - 26), to: nil)
        func mouse(_ type: NSEvent.EventType) -> NSEvent? {
            NSEvent.mouseEvent(with: type, location: rowPoint, modifierFlags: [], timestamp: ProcessInfo.processInfo.systemUptime,
                               windowNumber: window.windowNumber, context: nil, eventNumber: 0, clickCount: 1, pressure: 1)
        }
        func key(_ code: UInt16, _ characters: String, _ modifiers: NSEvent.ModifierFlags = []) {
            for type in [NSEvent.EventType.keyDown, .keyUp] {
                if let event = NSEvent.keyEvent(with: type, location: .zero, modifierFlags: modifiers, timestamp: ProcessInfo.processInfo.systemUptime,
                                                windowNumber: window.windowNumber, context: nil, characters: characters,
                                                charactersIgnoringModifiers: characters, isARepeat: false, keyCode: code) {
                    window.sendEvent(event)
                }
            }
        }
        if let down = mouse(.leftMouseDown), let up = mouse(.leftMouseUp) {
            window.sendEvent(down)
            window.sendEvent(up)
        }
        try? await Task.sleep(for: .seconds(0.4))
        let first = orbs.settingsSelection
        key(125, String(UnicodeScalar(NSDownArrowFunctionKey)!))
        try? await Task.sleep(for: .seconds(0.3))
        let second = orbs.settingsSelection
        let count = orbs.orbs.count
        key(2, "d", .command)
        try? await Task.sleep(for: .seconds(0.3))
        let added = orbs.orbs.count == count + 1
        key(51, String(UnicodeScalar(NSDeleteCharacter)!))
        try? await Task.sleep(for: .seconds(0.3))
        let removed = orbs.orbs.count == count
        orbs.dismissUndo()
        let index = { (id: String?) in orbs.orbs.firstIndex { $0.id == id } ?? -1 }
        return "keyboard: click selects row \(index(first)), ↓ selects row \(index(second)), ⌘D duplicates \(added), ⌫ removes \(removed)\n"
    }

    private static func groups(in view: NSView?) -> [OrbGroupCanvasView] {
        guard let view else { return [] }
        if let group = view as? OrbGroupCanvasView { return [group] }
        return view.subviews.flatMap { groups(in: $0) }
    }

    private static func canvases(in view: NSView?) -> [OrbCanvasView] {
        guard let view else { return [] }
        if let canvas = view as? OrbCanvasView { return [canvas] }
        return view.subviews.flatMap { canvases(in: $0) }
    }

    private static func cpuTime() -> Double {
        var usage = rusage()
        getrusage(RUSAGE_SELF, &usage)
        func seconds(_ t: timeval) -> Double { Double(t.tv_sec) + Double(t.tv_usec) / 1e6 }
        return seconds(usage.ru_utime) + seconds(usage.ru_stime)
    }
}
#endif
