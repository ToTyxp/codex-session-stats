import AgentSpeedCore
import Foundation

@MainActor
enum ExecutionModeLogicChecks {
    static func run(l10n: Localizer) -> [(String, Bool)] {
        let selected = ExecutionModeFact(kind: .serviceTier, rawValue: "priority", evidence: .selected,
                                         provider: "openai", scope: .turn)!
        let served = ExecutionModeFact(kind: .serviceTier, rawValue: "default", evidence: .served,
                                       provider: "openai", scope: .response)!
        let configured = ExecutionModeFact(kind: .serviceTier, rawValue: "priority", evidence: .configured,
                                           provider: "openai", scope: .configuration)!
        let modes = ExecutionModes([selected, served])
        let details = ExecutionModeText.detail(modes, l10n)
        return [
            ("modes: missing remains unknown", ExecutionModeText.summary(.empty, l10n) == l10n("mode.unknown")),
            ("modes: configured does not become a task mode", ExecutionModeText.summary(ExecutionModes([configured]), l10n) == l10n("mode.unknown")),
            ("modes: served wins and evidence is shown", ExecutionModeText.summary(modes, l10n) == l10n("mode.evidence.served") + " · " + l10n("mode.value.standard")),
            ("modes: detail keeps requested and served separately", details.contains(l10n("mode.evidence.selected")) && details.contains(l10n("mode.evidence.served"))),
            ("modes: unknown future names stay distinct", ExecutionModeText.value("warp-2", l10n) == "warp-2"),
        ]
    }
}
