import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit

/// `--render-previews` group `nameplates` (DESIGN 2b, Name plate): every style in light and dark on busy wallpapers —
/// a tight row with Latin, CJK and multi-source names (`Claude Code +1`, `OpenCode +2`), the placement rules (a top
/// row, a bottom-docked row, a side-docked column, a 2 × 2 cluster), sizes 96 / 112 / 148 / 196 with `minimal` at 72
/// (no plate), and the states (widened with statuses, unavailable, all sources, after a split with the undo chip,
/// Reduce Transparency, Increase Contrast, shortening).
@MainActor
enum NameplatePreviews {
    static func render(model: AppModel, into directory: URL) {
        let language = model.settings.language
        model.settings.language = .zhHant
        model.l10n.apply(.zhHant)
        defer {
            model.settings.language = language
            model.l10n.apply(language)
        }
        for dark in [false, true] {
            let scheme = dark ? "dark" : "light"
            row(model: model, dark: dark)?.write(to: directory.appendingPathComponent("nameplates-row-\(scheme).png"))
            placement(model: model, dark: dark)?.write(to: directory.appendingPathComponent("nameplates-placement-\(scheme).png"))
            sizes(model: model, dark: dark)?.write(to: directory.appendingPathComponent("nameplates-sizes-\(scheme).png"))
            states(model: model, dark: dark)?.write(to: directory.appendingPathComponent("nameplates-states-\(scheme).png"))
        }
    }

    private static func orb(_ style: OrbStyleKind, _ size: Double, _ sources: [SourceKind]?, name: String? = nil) -> OrbConfig {
        OrbConfig(id: UUID().uuidString, name: name, sources: sources.map { .explicit($0) } ?? .all, style: style, size: size)
    }

    /// Six orbs touching at 12 pt under the top of the screen (plates below, the pair shortened to `Claude Code +1`),
    /// and a three-source orb below them (`OpenCode +2`).
    private static func row(model: AppModel, dark: Bool) -> NameplatePreviewSheet? {
        guard let sheet = NameplatePreviewSheet(model: model, size: CGSize(width: 1000, height: 560), dark: dark, seed: 11) else { return nil }
        let entries: [(OrbStyleKind, [SourceKind], String?)] = [
            (.liquid, [.codex], nil), (.needle, [.claudeCode], nil), (.led, [.claudeDesktop], nil),
            (.reactor, [.claudeCode, .claudeDesktop], nil), (.minimal, [.gemini], nil), (.liquid, [.deepseek], "夜間批次"),
        ]
        for (index, entry) in entries.enumerated() {
            sheet.add(.init(config: orb(entry.0, 148, entry.1, name: entry.2), center: CGPoint(x: 26 + 74 + 160 * CGFloat(index), y: 112),
                            fraction: 0.5 + 0.07 * Double(index)))
        }
        sheet.add(.init(config: orb(.needle, 148, [.opencode, .omp, .antigravity]), center: CGPoint(x: 307, y: 420), fraction: 0.8))
        sheet.drawOrbs()
        sheet.drawPlates()
        return sheet
    }

