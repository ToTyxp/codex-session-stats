import AgentSpeedCore
import AgentSpeedOrbModel
import SwiftUI

/// Names and colors of a theme's titles for the picker (DESIGN 5, Title themes).
@MainActor
enum TitleThemeText {
    /// Levels shown in a card's preview strip.
    static let stripLevels: [SpeedTitle] = [.walk, .car, .maglev, .rocket]
    /// Subsets of `stripLevels` from most to fewest titles, keeping the first and last while they fit: the strip shows
    /// the first that fits whole, so it never ends on a truncated title.
    static let stripChoices: [[Int]] = [[0, 1, 2, 3], [0, 2, 3], [0, 1, 3], [0, 3], [3], [0]]

    /// The first choice whose titles fit `width` whole in the strip's 11 pt font (the last one when none does). Measured
    /// once per titles and width: cards are laid out again whenever the pane is.
    static func stripChoice(_ titles: [String], width: CGFloat) -> [Int] {
        let key = StripKey(titles: titles, width: Int(width.rounded(.down)))
        if let cached = stripCache[key] { return cached }
        let font = NSFont.systemFont(ofSize: 11)
        let choice = stripChoices.first { levels in
            let text = levels.map { titles[$0] }.joined(separator: " · ")
            return ceil(NSAttributedString(string: text, attributes: [.font: font]).size().width) <= width
        } ?? stripChoices[stripChoices.count - 1]
        if stripCache.count > 256 { stripCache.removeAll() }
        stripCache[key] = choice
        return choice
    }

    private struct StripKey: Hashable {
        var titles: [String]
        var width: Int
    }

    private static var stripCache: [StripKey: [Int]] = [:]

    /// The heat of a level's threshold, `(T_k / R)^0.6`.
    static func heat(_ title: SpeedTitle) -> Double {
        HeatPalette.heat(forFraction: title.threshold)
    }

    /// A theme's title as typed (custom) or as localized (preset).
    static func text(_ title: SpeedTitle, _ theme: TitleTheme, model: AppModel) -> String {
        model.titles.text(title, in: theme, l10n: model.l10n)
    }

    /// The whole ladder with its thresholds at the current redline (`凡人 0.5 · 練氣 40 · … · 道祖 2.00k`).
    static func ladder(_ theme: TitleTheme, model: AppModel) -> String {
        let redline = model.settings.redline
        return SpeedTitle.ladder
            .map { "\(text($0, theme, model: model)) \(TitleLadderValues.text($0.enterSpeed(redline: redline)))" }
            .joined(separator: " · ")
    }
}

/// The theme cards: the nine presets, then the custom sets and the dashed New card (DESIGN 5, Title themes 2). Arrow
/// keys move between cards row-major, Space or Return selects, ⌘D duplicates and edits, ⌫ deletes a custom set.
struct TitleThemeCards: View {
    let model: AppModel
    /// Width of the group's content, for the column count.
    let width: CGFloat
    @Binding var editorFocus: TitleEditorFocus?

    @FocusState private var focused: Bool
    @State private var cursor: String?
    @State private var hovered: String?

    private static let spacing: CGFloat = 8
    static let newCardID = "titles.custom.new"

    private var columns: Int { width < 560 ? 2 : 3 }

    private var titles: TitleThemesController { model.titles }

    /// Every focusable card id, row-major: presets, custom sets, New.
    private var order: [String] {
        TitlePreset.allCases.map(\.rawValue) + titles.document.custom.map(\.id) + [Self.newCardID]
    }

