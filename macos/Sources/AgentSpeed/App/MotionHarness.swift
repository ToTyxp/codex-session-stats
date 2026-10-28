#if DEBUG
import AgentSpeedCore
import AppKit
import QuartzCore

/// `--motion-harness DIR` (debug builds only): opens the real dashboard popover over a flat backdrop with
/// `AGENT_SPEED_HARNESS_AGENTS` agents (default 20), expands and collapses the agents list through the disclosure's own
/// action, and samples the popover on every display frame.
///
/// Writes `DIR/region` (the capture rect for `screencapture -v`), waits up to 10 s for `DIR/go`, then writes
/// `DIR/motion.csv` (one row per frame) and `DIR/summary.txt`, and quits. `AGENT_SPEED_HARNESS_ROOM` limits the room
/// under the anchor (in points) to exercise a short screen; `AGENT_SPEED_HARNESS_DARK=1` forces the dark appearance;
/// `AGENT_SPEED_HARNESS_SCROLL=1` scrolls the expanded list before collapsing it.
@MainActor
final class MotionHarness: NSObject {
    static let toggleAgents = Notification.Name("AgentSpeedMotionHarnessToggleAgents")

    private struct Sample {
        let phase: String
        let time: CFTimeInterval
        let content: CGFloat
        let window: CGFloat
        let bounds: CGFloat
    }

    private static var current: MotionHarness?
    private static let phases = ["expand", "collapse", "expand-interrupted", "collapse-interrupting"]

    private let directory: URL
    private let model: AppModel
    private let dashboard: DashboardController
    private let backdrop: NSWindow
    private let anchor: NSView
    private var link: CADisplayLink?
    private var resizeObserver: NSObjectProtocol?
    private var samples: [Sample] = []
    private var resizes: [String: Int] = [:]
    private var phase = "open"
    private var phaseStart = CACurrentMediaTime()

    static func start(into directory: URL) {
        let harness = MotionHarness(directory: directory)
        current = harness
        Task { await harness.run() }
    }

    private init(directory: URL) {
        self.directory = directory
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let environment = ProcessInfo.processInfo.environment
        let suite = "AgentSpeedMotionHarness"
        UserDefaults.standard.removePersistentDomain(forName: suite)
        let defaults = UserDefaults(suiteName: suite) ?? .standard
        model = AppModel(
            settings: AppSettings(defaults: defaults),
            updates: UpdateController(configuration: .current(environment: [:]), store: UpdateStore(defaults: defaults))
        )
        if environment["AGENT_SPEED_HARNESS_DARK"] == "1" { model.settings.appearance = .dark }
        model.activate(live: false)
        let count = environment["AGENT_SPEED_HARNESS_AGENTS"].flatMap(Int.init) ?? 20
        PreviewRenderer.publish(model, speed: model.settings.redline * 0.7, agents: .crowd(count))
        dashboard = DashboardController(model: model)

        let screen = NSScreen.screens[0]
        let visible = screen.visibleFrame
        let room = environment["AGENT_SPEED_HARNESS_ROOM"].flatMap(Double.init).map { CGFloat($0) }
        let height = room.map { min(visible.height, $0 + 32) } ?? visible.height
        let rect = CGRect(x: visible.minX + 80, y: visible.minY, width: 560, height: height)
        backdrop = NSWindow(contentRect: rect, styleMask: [.borderless], backing: .buffered, defer: false)
        backdrop.backgroundColor = NSColor(srgbRed: 0.18, green: 0.42, blue: 0.47, alpha: 1)
        backdrop.isReleasedWhenClosed = false
        anchor = NSView(frame: CGRect(x: rect.width / 2 - 12, y: rect.height - 4, width: 24, height: 4))
        backdrop.contentView?.addSubview(anchor)
        super.init()

        let top = NSScreen.screens[0].frame.maxY
        let region = "\(Int(rect.minX)),\(Int(top - rect.maxY)),\(Int(rect.width)),\(Int(rect.height))"
        try? region.write(to: directory.appendingPathComponent("region"), atomically: true, encoding: .utf8)
    }

    private func run() async {
        NSApp.activate()
        backdrop.orderFrontRegardless()
        dashboard.show(relativeTo: anchor)
        guard let window = dashboard.debugPopover?.contentViewController?.view.window else { return finish("no popover") }
        resizeObserver = NotificationCenter.default.addObserver(
            forName: NSWindow.didResizeNotification, object: window, queue: nil
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.countResize() }
        }
        let link = anchor.displayLink(target: self, selector: #selector(tick(_:)))
        link.add(to: .main, forMode: .common)
        self.link = link

        let go = directory.appendingPathComponent("go")
        for _ in 0..<100 where !FileManager.default.fileExists(atPath: go.path) {
            try? await Task.sleep(for: .milliseconds(100))
        }
        try? await Task.sleep(for: .milliseconds(600))
        for (name, hold) in zip(Self.phases, [1.4, 1.4, 0.18, 1.4]) {
            phase = name
            phaseStart = CACurrentMediaTime()
            NotificationCenter.default.post(name: Self.toggleAgents, object: nil)
            try? await Task.sleep(for: .seconds(hold))
            if name == "expand", ProcessInfo.processInfo.environment["AGENT_SPEED_HARNESS_SCROLL"] == "1" {
                await scrollAgents()
            }
        }
        finish(nil)
    }

