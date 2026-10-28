import AgentSpeedCore
import AgentSpeedHero
import AgentSpeedOrbModel
import AppKit

/// Hero title badge renders for `--render-previews` (group `badge`): every non-vehicle theme at idle and every level,
/// in each language, light and dark, plus the states and transitions of one theme (DESIGN 4c, Hero title badge).
@MainActor
enum HeroBadgePreviews {
    static let cell: CGFloat = 128

    private struct Frame {
        let image: CGImage
        let caption: String
    }

    static func render(model: AppModel, into directory: URL) {
        let originalLanguage = model.settings.language
        let originalTheme = model.titles.document.selected
        defer {
            model.settings.language = originalLanguage
            model.l10n.apply(originalLanguage)
            model.titles.select(originalTheme)
        }
        let themes = TitlePreset.allCases.filter { !$0.showsVehicleArt }
        for language in [LanguagePreference.zhHant, .zhHans, .en, .ja] {
            model.settings.language = language
            model.l10n.apply(language)
            for dark in [false, true] {
                ladderSheet(model, themes: themes, dark: dark,
                            to: directory.appendingPathComponent("badge-sheet-\(language.rawValue)-\(dark ? "dark" : "light").png"))
            }
        }
        model.settings.language = .zhHant
        model.l10n.apply(.zhHant)
        for dark in [false, true] {
            let scheme = dark ? "dark" : "light"
            states(model, theme: .cultivation, dark: dark, to: directory.appendingPathComponent("badge-states-cultivation-\(scheme).png"))
            transitions(model, theme: .wuxia, dark: dark, to: directory.appendingPathComponent("badge-transitions-wuxia-\(scheme).png"))
        }
        model.settings.language = .en
        model.l10n.apply(.en)
        states(model, theme: .space, dark: true, to: directory.appendingPathComponent("badge-states-space-en-dark.png"))
        checks(model, into: directory)
    }

    /// Frame demand of the hero center (DESIGN 4c: renders like the vehicle layer). Writes `badge-checks.txt`;
    /// failures also go to stderr.
    private static func checks(_ model: AppModel, into directory: URL) {
        var results: [(String, Bool)] = []
        func check(_ name: String, _ condition: Bool) { results.append((name, condition)) }
        let redline = model.settings.redline
        let cruise = SpeedTitle.car.enterSpeed(redline: redline) * 1.06
        model.titles.select(TitlePreset.cultivation.rawValue)
        func demand(_ timeline: HeroTimeline) -> HeroFrameDemand? {
            HeroCenter.demand(animator: timeline.animator, model: model, at: timeline.time)
        }

        var timeline = timeline(model, dark: true)
        timeline.reset(value: 0)
        var seen = Set<Int>()
        for _ in 0..<360 {
            timeline.run(for: 1.0 / 60, at: 0)
            switch demand(timeline) {
            case .idle(let fps): seen.insert(fps)
            default: seen.insert(-1)
            }
        }
        check("parked: 10 fps, 30 fps during the light band, nothing else", seen == [10, 30])
        timeline.run(for: 0.02, at: cruise)
        _ = timeline.runUntilTitleChange(at: cruise)
        timeline.run(for: 0.1, at: cruise)
        check("start-up in flight: display rate", demand(timeline) == .full)
        timeline.run(for: 2, at: cruise)
        check("cruising: display rate", demand(timeline) == .full)

        for (name, reduceMotion, level) in [("Reduce Motion", true, HeroLevel?.none), ("Off", false, .off)] {
            timeline = self.timeline(model, dark: true, reduceMotion: reduceMotion, level: level)
            timeline.reset(value: cruise)
            timeline.run(for: 2, at: cruise)
            check("\(name), cruising: redraw only on change", demand(timeline) == .still)
            timeline.reset(value: 0)
            timeline.run(for: 2, at: 0)
            check("\(name), parked: redraw only on change", demand(timeline) == .still)
        }

        timeline = self.timeline(model, dark: true)
        model.titles.select(TitlePreset.vehicles.rawValue)
        timeline.reset(value: cruise)
        timeline.run(for: 1, at: cruise)
        let built = timeline.animator.badge?.framesBuilt ?? 0
        timeline.run(for: 1, at: cruise)
        check("Vehicles: the badge is not built", timeline.animator.badge?.framesBuilt == built)
        model.titles.select(TitlePreset.wuxia.rawValue)
        timeline.run(for: 0.1, at: cruise)
        check("theme switch: display rate while crossfading", demand(timeline) == .full)
        check("theme switch: the badge is built", (timeline.animator.badge?.framesBuilt ?? 0) > built)
        timeline.run(for: 2, at: cruise)
        let vehicleFrames = timeline.hero?.stats.frames ?? 0
        timeline.run(for: 1, at: cruise)
        check("after the crossfade: the vehicle is not built", timeline.hero?.stats.frames == vehicleFrames)

        let report = results.map { "\($0.1 ? "PASS" : "FAIL")  \($0.0)" }.joined(separator: "\n") + "\n"
        try? report.write(to: directory.appendingPathComponent("badge-checks.txt"), atomically: true, encoding: .utf8)
        for (name, passed) in results where !passed {
            FileHandle.standardError.write(Data("badge check failed: \(name)\n".utf8))
        }
    }