    var body: some View {
        let l10n = model.l10n
        let grid = Array(repeating: GridItem(.flexible(), spacing: Self.spacing), count: columns)
        let custom = titles.document.custom
        VStack(alignment: .leading, spacing: Spacing.xs) {
            LazyVGrid(columns: grid, alignment: .leading, spacing: Self.spacing) {
                ForEach(TitlePreset.allCases) { preset in
                    card(.preset(preset))
                }
            }
            Text(l10n("titles.custom"))
                .font(.system(size: 11, weight: .semibold))
                .foregroundStyle(.secondary)
                .padding(.top, Spacing.xxs)
            LazyVGrid(columns: grid, alignment: .leading, spacing: Self.spacing) {
                ForEach(custom) { set in
                    card(.custom(set))
                        .transition(model.accessibility.reduceMotion ? .opacity : .scale(scale: 0.85).combined(with: .opacity))
                }
                newCard(l10n: l10n)
            }
            .animation(model.accessibility.reduceMotion ? .easeOut(duration: 0.2) : .spring(response: 0.35, dampingFraction: 0.8),
                       value: custom.map(\.id))
        }
        .focusable(interactions: .edit)
        .focused($focused)
        .focusEffectDisabled()
        .onKeyPress(keys: [.upArrow, .downArrow, .leftArrow, .rightArrow, .space, .return]) { press in
            handle(press.key) ? .handled : .ignored
        }
        .onKeyPress(characters: CharacterSet(charactersIn: "\u{7F}\u{8}\u{F728}")) { _ in
            guard let id = cursor, titles.document.customSet(id) != nil else { return .ignored }
            delete(id)
            return .handled
        }
        .onKeyPress(characters: CharacterSet(charactersIn: "dD")) { press in
            guard press.modifiers.contains(.command) else { return .ignored }
            let id = cursor ?? titles.document.selected
            duplicate(id == Self.newCardID ? titles.document.selected : id)
            return .handled
        }
        .onChange(of: focused) {
            if focused, cursor == nil { cursor = titles.document.selected }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(l10n("titles.theme"))
    }

    // MARK: Cards

    private func card(_ theme: TitleTheme) -> some View {
        let id = theme.id
        let selected = titles.document.selected == id
        let isCustom = { if case .custom = theme { true } else { false } }()
        return TitleThemeCard(
            model: model,
            theme: theme,
            stripWidth: (width - Self.spacing * CGFloat(columns - 1)) / CGFloat(columns) - 2 * TitleThemeCard.padding,
            selected: selected,
            hovered: hovered == id,
            keyboardFocused: focused && cursor == id,
            showsMore: isCustom && (hovered == id || selected || (focused && cursor == id)),
            actions: actions(for: theme)
        )
        .onTapGesture {
            titles.select(id)
            cursor = id
            focused = true
        }
        .onHover { inside in
            if inside { hovered = id } else if hovered == id { hovered = nil }
        }
    }

    private func newCard(l10n: Localizer) -> some View {
        let enabled = titles.document.canAddCustom
        let highlighted = focused && cursor == Self.newCardID
        return Button {
            duplicate(titles.document.selected)
        } label: {
            Label(l10n("titles.custom.new"), systemImage: "plus")
                .font(.system(size: 13, weight: .medium))
                .foregroundStyle(enabled ? Color.accentColor : Color.secondary)
                .frame(maxWidth: .infinity, minHeight: TitleThemeCard.height)
                .background {
                    RoundedRectangle(cornerRadius: TitleThemeCard.radius, style: .continuous)
                        .strokeBorder(style: StrokeStyle(lineWidth: 1, dash: [4, 3]))
                        .foregroundStyle(highlighted ? Color.accentColor : Color.secondary.opacity(0.5))
                }
                .contentShape(RoundedRectangle(cornerRadius: TitleThemeCard.radius, style: .continuous))
        }
        .buttonStyle(.plain)
        .disabled(!enabled)
        .help(enabled ? "" : l10n("titles.custom.limit", TitleThemesDocument.maxCustom))
        .accessibilityLabel(l10n("titles.custom.new"))
    }

    private func actions(for theme: TitleTheme) -> [TitleThemeCard.Action] {
        let l10n = model.l10n
        let canAdd = titles.document.canAddCustom
        switch theme {
        case .preset:
            return [.init(title: l10n("titles.duplicateEdit"), enabled: canAdd) { duplicate(theme.id) }]
        case .custom(let set):
            return [
                .init(title: l10n("titles.custom.edit"), enabled: true) { edit(set.id) },
                .init(title: l10n("titles.custom.duplicate"), enabled: canAdd) { duplicate(set.id) },
                .init(title: l10n("titles.custom.delete"), enabled: true, destructive: true) { delete(set.id) },
            ]
        }
    }

    // MARK: Commands

    /// A copy of `id`, selected, with its editor open and the name field focused.
    private func duplicate(_ id: String) {
        guard let newID = titles.copy(id) else { return }
        cursor = newID
        editorFocus = .name
    }

    private func edit(_ id: String) {
        titles.select(id)
        cursor = id
        editorFocus = .name
    }

    private func delete(_ id: String) {
        let list = order
        let index = list.firstIndex(of: id) ?? 0
        titles.delete(id)
        let remaining = order
        cursor = remaining.indices.contains(index) ? remaining[index] : remaining.last
    }

    private func handle(_ key: KeyEquivalent) -> Bool {
        let list = order
        let current = cursor.flatMap(list.firstIndex(of:)) ?? list.firstIndex(of: titles.document.selected) ?? 0
        let presets = TitlePreset.allCases.count
        func move(to index: Int) {
            cursor = list[min(max(index, 0), list.count - 1)]
        }
        switch key {
        case .leftArrow: move(to: current - 1)
        case .rightArrow: move(to: current + 1)
        case .upArrow, .downArrow:
            // Row-major within each group: presets, then custom sets with New.
            let inPresets = current < presets
            let groupStart = inPresets ? 0 : presets
            let column = (current - groupStart) % columns
            if key == .downArrow {
                let next = current + columns
                if inPresets, next >= presets {
                    move(to: min(presets + column, list.count - 1))
                } else if next < list.count {
                    move(to: next)
                }
            } else {
                let previous = current - columns
                if !inPresets, previous < presets {
                    let lastRow = (presets - 1) / columns * columns
                    move(to: min(lastRow + column, presets - 1))
                } else if previous >= 0 {
                    move(to: previous)
                }
            }
        case .space, .return:
            let id = cursor ?? list[min(current, list.count - 1)]
            if id == Self.newCardID {
                if titles.document.canAddCustom { duplicate(titles.document.selected) }
            } else {
                titles.select(id)
            }
        default:
            return false
        }
        return true
    }
}

/// Which editor control the cards ask to focus.
enum TitleEditorFocus: Equatable {
    case name
}

/// One theme card: name over a preview strip of levels 1, 5, 9 and 13 in the heat colors of their thresholds; the
/// selected card has an accent ring, a faint accent fill and a check badge; custom cards show a `⋯` menu.
struct TitleThemeCard: View {
    struct Action {
        let title: String
        let enabled: Bool
        var destructive = false
        let run: () -> Void
    }

