import AgentSpeedCore
import AgentSpeedOrbModel
import SwiftUI
import UniformTypeIdentifiers

/// The orb list (DESIGN 5, Floating orb pane 2): one 52 pt row per orb with a live thumbnail, name, style and size, live
/// value and visibility; the footer adds, removes, duplicates, splits by source and merges. Rows reorder by drag; ↑ / ↓
/// select, Space shows or hides, ⌘D duplicates, ⌫ removes.
struct OrbListSection: View {
    let model: AppModel
    let selection: String
    let active: Bool
    @Binding var hovered: String?

    static let footerHeight: CGFloat = 32

    @FocusState private var focused: Bool
    @State private var dragged: String?
    @State private var thumbnailFrames: [String: CGRect] = [:]
    @Environment(\.orbStillTime) private var stillTime

    /// Live thumbnails of the shown orbs share one Metal layer (one present per frame for every row).
    private var grouped: Bool { stillTime == nil }

    private var thumbnailSpecs: [OrbPreviewSpec] {
        let orbs = model.orbs
        return orbs.orbs.compactMap { orb in
            guard orbs.masterVisible, orb.visible, let feed = orbs.runtime(orb.id)?.feed,
                  let frame = thumbnailFrames[orb.id] else { return nil }
            let side = (OrbListRow.thumbnailDiameter * OrbView.canvasScale).rounded(.up)
            let canvas = CGRect(x: frame.midX - side / 2, y: frame.midY - side / 2, width: side, height: side)
            return OrbPreviewSpec(id: orb.id, feed: feed, style: orb.style, diameter: OrbListRow.thumbnailDiameter,
                                  frame: canvas, clip: frame, thumbnail: true)
        }
    }

    private var orbs: OrbsController { model.orbs }

    var body: some View {
        let l10n = model.l10n
        let list = orbs.orbs
        Section {
            VStack(spacing: 0) {
                ForEach(Array(list.enumerated()), id: \.element.id) { index, orb in
                    let selected = orb.id == selection
                    let previousSelected = index > 0 && list[index - 1].id == selection
                    OrbListRow(model: model, orb: orb, selected: selected, focused: focused, active: active,
                               grouped: grouped) { thumbnailFrames[orb.id] = $0 }
                        .overlay(alignment: .top) {
                            if index > 0, !selected, !previousSelected { OrbListHairline().padding(.leading, 60).padding(.trailing, 8) }
                        }
                        .contentShape(Rectangle())
                        .onTapGesture {
                            orbs.settingsSelection = orb.id
                            focused = true
                        }
                        .onHover { inside in
                            if inside { hovered = orb.id } else if hovered == orb.id { hovered = nil }
                        }
                        .onDrag {
                            dragged = orb.id
                            return NSItemProvider(object: orb.id as NSString)
                        }
                        .onDrop(of: [UTType.plainText], delegate: OrbRowDropDelegate(target: orb.id, orbs: orbs, dragged: $dragged))
                }
                footer(l10n: l10n)
                    .overlay(alignment: .top) { OrbListHairline() }
            }
            .coordinateSpace(.named(OrbListRow.space))
            .overlay {
                if grouped {
                    OrbPreviewGroupView(model: model, specs: thumbnailSpecs, cap: OrbListRow.thumbnailRate, active: active)
                        .allowsHitTesting(false)
                        .accessibilityHidden(true)
                }
            }
            .focusable(interactions: .edit)
            .focused($focused)
            .focusEffectDisabled()
            .onKeyPress(keys: [.upArrow, .downArrow, .space, .delete, .deleteForward]) { press in
                handle(press.key) ? .handled : .ignored
            }
            // The Delete key arrives as U+007F (backspace) or U+F728 (forward delete) on a Mac keyboard.
            .onKeyPress(characters: CharacterSet(charactersIn: "\u{7F}\u{8}\u{F728}")) { _ in
                handle(.delete) ? .handled : .ignored
            }
            .onKeyPress(characters: CharacterSet(charactersIn: "dD")) { press in
                guard press.modifiers.contains(.command), orbs.canAdd else { return .ignored }
                orbs.duplicate(selection)
                return .handled
            }
            .accessibilityElement(children: .contain)
            .accessibilityLabel(l10n("settings.orb"))
        } header: {
            VStack(alignment: .leading, spacing: Spacing.xs) {
                if orbs.undoBanner != nil {
                    OrbUndoBanner(model: model)
                        .transition(.move(edge: .top).combined(with: .opacity))
                }
                HStack(alignment: .firstTextBaseline) {
                    Text(l10n("settings.orb"))
                    Spacer()
                    Text(l10n("orbs.count", list.count, OrbsDocument.maxOrbs))
                        .font(.subheadline)
                        .monospacedDigit()
                        .foregroundStyle(.secondary)
                }
            }
            .animation(model.accessibility.reduceMotion ? nil : Motion.layout, value: orbs.undoBanner?.id)
        }
    }

