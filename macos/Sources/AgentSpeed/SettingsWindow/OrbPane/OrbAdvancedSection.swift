import AgentSpeedCore
import AgentSpeedOrbModel
import SwiftUI

/// The collapsed Advanced group of the selected orb (DESIGN 5, editor): its name (empty = automatic) and its own
/// redline for the current metric, following General by default.
struct OrbAdvancedSection: View {
    let model: AppModel
    let orb: OrbConfig
    @Binding var expanded: Bool

    @State private var name = ""
    @State private var nameOrb: String?
    @State private var highlightedTitle: SpeedTitle?

    var body: some View {
        let l10n = model.l10n
        Section {
            Button {
                withAnimation(model.accessibility.reduceMotion ? nil : Motion.layout) { expanded.toggle() }
            } label: {
                HStack(spacing: Spacing.xs) {
                    Text(l10n("orbs.advanced"))
                        .foregroundStyle(.primary)
                    Spacer(minLength: Spacing.s)
                    if !expanded {
                        Text(summary(l10n))
                            .font(.system(size: 12))
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                    }
                    Image(systemName: "chevron.right")
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundStyle(.secondary)
                        .rotationEffect(.degrees(expanded ? 90 : 0))
                }
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .accessibilityAddTraits(expanded ? .isSelected : [])
            .accessibilityValue(Text(expanded ? "" : summary(l10n)))
            if expanded {
                nameRow(l10n: l10n)
                themeRow(l10n: l10n)
                redlineRows(l10n: l10n)
            }
        }
        .onAppear { syncName() }
        .onChange(of: orb.id) { syncName() }
    }

    private func summary(_ l10n: Localizer) -> String {
        [l10n("orbs.name"), l10n("titles.theme"), l10n("settings.redline")].joined(separator: l10n("list.separator"))
    }

    /// This orb's title theme: General (default), a preset or a custom set (DESIGN 2b `titleTheme`; sets are edited in
    /// General).
    private func themeRow(l10n: Localizer) -> some View {
        let titles = model.titles
        let document = titles.document
        let current = document.theme(id: orb.titleTheme) == nil ? nil : orb.titleTheme
        return VStack(alignment: .leading, spacing: Spacing.xxs) {
            LabeledContent {
                Picker(l10n("titles.theme"), selection: Binding<String?>(
                    get: { current },
                    set: { model.orbs.setTitleTheme($0, of: orb.id) }
                )) {
                    Text(l10n("orbs.titleTheme.global", titles.name(of: document.general, l10n: l10n))).tag(String?.none)
                    Divider()
                    ForEach(TitlePreset.allCases) { preset in
                        Text(l10n(preset.nameKey)).tag(String?.some(preset.rawValue))
                    }
                    if !document.custom.isEmpty {
                        Divider()
                        ForEach(document.custom) { set in
                            Text(set.name).tag(String?.some(set.id))
                        }
                    }
                }
                .labelsHidden()
                .pickerStyle(.menu)
                .fixedSize()
            } label: {
                RowTitle(l10n("titles.theme"))
            }
            Footnote(l10n("orbs.titleTheme.hint"))
        }
    }

    private func syncName() {
        guard nameOrb != orb.id else { return }
        nameOrb = orb.id
        name = orb.name ?? ""
    }

    private func nameRow(l10n: Localizer) -> some View {
        LabeledContent {
            TextField(l10n("orbs.name"), text: $name, prompt: Text(l10n("orbs.name.auto", model.orbs.automaticName(of: orb.id))))
                .labelsHidden()
                .textFieldStyle(.roundedBorder)
                .multilineTextAlignment(.leading)
                .frame(width: 300)
                .onChange(of: name) { model.orbs.setName(name, of: orb.id) }
        } label: {
            RowTitle(l10n("orbs.name"))
        }
    }

    @ViewBuilder
    private func redlineRows(l10n: Localizer) -> some View {
        let settings = model.settings
        let metric = settings.metric
        let orbs = model.orbs
        let id = orb.id
        let general = settings.redline(for: metric)
        let custom = metric == .output ? orb.redlineOutput : orb.redlineTotal
        LabeledContent {
            Picker(l10n("settings.redline"), selection: Binding(
                get: { custom != nil },
                set: { orbs.setRedline($0 ? (custom ?? general) : nil, metric: metric, of: id) }
            )) {
                Text(l10n("orbs.redline.global", SpeedFormat.speed(general))).tag(false)
                Text(l10n("orbs.redline.custom")).tag(true)
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .fixedSize()
        } label: {
            RowTitle(l10n("settings.redline"))
        }
        if let custom {
            VStack(alignment: .leading, spacing: Spacing.xs) {
                RedlineControl(
                    redline: Binding(get: { custom }, set: { orbs.setRedline($0, metric: metric, of: id) }),
                    metric: metric,
                    l10n: l10n,
                    reduceMotion: model.accessibility.reduceMotion,
                    highlightedTitle: highlightedTitle,
                    title: l10n("orbs.redline.custom")
                )
                Footnote(l10n("orbs.redline.hint"))
                TitleLadderList(redline: custom, metric: metric, l10n: l10n, highlighted: $highlightedTitle,
                                theme: model.titles.theme(forOrb: orb))
                    .padding(.top, Spacing.xxs)
            }
            .padding(.bottom, Spacing.xxs)
        }
    }
}