    /// A top row (plates below), a row docked to the bottom edge (above), a column docked to the right edge (toward the
    /// center, like a legend) and a 2 × 2 cluster (top pair above, bottom pair below).
    private static func placement(model: AppModel, dark: Bool) -> NameplatePreviewSheet? {
        let size = CGSize(width: 1100, height: 640)
        guard let sheet = NameplatePreviewSheet(model: model, size: size, dark: dark, seed: 41) else { return nil }
        let small = 112.0
        let top: [(OrbStyleKind, [SourceKind])] = [(.led, [.codex]), (.needle, [.claudeCode]), (.minimal, [.claudeDesktop]),
                                                    (.reactor, [.gemini, .deepseek])]
        for (index, entry) in top.enumerated() {
            sheet.add(.init(config: orb(entry.0, small, entry.1), center: CGPoint(x: 12 + 56 + CGFloat(index) * 124, y: 12 + 56)))
        }
        let bottom: [(OrbStyleKind, [SourceKind]?, String?)] = [(.liquid, [.claudeCode, .claudeDesktop], nil), (.needle, nil, nil),
                                                                (.minimal, [.omp], "研究")]
        for (index, entry) in bottom.enumerated() {
            sheet.add(.init(config: orb(entry.0, 148, entry.1, name: entry.2),
                            center: CGPoint(x: 12 + 74 + CGFloat(index) * 160, y: size.height - 12 - 74), fraction: 0.7))
        }
        let column: [(OrbStyleKind, [SourceKind], String?)] = [(.liquid, [.codex], nil), (.led, [.claudeCode, .codex, .gemini], nil),
                                                               (.reactor, [.antigravity], "コードレビュー")]
        for (index, entry) in column.enumerated() {
            sheet.add(.init(config: orb(entry.0, small, entry.1, name: entry.2),
                            center: CGPoint(x: size.width - 12 - 56, y: 12 + 56 + CGFloat(index) * 124), fraction: 0.4))
        }
        let cluster: [(OrbStyleKind, SourceKind)] = [(.minimal, .deepseek), (.led, .opencode), (.needle, .omp), (.liquid, .claudeDesktop)]
        for (index, entry) in cluster.enumerated() {
            sheet.add(.init(config: orb(entry.0, small, [entry.1]),
                            center: CGPoint(x: 600 + CGFloat(index % 2) * 124, y: 230 + CGFloat(index / 2) * 124), fraction: 0.6))
        }
        sheet.drawOrbs()
        sheet.drawPlates()
        return sheet
    }

    /// Every style at 96 / 112 / 148 / 196 pt (Latin, CJK, pairs, all sources, long names), and `minimal` at 72
    /// without a plate.
    private static func sizes(model: AppModel, dark: Bool) -> NameplatePreviewSheet? {
        let sizes: [Double] = [96, 112, 148, 196]
        let columns: [CGFloat] = [190, 210, 260, 320]
        let rowHeight: CGFloat = 270
        let left: CGFloat = 20
        let width = left + columns.reduce(0, +) + 150
        guard let sheet = NameplatePreviewSheet(model: model, size: CGSize(width: width, height: rowHeight * 5 + 10), dark: dark, seed: 21)
        else { return nil }
        let names: [OrbStyleKind: [([SourceKind]?, String?)]] = [
            .led: [([.codex], nil), ([.claudeCode, .claudeDesktop], nil), (nil, nil), ([.gemini], "Nightly refactor")],
            .needle: [([.claudeCode], nil), ([.codex, .claudeCode, .gemini], nil), ([.claudeDesktop], "文件助理"), ([.claudeCode, .claudeDesktop], nil)],
            .liquid: [([.deepseek], nil), ([.claudeCode], "前端重構"), ([.claudeCode, .claudeDesktop], nil), ([.opencode, .omp], nil)],
            .reactor: [([.gemini], nil), ([.opencode, .omp], nil), ([.codex], "Nightly refactor"), (nil, nil)],
            .minimal: [([.omp], nil), ([.antigravity], "コードレビュー"), ([.claudeCode, .codex], "Claude Code 與 Codex 的夜間批次重構"),
                       ([.codex], nil)],
        ]
        for (row, style) in OrbStyleKind.allCases.enumerated() {
            var x = left
            let y = rowHeight * CGFloat(row) + rowHeight * 0.44
            for (column, size) in sizes.enumerated() {
                let entry = names[style]?[column] ?? (nil, nil)
                sheet.add(.init(config: orb(style, size, entry.0, name: entry.1), center: CGPoint(x: x + columns[column] / 2, y: y),
                                fraction: 0.35 + 0.15 * Double(column)))
                x += columns[column]
            }
            if style == .minimal {
                sheet.add(.init(config: orb(.minimal, 72, [.claudeCode]), center: CGPoint(x: x + 75, y: y), fraction: 0.5))
            }
        }
        sheet.drawOrbs()
        sheet.drawPlates()
        return sheet
    }

