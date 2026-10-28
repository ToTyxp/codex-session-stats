import AgentSpeedCore
import AgentSpeedOrbModel
import Foundation
import Observation

/// The scripted feeds of the five style cards (DESIGN 5): each loops cruise → redline → cruise every 8 s, 1.6 s after
/// the previous card, with a new value every 500 ms like the engine and an occasional response pulse. Runs only while
/// the pane is visible; with Reduce Motion every card holds a still frame at the redline.
@MainActor
final class OrbStyleCardFeeds {
    static let period: TimeInterval = 8
    static let stagger: TimeInterval = 1.6
    /// The script is written for R = 400 tok/s: cruise at 28, the redline at 360.
    static let redline = 400.0

    let feeds: [OrbStyleKind: OrbFeed]
    private var timer: Timer?
    private var start = Clock.now
    private var random = OrbRandom(seed: 0x5EED_CA2D)
    private var reduceMotion = false

    init() {
        var feeds: [OrbStyleKind: OrbFeed] = [:]
        for style in OrbStyleKind.allCases {
            let feed = OrbFeed()
            feed.gauge.redline = Self.redline
            feed.redlineOutput = Self.redline
            feed.set(generating: [.claudeCode, .codex], todayTokens: 1_240_000, agentCount: 2)
            feeds[style] = feed
        }
        self.feeds = feeds
        settle(at: 0)
    }

    func feed(_ style: OrbStyleKind) -> OrbFeed { feeds[style] ?? OrbFeed() }

    /// Runs the loop while `active`; with Reduce Motion the cards rest at the redline.
    func setActive(_ active: Bool, reduceMotion: Bool) {
        let changed = reduceMotion != self.reduceMotion
        self.reduceMotion = reduceMotion
        if reduceMotion {
            stop()
            if changed || timer == nil { holdAtRedline() }
            return
        }
        guard active else {
            stop()
            return
        }
        guard timer == nil else { return }
        let timer = Timer(timeInterval: 0.5, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.tick() }
        }
        timer.tolerance = 0.05
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
        tick()
    }

    func stop() {
        timer?.invalidate()
        timer = nil
    }

    /// Value of card `index` at `t` seconds into the loop (the designer's mock).
    static func value(card index: Int, at t: TimeInterval) -> Double {
        let ticked = (t * 2).rounded(.down) / 2
        let p = (ticked + Double(index) * stagger).truncatingRemainder(dividingBy: period)
        let u = p < 5.5 ? Easing.smoothstep(0, 5.5, p) : p < 6.5 ? 1 : 1 - Easing.smoothstep(6.5, 8, p)
        let v = exp(log(28) + (log(360) - log(28)) * u)
        return v * (1 + 0.03 * sin(ticked * 2.3 + Double(index)) + 0.015 * sin(ticked * 6.1 + 2 * Double(index)))
    }

    /// Snaps every card to its value at `t` (still previews start here).
    func settle(at t: TimeInterval) {
        let now = Clock.now
        for (index, style) in OrbStyleKind.allCases.enumerated() {
            feeds[style]?.gauge.snap(to: Self.value(card: index, at: t), now: now - 3)
        }
    }

    private func holdAtRedline() {
        let now = Clock.now
        for style in OrbStyleKind.allCases { feeds[style]?.gauge.snap(to: 360, now: now - 3) }
    }

    private func tick() {
        let now = Clock.now
        let t = now - start
        for (index, style) in OrbStyleKind.allCases.enumerated() {
            guard let feed = feeds[style] else { continue }
            let value = Self.value(card: index, at: t)
            feed.gauge.ingest(value, now: now)
            let heat = HeatPalette.heat(value: value, redline: Self.redline)
            if feed.gauge.tier != .idle, random.next() < (0.25 + 1.0 * heat) * 0.5 {
                feed.emit(.response(output: Int(random.range(200...4000)), speed: value * random.range(0.6...1.2)))
            }
        }
    }
}
