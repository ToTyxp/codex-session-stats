import AppKit
import SwiftUI

/// One custom title field (DESIGN 5, Title themes editor). An AppKit text field so the text rules run only on commit —
/// Return, focus leaving, or 0.8 s without typing — and never while an input method is composing; ↑ / ↓ move between
/// the rows, Return commits and moves down, Esc restores the value the field had when it was focused.
struct TitleField: NSViewRepresentable {
    /// The committed text ("" = the base title, shown as the placeholder).
    let text: String
    let placeholder: String
    let accessibilityLabel: String
    /// Asks the field to take the focus (the editor's keyboard navigation).
    let wantsFocus: Bool
    var onFocus: () -> Void
    /// Uncommitted text while typing (live previews).
    var onDraft: (String) -> Void
    /// Commits the text; returns the stored text after the rules.
    var onCommit: (String) -> String
    var onMove: (Int) -> Void
    /// Editing ended (focus left the field).
    var onBlur: () -> Void = {}

    /// Reports gaining the focus, which the delegate only learns about at the first keystroke.
    final class FocusReportingField: NSTextField {
        var onFocus: (() -> Void)?

        override func becomeFirstResponder() -> Bool {
            let accepted = super.becomeFirstResponder()
            if accepted { onFocus?() }
            return accepted
        }
    }

    @MainActor
    final class Coordinator: NSObject, NSTextFieldDelegate {
        var parent: TitleField
        var focusText = ""
        var debounce: Timer?
        /// The committed text last seen, and the one this field stored itself (not an outside change).
        var modelText: String
        var ownCommit: String?
        weak var field: NSTextField?

        init(parent: TitleField) {
            self.parent = parent
            modelText = parent.text
        }

        func focused() {
            focusText = field?.stringValue ?? ""
            parent.onFocus()
        }

        func controlTextDidChange(_ notification: Notification) {
            guard let field else { return }
            parent.onDraft(field.stringValue)
            debounce?.invalidate()
            let timer = Timer(timeInterval: 0.8, repeats: false) { [weak self] _ in
                MainActor.assumeIsolated { self?.commitIfIdle() }
            }
            RunLoop.main.add(timer, forMode: .common)
            debounce = timer
        }

        func controlTextDidEndEditing(_ notification: Notification) {
            commit()
            parent.onBlur()
        }

        func control(_ control: NSControl, textView: NSTextView, doCommandBy selector: Selector) -> Bool {
            switch selector {
            case #selector(NSResponder.insertNewline(_:)):
                commit()
                parent.onMove(1)
                return true
            case #selector(NSResponder.moveUp(_:)):
                guard !textView.hasMarkedText() else { return false }
                parent.onMove(-1)
                return true
            case #selector(NSResponder.moveDown(_:)):
                guard !textView.hasMarkedText() else { return false }
                parent.onMove(1)
                return true
            case #selector(NSResponder.cancelOperation(_:)):
                guard !textView.hasMarkedText() else { return false }
                textView.string = focusText
                field?.stringValue = focusText
                parent.onDraft(focusText)
                return true
            default:
                return false
            }
        }

        /// The 0.8 s pause commits, unless an input method is still composing.
        private func commitIfIdle() {
            guard let field, let editor = field.currentEditor() as? NSTextView else {
                commit()
                return
            }
            guard !editor.hasMarkedText() else { return }
            commit()
        }

        func commit() {
            debounce?.invalidate()
            debounce = nil
            guard let field else { return }
            let stored = parent.onCommit(field.stringValue)
            ownCommit = stored
            if stored != field.stringValue { show(stored) }
        }

        func show(_ text: String) {
            (field?.currentEditor() as? NSTextView)?.string = text
            field?.stringValue = text
        }
    }

    func makeCoordinator() -> Coordinator { Coordinator(parent: self) }

    func makeNSView(context: Context) -> NSTextField {
        let field = FocusReportingField(string: text)
        field.onFocus = { [weak coordinator = context.coordinator] in coordinator?.focused() }
        field.placeholderString = placeholder
        field.font = .systemFont(ofSize: 13)
        field.bezelStyle = .roundedBezel
        field.isBezeled = true
        field.lineBreakMode = .byTruncatingTail
        field.usesSingleLineMode = true
        field.cell?.isScrollable = true
        field.delegate = context.coordinator
        field.setAccessibilityLabel(accessibilityLabel)
        field.setContentHuggingPriority(.defaultLow, for: .horizontal)
        field.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        context.coordinator.field = field
        return field
    }

    func updateNSView(_ field: NSTextField, context: Context) {
        context.coordinator.parent = self
        if field.placeholderString != placeholder { field.placeholderString = placeholder }
        if field.accessibilityLabel() != accessibilityLabel { field.setAccessibilityLabel(accessibilityLabel) }
        // Typing only changes the draft, so a new committed text is this field's own commit or an outside edit (reset,
        // undo, base change), which replaces the field's text unless an input method is composing.
        let coordinator = context.coordinator
        if text != coordinator.modelText {
            coordinator.modelText = text
            let own = coordinator.ownCommit == text
            coordinator.ownCommit = nil
            let composing = (field.currentEditor() as? NSTextView)?.hasMarkedText() ?? false
            if !own, !composing, field.stringValue != text { coordinator.show(text) }
        }
        if wantsFocus, field.window != nil, field.currentEditor() == nil {
            DispatchQueue.main.async { field.window?.makeFirstResponder(field) }
        }
    }

    static func dismantleNSView(_ field: NSTextField, coordinator: Coordinator) {
        coordinator.debounce?.invalidate()
        coordinator.debounce = nil
    }
}
