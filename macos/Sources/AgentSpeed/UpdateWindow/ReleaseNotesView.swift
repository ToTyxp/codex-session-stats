import AgentSpeedCore
import SwiftUI

/// Release notes as rich text in a scrolling card at most 240 pt tall. Equatable on the tag, so progress updates
/// elsewhere in the window never re-parse the Markdown.
struct ReleaseNotesView: View, Equatable {
    static let maxHeight: CGFloat = 240

    let tag: String
    let markdown: String

    @Environment(\.colorSchemeContrast) private var contrast

    nonisolated static func == (lhs: ReleaseNotesView, rhs: ReleaseNotesView) -> Bool {
        lhs.tag == rhs.tag && lhs.markdown == rhs.markdown
    }

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: 10, style: .continuous)
        let blocks = ReleaseNotes.blocks(from: markdown)
        ScrollView(.vertical) {
            VStack(alignment: .leading, spacing: 7) {
                ForEach(Array(blocks.enumerated()), id: \.offset) { _, block in
                    ReleaseNotesBlockView(block: block)
                }
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 12)
            .frame(maxWidth: .infinity, alignment: .leading)
            .textSelection(.enabled)
        }
        .scrollBounceBehavior(.basedOnSize)
        .frame(maxHeight: Self.maxHeight)
        .fixedSize(horizontal: false, vertical: true)
        .background(Color.primary.opacity(0.04), in: shape)
        .overlay {
            shape.strokeBorder(Color.primary.opacity(contrast == .increased ? 0.3 : 0.08), lineWidth: 1)
        }
        .clipShape(shape)
        .environment(\.openURL, OpenURLAction { url in
            ReleaseNotesInline.isWebLink(url) ? .systemAction : .discarded
        })
    }
}

private struct ReleaseNotesBlockView: View {
    let block: ReleaseNotesBlock

    var body: some View {
        switch block {
        case .heading(let level, let text):
            Text(ReleaseNotesInline.render(text))
                .font(.system(size: level <= 2 ? 13 : 12, weight: .semibold))
                .padding(.top, 3)
                .fixedSize(horizontal: false, vertical: true)
                .accessibilityAddTraits(.isHeader)
        case .paragraph(let text):
            Text(ReleaseNotesInline.render(text))
                .font(.system(size: 12))
                .lineSpacing(2)
                .fixedSize(horizontal: false, vertical: true)
        case .listItem(let marker, let depth, let text):
            HStack(alignment: .firstTextBaseline, spacing: 6) {
                Text(marker)
                    .font(.system(size: 12).monospacedDigit())
                    .foregroundStyle(.secondary)
                    .frame(minWidth: marker == "•" ? 8 : 14, alignment: .trailing)
                    .accessibilityHidden(marker == "•")
                Text(ReleaseNotesInline.render(text))
                    .font(.system(size: 12))
                    .lineSpacing(2)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .padding(.leading, CGFloat(depth) * 14)
        case .code(let text):
            Text(text)
                .font(.system(size: 11, design: .monospaced))
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(8)
                .background(Color.primary.opacity(0.05), in: RoundedRectangle(cornerRadius: 6, style: .continuous))
        case .quote(let text):
            HStack(spacing: 8) {
                Capsule()
                    .fill(Color.secondary.opacity(0.45))
                    .frame(width: 2.5)
                Text(ReleaseNotesInline.render(text))
                    .font(.system(size: 12))
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        case .rule:
            Hairline()
                .padding(.vertical, 2)
        }
    }
}

/// Inline Markdown (emphasis, code, links). Only web links stay clickable.
enum ReleaseNotesInline {
    static func render(_ text: String) -> AttributedString {
        let options = AttributedString.MarkdownParsingOptions(
            interpretedSyntax: .inlineOnlyPreservingWhitespace,
            failurePolicy: .returnPartiallyParsedIfPossible
        )
        var result = (try? AttributedString(markdown: text, options: options)) ?? AttributedString(text)
        let unsafe = result.runs.compactMap { run -> Range<AttributedString.Index>? in
            guard let link = run.link, !isWebLink(link) else { return nil }
            return run.range
        }
        for range in unsafe { result[range].link = nil }
        return result
    }

    static func isWebLink(_ url: URL) -> Bool {
        let scheme = url.scheme?.lowercased()
        return scheme == "https" || scheme == "http"
    }
}