    /// Scrolls the expanded list with wheel events, so the knob, the edge fades and the collapse back to the top can
    /// be recorded.
    private func scrollAgents() async {
        guard let view = dashboard.debugPopover?.contentViewController?.view else { return }
        phase = "scroll"
        phaseStart = CACurrentMediaTime()
        // The innermost scroll view is the agents list; the dashboard's own one is taller.
        guard let list = Self.scrollViews(in: view).min(by: { $0.frame.height < $1.frame.height }) else { return }
        for _ in 0..<12 {
            let wheel = CGEvent(scrollWheelEvent2Source: nil, units: .pixel, wheelCount: 1, wheel1: -24, wheel2: 0, wheel3: 0)
            if let event = wheel.flatMap(NSEvent.init(cgEvent:)) { list.scrollWheel(with: event) }
            try? await Task.sleep(for: .milliseconds(16))
        }
        try? await Task.sleep(for: .seconds(1.6))
    }

    private static func scrollViews(in view: NSView) -> [NSScrollView] {
        view.subviews.flatMap { subview in
            (subview as? NSScrollView).map { [$0] + scrollViews(in: $0) } ?? scrollViews(in: subview)
        }
    }

    private func countResize() {
        resizes[phase, default: 0] += 1
    }

    @objc private func tick(_ link: CADisplayLink) {
        guard let popover = dashboard.debugPopover, let view = popover.contentViewController?.view,
              let window = view.window else { return }
        samples.append(Sample(
            phase: phase,
            time: CACurrentMediaTime() - phaseStart,
            content: popover.contentSize.height,
            window: window.frame.height,
            bounds: view.bounds.height
        ))
    }

    private func finish(_ error: String?) {
        link?.invalidate()
        var csv = "phase,time,content,window,bounds\n"
        for s in samples {
            csv += "\(s.phase),\(String(format: "%.4f", s.time)),\(s.content),\(s.window),\(s.bounds)\n"
        }
        try? csv.write(to: directory.appendingPathComponent("motion.csv"), atomically: true, encoding: .utf8)
        var summary = error.map { "error: \($0)\n" } ?? ""
        summary += "scrollers=\(NSScroller.preferredScrollerStyle == .legacy ? "legacy" : "overlay") "
        summary += "reduceMotion=\(model.accessibility.reduceMotion) agents=\(model.snapshot.agents.count)\n"
        for name in Self.phases {
            summary += describe(name, samples.filter { $0.phase == name })
        }
        try? summary.write(to: directory.appendingPathComponent("summary.txt"), atomically: true, encoding: .utf8)
        print(summary, terminator: "")
        dashboard.close()
        backdrop.orderOut(nil)
        NSApp.terminate(nil)
    }

    /// Height travel, direction reversals (a spring's overshoot is one), the largest step, resizes per frame,
    /// frames whose hosting view lags the popover, and frame pacing.
    private func describe(_ name: String, _ rows: [Sample]) -> String {
        guard let first = rows.first, let last = rows.last else { return "\(name): no frames\n" }
        var changes = 0, reversals = 0, direction = 0.0, maxStep = 0.0, settled = 0.0
        for (index, (a, b)) in zip(rows, rows.dropFirst()).enumerated() where a.content != b.content {
            let delta = Double(b.content - a.content)
            if direction != 0, (delta > 0) != (direction > 0) { reversals += 1 }
            direction = delta
            changes += 1
            maxStep = max(maxStep, abs(delta))
            settled = rows[index + 1].time
        }
        let lagging = rows.filter { abs($0.bounds - $0.content) > 1 }.count
        let intervals = zip(rows, rows.dropFirst()).map { ($1.time - $0.time) * 1000 }.sorted()
        let p95 = intervals.isEmpty ? 0 : intervals[Int(Double(intervals.count - 1) * 0.95)]
        let mean = intervals.isEmpty ? 0 : intervals.reduce(0, +) / Double(intervals.count)
        return "\(name): frames=\(rows.count) height \(f(first.content))->\(f(last.content)) changes=\(changes) "
            + "resizes=\(resizes[name, default: 0]) reversals=\(reversals) maxStep=\(f(maxStep)) "
            + "settled=\(String(format: "%.3f", settled))s laggingFrames=\(lagging) "
            + "frameMs mean=\(f(mean)) p95=\(f(p95)) max=\(f(intervals.last ?? 0))\n"
    }

    private func f(_ value: some BinaryFloatingPoint) -> String {
        String(format: "%.1f", Double(value))
    }
}
#endif
