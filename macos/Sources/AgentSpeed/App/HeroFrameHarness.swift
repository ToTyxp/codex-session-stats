#if DEBUG
import AgentSpeedCore
import AgentSpeedHero
import AgentSpeedOrbModel
import AppKit
import SwiftUI

/// `--hero-frames DIR` (debug builds): steps the dashboard hero on a simulated clock and writes one PNG per frame of a
/// hero mock (orb, value, sliding title), then encodes `DIR/<clip>.mp4` when ffmpeg is installed. Quits when done.
///
/// `AGENT_SPEED_HERO_CLIPS` picks clips as `scenario-appearance[-level][-rm][-theme][-language]`, comma separated
/// (default: the tour and start-ups in dark, the tour in light, Soft, and Reduce Motion); a title theme other than
/// Vehicles shows the title badge (e.g. `stairs-dark-cultivation`, `stairs-light-space-ja`; languages `hans`, `en`,
/// `ja`, else Traditional Chinese). `AGENT_SPEED_HERO_FPS` sets the frame rate (30),
/// `AGENT_SPEED_HERO_SECONDS` limits each clip, `AGENT_SPEED_HERO_KEEP_FRAMES=1` keeps the PNGs next to the video.
@MainActor
enum HeroFrameHarness {
    struct Clip {
        let name: String
        let scenario: HeroScenario
        let dark: Bool
        let level: HeroLevel
        let reduceMotion: Bool
        let theme: TitlePreset
        let language: LanguagePreference

        init?(_ name: String) {
            let parts = name.split(separator: "-").map(String.init)
            guard let first = parts.first, let scenario = HeroScenario.named(first) else { return nil }
            self.name = name
            self.scenario = scenario
            dark = !parts.contains("light")
            level = parts.contains("max") ? .max : parts.contains("soft") ? .soft : parts.contains("off") ? .off : .standard
            reduceMotion = parts.contains("rm")
            theme = parts.lazy.compactMap(TitlePreset.init(rawValue:)).first ?? .vehicles
            language = parts.contains("hans") ? .zhHans : parts.contains("en") ? .en : parts.contains("ja") ? .ja : .zhHant
        }
    }

    static let size = CGSize(width: 368, height: 140)

    static func run(into directory: URL) {
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let environment = ProcessInfo.processInfo.environment
        let names = environment["AGENT_SPEED_HERO_CLIPS"]?.split(separator: ",").map(String.init)
            ?? ["tour-dark", "startups-dark", "tour-light", "tour-dark-soft", "tour-light-rm"]
        let fps = environment["AGENT_SPEED_HERO_FPS"].flatMap(Double.init) ?? 30
        let limit = environment["AGENT_SPEED_HERO_SECONDS"].flatMap(Double.init)
        let keep = environment["AGENT_SPEED_HERO_KEEP_FRAMES"] == "1"
        let suite = "AgentSpeedHeroFrames"
        UserDefaults.standard.removePersistentDomain(forName: suite)
        let defaults = UserDefaults(suiteName: suite) ?? .standard
        let model = AppModel(
            settings: AppSettings(defaults: defaults),
            updates: UpdateController(configuration: .current(environment: [:]), store: UpdateStore(defaults: defaults))
        )
        // The designer's scenarios are written for R = 400 tok/s.
        model.settings.redlineOutput = 400
        var report = ""
        for clip in names.compactMap(Clip.init) {
            let frames = directory.appendingPathComponent(clip.name, isDirectory: true)
            try? FileManager.default.removeItem(at: frames)
            try? FileManager.default.createDirectory(at: frames, withIntermediateDirectories: true)
            model.settings.heroLastTitle = nil
            model.settings.language = clip.language
            model.l10n.apply(clip.language)
            model.titles.select(clip.theme.rawValue)
            let summary = record(clip, model: model, fps: fps, seconds: min(limit ?? .infinity, clip.scenario.duration), into: frames)
            report += "\(clip.name): \(summary)\n"
            if encode(frames: frames, fps: fps, to: directory.appendingPathComponent("\(clip.name).mp4")), !keep {
                try? FileManager.default.removeItem(at: frames)
            }
        }
        try? report.write(to: directory.appendingPathComponent("hero-frames.txt"), atomically: true, encoding: .utf8)
        print(report, terminator: "")
    }

    private static func record(_ clip: Clip, model: AppModel, fps: Double, seconds: TimeInterval, into directory: URL) -> String {
        let timeline = HeroTimeline(model: model)
        timeline.dark = clip.dark
        timeline.hero?.overrides.level = clip.level
        timeline.hero?.overrides.reduceMotion = clip.reduceMotion
        timeline.hero?.collectsSamples = true
        timeline.animator.badge?.collectsSamples = true
        let start = timeline.time
        timeline.reset(value: clip.scenario.value(at: 0))
        var events: [String] = []
        var lastTitle = model.gauge.title
        let count = Int((seconds * fps).rounded())
        for index in 0..<count {
            let target = start + Double(index + 1) / fps
            timeline.run(until: target) { clip.scenario.value(at: $0 - start) }
            if model.gauge.title != lastTitle {
                lastTitle = model.gauge.title
                events.append(String(format: "%.2f→%@", model.gauge.titleChangedAt - start, lastTitle.vehicleKey ?? "idle"))
            }
            guard let image = frame(timeline, model: model, clip: clip) else { continue }
            PreviewSnapshot.write(image, to: directory.appendingPathComponent(String(format: "f%05d.png", index)))
        }
        let badge = !clip.theme.showsVehicleArt
        let samples = ((badge ? timeline.animator.badge?.samples : timeline.hero?.stats.samples) ?? []).sorted()
        func percentile(_ p: Double) -> Double {
            samples.isEmpty ? 0 : Double(samples[min(samples.count - 1, Int(Double(samples.count - 1) * p))]) / 1000
        }
        return String(format: "%d frames, %@ µs p50 %.0f p95 %.0f max %.0f (debug build); titles %@",
                      count, badge ? "title badge" : "hero layer", percentile(0.5), percentile(0.95), percentile(1),
                      events.joined(separator: " "))
    }

