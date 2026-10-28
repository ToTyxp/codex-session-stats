import AgentSpeedCore
import AppKit
import Observation
import SwiftUI

/// The filter and derived content of the Usage History window. The content is rebuilt once per change of the stored
/// days, the filter, the language or the date, never per frame.
@MainActor
@Observable
final class HistoryViewModel {
    /// The window's own `history.includeSubscriptions` choice (DESIGN 9); absent until the user changes it.
    static let includeSubscriptionsKey = "historyIncludeSubscriptions"

    let store: HistoryStore
    let l10n: Localizer
    let accessibility: AccessibilityState
    /// Currency, rates and subscription marks (DESIGN 9).
    let cost: CostDisplay
    /// Stored traffic days (METRICS 10.9).
    let traffic: TrafficStore
    /// Pointer, keyboard and selection state, kept apart so hovering never rebuilds the content.
    let interaction = HistoryInteraction()

    private(set) var source: SourceKind?
    private(set) var rangeKind: HistoryRangeKind = .days30
    private(set) var customRange: DayRange?
    private(set) var sort = HistorySort()
    private(set) var chartMetric: HistoryChartMetric = .output
    /// Whether subscription sources count at their API-equivalent cost in this window.
    private(set) var includeSubscriptions: Bool
    private(set) var content: HistoryContent

    @ObservationIgnored private var dataLoop: ObservationLoop?
    @ObservationIgnored private var observers: [NSObjectProtocol] = []

    init(store: HistoryStore, l10n: Localizer, accessibility: AccessibilityState, cost: CostDisplay, traffic: TrafficStore) {
        self.store = store
        self.l10n = l10n
        self.accessibility = accessibility
        self.cost = cost
        self.traffic = traffic
        let stored = cost.settings.storage.object(forKey: Self.includeSubscriptionsKey) as? Bool
        let include = stored ?? cost.settings.includeSubscriptions
        includeSubscriptions = include
        let today = DayKey(Date(), calendar: HistoryDates.calendar)
        content = HistoryContent.build(
            usage: store.days, source: nil, rangeKind: .days30, customRange: nil, sort: HistorySort(), today: today, l10n: l10n,
            money: HistoryMoney(cost), costOptions: HistoryCostOptions(marked: cost.settings.subscriptionSources,
                                                                        includeSubscriptions: include),
            traffic: Self.historyTraffic(traffic, settings: cost.settings)
        )
        dataLoop = ObservationLoop { [weak self] in
            guard let self else { return }
            _ = self.store.days
            _ = self.l10n.language
            _ = HistoryMoney(self.cost)
            _ = self.cost.settings.subscriptionSources
            _ = (self.traffic.days, self.traffic.since, self.cost.settings.trafficIncludeTools, self.cost.settings.trafficUnit)
            _ = self.cost.settings.enabledSources
            self.rebuild()
        }
        let center = NotificationCenter.default
        for name in [Notification.Name.NSCalendarDayChanged, .NSSystemTimeZoneDidChange, .NSSystemClockDidChange] {
            observers.append(center.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated { self?.rebuild() }
            })
        }
    }

    var motion: Animation? { accessibility.reduceMotion ? nil : HistoryMotion.chart }

    /// Also when the source is already selected: opening the window applies the traffic rule below again.
    func select(source: SourceKind?) {
        if source != self.source {
            self.source = source
            interaction.reset()
            rebuild()
        }
        followTraffic()
    }

    func select(range kind: HistoryRangeKind) {
        guard kind != .custom, kind != rangeKind else { return }
        rangeKind = kind
        interaction.reset()
        rebuild()
        followTraffic()
        // Today is still being written: bring in what the logs hold now, the view follows as the store publishes.
        if kind == .today { refreshToday() }
    }

    func applyCustom(from: DayKey, to: DayKey) {
        customRange = HistoryRanges.clamp(from: from, to: to, within: content.selectableBounds)
        rangeKind = .custom
        interaction.reset()
        rebuild()
        followTraffic()
    }

    /// DESIGN 10, Opening on traffic: a range with agent traffic but no token usage would chart nothing as output or
    /// cost, so the chart switches to traffic; it never switches back by itself.
    private func followTraffic() {
        guard chartMetric != .traffic, offersTraffic, content.showsOnlyTraffic else { return }
        select(metric: .traffic)
    }

    /// The chart's (and a single day's breakdown's) metric: output or cost.
    func select(metric: HistoryChartMetric) {
        guard metric != chartMetric else { return }
        chartMetric = metric
        interaction.hover(nil, from: .chart)
        rebuild()
    }

    /// The toolbar checkbox; the window remembers its own choice.
    func setIncludeSubscriptions(_ include: Bool) {
        guard include != includeSubscriptions else { return }
        includeSubscriptions = include
        cost.settings.storage.set(include, forKey: Self.includeSubscriptionsKey)
        rebuild()
    }

    /// `以 2026/10/22 的匯率換算` for a non-USD currency (DESIGN 9 rate note).
    var rateNote: String? { cost.historyNote }

    func toggleSort(_ column: HistoryColumn) {
        sort = content.sort.toggled(column)
        content = content.sorted(by: sort)
    }

    func refresh() {
        store.refresh()
    }

    /// The range is the single day that is still being written (DESIGN 4b: today's figures update live).
    var showsLiveDay: Bool {
        content.range.isSingleDay && content.range.start == content.today
    }

    /// Re-reads today's logs without the analysing progress, so the live day updates in place.
    func refreshToday() {
        store.refresh(showsProgress: false)
    }

    private func rebuild() {
        let today = DayKey(Date(), calendar: HistoryDates.calendar)
        let options = HistoryCostOptions(
            marked: cost.settings.subscriptionSources, includeSubscriptions: includeSubscriptions, metric: chartMetric
        )
        let next = HistoryContent.build(
            usage: store.days, source: source, rangeKind: rangeKind, customRange: customRange, sort: sort,
            today: today, l10n: l10n, money: HistoryMoney(cost), costOptions: options,
            traffic: Self.historyTraffic(traffic, settings: cost.settings)
        )
        if next != content { content = next }
    }

    /// Disabled sources' rows are left out (METRICS 10.5, DESIGN 10); this computer's rows stay.
    private static func historyTraffic(_ store: TrafficStore, settings: AppSettings) -> HistoryTraffic {
        HistoryTraffic(days: TrafficVisibility.days(store.days, enabled: SourceSet(settings.enabledSources)), since: store.since,
                       includeTools: settings.trafficIncludeTools, bits: settings.trafficUnit == .bits)
    }

    /// The metric control offers traffic while measuring is on or traffic history exists (DESIGN 10).
    var offersTraffic: Bool { cost.settings.trafficEnabled || !traffic.days.isEmpty }
}

