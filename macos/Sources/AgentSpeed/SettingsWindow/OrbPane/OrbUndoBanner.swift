import SwiftUI

/// The inline banner above the orb list after a split, merge or remove: what happened and Undo, for 8 s
/// (DESIGN 2b, Split and merge).
struct OrbUndoBanner: View {
    let model: AppModel

    var body: some View {
        let l10n = model.l10n
        HStack(spacing: Spacing.xs) {
            Image(systemName: "arrow.uturn.backward.circle.fill")
                .foregroundStyle(.secondary)
            Text(model.orbs.undoMessage(l10n))
                .font(.system(size: 12))
                .foregroundStyle(.primary)
                .lineLimit(2)
                .fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: Spacing.s)
            Button(l10n("orbs.undo")) {
                model.orbs.undo()
            }
            .buttonStyle(.borderless)
            .font(.system(size: 12, weight: .semibold))
            .tint(.accentColor)
            .keyboardShortcut("z", modifiers: .command)
        }
        .padding(.horizontal, Spacing.s)
        .padding(.vertical, 7)
        .background(RoundedRectangle(cornerRadius: 8, style: .continuous).fill(.quaternary.opacity(0.6)))
        .textCase(nil)
        .accessibilityElement(children: .contain)
    }
}
