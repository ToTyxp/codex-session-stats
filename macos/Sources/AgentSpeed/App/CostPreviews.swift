import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit
import SwiftUI

/// The money unit for `--render-previews` (group `cost`, DESIGN 9): every orb style in each money state, the unit
/// switch and its hover affordance, the menu bar, the dashboard and its Today popover, and Settings → Cost with the
/// currency list — light and dark, in USD (en), TWD (zh-Hant), CNY (zh-Hans) and JPY (ja).
@MainActor
enum CostPreviews {
    struct PreviewLocale {
        let name: String
        let language: LanguagePreference
        let currency: String
    }

    static let locales: [PreviewLocale] = [
        PreviewLocale(name: "en-usd", language: .en, currency: "USD"),
        PreviewLocale(name: "zhhant-twd", language: .zhHant, currency: "TWD"),
        PreviewLocale(name: "zhhans-cny", language: .zhHans, currency: "CNY"),
        PreviewLocale(name: "ja-jpy", language: .ja, currency: "JPY"),
    ]

    /// Deterministic rates of 2026-10-22 from ExchangeRate-API (its attribution shows in Settings).
    static let rates = ExchangeRateSet(
        provider: ExchangeRates.erAPIHost, url: "https://open.er-api.com/v6/latest/USD", dateMs: 1_792_627_351_000,
        fetchedMs: 1_792_630_000_000,
        rates: ["CNY": Decimal(string: "6.689327")!, "TWD": Decimal(string: "32.411205")!, "JPY": Decimal(string: "158.100039")!]
    )

    enum State: CaseIterable {
        case idle, cruise, partial, unknown, subscription, redline, hover
    }

    static func render(model: AppModel, into directory: URL) {
        let savedLanguage = model.settings.language
        let savedCurrency = model.settings.currency
        let savedUnit = model.settings.liveUnit
        defer {
            apply(model, language: savedLanguage, currency: savedCurrency)
            model.settings.liveUnit = savedUnit
        }
        model.prices.preview(lastSync: Date().addingTimeInterval(-3 * 3600))
        for locale in locales {
            apply(model, language: locale.language, currency: locale.currency)
            for dark in [false, true] {
                renderOrbSheet(model: model, name: "\(locale.name)-\(dark ? "dark" : "light")", dark: dark, into: directory)
            }
        }
        apply(model, language: .zhHant, currency: "TWD")
        for dark in [false, true] {
            let scheme = dark ? "dark" : "light"
            renderSwitch(model: model, dark: dark, to: directory.appendingPathComponent("cost-orb-switch-\(scheme).png"))
        }
        renderMenuBar(model: model, into: directory)
        renderMenuBarSecondary(model: model, into: directory)
        renderDashboards(model: model, into: directory)
        renderSettings(model: model, into: directory)
    }

    static func apply(_ model: AppModel, language: LanguagePreference, currency: String) {
        model.settings.language = language
        model.l10n.apply(language)
        model.settings.currency = currency
        model.rates.preview(rates: rates, currency: currency)
    }

    // MARK: - Orbs

    /// Rows: the five styles; columns: idle, cruise, partly priced (≥), nothing priced (–), subscription only,
    /// redline, and the hover line.
    static func renderOrbSheet(model: AppModel, name: String, dark: Bool, into directory: URL) {
        let redline = model.settings.redline
        var rows: [[NSImage]] = []
        for style in OrbStyleKind.allCases {
            var row: [NSImage] = []
            for state in State.allCases {
                let speed: Double = switch state {
                case .idle, .subscription: 0
                case .cruise, .partial, .unknown, .hover: redline * 0.07
                case .redline: redline * 0.9
                }
                let image = SurfacePreviews.stageImage(model: model, style: style, size: 148, dark: dark, value: speed,
                                                       extra: state == .hover ? .hover : .none) { stage in
                    stage.feed.unit = .cost
                    stage.feed.money = money(model: model, state: state, speed: speed)
                }
                if let image { row.append(SurfacePreviews.pointImage(image)) }
            }
            rows.append(row)
        }
        SurfacePreviews.writeSheet(rows, dark: dark, to: directory.appendingPathComponent("cost-orbs-\(name).png"))
    }

    static func money(model: AppModel, state: State, speed: Double) -> LiveMoney {
        let perToken = 21.0
        let reading: CostReading = switch state {
        case .idle:
            CostReading(today: CostTally(costMicroUsd: 4_210_000), todayRequests: 420)
        case .cruise, .hover:
            CostReading(microUsdPerSecond: speed * perToken, liveOutput: speed, today: CostTally(costMicroUsd: 4_210_000),
                        todayRequests: 420)
        case .partial:
            CostReading(microUsdPerSecond: speed * perToken * 0.6, knownShare: 0.6, liveOutput: speed,
                        today: CostTally(costMicroUsd: 2_380_000, unpricedRequests: 31), todayRequests: 420, state: .partial)
        case .unknown:
            CostReading(knownShare: 0, liveOutput: speed, today: CostTally(unpricedRequests: 88), todayRequests: 88,
                        unpricedModels: ["gemini-3.1-flash", "gemini-pro-default", "neko-model-7"], state: .nothingPriced)
        case .subscription:
            CostReading(excluded: [.claudeCode], state: .subscriptionOnly)
        case .redline:
            CostReading(microUsdPerSecond: speed * perToken, liveOutput: speed, today: CostTally(costMicroUsd: 61_900_000),
                        todayRequests: 3_100)
        }
        return model.cost.live(reading, agentCount: 3)
    }

