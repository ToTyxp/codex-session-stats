import AgentSpeedCore
import AppKit
import SwiftUI

@MainActor
enum ExecutionModePreviews {
    static func render(model: AppModel, into directory: URL) {
        let saved = model.settings.language
        defer { model.settings.language = saved; model.l10n.apply(saved); model.report.profile.reset() }
        let today = DayKey(Date(), calendar: HistoryDates.calendar).description
        let selected = ExecutionModeFact(kind: .serviceTier, rawValue: "ultrafast", evidence: .selected,
                                         provider: "openai", scope: .turn)!
        let served = ExecutionModeFact(kind: .serviceTier, rawValue: "fast", evidence: .served,
                                       provider: "anthropic", scope: .response)!
        var snapshot = PreviewData.snapshot(speed: 420, redline: 2000)
        if snapshot.agents.count > 2 {
            snapshot.agents[0].modes = ExecutionModes([selected])
            snapshot.agents[1].modes = ExecutionModes([served])
        }
        PreviewRenderer.publish(model, snapshot: snapshot)
        model.history.publish(state: .ready(updatedAt: Date()), days: [DailyUsage(day: today, source: .claudeCode,
                                        totals: .init(requests: 5, output: 1500), measuredOutput: 1500, measuredMs: 6000,
                                        unfinalized: 0, agents: 1, peak: nil,
                                        models: [.init(model: "claude-opus-5-5", requests: 5, output: 1500, measuredOutput: 1500, measuredMs: 6000)],
                                        modeUsage: [.init(model: "claude-opus-5-5", attribution: .init(kind: .serviceTier, mode: "fast", evidence: "served", provider: "anthropic"),
                                                          requests: 3, output: 900, measuredOutput: 900, measuredMs: 3000)])])
        let history = HistoryViewModel(store: model.history, l10n: model.l10n, accessibility: model.accessibility,
                                       cost: model.cost, traffic: model.traffic)
        for (name, language, _) in ReportPreviews.languages {
            model.settings.language = language; model.l10n.apply(language)
            let checks = ExecutionModeLogicChecks.run(l10n: model.l10n).map { ($0.1 ? "PASS " : "FAIL ") + $0.0 }
            try? (checks.joined(separator: "\n") + "\n").write(to: directory.appendingPathComponent("modes-checks-\(name).txt"), atomically: true, encoding: .utf8)
            for dark in [false, true] {
                let scheme = dark ? "dark" : "light"
                PreviewSnapshot.writeHosted(
                    VStack(spacing: 0) {
                        ForEach(Array(model.snapshot.agents.prefix(3))) { agent in
                            AgentRow(model: model, agent: agent, depth: 0, now: model.snapshot.timestamp)
                        }
                    }.padding(18).frame(width: 780).appEnvironment(model), dark: dark,
                    to: directory.appendingPathComponent("modes-agents-\(name)-\(scheme).png"))
                PreviewSnapshot.writeHosted(
                    ModeHistorySection(model: history, expanded: true).padding(20).frame(width: 720).appEnvironment(model), dark: dark,
                    to: directory.appendingPathComponent("modes-history-\(name)-\(scheme).png"))
                model.report.preview(.reporting, username: "Profile Group")
                var document = model.report.discovery!
                document.extensions = .init(profileAvatar: true, sharedIdentity: true)
                model.report.serviceState = .ready(document)
                model.report.profile.snapshot = ReportProfileSnapshot.parse(Data(#"{"sharedUserId":"11111111-1111-4111-8111-111111111111","displayName":"Profile Group","avatar":null,"devices":[{"deviceId":"22222222-2222-4222-8222-222222222222","platform":"macOS","isCurrent":true,"online":true},{"deviceId":"44444444-4444-4444-8444-444444444444","platform":"Windows","isCurrent":false,"online":true}]}"#.utf8))
                PreviewSnapshot.writeHosted(
                    Form { ReportProfileSection(model: model, loadsProfileOnAppearance: false) }.settingsForm().frame(width: 680, height: 400).appEnvironment(model),
                    dark: dark, to: directory.appendingPathComponent("profile-\(name)-\(scheme).png"))
                model.report.profile.pairingIsInvitation = true
                let expires = ReportInstant.format(unixMs: Int64(Date().addingTimeInterval(300).timeIntervalSince1970 * 1000))
                model.report.profile.pairing = ReportPairingSnapshot.parse(Data("""
                    {"id":"33333333-3333-4333-8333-333333333333","state":"pending","expiresAt":"\(expires)",
                     "claimant":{"deviceId":"44444444-4444-4444-8444-444444444444","platform":"Windows"}}
                    """.utf8))
                PreviewSnapshot.writeHosted(ReportPairingSheet(model: model).appEnvironment(model), dark: dark,
                                            to: directory.appendingPathComponent("pairing-\(name)-\(scheme).png"))
            }
        }
    }
}
