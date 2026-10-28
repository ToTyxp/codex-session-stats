import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit
import SwiftUI

/// `--render-previews` group `cards`: the title theme cards of Settings → General in all four languages at the pane's
/// three-column width and at the two-column width, light and dark, plus `title-cards-check.txt`: which titles each
/// card's strip shows and whether any strip would end on a truncated title (DESIGN 5, Title themes 2).
@MainActor
enum TitleCardPreviews {
    /// Group widths: the General pane's content (three columns) and just under the two-column breakpoint.
    static let widths: [CGFloat] = [SettingsPaneView.wideWidth - 60, 540]

    static func render(model: AppModel, into directory: URL) {
        let titles = model.titles
        let original = model.settings.language
        defer {
            model.settings.language = original
            model.l10n.apply(original)
        }
        if titles.document.custom.isEmpty, let long = titles.copy(TitlePreset.engineer.rawValue),
           let short = titles.copy(TitlePreset.wuxia.rawValue) {
            titles.rename(long, to: "Night Shift Batch Pipeline")
            _ = titles.commitField(level: 13, of: long, text: "Distinguished Principal Fellow")
            titles.rename(short, to: "我的武林")
        }
        var report = ""
        var truncated = 0
        for language in TitleThemePreviews.languages {
            model.settings.language = language
            model.l10n.apply(language)
            for width in widths {
                let columns: CGFloat = width < 560 ? 2 : 3
                let content = (width - 8 * (columns - 1)) / columns - 24
                for theme in titles.document.allThemes {
                    let names = TitleThemeText.stripLevels.map { TitleThemeText.text($0, theme, model: model) }
                    let fit = TitleThemeText.stripChoices.first { stripWidth($0, names) <= content }
                    if fit == nil { truncated += 1 }
                    let shown = (fit ?? [0]).map { names[$0] }.joined(separator: " · ")
                    report += "\(language.rawValue)\t\(Int(width))\t\(theme.id)\t\(fit.map { "\($0.count)" } ?? "TRUNCATED")\t\(shown)\n"
                }
                for dark in [false, true] {
                    let view = TitleThemeCards(model: model, width: width, editorFocus: .constant(nil))
                        .padding(20)
                        .frame(width: width + 40)
                        .appEnvironment(model)
                    PreviewSnapshot.writeHosted(view, dark: dark, to: directory.appendingPathComponent(
                        "title-cards-\(language.rawValue)-\(Int(width))-\(dark ? "dark" : "light").png"
                    ))
                }
            }
        }
        report += "truncated strips: \(truncated)\n"
        try? report.write(to: directory.appendingPathComponent("title-cards-check.txt"), atomically: true, encoding: .utf8)
    }

    /// The strip's width in the card's 11 pt system font.
    private static func stripWidth(_ levels: [Int], _ names: [String]) -> CGFloat {
        let text = levels.map { names[$0] }.joined(separator: " · ")
        let font = NSFont.systemFont(ofSize: 11)
        return ceil(NSAttributedString(string: text, attributes: [.font: font]).size().width)
    }
}