    /// The slide from tok/s to money: before, 25 %, 50 %, 75 % and after (DESIGN 9, Transition), at 196 pt.
    static func renderSwitch(model: AppModel, dark: Bool, to url: URL) {
        let redline = model.settings.redline
        let speed = redline * 0.24
        var row: [NSImage] = []
        for progress in [0.0, 0.25, 0.5, 0.75, 1.0] {
            let stage = OrbPreviewStage(model: model, style: .led, diameter: 196, dark: dark)
            stage.reset(value: speed)
            stage.run(for: 2.4, at: speed)
            stage.feed.unit = .cost
            stage.feed.money = money(model: model, state: .cruise, speed: speed)
            stage.run(for: max(1.0 / 60, OrbReadoutBuilder.unitSwitchDuration * progress), at: speed)
            if let image = stage.image() { row.append(SurfacePreviews.pointImage(image)) }
            stage.stop()
        }
        let hovered = OrbPreviewStage(model: model, style: .led, diameter: 196, dark: dark)
        hovered.feed.unit = .cost
        hovered.feed.money = money(model: model, state: .cruise, speed: speed)
        hovered.reset(value: speed)
        hovered.animator.readout.unitHoverAmount = 1
        hovered.run(for: 2.4, at: speed)
        if let image = hovered.image() { row.append(SurfacePreviews.pointImage(image)) }
        hovered.stop()
        SurfacePreviews.writeSheet([row], dark: dark, to: url)
    }

    // MARK: - Menu bar

    /// Rows per locale: priced with the unit, the unit hidden, partly priced, nothing priced.
    static func renderMenuBar(model: AppModel, into directory: URL) {
        let redline = model.settings.redline
        model.settings.liveUnit = .cost
        defer {
            model.settings.liveUnit = .tokens
            model.settings.barShowsUnit = true
        }
        for dark in [false, true] {
            var images: [NSImage] = []
            for locale in locales {
                apply(model, language: locale.language, currency: locale.currency)
                for variant in 0..<4 {
                    var snapshot = PreviewData.snapshot(speed: redline * 0.2, redline: redline)
                    model.settings.barShowsUnit = variant != 1
                    switch variant {
                    case 0, 1:
                        // Every live token priced.
                        snapshot.sources = snapshot.sources.map { source in
                            var source = source
                            source.liveCost = LiveCost(microUsdPerSecond: source.live.output * 21, knownOutput: source.live.output)
                            return source
                        }
                    case 2: snapshot.sources = snapshot.sources.map { var s = $0; s.liveCost.knownOutput *= 0.5; return s }
                    default: snapshot.sources = snapshot.sources.map { var s = $0; s.liveCost = .zero; return s }
                    }
                    model.store.publishLive(snapshot)
                    PreviewRenderer.snapGauge(model, to: redline * 0.2, now: Clock.now - 3)
                    images.append(menuBarImage(model: model, dark: dark))
                }
            }
            SurfacePreviews.writeColumn(images, to: directory.appendingPathComponent("cost-menubar-\(dark ? "dark" : "light").png"))
        }
    }

    /// The money block per locale: tok/s with the live rate and today's total after it (DESIGN 3), then money alone.
    static func renderMenuBarSecondary(model: AppModel, into directory: URL) {
        let redline = model.settings.redline
        model.settings.barSecondary = true
        defer {
            model.settings.barSecondary = false
            model.settings.liveUnit = .tokens
        }
        for dark in [false, true] {
            var images: [NSImage] = []
            for locale in locales {
                apply(model, language: locale.language, currency: locale.currency)
                for unit in [LiveUnit.tokens, .cost] {
                    model.settings.liveUnit = unit
                    var snapshot = PreviewData.snapshot(speed: redline * 0.2, redline: redline)
                    snapshot.sources = snapshot.sources.map { source in
                        var source = source
                        source.liveCost = LiveCost(microUsdPerSecond: source.live.output * 21, knownOutput: source.live.output)
                        return source
                    }
                    model.store.publishLive(snapshot)
                    PreviewRenderer.snapGauge(model, to: redline * 0.2, now: Clock.now - 3)
                    images.append(menuBarImage(model: model, dark: dark, width: 230))
                }
            }
            SurfacePreviews.writeColumn(images, to: directory.appendingPathComponent("cost-menubar-secondary-\(dark ? "dark" : "light").png"))
        }
    }

