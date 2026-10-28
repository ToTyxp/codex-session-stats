#if DEBUG
import AgentSpeedCore
import AppKit

/// `--hero-cpu DIR` (debug builds; build with `-c release -Xswiftc -DDEBUG` for representative numbers): opens the real
/// dashboard popover over a backdrop and measures the app's CPU time with the hero parked at idle, cruising and at
/// redline, then closes it and checks that no hero frame is drawn while closed. Writes `DIR/hero-cpu.txt` and quits.
@MainActor
final class HeroLiveHarness {
    private static var current: HeroLiveHarness?

    private let directory: URL
    private let model: AppModel
    private let dashboard: DashboardController
    private let backdrop: NSWindow
    private let anchor: NSView

    static func start(into directory: URL) {
        let harness = HeroLiveHarness(directory: directory)
        current = harness
        Task { await harness.run() }
    }

    private init(directory: URL) {
        self.directory = directory
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let suite = "AgentSpeedHeroLiveHarness"
        UserDefaults.standard.removePersistentDomain(forName: suite)
        let defaults = UserDefaults(suiteName: suite) ?? .standard
        model = AppModel(
            settings: AppSettings(defaults: defaults),
            updates: UpdateController(configuration: .current(environment: [:]), store: UpdateStore(defaults: defaults))
        )
        if ProcessInfo.processInfo.environment["AGENT_SPEED_HARNESS_DARK"] == "1" { model.settings.appearance = .dark }
        model.settings.heroLastTitle = "motorcycle"
        model.activate(live: false)
        dashboard = DashboardController(model: model)
        let visible = NSScreen.screens[0].visibleFrame
        let rect = CGRect(x: visible.minX + 80, y: visible.minY, width: 560, height: visible.height)
        backdrop = NSWindow(contentRect: rect, styleMask: [.borderless], backing: .buffered, defer: false)
        backdrop.backgroundColor = NSColor(srgbRed: 0.18, green: 0.42, blue: 0.47, alpha: 1)
        backdrop.isReleasedWhenClosed = false
        anchor = NSView(frame: CGRect(x: rect.width / 2 - 12, y: rect.height - 4, width: 24, height: 4))
        backdrop.contentView?.addSubview(anchor)
    }

    private func run() async {
        NSApp.activate()
        backdrop.orderFrontRegardless()
        let redline = model.settings.redline
        var report = ""
        publish(0)
        dashboard.show(relativeTo: anchor)
        try? await Task.sleep(for: .seconds(1.5))
        report += await measure("open, idle (parked motorcycle)", seconds: 12)
        publish(10)
        try? await Task.sleep(for: .seconds(2.5))
        report += await measure("open, bicycle at 10 tok/s", seconds: 10)
        publish(redline * 0.9)
        try? await Task.sleep(for: .seconds(2.5))
        report += await measure("open, redline (0.9 R)", seconds: 10)
        publish(redline * 1.5)
        try? await Task.sleep(for: .seconds(2.5))
        report += await measure("open, overdrive rocket (1.5 R)", seconds: 10)
        dashboard.close()
        try? await Task.sleep(for: .seconds(1.5))
        report += await measure("closed, overdrive rocket", seconds: 8)
        try? report.write(to: directory.appendingPathComponent("hero-cpu.txt"), atomically: true, encoding: .utf8)
        print(report, terminator: "")
        backdrop.orderOut(nil)
        NSApp.terminate(nil)
    }

    private func publish(_ speed: Double) {
        PreviewRenderer.publish(model, speed: speed)
    }

    private func measure(_ name: String, seconds: Double) async -> String {
        let frames = HeroVehicleDriver.framesBuilt
        let nanoseconds = HeroVehicleDriver.totalNanoseconds
        let cpu = Self.cpuTime()
        let wall = Date()
        try? await Task.sleep(for: .seconds(seconds))
        let elapsed = Date().timeIntervalSince(wall)
        let used = Self.cpuTime() - cpu
        let built = HeroVehicleDriver.framesBuilt - frames
        let heroTime = Double(HeroVehicleDriver.totalNanoseconds - nanoseconds) / 1e9
        return String(format: "%@: app CPU %.1f %% of one core, hero frames %.1f/s (%d), hero layer %.3f %% of one core (%.1f µs/frame)\n",
                      name, used / elapsed * 100, Double(built) / elapsed, built, heroTime / elapsed * 100,
                      built > 0 ? heroTime / Double(built) * 1e6 : 0)
    }

    /// User + system CPU seconds of this process.
    private static func cpuTime() -> Double {
        var usage = rusage()
        getrusage(RUSAGE_SELF, &usage)
        func seconds(_ t: timeval) -> Double { Double(t.tv_sec) + Double(t.tv_usec) / 1e6 }
        return seconds(usage.ru_utime) + seconds(usage.ru_stime)
    }
}
#endif
