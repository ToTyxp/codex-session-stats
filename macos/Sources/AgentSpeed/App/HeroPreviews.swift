import AgentSpeedCore
import AgentSpeedHero
import AppKit
import SwiftUI

/// Hero vehicle renders for `--render-previews` (group `hero`): every vehicle at its threshold and mid-band speed,
/// parked and sweeping at idle, frame strips of upgrades, downgrades, start-ups and parking, Reduce Motion, Off and
/// Increase Contrast, in both appearances.
@MainActor
enum HeroPreviews {
    static let cell: CGFloat = 140

    private struct Entry {
        let title: SpeedTitle
        let speed: Double
        var parkedKey: String?
    }

    private struct Frame {
        let image: CGImage
        let caption: String
    }

    static func render(model: AppModel, into directory: URL) {
        let original = model.settings.heroLastTitle
        defer { model.settings.heroLastTitle = original }
        let redline = model.settings.redline
        let thresholds = SpeedTitle.ladder.map { Entry(title: $0, speed: $0.enterSpeed(redline: redline)) }
            + [Entry(title: .idle, speed: 0), Entry(title: .idle, speed: 0, parkedKey: "sportsCar")]
        let midBand = SpeedTitle.ladder.map { title in
            Entry(title: title, speed: title.next.map { (title.enterSpeed(redline: redline) * $0.enterSpeed(redline: redline)).squareRoot() }
                ?? redline * 1.35)
        } + [Entry(title: .idle, speed: 0)]

        for dark in [false, true] {
            let scheme = dark ? "dark" : "light"
            sheet(model, thresholds, dark: dark, individual: directory.appendingPathComponent("hero-%@-\(scheme).png"),
                  to: directory.appendingPathComponent("hero-sheet-threshold-\(scheme).png"))
            sheet(model, midBand, dark: dark, to: directory.appendingPathComponent("hero-sheet-band-\(scheme).png"))
            sheet(model, midBand, dark: dark, reduceMotion: true, to: directory.appendingPathComponent("hero-sheet-reduce-motion-\(scheme).png"))
            sheet(model, midBand, dark: dark, increaseContrast: true, to: directory.appendingPathComponent("hero-sheet-contrast-\(scheme).png"))
            sheet(model, midBand, dark: dark, level: .off, to: directory.appendingPathComponent("hero-sheet-off-\(scheme).png"))

            let taus = [0.0, 0.06, 0.12, 0.18, 0.24, 0.32, 0.4, 0.5, 0.62, 0.8]
            strip(model, dark: dark, title: "bicycle → e-bike", from: 9, to: 13, taus: taus,
                  to: directory.appendingPathComponent("hero-strip-upgrade-\(scheme).png"))
            strip(model, dark: dark, title: "sports car → race car (2 titles)", from: 40, to: 72, taus: taus,
                  to: directory.appendingPathComponent("hero-strip-upgrade-jump-\(scheme).png"))
            strip(model, dark: dark, title: "fighter jet → hypersonic", from: 240, to: 300, taus: taus,
                  to: directory.appendingPathComponent("hero-strip-upgrade-air-\(scheme).png"))
            strip(model, dark: dark, title: "hypersonic → rocket", from: 300, to: 420, taus: taus,
                  to: directory.appendingPathComponent("hero-strip-upgrade-rocket-\(scheme).png"))
            strip(model, dark: dark, title: "sports car → car", from: 34, to: 25, taus: [0, 0.08, 0.16, 0.24, 0.32, 0.4, 0.5, 0.6, 0.75],
                  to: directory.appendingPathComponent("hero-strip-downgrade-\(scheme).png"))
            let startTaus = [0.0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.85, 1.0, 1.2]
            for (key, value) in [("bicycle", 10.0), ("ebike", 14), ("motorcycle", 18), ("car", 26), ("sportsCar", 34),
                                 ("raceCar", 72), ("maglev", 100), ("jetliner", 140), ("fighterJet", 210), ("rocket", 420)] {
                strip(model, dark: dark, title: "start-up: \(key) parked → \(key)", from: 0, to: value, taus: startTaus,
                      parked: key, to: directory.appendingPathComponent("hero-strip-startup-\(key)-\(scheme).png"))
            }
            strip(model, dark: dark, title: "start-up: walker parked → bicycle", from: 0, to: 10, taus: startTaus, parked: nil,
                  to: directory.appendingPathComponent("hero-strip-startup-walk-to-bicycle-\(scheme).png"))
            strip(model, dark: dark, title: "Reduce Motion: bicycle → e-bike", from: 9, to: 13, taus: [0, 0.1, 0.2, 0.3, 0.4],
                  reduceMotion: true, to: directory.appendingPathComponent("hero-strip-reduce-motion-\(scheme).png"))
            parking(model, dark: dark, to: directory.appendingPathComponent("hero-strip-parking-\(scheme).png"))
            idleSweep(model, dark: dark, to: directory.appendingPathComponent("hero-strip-idle-sweep-\(scheme).png"))
        }
    }

    // MARK: Sheets

    private static func sheet(_ model: AppModel, _ entries: [Entry], dark: Bool, reduceMotion: Bool = false,
                              increaseContrast: Bool = false, level: HeroLevel? = nil, individual: URL? = nil, to url: URL) {
        var frames: [Frame] = []
        for entry in entries {
            model.settings.heroLastTitle = entry.parkedKey
            let timeline = timeline(model, dark: dark, reduceMotion: reduceMotion, increaseContrast: increaseContrast, level: level)
            timeline.reset(value: entry.speed)
            timeline.run(for: 1.6, at: entry.speed)
            guard let image = timeline.orbImage(cell: cell) else { continue }
            let name = entry.title == .idle ? "\(model.l10n(entry.title.titleKey)) · \(entry.parkedKey ?? "walk")"
                : model.l10n(entry.title.titleKey)
            frames.append(Frame(image: image, caption: "\(name)  \(SpeedFormat.speed(entry.speed)) tok/s"))
            if let individual {
                let key = entry.title.vehicleKey ?? "idle-\(entry.parkedKey ?? "walk")"
                PreviewSnapshot.write(image, to: URL(fileURLWithPath: String(format: individual.path, key)))
            }
        }
        compose(frames, columns: 7, dark: dark, heading: nil, to: url)
    }