    private static func menuBarImage(model: AppModel, dark: Bool, width: CGFloat = 150) -> NSImage {
        let state = StatusItemState(model: model, time: Clock.now)
        let size = CGSize(width: width, height: state.layout.height)
        let image = NSImage(size: size, flipped: true) { rect in
            (dark ? NSColor(white: 0.16, alpha: 1) : NSColor(white: 0.93, alpha: 1)).setFill()
            rect.fill()
            NSGraphicsContext.current?.cgContext.translateBy(x: 10, y: 0)
            StatusItemArtist.draw(state, dark: dark,
                                  in: CGRect(origin: .zero, size: CGSize(width: state.layout.length, height: state.layout.height)))
            return true
        }
        let scale: CGFloat = 3
        let big = NSImage(size: CGSize(width: size.width * scale, height: size.height * scale), flipped: false) { rect in
            image.draw(in: rect)
            return true
        }
        return big
    }

    // MARK: - Dashboard

    static func renderDashboards(model: AppModel, into directory: URL) {
        let redline = model.settings.redline
        defer { model.settings.liveUnit = .tokens }
        for locale in locales {
            apply(model, language: locale.language, currency: locale.currency)
            model.settings.liveUnit = .cost
            PreviewRenderer.publish(model, speed: redline * 0.3)
            for dark in [false, true] {
                let scheme = dark ? "dark" : "light"
                PreviewSnapshot.writeHosted(
                    DashboardView(model: model).appEnvironment(model).environment(\.orbStillTime, Clock.now),
                    dark: dark,
                    to: directory.appendingPathComponent("cost-dashboard-\(locale.name)-\(scheme).png")
                )
                PreviewSnapshot.writeHosted(
                    TodayCostPopover(model: model, summary: TodayCostSummary(model: model)).appEnvironment(model),
                    dark: dark,
                    to: directory.appendingPathComponent("cost-today-popover-\(locale.name)-\(scheme).png")
                )
            }
        }
        // A subscription source excluded from the totals, in tokens mode.
        apply(model, language: .zhHant, currency: "TWD")
        model.settings.liveUnit = .tokens
        model.settings.subscriptionSources = [.claudeCode]
        defer { model.settings.subscriptionSources = [] }
        PreviewRenderer.publish(model, speed: redline * 0.3)
        for dark in [false, true] {
            let scheme = dark ? "dark" : "light"
            PreviewSnapshot.writeHosted(
                DashboardView(model: model).appEnvironment(model).environment(\.orbStillTime, Clock.now),
                dark: dark,
                to: directory.appendingPathComponent("cost-dashboard-subscription-\(scheme).png")
            )
            PreviewSnapshot.writeHosted(
                TodayCostPopover(model: model, summary: TodayCostSummary(model: model)).appEnvironment(model),
                dark: dark,
                to: directory.appendingPathComponent("cost-today-popover-subscription-\(scheme).png")
            )
        }
    }

    // MARK: - Settings

    static func renderSettings(model: AppModel, into directory: URL) {
        let redline = model.settings.redline
        PreviewRenderer.publish(model, speed: redline * 0.3)
        model.settings.subscriptionSources = [.claudeDesktop]
        defer { model.settings.subscriptionSources = [] }
        for locale in locales {
            apply(model, language: locale.language, currency: locale.currency)
            for dark in [false, true] {
                let scheme = dark ? "dark" : "light"
                PreviewSnapshot.writeHosted(
                    SettingsPaneView(pane: .cost, model: model).appEnvironment(model),
                    dark: dark,
                    to: directory.appendingPathComponent("cost-settings-\(locale.name)-\(scheme).png")
                )
            }
        }
        apply(model, language: .en, currency: "TWD")
        model.prices.preview(lastSync: Date().addingTimeInterval(-26 * 3600), checkState: .finished(.failed(reason: "HTTP 503")),
                             networkEnabled: false)
        model.rates.preview(rates: nil, currency: "TWD", failure: nil, networkEnabled: false)
        model.settings.networkEnabled = false
        defer {
            model.settings.networkEnabled = true
            model.prices.preview(lastSync: Date().addingTimeInterval(-3 * 3600))
        }
        for dark in [false, true] {
            let scheme = dark ? "dark" : "light"
            PreviewSnapshot.writeHosted(
                SettingsPaneView(pane: .cost, model: model).appEnvironment(model),
                dark: dark,
                to: directory.appendingPathComponent("cost-settings-offline-fallback-\(scheme).png")
            )
            PreviewSnapshot.writeHosted(
                SettingsPaneView(pane: .about, model: model).appEnvironment(model),
                dark: dark,
                to: directory.appendingPathComponent("cost-about-offline-\(scheme).png")
            )
        }
        for (name, language, query) in [("en", LanguagePreference.en, ""), ("zhhant-search", .zhHant, "dol"), ("ja-search", .ja, "円"),
                                         ("none", .en, "zzz")] {
            apply(model, language: language, currency: "TWD")
            for dark in [false, true] {
                PreviewSnapshot.writeHosted(
                    CurrencyList(model: model, query: query, select: { _ in }).appEnvironment(model),
                    dark: dark,
                    to: directory.appendingPathComponent("cost-currency-list-\(name)-\(dark ? "dark" : "light").png")
                )
            }
        }
    }
}