/// Hover, keyboard focus day, row selection and the reveal flash.
@MainActor
@Observable
final class HistoryInteraction {
    enum Origin: Equatable { case chart, table }

    /// The day under the pointer, and where the pointer is.
    private(set) var hovered: (day: DayKey, origin: Origin)?
    /// The day the chart's keyboard cursor sits on while the chart has focus.
    var keyboardDay: DayKey?
    var selection: Set<DayKey> = []
    /// Anchor of shift-extended selections.
    var anchor: DayKey?
    /// A request to scroll the table to a day and flash it; the id makes every request unique.
    private(set) var reveal: (day: DayKey, id: Int)?
    private(set) var flash: (day: DayKey, id: Int)?
    @ObservationIgnored private var revealCount = 0

    var hoveredDay: DayKey? { hovered?.day }

    func hover(_ day: DayKey?, from origin: Origin) {
        if let day {
            if hovered?.day != day || hovered?.origin != origin { hovered = (day, origin) }
        } else if hovered?.origin == origin {
            hovered = nil
        }
    }

    /// The pointer left `day`'s row; a row entered in the meantime keeps its hover.
    func unhover(_ day: DayKey, from origin: Origin) {
        if hovered?.day == day, hovered?.origin == origin { hovered = nil }
    }

    func revealInTable(_ day: DayKey) {
        revealCount += 1
        reveal = (day, revealCount)
        let id = revealCount
        // The flash starts once the scroll has brought the row on screen.
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.32) { [weak self] in
            guard let self, self.reveal?.id == id else { return }
            self.flash = (day, id)
        }
    }

    func reset() {
        hovered = nil
        keyboardDay = nil
        selection = []
        anchor = nil
    }
}

enum HistoryMotion {
    /// Bar heights between ranges and sources (DESIGN 4b: spring, at most 0.45 s).
    static let chart = Animation.spring(response: 0.42, dampingFraction: 0.86)
    static let fade = Animation.easeOut(duration: 0.2)
    static let scroll = Animation.easeInOut(duration: 0.35)
}
