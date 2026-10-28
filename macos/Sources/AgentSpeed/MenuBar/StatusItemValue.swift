import AgentSpeedCore
import Foundation

/// The text of the menu bar value (DESIGN 3, 9): tok/s, or with `settings.liveUnit` set to money the billable live
/// cost rate (`0.0042` · `US$/s`, or `$0.0042` with the unit hidden; `≥` when partly priced, `–` when nothing is).
@MainActor
enum StatusItemValue {
    static func current(_ model: AppModel) -> (text: String, unit: String) {
        guard let money = money(model) else {
            return (SpeedFormat.speed(model.gauge.readoutValue), model.l10n("unit.tokS"))
        }
        if money.unknown { return ("–", money.unit) }
        let number = model.settings.barShowsUnit ? money.number : money.inline
        return ((money.atLeast ? "≥" : "") + number, money.unit)
    }

    static func money(_ model: AppModel) -> LiveMoney? {
        guard model.settings.liveUnit == .cost else { return nil }
        return liveMoney(model)
    }

    /// The billable live cost rate, whatever the live unit (the money block uses it next to tok/s).
    static func liveMoney(_ model: AppModel) -> LiveMoney {
        let snapshot = model.snapshot
        return model.cost.live(OrbSubset.cost(snapshot, sources: nil, billable: model.cost.billable),
                               agentCount: snapshot.agents.count)
    }
}
