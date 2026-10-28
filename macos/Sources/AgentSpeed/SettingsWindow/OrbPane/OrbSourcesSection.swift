import AgentSpeedCore
import AgentSpeedOrbModel
import SwiftUI

/// Which sources the selected orb tracks (DESIGN 5, editor): All Sources, then every source in source order with its
/// colour dot and status. With All Sources on, the rows show checked and disabled; the last tracked source cannot be
/// unchecked.
struct OrbSourcesSection<Heading: View>: View {
    let model: AppModel
    let orb: OrbConfig
    @ViewBuilder var heading: Heading

    var body: some View {
        let l10n = model.l10n
        let orbs = model.orbs
        let all = orb.sources.isAll
        Section {
            Toggle(isOn: Binding(
                get: { all },
                set: { on in
                    let first = orbs.availability.detected.first ?? SourceKind.allCases[0]
                    orbs.setSources(on ? .all : .explicit([first]), of: orb.id)
                }
            )) {
                VStack(alignment: .leading, spacing: 2) {
                    Text(l10n("orbs.sources.all"))
                    Footnote(l10n("orbs.sources.allHint"))
                }
            }
            .toggleStyle(.checkbox)

            ForEach(SourceKind.allCases) { source in
                let tracked = orb.sources.tracks(source)
                let isLast = !all && tracked && orb.sources.knownSources.count == 1
                Toggle(isOn: Binding(
                    get: { tracked },
                    set: { _ in orbs.toggleSource(source, of: orb.id) }
                )) {
                    HStack(spacing: Spacing.xs) {
                        SourceDot(source: source, available: status(of: source) == nil)
                        Text(l10n(source.titleKey))
                        Spacer(minLength: Spacing.s)
                        if let status = status(of: source) {
                            Text(l10n(status))
                                .font(.system(size: 12))
                                .foregroundStyle(.secondary)
                        }
                    }
                }
                .toggleStyle(.checkbox)
                .disabled(all || isLast)
                .help(isLast ? l10n("orbs.sources.lastOne") : "")
            }
        } header: {
            VStack(alignment: .leading, spacing: 0) {
                heading
                Text(l10n("orbs.sources"))
            }
        }
    }

    /// `source.disabled` or `source.notFound` for an unavailable source (from the orbs' availability, which changes
    /// rarely, rather than every snapshot).
    private func status(of source: SourceKind) -> String? {
        if !model.settings.isEnabled(source) { return "source.disabled" }
        return model.orbs.availability.detected.contains(source) ? nil : "source.notFound"
    }
}