    /// The states: at rest, widened on hover, a source unavailable (hollow dot) and widened with its status, after a
    /// split with the undo chip, every source unavailable (dimmed), Reduce Transparency, Increase Contrast, a tight
    /// pair, a long custom name, all sources, and `minimal` at 88 (plate) and 64 (none).
    private static func states(model: AppModel, dark: Bool) -> NameplatePreviewSheet? {
        let cell = CGSize(width: 250, height: 262)
        guard let sheet = NameplatePreviewSheet(model: model, size: CGSize(width: cell.width * 4, height: cell.height * 3), dark: dark, seed: 33)
        else { return nil }
        func center(_ index: Int, dx: CGFloat = 0, dy: CGFloat = 0) -> CGPoint {
            CGPoint(x: cell.width * CGFloat(index % 4) + cell.width / 2 + dx, y: cell.height * CGFloat(index / 4) + 112 + dy)
        }
        let night = orb(.led, 148, [.codex, .claudeCode], name: "夜間批次")
        sheet.add(.init(config: night, center: center(0)))
        sheet.add(.init(config: night, center: center(1), hovered: true))
        let partial = orb(.needle, 148, [.claudeCode, .gemini])
        sheet.add(.init(config: partial, center: center(2), fraction: 0.4, unavailable: [.gemini]))
        sheet.add(.init(config: partial, center: center(3), fraction: 0.4, unavailable: [.gemini], hovered: true))
        sheet.add(.init(config: orb(.reactor, 148, [.claudeCode]), center: center(4), fraction: 0.7,
                        undo: model.l10n("orbs.didSplit", 3)))
        sheet.add(.init(config: orb(.minimal, 148, [.gemini]), center: center(5), unavailable: [.gemini], alpha: 0.55))
        sheet.add(.init(config: orb(.liquid, 148, [.claudeDesktop]), center: center(6), fraction: 0.45, reduceTransparency: true))
        sheet.add(.init(config: orb(.liquid, 148, [.claudeDesktop]), center: center(7), fraction: 0.45, increaseContrast: true))
        sheet.add(.init(config: orb(.needle, 112, [.claudeCode, .claudeDesktop]), center: center(8, dx: -62, dy: 10), fraction: 0.5))
        sheet.add(.init(config: orb(.minimal, 112, [.codex, .gemini]), center: center(8, dx: 62, dy: 10), fraction: 0.3))
        sheet.add(.init(config: orb(.reactor, 112, [.codex], name: "Codex 夜間重構批次任務與相依套件升級檢查"), center: center(9, dy: 10),
                        fraction: 0.6))
        sheet.add(.init(config: orb(.led, 112, nil), center: center(10, dy: 10), fraction: 0.25))
        sheet.add(.init(config: orb(.minimal, 88, [.codex]), center: center(11, dx: -58, dy: 0), fraction: 0.5))
        sheet.add(.init(config: orb(.minimal, 64, [.claudeCode]), center: center(11, dx: 52, dy: -12), fraction: 0.5))
        sheet.drawOrbs()
        sheet.drawPlates()
        let captions = ["At rest: custom name + dots", "Hover: widened to the chip", "A source unavailable: hollow dot",
                        "Hover: the reason", "After a split: undo beyond the plate", "All unavailable: dims with the orb",
                        "Reduce Transparency: solid", "Increase Contrast: border", "Tight row: pair → first +1",
                        "Long custom name: tail ellipsis", "All sources: spectrum dot", "minimal 88 has a plate, 64 none"]
        for (index, text) in captions.enumerated() {
            sheet.caption(text, x: center(index).x, y: cell.height * CGFloat(index / 4 + 1) - 12, width: cell.width - 12)
        }
        return sheet
    }
}