    /// One row per theme: idle and the 13 levels, each just above its threshold.
    private static func ladderSheet(_ model: AppModel, themes: [TitlePreset], dark: Bool, to url: URL) {
        var rows: [(String, [Frame])] = []
        let redline = model.settings.redline
        for theme in themes {
            model.titles.select(theme.rawValue)
            var frames: [Frame] = []
            for title in SpeedTitle.allCases {
                let value = title == .idle ? 0 : title.enterSpeed(redline: redline) * 1.05
                let timeline = timeline(model, dark: dark)
                timeline.reset(value: value)
                timeline.run(for: 1.2, at: value)
                if let image = timeline.orbImage(cell: cell) { frames.append(Frame(image: image, caption: "")) }
            }
            rows.append((model.l10n(theme.nameKey), frames))
        }
        compose(rows, dark: dark, to: url)
    }

    /// Idle, level 1, a middle level, level 13, beyond the redline, Reduce Motion, Increase Contrast and Off.
    private static func states(_ model: AppModel, theme: TitlePreset, dark: Bool, to url: URL) {
        model.titles.select(theme.rawValue)
        let redline = model.settings.redline
        let cases: [(String, Double, Bool, Bool, HeroLevel?)] = [
            ("idle", 0, false, false, nil),
            ("level 1", 0.6, false, false, nil),
            ("level 7", SpeedTitle.bulletTrain.enterSpeed(redline: redline) * 1.05, false, false, nil),
            ("level 13", redline * 1.02, false, false, nil),
            ("overdrive 1.6 R", redline * 1.6, false, false, nil),
            ("Max 1.6 R", redline * 1.6, false, false, .max),
            ("Reduce Motion", redline * 0.6, true, false, nil),
            ("Increase Contrast", redline * 0.6, false, true, nil),
            ("Off", redline * 0.6, false, false, .off),
        ]
        var frames: [Frame] = []
        for (name, value, reduceMotion, contrast, level) in cases {
            let timeline = timeline(model, dark: dark, reduceMotion: reduceMotion, increaseContrast: contrast, level: level)
            timeline.reset(value: value)
            timeline.run(for: 2.0, at: value)
            if let image = timeline.orbImage(cell: cell) { frames.append(Frame(image: image, caption: name)) }
        }
        compose([(model.l10n(theme.nameKey), frames)], dark: dark, to: url)
    }

