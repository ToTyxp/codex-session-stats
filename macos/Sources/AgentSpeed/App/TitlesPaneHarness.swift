#if DEBUG
import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit
import MetalKit

/// `--screenshots DIR` group `titles` (debug builds): opens the real Settings window on General with a custom title set
/// selected, so the editor and its two live previews show; measures the app's CPU and the preview frames for 10 s with
/// no focus and 10 s while the focus walks down the rows, then with another pane selected and with the window closed
/// (the previews must draw nothing then), captures the theme grid and the editor in light and dark, and checks the
/// editor's keyboard and commit rules. Writes `DIR/titles-pane-cpu.txt`.
@MainActor
enum TitlesPaneHarness {
    static func run(model: AppModel, into directory: URL) async {
        let titles = model.titles
        if case .preset = titles.document.general,
           let vehicles = titles.copy(TitlePreset.vehicles.rawValue),
           let custom = titles.copy(TitlePreset.cultivation.rawValue) {
            titles.rename(custom, to: "我的修仙路")
            _ = titles.commitField(level: 4, of: custom, text: "結丹")
            _ = titles.commitField(level: 12, of: custom, text: "飛升")
            _ = titles.commitField(level: 6, of: vehicles, text: "保時捷")
            titles.select(custom)
        }

        let settings = SettingsWindowController(model: model)
        var report = ""
        NSApp.appearance = NSAppearance(named: .darkAqua)
        settings.show(pane: .general)
        let window = NSApp.windows.first { $0 is SettingsWindow }
        await ScreenshotHarness.bringForward(window)
        try? await Task.sleep(for: .seconds(1))
        let fields = titleFields(in: window)
        scroll(window, toBottom: true)
        try? await Task.sleep(for: .seconds(1.5))
        let hero = FrameCounter(views(OrbMTKView.self, in: window?.contentView).first)
        let canvases = views(OrbCanvasView.self, in: window?.contentView)
        report += "title fields: \(fields.count), hero views: \(hero.attached ? 1 : 0), preview canvases: \(canvases.count)\n"

        canvases.forEach { $0.resetStats() }
        report += await measure("general pane, editor visible, no focus", seconds: 10, hero: hero, canvases: canvases)

        var row = 1
        let walker = Timer(timeInterval: 0.8, repeats: true) { _ in
            MainActor.assumeIsolated {
                guard row < fields.count else { return }
                window?.makeFirstResponder(fields[row])
                row += 1
            }
        }
        RunLoop.main.add(walker, forMode: .common)
        canvases.forEach { $0.resetStats() }
        report += await measure("general pane, focus walking rows 1–12", seconds: 10, hero: hero, canvases: canvases)
        walker.invalidate()

        // Quick keyboard walk: ↓ / ↑ every 0.2 s through the rows' own key handling, bouncing at the ends.
        if fields.count > 1 { window?.makeFirstResponder(fields[1]) }
        var position = 1
        var step = 1
        let quick = Timer(timeInterval: 0.2, repeats: true) { _ in
            MainActor.assumeIsolated {
                guard let window else { return }
                if position + step < 1 || position + step >= fields.count { step = -step }
                position += step
                let down = step > 0
                let characters = String(UnicodeScalar(down ? NSDownArrowFunctionKey : NSUpArrowFunctionKey)!)
                for type in [NSEvent.EventType.keyDown, .keyUp] {
                    if let event = NSEvent.keyEvent(with: type, location: .zero, modifierFlags: [.numericPad, .function],
                                                    timestamp: ProcessInfo.processInfo.systemUptime, windowNumber: window.windowNumber,
                                                    context: nil, characters: characters, charactersIgnoringModifiers: characters,
                                                    isARepeat: false, keyCode: down ? 125 : 126) {
                        window.sendEvent(event)
                    }
                }
            }
        }
        RunLoop.main.add(quick, forMode: .common)
        canvases.forEach { $0.resetStats() }
        report += await measure("general pane, ↓/↑ every 0.2 s", seconds: 10, hero: hero, canvases: canvases)
        quick.invalidate()
        window?.makeFirstResponder(nil)

        settings.show(pane: .menuBar)
        try? await Task.sleep(for: .seconds(1.5))
        report += await measure("menu bar pane selected", seconds: 5, hero: hero, canvases: [])
        window?.orderOut(nil)
        try? await Task.sleep(for: .seconds(1.5))
        report += await measure("settings window closed", seconds: 5, hero: hero, canvases: [])

        for dark in [false, true] {
            NSApp.appearance = NSAppearance(named: dark ? .darkAqua : .aqua)
            settings.show(pane: .general)
            let shown = NSApp.windows.first { $0 is SettingsWindow && $0.isVisible }
            await ScreenshotHarness.bringForward(shown)
            try? await Task.sleep(for: .seconds(1))
            let fields = titleFields(in: shown)
            scroll(shown, toBottom: false)
            try? await Task.sleep(for: .seconds(1))
            ScreenshotHarness.capture(shown, "settings-titles-grid", dark: dark, into: directory)
            scroll(shown, toBottom: true)
            if fields.count > 6 { shown?.makeFirstResponder(fields[6]) }
            try? await Task.sleep(for: .seconds(1.6))
            ScreenshotHarness.capture(shown, "settings-titles-editor", dark: dark, into: directory)
            shown?.makeFirstResponder(nil)
        }
        report += await focusCheck(window: NSApp.windows.first { $0 is SettingsWindow && $0.isVisible }, model: model)
        window?.orderOut(nil)
        try? report.write(to: directory.appendingPathComponent("titles-pane-cpu.txt"), atomically: true, encoding: .utf8)
        print(report, terminator: "")
    }