    static let height: CGFloat = 64
    static let radius: CGFloat = 10
    static let padding: CGFloat = 12

    let model: AppModel
    let theme: TitleTheme
    /// Room for the preview strip inside the card.
    let stripWidth: CGFloat
    let selected: Bool
    let hovered: Bool
    let keyboardFocused: Bool
    let showsMore: Bool
    let actions: [Action]

    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.colorSchemeContrast) private var contrast

    var body: some View {
        let l10n = model.l10n
        let name = model.titles.name(of: theme, l10n: l10n)
        let titles = TitleThemeText.stripLevels.map { TitleThemeText.text($0, theme, model: model) }
        let shape = RoundedRectangle(cornerRadius: Self.radius, style: .continuous)
        VStack(alignment: .leading, spacing: 5) {
            Text(name)
                .font(.system(size: 13, weight: .semibold))
                .lineLimit(1)
                .padding(.trailing, selected || showsMore ? 44 : 0)
            strip(TitleThemeText.stripChoice(titles, width: stripWidth), titles: titles)
                .font(.system(size: 11))
                .lineLimit(1)
                .truncationMode(.tail)
        }
        .padding(.horizontal, Self.padding)
        .frame(maxWidth: .infinity, minHeight: Self.height, maxHeight: Self.height, alignment: .leading)
        .background(shape.fill(fill))
        .overlay {
            if selected {
                shape.strokeBorder(Color.accentColor, lineWidth: 2)
            } else if keyboardFocused {
                shape.strokeBorder(Color.accentColor.opacity(0.6), lineWidth: 1.5)
            }
        }
        .overlay(alignment: .topTrailing) { badges }
        .contentShape(shape)
        .animation(.easeOut(duration: 0.2), value: hovered)
        .animation(.easeOut(duration: 0.2), value: showsMore)
        .help(TitleThemeText.ladder(theme, model: model))
        .contextMenu { menuItems }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(l10n("titles.card.a11y", name, titles.joined(separator: l10n("list.separator"))))
        .accessibilityAddTraits(selected ? [.isButton, .isSelected] : [.isButton])
        .accessibilityAction { model.titles.select(theme.id) }
        .modifier(CardActions(actions: actions))
    }

