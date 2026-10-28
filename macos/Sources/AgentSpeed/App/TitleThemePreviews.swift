import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit

/// `--render-previews` group `themes`: every title theme's ladder on the floating orb at the smallest sizes (`led` at
/// 96 pt, `minimal` at 64 pt), idle and levels 1–13, in all four languages, light and dark (DESIGN 1, Title themes).
@MainActor
enum TitleThemePreviews {
    static let languages: [LanguagePreference] = [.zhHant, .zhHans, .en, .ja]

    static func render(model: AppModel, into directory: URL) {
        let original = model.settings.language
        let originalRedline = model.settings.redlineOutput
        model.settings.redlineOutput = 400
        defer {
            model.settings.language = original
            model.l10n.apply(original)
            model.settings.redlineOutput = originalRedline
        }
        for theme in model.titles.document.allThemes {
            for dark in [false, true] {
                var rows: [[NSImage]] = []
                for language in languages {
                    model.settings.language = language
                    model.l10n.apply(language)
                    for (style, size) in [(OrbStyleKind.led, CGFloat(96)), (.minimal, 64)] {
                        rows.append((0...TitlePreset.levelCount).compactMap { level in
                            image(model: model, theme: theme, style: style, size: size, dark: dark, level: level)
                                .map(SurfacePreviews.pointImage)
                        })
                    }
                }
                SurfacePreviews.writeSheet(rows, dark: dark, to: directory.appendingPathComponent(
                    "themes-\(theme.id)-\(dark ? "dark" : "light").png"
                ))
            }
        }
    }

    /// A still at 1.05 × the level's threshold (idle at 0).
    static func image(model: AppModel, theme: TitleTheme, style: OrbStyleKind, size: CGFloat, dark: Bool, level: Int) -> CGImage? {
        let redline = model.settings.redline
        let title = SpeedTitle(rawValue: level) ?? .idle
        let value = level == 0 ? 0 : max(0.6, title.enterSpeed(redline: redline) * 1.05)
        let stage = OrbPreviewStage(model: model, style: style, diameter: size, dark: dark)
        defer { stage.stop() }
        stage.titleTheme = theme
        stage.reset(value: value)
        stage.run(for: 1.2, at: value)
        return stage.image()
    }
}