    /// Focuses row 6, presses ↓, types a letter, presses Esc and leaves the field: the previews must play the level change
    /// at the transition rate, ↓ must move the focus to row 7, the letter must commit after the 0.8 s pause, and Esc and
    /// leaving must restore the row.
    private static func focusCheck(window: NSWindow?, model: AppModel) async -> String {
        guard let window, case .custom(let set) = model.titles.document.general else { return "focus: no editor\n" }
        let fields = titleFields(in: window)
        guard fields.count == TitlePreset.levelCount + 1 else { return "focus: \(fields.count) fields\n" }
        let hero = views(OrbMTKView.self, in: window.contentView).first
        func focused() -> Int? {
            guard let editor = window.firstResponder as? NSTextView else { return nil }
            return fields.firstIndex { $0 === editor.delegate as? NSTextField }
        }
        func stored() -> String {
            guard case .custom(let current) = model.titles.document.general, current.id == set.id else { return "?" }
            return current.field(level: 7)
        }
        func key(_ code: UInt16, _ characters: String) {
            for type in [NSEvent.EventType.keyDown, .keyUp] {
                if let event = NSEvent.keyEvent(with: type, location: .zero, modifierFlags: [], timestamp: ProcessInfo.processInfo.systemUptime,
                                                windowNumber: window.windowNumber, context: nil, characters: characters,
                                                charactersIgnoringModifiers: characters, isARepeat: false, keyCode: code) {
                    window.sendEvent(event)
                }
            }
        }
        try? await Task.sleep(for: .seconds(TitleEditorPreview.transitionTime + 0.3))
        let steady = hero?.preferredFramesPerSecond ?? 0
        window.makeFirstResponder(fields[6])
        try? await Task.sleep(for: .seconds(0.3))
        let playing = hero?.preferredFramesPerSecond ?? 0
        let six = focused()
        key(125, String(UnicodeScalar(NSDownArrowFunctionKey)!))
        try? await Task.sleep(for: .seconds(0.3))
        let seven = focused()
        key(12, "q")
        try? await Task.sleep(for: .seconds(1.2))
        let typed = stored()
        key(53, "\u{1b}")
        try? await Task.sleep(for: .seconds(0.2))
        window.makeFirstResponder(nil)
        try? await Task.sleep(for: .seconds(0.3))
        return "focus: hero \(steady) fps settled, \(playing) fps after focusing row \(six.map(String.init) ?? "none"); ↓ focuses row "
            + "\(seven.map(String.init) ?? "none"); typing commits \"\(typed)\" after the pause; Esc and leaving keep \"\(stored())\"\n"
    }

    /// Counts the hero's Metal frames by standing in as its delegate and forwarding every call.
    @MainActor
    private final class FrameCounter: NSObject, MTKViewDelegate {
        private weak var view: OrbMTKView?
        private(set) var frames = 0
        private(set) var nanoseconds: UInt64 = 0

        init(_ view: OrbMTKView?) {
            self.view = view
            super.init()
            view?.delegate = self
        }

        var attached: Bool { view != nil }
        var rate: Int { view.map { $0.isPaused ? 0 : $0.preferredFramesPerSecond } ?? 0 }

        func mtkView(_ view: MTKView, drawableSizeWillChange size: CGSize) {}