    private func handle(_ key: KeyEquivalent) -> Bool {
        let list = orbs.orbs
        guard let index = list.firstIndex(where: { $0.id == selection }) else { return false }
        switch key {
        case .upArrow:
            guard index > 0 else { return true }
            orbs.settingsSelection = list[index - 1].id
        case .downArrow:
            guard index + 1 < list.count else { return true }
            orbs.settingsSelection = list[index + 1].id
        case .space:
            orbs.setVisible(!(orbs.masterVisible && list[index].visible), of: list[index].id)
        case .delete, .deleteForward:
            guard list.count > 1 else { return true }
            orbs.remove(list[index].id)
        default:
            return false
        }
        return true
    }

    @ViewBuilder
    private func footer(l10n: Localizer) -> some View {
        let count = orbs.orbs.count
        let limit = l10n("orbs.limit", OrbsDocument.maxOrbs)
        HStack(spacing: Spacing.xxs) {
            Button {
                orbs.add(copying: selection)
            } label: {
                Image(systemName: "plus").frame(width: 22, height: 22)
            }
            .disabled(!orbs.canAdd)
            .help(orbs.canAdd ? l10n("orbs.add") : limit)
            .accessibilityLabel(l10n("orbs.add"))

            Button {
                orbs.remove(selection)
            } label: {
                Image(systemName: "minus").frame(width: 22, height: 22)
            }
            .disabled(count <= 1)
            .help(l10n("orbs.remove"))
            .accessibilityLabel(l10n("orbs.remove"))

            Divider().frame(height: 14).padding(.horizontal, Spacing.xxs)

            Button {
                orbs.duplicate(selection)
            } label: {
                Label(l10n("orbs.duplicate"), systemImage: "plus.square.on.square")
            }
            .disabled(!orbs.canAdd)
            .help(orbs.canAdd ? l10n("orbs.duplicate") : limit)

            Spacer(minLength: Spacing.s)

            Button(l10n("orbs.split")) {
                orbs.splitBySource(selected: selection)
            }
            .buttonStyle(OrbListFooterButtonStyle(accent: true))
            .disabled(!orbs.canSplitBySource)
            .help(orbs.canSplitBySource ? l10n("orbs.split.hint") : l10n("orbs.split.unavailable"))
            .padding(.trailing, Spacing.xs)

            Button(l10n("orbs.merge")) {
                orbs.merge(keeping: selection)
            }
            .buttonStyle(OrbListFooterButtonStyle(accent: true))
            .disabled(!orbs.canMerge)
            .help(l10n("orbs.merge.hint"))
        }
        .buttonStyle(OrbListFooterButtonStyle())
        .padding(.horizontal, Spacing.xs)
        .frame(height: Self.footerHeight)
    }
}

/// Borderless footer buttons: glyphs in the label color, text actions in the accent color.
private struct OrbListFooterButtonStyle: ButtonStyle {
    var accent = false
    @Environment(\.isEnabled) private var isEnabled

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.system(size: 12, weight: .medium))
            .labelStyle(OrbFooterLabelStyle())
            .foregroundStyle(isEnabled ? (accent ? AnyShapeStyle(Color.accentColor) : AnyShapeStyle(.primary)) : AnyShapeStyle(.quaternary))
            .contentShape(Rectangle())
            .opacity(configuration.isPressed ? 0.55 : 1)
    }
}

private struct OrbFooterLabelStyle: LabelStyle {
    func makeBody(configuration: Configuration) -> some View {
        HStack(spacing: 5) {
            configuration.icon
            configuration.title
        }
    }
}

/// Moves the dragged row to the row it passes over, so the list reorders live.
private struct OrbRowDropDelegate: DropDelegate {
    let target: String
    let orbs: OrbsController
    @Binding var dragged: String?

    func dropEntered(info: DropInfo) {
        guard let dragged, dragged != target,
              let from = orbs.orbs.firstIndex(where: { $0.id == dragged }),
              let to = orbs.orbs.firstIndex(where: { $0.id == target }) else { return }
        orbs.move(fromOffsets: IndexSet(integer: from), toOffset: to > from ? to + 1 : to)
    }

    func dropUpdated(info: DropInfo) -> DropProposal? { DropProposal(operation: .move) }

