import AgentSpeedCore
import Foundation

@MainActor
enum ExecutionModeText {
    static func value(_ value: String, _ l10n: Localizer) -> String {
        let known = ["standard", "fast", "ultrafast", "highspeed", "balanced", "deep", "planning", "auto", "flex", "scale", "unknown"]
        return known.contains(value) ? l10n("mode.value." + value) : value
    }

    static func summary(_ modes: ExecutionModes, _ l10n: Localizer) -> String {
        let parts = modes.attributed.map { attribution in
            l10n("mode.evidence." + attribution.evidence) + " · " + value(attribution.mode, l10n)
        }
        return parts.isEmpty ? l10n("mode.unknown") : parts.joined(separator: " / ")
    }

    static func detail(_ modes: ExecutionModes, _ l10n: Localizer) -> String {
        guard !modes.facts.isEmpty else { return l10n("mode.label") + ": " + l10n("mode.unknown") }
        return modes.facts.map { fact in
            let native = fact.rawValue.lowercased() == fact.value ? "" : " [" + fact.rawValue + "]"
            return l10n("mode.kind." + fact.kind.rawValue) + " · " + l10n("mode.evidence." + fact.evidence.rawValue)
                + ": " + value(fact.value, l10n) + native
        }.joined(separator: "\n")
    }

    static func orb(agents: [AgentStatus], _ l10n: Localizer) -> (summary: String, detail: String) {
        let active = agents.filter { $0.phase != .idle }
        let rows = active.isEmpty ? agents : active
        let summaries = Set(rows.map { summary($0.modes, l10n) })
        let title = summaries.count > 1 ? l10n("mode.mixed") : summaries.first ?? l10n("mode.unknown")
        let details = rows.prefix(20).map { agent in
            (agent.project.isEmpty ? l10n(agent.source.titleKey) : agent.project) + "\n" + detail(agent.modes, l10n)
        }.joined(separator: "\n\n")
        return (l10n("mode.label") + ": " + title, details.isEmpty ? l10n("mode.unknown") : details)
    }
}