        func draw(in view: MTKView) {
            let start = DispatchTime.now().uptimeNanoseconds
            self.view?.draw(in: view)
            nanoseconds &+= DispatchTime.now().uptimeNanoseconds - start
            frames += 1
        }
    }

    private static func measure(_ name: String, seconds: Double, hero: FrameCounter, canvases: [OrbCanvasView]) async -> String {
        let clockFrames = OrbFrameClock.shared.framesRendered
        let heroFrames = hero.frames
        let heroTime = hero.nanoseconds
        let cpu = cpuTime()
        let main = mainThreadTime()
        let wall = Date()
        // Another app in front hides the window and rightly stops the previews; such a run doesn't measure them.
        func onScreen() -> Bool { NSApp.windows.contains { $0 is SettingsWindow && $0.occlusionState.contains(.visible) } }
        let shownBefore = onScreen()
        try? await Task.sleep(for: .seconds(seconds))
        let elapsed = Date().timeIntervalSince(wall)
        let used = cpuTime() - cpu
        let usedMain = mainThreadTime() - main
        let drawn = hero.frames - heroFrames
        let shown = shownBefore && onScreen() ? "" : " [window not fully on screen]"
        var line = String(format: "%@%@: app CPU %.2f %% of one core (main thread %.2f %%), hero %.1f frames/s (preferred %d, %.0f µs per frame), canvas frames %.1f/s\n",
                          name, shown, used / elapsed * 100, usedMain / elapsed * 100, Double(drawn) / elapsed, hero.rate,
                          drawn > 0 ? Double(hero.nanoseconds - heroTime) / Double(drawn) / 1000 : 0,
                          Double(OrbFrameClock.shared.framesRendered - clockFrames) / elapsed)
        for canvas in canvases {
            line += String(format: "  canvas %.0f pt: %.1f frames/s, %.0f µs CPU per frame\n", canvas.bounds.width,
                           Double(canvas.framesDrawn) / elapsed,
                           canvas.framesDrawn > 0 ? Double(canvas.cpuNanoseconds) / Double(canvas.framesDrawn) / 1000 : 0)
        }
        return line
    }

    /// Scrolls the pane (taller than the screen) to its top or bottom.
    private static func scroll(_ window: NSWindow?, toBottom: Bool) {
        guard let scrollView = views(NSScrollView.self, in: window?.contentView).max(by: { $0.bounds.height < $1.bounds.height }),
              let document = scrollView.documentView else { return }
        let clip = scrollView.contentView
        let range = max(0, document.bounds.height - clip.bounds.height)
        clip.scroll(to: NSPoint(x: 0, y: toBottom == document.isFlipped ? range : 0))
        scrollView.reflectScrolledClipView(clip)
    }

    /// The editor's title fields from the top row (idle) down.
    private static func titleFields(in window: NSWindow?) -> [NSTextField] {
        views(NSTextField.self, in: window?.contentView)
            .filter { $0.delegate is TitleField.Coordinator }
            .sorted { $0.convert(NSPoint.zero, to: nil).y > $1.convert(NSPoint.zero, to: nil).y }
    }

    private static func views<T: NSView>(_ type: T.Type, in view: NSView?) -> [T] {
        guard let view else { return [] }
        if let match = view as? T { return [match] }
        return view.subviews.flatMap { views(type, in: $0) }
    }


    /// CPU time of the main thread (the harness runs on it).
    private static func mainThreadTime() -> Double {
        var info = thread_basic_info()
        var count = mach_msg_type_number_t(MemoryLayout<thread_basic_info>.size / MemoryLayout<integer_t>.size)
        let thread = mach_thread_self()
        defer { mach_port_deallocate(mach_task_self_, thread) }
        let result = withUnsafeMutablePointer(to: &info) {
            $0.withMemoryRebound(to: integer_t.self, capacity: Int(count)) { thread_info(thread, thread_flavor_t(THREAD_BASIC_INFO), $0, &count) }
        }
        guard result == KERN_SUCCESS else { return 0 }
        func seconds(_ t: time_value_t) -> Double { Double(t.seconds) + Double(t.microseconds) / 1e6 }
        return seconds(info.user_time) + seconds(info.system_time)
    }

    private static func cpuTime() -> Double {
        var usage = rusage()
        getrusage(RUSAGE_SELF, &usage)
        func seconds(_ t: timeval) -> Double { Double(t.tv_sec) + Double(t.tv_usec) / 1e6 }
        return seconds(usage.ru_utime) + seconds(usage.ru_stime)
    }
}
#endif