    /// The dashboard hero mock: orb on the left, value, `tok/s`, the title sliding up on change, context line.
    private static func frame(_ timeline: HeroTimeline, model: AppModel, clip: Clip) -> CGImage? {
        let dark = clip.dark
        let background = dark ? NSColor(srgbRed: 0.137, green: 0.137, blue: 0.153, alpha: 1)
            : NSColor(srgbRed: 0.925, green: 0.925, blue: 0.937, alpha: 1)
        guard let canvas = PreviewCanvas(size: size, background: background),
              let orb = timeline.orbImage(cell: 140) else { return nil }
        canvas.draw(orb, at: CGPoint(x: 6, y: 0), size: CGSize(width: 140, height: 140))
        let gauge = model.gauge
        let idle = gauge.title == .idle
        let primary = dark ? NSColor(white: 1, alpha: 0.96) : NSColor(srgbRed: 0.043, green: 0.051, blue: 0.071, alpha: 0.92)
        let secondary = dark ? NSColor(white: 1, alpha: 0.64) : NSColor(white: 0, alpha: 0.62)
        let value = timeline.value
        let text = SpeedFormat.speed(value)
        canvas.text(text, at: CGPoint(x: 156, y: 72), size: 44, weight: .bold, color: idle ? secondary : primary, monospacedDigits: true)
        let width = (text as NSString).size(withAttributes: [.font: NSFont.monospacedDigitSystemFont(ofSize: 44, weight: .bold)]).width
        canvas.text("tok/s", at: CGPoint(x: 156 + width + 6, y: 72), size: 15, weight: .medium, color: secondary)

        let heat = HeatPalette.heat(value: value, redline: model.settings.redline)
        let label = idle ? secondary : DashboardPalette.legible(HeatPalette.color(heat, dark: dark), dark: dark, minimum: 4.5).nsColor
        let elapsed = timeline.time - gauge.titleChangedAt
        let p = min(1, max(0, elapsed / 0.35))
        let travel: CGFloat = clip.reduceMotion ? 0 : 14
        let outgoing = OrbReadoutBuilder.titleOut(p)
        let incoming = OrbReadoutBuilder.titleIn(p)
        let titles = model.titles
        canvas.context.saveGState()
        canvas.context.clip(to: CGRect(x: 150, y: 80, width: 214, height: 22))
        if p < 1, gauge.previousTitle != gauge.title {
            canvas.text(titles.label(gauge.previousTitle, in: titles.general, l10n: model.l10n),
                        at: CGPoint(x: 156, y: 96 - travel * outgoing.travel), size: 15, weight: .semibold,
                        color: label.withAlphaComponent(outgoing.alpha))
        }
        canvas.text(titles.label(gauge.title, in: titles.general, l10n: model.l10n),
                    at: CGPoint(x: 156, y: 96 + travel * (1 - (p < 1 ? incoming.travel : 1))), size: 15,
                    weight: .semibold, color: label.withAlphaComponent(p < 1 && gauge.previousTitle != gauge.title ? incoming.alpha : 1))
        canvas.context.restoreGState()
        let snapshot = model.snapshot
        canvas.text(model.l10n("agents.context", idle ? 0 : snapshot.generatingCount, idle ? 0 : snapshot.toolsCount),
                    at: CGPoint(x: 156, y: 118), size: 13, color: secondary)
        let level = clip.reduceMotion ? "Reduce Motion" : "\(clip.level)"
        canvas.text(level, at: CGPoint(x: size.width - 8, y: 132), size: 9, color: secondary.withAlphaComponent(0.5))
        return canvas.image
    }

    private static func encode(frames: URL, fps: Double, to video: URL) -> Bool {
        let candidates = ["/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg", "/usr/bin/ffmpeg"]
        guard let ffmpeg = candidates.first(where: { FileManager.default.isExecutableFile(atPath: $0) }) else { return false }
        let process = Process()
        process.executableURL = URL(fileURLWithPath: ffmpeg)
        process.arguments = ["-loglevel", "error", "-y", "-framerate", String(Int(fps)), "-i", frames.appendingPathComponent("f%05d.png").path,
                             "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "15", "-movflags", "+faststart", video.path]
        do {
            try process.run()
        } catch {
            return false
        }
        process.waitUntilExit()
        return process.terminationStatus == 0
    }
}
#endif