    private var fill: Color {
        if selected { return Color.accentColor.opacity(colorScheme == .dark ? 0.16 : 0.09) }
        return Color.primary.opacity(hovered ? (colorScheme == .dark ? 0.09 : 0.07) : (colorScheme == .dark ? 0.055 : 0.04))
    }

    /// The strip with the titles at `levels` (indexes into `stripLevels`).
    private func strip(_ levels: [Int], titles: [String]) -> Text {
        let dark = colorScheme == .dark
        var text = Text("")
        for (position, index) in levels.enumerated() {
            if position > 0 { text = text + Text(" · ").foregroundStyle(.tertiary) }
            let title = TitleThemeText.stripLevels[index]
            let color = HeatText.color(TitleThemeText.heat(title), dark: dark, increasedContrast: contrast == .increased)
            text = text + Text(titles[index]).foregroundStyle(color)
        }
        return text
    }

    @ViewBuilder
    private var badges: some View {
        HStack(spacing: 6) {
            if showsMore, actions.count > 1 {
                Menu {
                    menuItems
                } label: {
                    Image(systemName: "ellipsis")
                        .font(.system(size: 12, weight: .semibold))
                        .foregroundStyle(.secondary)
                        .frame(width: 18, height: 18)
                        .contentShape(Rectangle())
                }
                .menuStyle(.borderlessButton)
                .menuIndicator(.hidden)
                .fixedSize()
                .transition(.opacity)
                .accessibilityLabel(model.l10n("titles.custom.more.a11y"))
            }
            if selected {
                Image(systemName: "checkmark.circle.fill")
                    .font(.system(size: 15))
                    .symbolRenderingMode(.palette)
                    .foregroundStyle(.white, Color.accentColor)
                    .accessibilityHidden(true)
            }
        }
        .padding(.top, 8)
        .padding(.trailing, 9)
    }

    @ViewBuilder
    private var menuItems: some View {
        ForEach(Array(actions.enumerated()), id: \.offset) { _, action in
            Button(role: action.destructive ? .destructive : nil, action: action.run) {
                Text(action.title)
            }
            .disabled(!action.enabled)
        }
    }
}

/// The card's menu actions as VoiceOver custom actions.
private struct CardActions: ViewModifier {
    let actions: [TitleThemeCard.Action]

    func body(content: Content) -> some View {
        actions.reduce(AnyView(content)) { view, action in
            AnyView(view.accessibilityAction(named: Text(action.title)) { if action.enabled { action.run() } })
        }
    }
}
