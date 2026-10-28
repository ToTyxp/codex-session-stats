import AgentSpeedCore
import AgentSpeedOrbModel
import SwiftUI

/// Settings → General, below the redline: the title theme picker, the undo banner and the custom set editor
/// (DESIGN 5, Title themes). Every change applies live and persists at once.
struct TitleThemeSection: View {
    let model: AppModel
    /// The pane can be seen; the editor's previews run only then.
    let active: Bool

    @State private var width: CGFloat = SettingsPaneView.wideWidth - 40
    @State private var editorFocus: TitleEditorFocus?

    var body: some View {
        let l10n = model.l10n
        let titles = model.titles
        let reduceMotion = model.accessibility.reduceMotion
        Section {
            VStack(alignment: .leading, spacing: Spacing.s) {
                VStack(alignment: .leading, spacing: 3) {
                    Text(l10n("titles.theme"))
                        .font(.system(size: 13, weight: .semibold))
                    Text(l10n("titles.theme.hint"))
                        .font(.system(size: 11))
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
                TitleThemeCards(model: model, width: width, editorFocus: $editorFocus)
            }
            .padding(.vertical, Spacing.xxs)
            .onGeometryChange(for: CGFloat.self) { $0.size.width } action: { width = $0 }
        }
        if titles.undoBanner != nil {
            Section {
                TitleThemesBanner(model: model)
            }
            .transition(.opacity)
        }
        if case .custom(let set) = titles.document.general {
            Section {
                TitleSetEditor(model: model, set: set, width: width, active: active, focusRequest: $editorFocus)
            }
            .transition(reduceMotion ? .opacity : .opacity.combined(with: .move(edge: .top)))
        }
    }
}

/// The inline banner after a custom set is deleted or reset: what happened and Undo, for 8 s.
struct TitleThemesBanner: View {
    let model: AppModel

    var body: some View {
        let l10n = model.l10n
        HStack(spacing: Spacing.xs) {
            Image(systemName: "arrow.uturn.backward.circle.fill")
                .foregroundStyle(.secondary)
            Text(model.titles.undoMessage(l10n))
                .font(.system(size: 12))
                .lineLimit(2)
                .fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: Spacing.s)
            Button(l10n("orbs.undo")) {
                model.titles.undo()
            }
            .buttonStyle(.borderless)
            .font(.system(size: 12, weight: .semibold))
            .tint(.accentColor)
        }
        .accessibilityElement(children: .contain)
    }
}
