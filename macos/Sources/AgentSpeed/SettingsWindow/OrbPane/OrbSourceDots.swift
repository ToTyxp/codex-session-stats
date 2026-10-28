import AgentSpeedCore
import SwiftUI

/// The colour dots of an orb's sources (DESIGN 2b, Identity at a glance); an unavailable source's dot is faded.
struct OrbSourceDots: View {
    let dots: [(source: SourceKind, available: Bool)]
    var size: CGFloat = 8

    @Environment(\.colorScheme) private var colorScheme

    var body: some View {
        HStack(spacing: size * 0.5) {
            ForEach(dots, id: \.source) { dot in
                Circle()
                    .fill(dot.source.tint(dark: colorScheme == .dark))
                    .opacity(dot.available ? 1 : 0.35)
                    .frame(width: size, height: size)
            }
        }
        .accessibilityHidden(true)
    }
}

/// A single source's colour dot.
struct SourceDot: View {
    let source: SourceKind
    var available = true
    var size: CGFloat = 8

    @Environment(\.colorScheme) private var colorScheme

    var body: some View {
        Circle()
            .fill(source.tint(dark: colorScheme == .dark))
            .opacity(available ? 1 : 0.45)
            .frame(width: size, height: size)
            .accessibilityHidden(true)
    }
}
