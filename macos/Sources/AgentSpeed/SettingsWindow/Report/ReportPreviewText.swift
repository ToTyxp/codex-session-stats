import AgentSpeedCore
import AppKit
import SwiftUI

/// The report body as the preview shows it (DESIGN 11): pretty-printed in the monospaced system font at 11 pt, keys
/// in the secondary color, values in the label color; selectable, refreshed in place so the scroll position stays.
struct ReportJSONText: View {
    let json: ReportJSON
    var headerLines: [String] = []

    var body: some View {
        Text(Self.attributed(json, headerLines: headerLines))
            .font(.system(size: 11, design: .monospaced))
            .lineSpacing(1.5)
            .textSelection(.enabled)
            .fixedSize(horizontal: false, vertical: true)
            .frame(maxWidth: .infinity, alignment: .leading)
    }

    static func attributed(_ json: ReportJSON, headerLines: [String]) -> AttributedString {
        var out = AttributedString()
        for line in headerLines {
            var run = AttributedString(line + "\n")
            run.foregroundColor = .secondary
            out += run
        }
        if !headerLines.isEmpty { out += AttributedString("\n") }
        for token in json.prettyTokens {
            var run = AttributedString(token.text)
            switch token.kind {
            case .key: run.foregroundColor = .secondary
            case .punctuation: run.foregroundColor = Color(nsColor: .tertiaryLabelColor)
            case .string, .number: run.foregroundColor = .primary
            }
            out += run
        }
        return out
    }
}

/// The scrolling, framed preview with its copy button and caption (consent sheet and preview window).
struct ReportPreviewBox: View {
    let model: AppModel
    let preview: ReportController.Preview?
    var showsRequest = false
    var maxHeight: CGFloat = 220

    @Environment(\.colorSchemeContrast) private var contrast
    @State private var copied = false

    var body: some View {
        let l10n = model.l10n
        let shape = RoundedRectangle(cornerRadius: 8, style: .continuous)
        VStack(alignment: .leading, spacing: Spacing.xs) {
            ScrollView(.vertical) {
                Group {
                    if let preview {
                        // Before the document is known the API URL, and so the request line, is not (DESIGN 11).
                        ReportJSONText(json: preview.body,
                                       headerLines: showsRequest && !preview.assumed ? requestLines(preview) : [])
                    } else {
                        Text("{}")
                            .font(.system(size: 11, design: .monospaced))
                            .foregroundStyle(.tertiary)
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                }
                .padding(Spacing.s)
            }
            .frame(maxHeight: maxHeight)
            .background(Color(nsColor: .textBackgroundColor).opacity(0.6), in: shape)
            .overlay { shape.strokeBorder(Color.primary.opacity(contrast == .increased ? 0.35 : 0.1), lineWidth: 1) }
            .accessibilityElement(children: .combine)
            .accessibilityLabel(l10n("report.preview.title"))
            .accessibilityValue(preview?.copyText ?? "")

            HStack(alignment: .firstTextBaseline, spacing: Spacing.s) {
                Text(l10n(preview?.assumed == true ? "report.preview.assumed" : "report.preview.hint"))
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                    .frame(maxWidth: .infinity, alignment: .leading)
                Button {
                    copy()
                } label: {
                    Label(l10n("report.preview.copy"), systemImage: copied ? "checkmark" : "doc.on.doc")
                        .contentTransition(.symbolEffect(.replace))
                }
                .buttonStyle(.borderless)
                .font(.system(size: 12))
                .disabled(preview == nil)
            }
        }
    }

    private func requestLines(_ preview: ReportController.Preview) -> [String] {
        [preview.requestLine] + preview.headers.map { "\($0.name): \($0.value)" }
    }

    private func copy() {
        guard let preview else { return }
        let pasteboard = NSPasteboard.general
        pasteboard.clearContents()
        pasteboard.setString(preview.copyText, forType: .string)
        copied = true
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) { copied = false }
    }
}