    // MARK: Strips

    private static func strip(_ model: AppModel, dark: Bool, title: String, from: Double, to target: Double, taus: [Double],
                              parked: String? = nil, reduceMotion: Bool = false, to url: URL) {
        model.settings.heroLastTitle = parked
        let timeline = timeline(model, dark: dark, reduceMotion: reduceMotion)
        timeline.reset(value: from)
        timeline.run(for: from == 0 ? 3.5 : 1.5, at: from)
        guard let changed = timeline.runUntilTitleChange(at: target) else { return }
        var frames: [Frame] = []
        for tau in taus {
            timeline.run(until: changed + tau) { _ in target }
            if let image = timeline.orbImage(cell: cell) {
                frames.append(Frame(image: image, caption: String(format: "τ %.2f s", tau)))
            }
        }
        compose(frames, columns: frames.count, dark: dark, heading: title, to: url)
    }

    /// A car coasting to a stop when the agents stop, then parking when the title drops to idle.
    private static func parking(_ model: AppModel, dark: Bool, to url: URL) {
        let timeline = timeline(model, dark: dark)
        timeline.reset(value: 26)
        timeline.run(for: 1.5, at: 26)
        let stop = timeline.time
        var frames: [Frame] = []
        for offset in [0.0, 0.4, 0.8, 1.4, 2.4] {
            timeline.run(until: stop + offset) { _ in 0 }
            if let image = timeline.orbImage(cell: cell) { frames.append(Frame(image: image, caption: String(format: "stop +%.1f s", offset))) }
        }
        guard let idle = timeline.runUntilTitleChange(at: 0) else { return }
        for tau in [0.0, 0.25, 0.5, 1.0, 2.0] {
            timeline.run(until: idle + tau) { _ in 0 }
            if let image = timeline.orbImage(cell: cell) { frames.append(Frame(image: image, caption: String(format: "idle +%.2f s", tau))) }
        }
        compose(frames, columns: frames.count, dark: dark, heading: "parking: car → idle", to: url)
    }

    /// The parked vehicle's light band crossing, in step with the gauge's idle sweep.
    private static func idleSweep(_ model: AppModel, dark: Bool, to url: URL) {
        model.settings.heroLastTitle = "motorcycle"
        let timeline = timeline(model, dark: dark)
        timeline.reset(value: 0)
        let epoch = timeline.animator.idleEpoch(for: model.gauge) + HeroVehicleDriver.sweepOffset
        var start = epoch
        while start < timeline.time + 0.5 { start += HeroStage.sweepPeriod }
        timeline.run(until: start - 0.2) { _ in 0 }
        var frames: [Frame] = []
        for offset in stride(from: -0.1, through: 1.0, by: 0.1) {
            timeline.run(until: start + offset) { _ in 0 }
            if let image = timeline.orbImage(cell: cell) { frames.append(Frame(image: image, caption: String(format: "%+.1f s", offset))) }
        }
        compose(frames, columns: frames.count, dark: dark, heading: "idle: parked motorcycle, light band", to: url)
    }

    // MARK: Helpers

    private static func timeline(_ model: AppModel, dark: Bool, reduceMotion: Bool = false, increaseContrast: Bool = false,
                                 level: HeroLevel? = nil) -> HeroTimeline {
        let timeline = HeroTimeline(model: model)
        timeline.dark = dark
        timeline.increaseContrast = increaseContrast
        timeline.hero?.overrides.reduceMotion = reduceMotion
        timeline.hero?.overrides.level = level
        return timeline
    }

    private static func compose(_ frames: [Frame], columns: Int, dark: Bool, heading: String?, to url: URL) {
        guard !frames.isEmpty else { return }
        let rows = (frames.count + columns - 1) / columns
        let top: CGFloat = heading == nil ? 0 : 26
        let labelHeight: CGFloat = 20
        let size = CGSize(width: CGFloat(columns) * cell, height: top + CGFloat(rows) * (cell + labelHeight))
        let background = dark ? NSColor(srgbRed: 0.137, green: 0.137, blue: 0.153, alpha: 1)
            : NSColor(srgbRed: 0.925, green: 0.925, blue: 0.937, alpha: 1)
        let ink = dark ? NSColor(white: 1, alpha: 0.72) : NSColor(white: 0, alpha: 0.66)
        guard let canvas = PreviewCanvas(size: size, background: background) else { return }
        if let heading {
            canvas.text(heading, at: CGPoint(x: 12, y: 18), size: 12, weight: .semibold, color: ink)
        }
        for (index, frame) in frames.enumerated() {
            let x = CGFloat(index % columns) * cell
            let y = top + CGFloat(index / columns) * (cell + labelHeight)
            canvas.draw(frame.image, at: CGPoint(x: x, y: y), size: CGSize(width: cell, height: cell))
            canvas.text(frame.caption, at: CGPoint(x: x + cell / 2, y: y + cell + 10), size: 10.5, weight: .medium, color: ink,
                        alignment: .center, monospacedDigits: true)
        }
        canvas.write(to: url)
    }
}
