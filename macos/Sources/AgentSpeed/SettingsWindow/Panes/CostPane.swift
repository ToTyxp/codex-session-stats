import AgentSpeedCore
import SwiftUI

/// Settings → 費用 (DESIGN 9, Settings → Cost): currency with the rate status, the live value unit, subscription
/// marks, and the price table's status and sync.
struct CostPane: View {
    let model: AppModel

    var body: some View {
        @Bindable var settings = model.settings
        let l10n = model.l10n
        Form {
            Section {
                LabeledContent {
                    CurrencyPicker(model: model)
                } label: {
                    RowTitle(l10n("settings.currency"))
                }
                if !model.cost.isUSD {
                    RateStatusRow(model: model)
                }
            } footer: {
                SectionFootnote(l10n("cost.apiEquivalent.hint"))
            }

            Section {
                LiveUnitPicker(model: model)
            } footer: {
                SectionFootnote(l10n("settings.liveUnit.hint"))
            }

            SubscriptionsSection(model: model)

            PriceTableSection(model: model)
        }
        .settingsForm()
        .animation(.easeOut(duration: 0.2), value: model.cost.isUSD)
    }
}

/// `settings.liveUnit` (DESIGN 9): the unit of the menu bar widget and the dashboard hero; orbs choose their own.
struct LiveUnitPicker: View {
    let model: AppModel

    var body: some View {
        @Bindable var settings = model.settings
        let l10n = model.l10n
        SegmentedRow(title: l10n("settings.liveUnit"), selection: $settings.liveUnit, segmentWidth: 110, count: 2) {
            Text(l10n("settings.liveUnit.tokens")).tag(LiveUnit.tokens)
            Text(l10n("settings.liveUnit.cost", model.cost.symbol)).tag(LiveUnit.cost)
        }
    }
}

/// Rate status under the currency (DESIGN 9): `US$1 = NT$32.55 · 2026/10/22 · open.er-api.com` with the
/// ExchangeRate-API attribution when it is the source, `rates.fallback` before the first fetch, `rates.stale` after
/// 7 days, `rates.failed` for 6 s after a failed refresh, and the refresh button.
private struct RateStatusRow: View {
    let model: AppModel