    /// A level-up, a jump, a level-down, the start-up, parking and General theme switches, frame by frame.
    private static func transitions(_ model: AppModel, theme: TitlePreset, dark: Bool, to url: URL) {
        model.titles.select(theme.rawValue)
        let redline = model.settings.redline
        func speed(_ title: SpeedTitle) -> Double { title.enterSpeed(redline: redline) * 1.06 }
        let taus = [0.0, 0.06, 0.12, 0.2, 0.3, 0.45, 0.7]
        var rows: [(String, [Frame])] = []
        let script: [(String, Double, Double)] = [
            ("level up", speed(.car), speed(.sportsCar)),
            ("jump +3", speed(.car), speed(.raceCar)),
            ("level down", speed(.raceCar), speed(.bulletTrain) * 0.8),
            ("start-up", 0, speed(.motorcycle)),
            ("parking", speed(.walk), 0),
        ]
        for (name, from, to) in script {
            let timeline = timeline(model, dark: dark)
            timeline.reset(value: from)
            timeline.run(for: from == 0 ? 3.5 : 1.5, at: from)
            guard var changed = timeline.runUntilTitleChange(at: to, limit: to == 0 ? 6 : 3) else { continue }
            while to == 0, model.gauge.title != .idle, let next = timeline.runUntilTitleChange(at: 0, limit: 6) { changed = next }
            var frames: [Frame] = []
            for tau in taus {
                timeline.run(until: changed + tau) { _ in to }
                if let image = timeline.orbImage(cell: cell) { frames.append(Frame(image: image, caption: String(format: "τ %.2f", tau))) }
            }
            rows.append((name, frames))
        }
        let switches: [(String, TitlePreset, TitlePreset)] = [
            ("Vehicles → \(model.l10n(theme.nameKey))", .vehicles, theme),
            ("\(model.l10n(theme.nameKey)) → \(model.l10n(TitlePreset.space.nameKey))", theme, .space),
        ]
        for (name, from, to) in switches {
            model.titles.select(from.rawValue)
            let timeline = timeline(model, dark: dark)
            let value = speed(.car)
            timeline.reset(value: value)
            timeline.run(for: 1.5, at: value)
            model.titles.select(to.rawValue)
            let start = timeline.time
            var frames: [Frame] = []
            for tau in [0.0, 0.06, 0.1, 0.15, 0.2, 0.25, 0.35] {
                timeline.run(until: start + tau) { _ in value }
                if let image = timeline.orbImage(cell: cell) { frames.append(Frame(image: image, caption: String(format: "τ %.2f", tau))) }
            }
            rows.append((name, frames))
        }
        model.titles.select(theme.rawValue)
        compose(rows, dark: dark, to: url)
    }

    private static func timeline(_ model: AppModel, dark: Bool, reduceMotion: Bool = false, increaseContrast: Bool = false,
                                 level: HeroLevel? = nil) -> HeroTimeline {
        let timeline = HeroTimeline(model: model)
        timeline.dark = dark
        timeline.increaseContrast = increaseContrast
        timeline.hero?.overrides.reduceMotion = reduceMotion
        timeline.hero?.overrides.level = level
        return timeline
    }

    private static func compose(_ rows: [(String, [Frame])], dark: Bool, to url: URL) {
        let columns = rows.map(\.1.count).max() ?? 0
        guard columns > 0 else { return }
        let labelWidth: CGFloat = 132
        let captions = rows.contains { $0.1.contains { !$0.caption.isEmpty } }
        let rowHeight = cell + (captions ? 18 : 0)
        let size = CGSize(width: labelWidth + CGFloat(columns) * cell, height: CGFloat(rows.count) * rowHeight)
        let background = dark ? NSColor(srgbRed: 0.118, green: 0.122, blue: 0.141, alpha: 1)
            : NSColor(srgbRed: 0.914, green: 0.918, blue: 0.933, alpha: 1)
        let ink = dark ? NSColor(white: 1, alpha: 0.7) : NSColor(white: 0, alpha: 0.62)
        guard let canvas = PreviewCanvas(size: size, background: background) else { return }
        for (row, entry) in rows.enumerated() {
            let y = CGFloat(row) * rowHeight
            canvas.text(entry.0, at: CGPoint(x: 10, y: y + cell / 2 + 4), size: 12, weight: .semibold, color: ink)
            for (column, frame) in entry.1.enumerated() {
                let x = labelWidth + CGFloat(column) * cell
                canvas.draw(frame.image, at: CGPoint(x: x, y: y), size: CGSize(width: cell, height: cell))
                if !frame.caption.isEmpty {
                    canvas.text(frame.caption, at: CGPoint(x: x + cell / 2, y: y + cell + 11), size: 10, weight: .medium,
                                color: ink, alignment: .center)
                }
            }
        }
        canvas.write(to: url)
    }
}