    func performDrop(info: DropInfo) -> Bool {
        dragged = nil
        return true
    }
}

/// One orb: thumbnail, name over style and size, live value and the visibility toggle.
struct OrbListRow: View {
    static let height: CGFloat = 52
    static let thumbnailDiameter: CGFloat = 38
    nonisolated static let space = "orbList"
    /// A 38 pt thumbnail shows little more than the value, which changes at most four times a second (DESIGN 5:
    /// ≤ 15 fps).
    static let thumbnailRate = 10.0

    let model: AppModel
    let orb: OrbConfig
    let selected: Bool
    let focused: Bool
    let active: Bool
    /// The list's shared layer draws the thumbnail of a shown orb; a hidden orb's row draws its own (dimmed, still).
    var grouped = false
    var onThumbnailFrame: (CGRect) -> Void = { _ in }

    var body: some View {
        let l10n = model.l10n
        let orbs = model.orbs
        let shown = orbs.masterVisible && orb.visible
        let runtime = orbs.runtime(orb.id)
        HStack(spacing: 0) {
            Group {
                if let runtime {
                    OrbPreviewView(model: model, feed: runtime.feed, style: orb.style, diameter: Self.thumbnailDiameter,
                                   thumbnail: true, cap: Self.thumbnailRate, active: false, dynamic: !(grouped && shown))
                } else {
                    Color.clear
                }
            }
            .frame(width: 60, height: Self.height)
            .clipped()
            .onGeometryChange(for: CGRect.self) { $0.frame(in: .named(Self.space)) } action: { onThumbnailFrame($0) }
            VStack(alignment: .leading, spacing: 1) {
                Text(orbs.displayName(of: orb.id))
                    .font(.system(size: 13, weight: selected ? .medium : .regular))
                    .foregroundStyle(shown ? AnyShapeStyle(.primary) : AnyShapeStyle(.secondary))
                    .lineLimit(1)
                Text(l10n("orbs.row.detail", l10n(orb.style.titleKey), Int(orb.size.rounded())))
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
            Spacer(minLength: Spacing.s)
            valueText(runtime: runtime, shown: shown, l10n: l10n)
            Button {
                orbs.setVisible(!shown, of: orb.id)
            } label: {
                Image(systemName: shown ? "eye" : "eye.slash")
                    .font(.system(size: 13))
                    .foregroundStyle(shown ? AnyShapeStyle(.secondary) : AnyShapeStyle(.tertiary))
                    .frame(width: 28, height: 28)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .help(l10n("orbs.visible"))
            .accessibilityLabel(l10n("orbs.visible"))
            .accessibilityAddTraits(shown ? .isSelected : [])
            .padding(.trailing, Spacing.xs)
        }
        .frame(height: Self.height)
        .opacity(shown ? 1 : 0.5)
        .background(OrbRowTint(selected: selected, focused: focused))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(orbs.displayName(of: orb.id))
        .accessibilityAddTraits(selected ? .isSelected : [])
    }

    @ViewBuilder
    private func valueText(runtime: OrbRuntime?, shown: Bool, l10n: Localizer) -> some View {
        let text: String = if !shown {
            l10n("orbs.hidden")
        } else if let runtime, !runtime.isUnavailable {
            if let money = runtime.feed.money {
                // A money orb lists its own unit (DESIGN 9): `0.0042 US$/s`.
                money.unknown ? "– \(money.unit)" : "\(money.atLeast ? "≥" : "")\(money.number) \(money.unit)"
            } else {
                l10n("mac.settings.speedValue", SpeedFormat.speed(runtime.feed.gauge.readoutValue))
            }
        } else {
            "–"
        }
        Text(text)
            .font(.system(size: 12))
            .monospacedDigit()
            .foregroundStyle(.tertiary)
            .lineLimit(1)
            .padding(.trailing, Spacing.xxs)
    }
}

/// A separator line that takes no layout space.
private struct OrbListHairline: View {
    var body: some View {
        Rectangle()
            .fill(Color(nsColor: .separatorColor))
            .frame(height: 0.5)
            .frame(maxWidth: .infinity)
            .allowsHitTesting(false)
    }
}

/// The accent tint of the selected row: a rounded band inside the group, as in macOS source lists.
private struct OrbRowTint: View {
    let selected: Bool
    let focused: Bool
    @Environment(\.colorScheme) private var colorScheme

    var body: some View {
        RoundedRectangle(cornerRadius: 8, style: .continuous)
            .fill(Color.accentColor.opacity(selected ? (colorScheme == .dark ? 0.2 : 0.12) * (focused ? 1.25 : 1) : 0))
    }
}