    var body: some View {
        let l10n = model.l10n
        let rates = model.rates
        let network = model.settings.networkEnabled
        HStack(alignment: .firstTextBaseline, spacing: Spacing.s) {
            VStack(alignment: .leading, spacing: 3) {
                Group {
                    if let failure = rates.failureReason {
                        Text(l10n("rates.failed", failure))
                            .foregroundStyle(.secondary)
                    } else {
                        Text(model.cost.rateStatus)
                            .foregroundStyle(.secondary)
                            .textSelection(.enabled)
                    }
                }
                .font(.system(size: 11).monospacedDigit())
                .contentTransition(.opacity)
                if rates.isStale, rates.failureReason == nil {
                    Label(l10n("rates.stale"), systemImage: "clock.badge.exclamationmark")
                        .font(.system(size: 11))
                        .foregroundStyle(.secondary)
                        .imageScale(.small)
                }
                if rates.rates.needsAttribution {
                    Link(l10n("rates.attribution"), destination: ExchangeRateProvider.attributionURL)
                        .font(.system(size: 11))
                }
                if !network {
                    NetworkOffHint(model: model)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            Button(rates.isRefreshing ? l10n("rates.refreshing") : l10n("rates.refresh")) {
                rates.refreshNow()
            }
            .buttonStyle(.borderless)
            .font(.system(size: 12))
            .disabled(!rates.canRefresh)
            .help(network ? "" : l10n("settings.network.offHint"))
        }
        .animation(Motion.fade, value: rates.failureReason)
    }
}

/// Subscription marks per detected source and `includeSubscriptions` (METRICS 9.4, DESIGN 9).
private struct SubscriptionsSection: View {
    let model: AppModel

    @Environment(\.colorScheme) private var colorScheme

    var body: some View {
        @Bindable var settings = model.settings
        let l10n = model.l10n
        let detected = SourceKind.allCases.filter { kind in
            model.snapshot.sources.first { $0.kind == kind }.map { $0.isDetected } ?? false
        }
        Section {
            ForEach(detected) { kind in
                Toggle(isOn: Binding(get: { settings.isSubscription(kind) }, set: { settings.setSubscription(kind, $0) })) {
                    HStack(spacing: Spacing.s) {
                        Circle()
                            .fill(kind.tint(dark: colorScheme == .dark))
                            .frame(width: 8, height: 8)
                            .accessibilityHidden(true)
                        RowTitle(l10n(kind.titleKey))
                    }
                }
                .accessibilityLabel("\(l10n(kind.titleKey)), \(l10n("settings.cost.subscription"))")
                .help(l10n("settings.cost.subscription"))
            }
            SettingToggle(
                title: l10n("settings.cost.includeSubscriptions"),
                subtitle: l10n("settings.cost.includeSubscriptionsHint"),
                isOn: $settings.includeSubscriptions
            )
        } header: {
            Text(l10n("settings.cost.subscriptions"))
        } footer: {
            SectionFootnote(l10n("settings.cost.subscriptionsHint"))
        }
    }
}

/// The price table (DESIGN 9): version, date and model count, last sync, daily sync, Check Now with its inline
/// result, the source link, and today's models without a price.
private struct PriceTableSection: View {
    let model: AppModel

    var body: some View {
        @Bindable var settings = model.settings
        let l10n = model.l10n
        let prices = model.prices
        let table = prices.table
        let network = settings.networkEnabled
        let unknown = unpricedModels
        Section {
            HStack(alignment: .firstTextBaseline, spacing: Spacing.xs) {
                VStack(alignment: .leading, spacing: 2) {
                    RowTitle(l10n("prices.status", table.version, updatedText(table.updated), table.modelCount))
                        .monospacedDigit()
                    TimelineView(.periodic(from: .now, by: 30)) { context in
                        Footnote(lastSyncText(now: context.date))
                    }
                }
                Spacer(minLength: Spacing.xs)
                if table.isBundled {
                    Text(l10n("prices.bundled"))
                        .font(.system(size: 10, weight: .medium))
                        .foregroundStyle(.secondary)
                        .padding(.horizontal, 6)
                        .padding(.vertical, 1)
                        .background(Color.primary.opacity(0.07), in: Capsule())
                }
            }
            .accessibilityElement(children: .combine)

            SettingToggle(title: l10n("prices.autoSync"), subtitle: l10n("prices.autoSync.hint"), isOn: $settings.syncPrices)
                .disabled(!network)

            HStack(alignment: .firstTextBaseline, spacing: Spacing.s) {
                Button(l10n("prices.check")) { prices.checkNow() }
                    .disabled(!prices.canCheck)
                    .help(network ? "" : l10n("settings.network.offHint"))
                checkResult
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                    .lineLimit(2)
                Spacer(minLength: Spacing.xs)
                Link(destination: PriceTableSync.sourcePage) {
                    Label(l10n("prices.viewSource"), systemImage: "arrow.up.right")
                        .labelStyle(TrailingIcon())
                }
                .font(.subheadline)
            }
            .animation(Motion.fade, value: prices.checkState)

            if !network {
                NetworkOffHint(model: model)
            }
            if !unknown.isEmpty {
                Text(l10n("prices.unknownToday", unknown.joined(separator: l10n("list.separator"))))
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                    .textSelection(.enabled)
                    .fixedSize(horizontal: false, vertical: true)
            }
        } header: {
            Text(l10n("prices.title"))
        }
    }

    @ViewBuilder
    private var checkResult: some View {
        let l10n = model.l10n
        let prices = model.prices
        switch prices.checkState {
        case .idle:
            if prices.newerSchemaAvailable { Text(l10n("prices.newerSchema")) }
        case .checking:
            HStack(spacing: 5) {
                ProgressView().controlSize(.mini)
                Text(l10n("prices.checking"))
            }
        case .finished(let outcome):
            switch outcome {
            case .upToDate: Text(l10n("prices.upToDate"))
            case .updated(let version): Text(l10n("prices.updatedTo", version))
            case .newerSchema: Text(l10n("prices.newerSchema"))
            case .failed(let reason): Text(l10n("prices.failed", reason))
            }
        }
    }

    /// Today's models without a price, over every source (selectable, so they can be reported).
    private var unpricedModels: [String] {
        var seen: Set<String> = []
        var models: [String] = []
        for source in model.snapshot.sources {
            for name in source.unpricedModels where seen.insert(name).inserted { models.append(name) }
        }
        return models
    }

    private func updatedText(_ day: String) -> String {
        guard let ms = PriceDate.ms(day) else { return day }
        let date = Date(timeIntervalSince1970: TimeInterval(ms) / 1000)
        var style = Date.FormatStyle(date: .numeric, time: .omitted, locale: model.l10n.locale,
                                     calendar: Calendar(identifier: .gregorian), timeZone: TimeZone(identifier: "UTC")!)
        style = style.year(.defaultDigits).month(.twoDigits).day(.twoDigits)
        return date.formatted(style)
    }

    private func lastSyncText(now: Date) -> String {
        let l10n = model.l10n
        guard let last = model.prices.lastSync else { return l10n("prices.never") }
        let when = now.timeIntervalSince(last) < 60
            ? l10n("time.justNow")
            : last.formatted(Date.RelativeFormatStyle(presentation: .named, unitsStyle: .wide).locale(l10n.locale))
        return l10n("prices.lastSync", when)
    }
}

/// `YYYY-MM-DD` of the price table as a UTC instant.
enum PriceDate {
    static func ms(_ day: String) -> Int64? {
        let parts = day.split(separator: "-").compactMap { Int($0) }
        guard parts.count == 3 else { return nil }
        var components = DateComponents()
        components.year = parts[0]
        components.month = parts[1]
        components.day = parts[2]
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(identifier: "UTC")!
        return calendar.date(from: components).map { Int64($0.timeIntervalSince1970 * 1000) }
    }
}

/// Title first, then a small icon: "View Prices and Sources ↗".
private struct TrailingIcon: LabelStyle {
    func makeBody(configuration: Configuration) -> some View {
        HStack(spacing: 3) {
            configuration.title
            configuration.icon
                .imageScale(.small)
        }
    }
}
