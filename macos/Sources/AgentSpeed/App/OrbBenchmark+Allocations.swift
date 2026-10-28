import AgentSpeedCore
import AgentSpeedOrbModel
import Darwin
import Foundation

/// Heap allocations and CPU time of warm orb frames (`OrbSceneBuilder.build`: every style's step and build plus the
/// shared layers and readout) at the redline with Max effects, counted on the main thread through the system's
/// malloc logger hook (the pattern of `HeroAllocationTests`). Representative in optimized builds only.
@MainActor
enum OrbAllocationCheck {
    static let frames = 600

    static func run() -> [String] {
        guard OrbAllocationProbe.isAvailable else { return ["allocations: malloc logger unavailable"] }
        let suite = "AgentSpeedOrbAllocations"
        UserDefaults.standard.removePersistentDomain(forName: suite)
        let defaults = UserDefaults(suiteName: suite) ?? .standard
        let model = AppModel(
            settings: AppSettings(defaults: defaults),
            updates: UpdateController(configuration: .current(environment: [:]), store: UpdateStore(defaults: defaults))
        )
        var lines = ["warm frames at 0.95 R, Max, 148 pt, dark (\(frames) frames after \(frames) warm-up frames; step + build of one orb):"]
        for style in OrbStyleKind.allCases {
            lines.append(measure(style, model: model))
        }
        return lines
    }

    private static func measure(_ style: OrbStyleKind, model: AppModel) -> String {
        let redline = model.settings.redline
        let feed = OrbFeed()
        feed.gauge.redline = redline
        feed.redlineOutput = redline
        feed.set(generating: [.codex, .claudeCode, .claudeDesktop], todayTokens: 1_240_000, agentCount: 3)
        let animator = OrbAnimator(surface: .floating, style: style)
        animator.overrides = .init(level: .max, reduceMotion: false)
        animator.attach(model: model, feed: feed, interaction: nil)
        defer { animator.detach() }
        var scene = OrbScene(capacity: 2048)
        var time = Clock.now
        let base = redline * 0.95
        feed.gauge.snap(to: base, now: time - 3)
        let content = OrbContentBuilder.content(model: model, feed: feed, diameter: 148, dark: true, contrast: false,
                                                style: style, showsReadout: true)
        var counts: [Int] = []
        var nanoseconds: [UInt64] = []
        counts.reserveCapacity(frames)
        nanoseconds.reserveCapacity(frames)
        for pass in 0..<2 {
            for index in 0..<frames {
                time += 1.0 / 60
                // Engine ticks, response events and a new peak happen between frames, like in the app.
                if index % 30 == 0 { feed.gauge.ingest(base * (1 + 0.04 * sin(Double(index) * 0.37)), now: time) }
                if index % 23 == 0 { animator.addPulse(at: time, strength: 0.4 + 0.5 * Double(index % 3) / 2, heat: 0.95) }
                if index == 200 { animator.celebrate(at: time) }
                let start = DispatchTime.now().uptimeNanoseconds
                let count = OrbAllocationProbe.count {
                    OrbSceneBuilder.build(into: &scene, content: content, animator: animator, model: model, feed: feed,
                                          canvasSide: 222, scale: 2, time: time)
                }
                let elapsed = DispatchTime.now().uptimeNanoseconds - start
                if pass == 1 {
                    counts.append(count)
                    nanoseconds.append(elapsed)
                }
            }
        }
        let total = counts.reduce(0, +)
        let framesWithAllocations = counts.filter { $0 > 0 }.count
        let sorted = nanoseconds.sorted()
        let mean = Double(sorted.reduce(0, +)) / Double(max(1, sorted.count)) / 1000
        let p95 = Double(sorted[min(sorted.count - 1, Int(Double(sorted.count) * 0.95))]) / 1000
        return String(format: "  %-8@ %5.2f allocations/frame (%d frames allocate, %d in total), %6.1f µs mean, %6.1f µs p95, %4d instances",
                      style.rawValue, Double(total) / Double(frames), framesWithAllocations, total, mean, p95, scene.instanceCount)
    }
}

/// Counts heap allocations made on the main thread through the system's malloc logger hook.
enum OrbAllocationProbe {
    typealias Logger = @convention(c) (UInt32, UInt, UInt, UInt, UInt, UInt32) -> Void

    nonisolated(unsafe) private static var allocations = 0
    nonisolated(unsafe) private static var thread: pthread_t?
    nonisolated(unsafe) private static let slot = dlsym(UnsafeMutableRawPointer(bitPattern: -2), "malloc_logger")?
        .assumingMemoryBound(to: Logger?.self)

    static var isAvailable: Bool { slot != nil }

    /// Allocations `body` makes on the calling thread (0 when the hook is unavailable).
    static func count(_ body: () -> Void) -> Int {
        guard let slot else {
            body()
            return 0
        }
        allocations = 0
        thread = pthread_self()
        slot.pointee = { type, _, _, _, _, _ in
            if type & 2 != 0, let thread = OrbAllocationProbe.thread, pthread_equal(pthread_self(), thread) != 0 {
                OrbAllocationProbe.allocations += 1
            }
        }
        body()
        slot.pointee = nil
        thread = nil
        return allocations
    }
}
