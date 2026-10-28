import AgentSpeedCore
import Foundation

/// Deterministic sample data for `--render-previews`.
enum PreviewData {
    enum AgentSet {
        case standard
        /// Nested groups, a sub-agent whose parent is not listed, and a group cut by the row limit.
        case grouped
        /// The first `count` (up to 27) agents of a busy machine, for the expandable list.
        case crowd(Int)
    }

    static func snapshot(speed: Double, redline: Double, agents set: AgentSet = .standard, now: Date = Date()) -> SpeedSnapshot {
        let second = floor(now.timeIntervalSince1970)
        let history: [HistoryPoint] = (0..<300).map { index in
            let t = Double(index)
            let wave = 0.55 + 0.35 * sin(t / 23) + 0.18 * sin(t / 7.3) + 0.08 * sin(t * 1.7)
            let ramp = Easing.smoothstep(0, 80, t)
            let ending = index > 288 ? speed / max(redline, 1) : wave * ramp
            let base = max(0, ending) * redline * 0.9
            let isTail = index >= 297
            return HistoryPoint(
                time: Date(timeIntervalSince1970: second - Double(299 - index)),
                measured: Rate(output: isTail ? base * 0.4 : base, total: base * 26),
                estimated: Rate(output: isTail ? base * 0.6 : 0, total: isTail ? base * 15 : 0)
            )
        }
        let agents: [AgentStatus]
        switch set {
        case .standard: agents = standardAgents(speed: speed, now: now)
        case .grouped: agents = groupedAgents(speed: speed, now: now)
        case .crowd(let count): agents = Array(crowdAgents(speed: speed, now: now).prefix(count))
        }
        let today = TokenTotals(requests: 1_284, input: 48_200_000, cachedInput: 41_000_000, cacheWriteInput: 2_100_000, output: 1_240_000, reasoning: 380_000)
        let sources: [SourceStatus] = [
            source(.codex, speed * 0.34, TokenTotals(requests: 520, input: 19_000_000, output: 520_000), 1),
            source(.claudeCode, speed * 0.42, TokenTotals(requests: 480, input: 19_400_000, output: 440_000), 2),
            source(.claudeDesktop, speed * 0.10, TokenTotals(requests: 120, input: 4_600_000, output: 120_000), 1),
            source(.deepseek, speed * 0.14, TokenTotals(requests: 164, input: 5_200_000, output: 160_000), 1),
            source(.opencode, 0, TokenTotals(requests: 36, input: 1_100_000, output: 38_000), 0),
            source(.gemini, speed * 0.04, TokenTotals(requests: 22, input: 700_000, output: 24_000), 1),
            source(.omp, 0, TokenTotals(requests: 64, input: 2_400_000, output: 58_000), 0),
            source(.antigravity, speed * 0.05, TokenTotals(requests: 41, input: 1_600_000, output: 46_000), 1),
            source(.workbuddy, speed * 0.05, TokenTotals(requests: 58, input: 2_000_000, output: 52_000), 1),
            source(.workbuddyAi, 0, TokenTotals(requests: 19, input: 640_000, output: 21_000), 0),
            source(.zcode, speed * 0.06, TokenTotals(requests: 34, input: 900_000, output: 42_000), 1),
            source(.kimi, speed * 0.03, TokenTotals(requests: 28, input: 780_000, output: 32_000), 1),
            source(.grokBuild, speed * 0.02, TokenTotals(requests: 18, input: 530_000, output: 23_000), 1),
        ]
        var liveCost = LiveCost.zero
        var todayCost = CostTally()
        for source in sources {
            liveCost.add(source.liveCost)
            todayCost.add(source.todayCost)
        }
        return SpeedSnapshot(
            timestamp: now,
            state: .running,
            live: Rate(output: speed, total: speed * 26),
            liveEstimatedShare: 0.32,
            peakToday: Rate(output: redline * 1.08, total: redline * 26),
            peakTodayAt: now.addingTimeInterval(-3_600 * 2.4),
            averageToday: redline * 0.31,
            today: today,
            sources: sources,
            agents: agents,
            generatingCount: agents.filter { $0.phase == .generating }.count,
            toolsCount: agents.filter { $0.phase == .tools }.count,
            history: history,
            lastResponse: nil,
            warnings: [],
            liveCost: liveCost,
            todayCost: todayCost
        )
    }

    private static func standardAgents(speed: Double, now: Date) -> [AgentStatus] {
        [
            agent("rocket", .codex, "rocket", "gpt-6.1-sol", .generating, false, speed * 0.34, now),
            agent("nova", .claudeCode, "nova", "claude-opus-5-5", .generating, false, speed * 0.27, now),
            agent("nova-sub", .claudeCode, "nova", "claude-sonnet-5-5", .generating, true, speed * 0.15, now, parent: "nova"),
            agent("quasar", .deepseek, "quasar", "deepseek-v4-pro", .generating, false, speed * 0.14, now),
            agent("orbit", .claudeDesktop, "orbit", "claude-fable-5-1", .generating, false, speed * 0.10, now),
            agent("lumen", .omp, "lumen", "claude-opus-5-5", .idle, false, 0, now.addingTimeInterval(-220)),
        ]
    }

    /// Twenty-seven agents in engine order: groups of every source, then tools and idle stragglers.
    private static func crowdAgents(speed: Double, now: Date) -> [AgentStatus] {
        [
            agent("orbit", .claudeDesktop, "orbit", "claude-opus-5-5", .generating, false, speed * 0.16, now),
            agent("orbit-explore", .claudeDesktop, "orbit", "claude-sonnet-5-5", .generating, true, speed * 0.1, now, parent: "orbit"),
            agent("orbit-tests", .claudeDesktop, "orbit", "claude-haiku-5", .tools, true, 0, now.addingTimeInterval(-4), parent: "orbit"),
            agent("quasar", .deepseek, "quasar", "deepseek-v4-pro", .generating, false, speed * 0.14, now),
            agent("quasar-scout", .deepseek, "quasar", "deepseek-v4-flash", .generating, true, speed * 0.08, now, parent: "quasar"),
            agent("rocket", .codex, "rocket", "gpt-6.1-sol", .generating, false, speed * 0.12, now),
            agent("rocket-worker", .codex, "rocket", "gpt-6.1-sol-mini", .generating, true, speed * 0.06, now, parent: "rocket"),
            agent("rocket-review", .codex, "rocket", "gpt-6.1-sol-mini", .generating, true, speed * 0.04, now, parent: "rocket"),
            agent("halo", .antigravity, "halo", "gemini-3.1-pro", .generating, false, speed * 0.12, now),
            agent("halo-research", .antigravity, "halo", "gemini-3.1-flash", .generating, true, speed * 0.06, now, parent: "halo"),
            agent("nova", .claudeCode, "nova", "claude-opus-5-5", .generating, false, speed * 0.1, now),
            agent("nova-sub", .claudeCode, "nova", "claude-sonnet-5-5", .generating, true, speed * 0.05, now, parent: "nova"),
            agent("meridian", .omp, "meridian", "claude-opus-5-5", .generating, false, speed * 0.08, now),
            agent("atlas", .codex, "atlas-web", "gpt-6.1-sol", .generating, false, speed * 0.07, now),
            agent("vega", .workbuddy, "vega", "glm-5.3", .generating, false, speed * 0.05, now),
            agent("lumen", .claudeCode, "lumen", "claude-haiku-5", .tools, false, 0, now.addingTimeInterval(-9)),
            agent("pulse", .claudeDesktop, "pulse", "claude-sonnet-5-5", .idle, false, 0, now.addingTimeInterval(-95)),
            agent("ember", .codex, "ember", "gpt-6.1-sol-mini", .idle, false, 0, now.addingTimeInterval(-240)),
            agent("cinder", .claudeCode, "cinder", "claude-opus-5-5", .idle, false, 0, now.addingTimeInterval(-420)),
            agent("delta", .deepseek, "delta", "deepseek-v4-pro", .idle, false, 0, now.addingTimeInterval(-480)),
            agent("helix", .codex, "helix", "gpt-6.1-sol", .idle, false, 0, now.addingTimeInterval(-540)),
            agent("cairn", .omp, "cairn", "gpt-5.5", .idle, false, 0, now.addingTimeInterval(-570)),
            agent("prism", .claudeDesktop, "prism", "claude-opus-5-5", .idle, false, 0, now.addingTimeInterval(-600)),
            agent("zenith", .claudeCode, "zenith", "claude-sonnet-5-5", .idle, false, 0, now.addingTimeInterval(-660)),
            agent("cobalt", .codex, "cobalt", "gpt-6.1-sol-mini", .idle, false, 0, now.addingTimeInterval(-720)),
            agent("aurora", .workbuddyAi, "aurora", "kimi-k3", .idle, false, 0, now.addingTimeInterval(-780)),
            agent("ion", .claudeCode, "ion", "claude-haiku-5", .idle, false, 0, now.addingTimeInterval(-840)),
            agent("tide", .claudeDesktop, "tide", "claude-sonnet-5-5", .idle, false, 0, now.addingTimeInterval(-900)),
        ]
    }

    /// Nested groups, a sub-agent whose parent is not listed, and a group cut by the row limit.
    private static func groupedAgents(speed: Double, now: Date) -> [AgentStatus] {
        [
            agent("orbit", .claudeDesktop, "orbit", "claude-opus-5-5", .generating, false, speed * 0.22, now),
            agent("orbit-explore", .claudeDesktop, "orbit", "claude-sonnet-5-5", .generating, true, speed * 0.16, now, parent: "orbit"),
            agent("orbit-review", .claudeDesktop, "orbit", "claude-haiku-5", .generating, true, speed * 0.06, now, parent: "orbit-explore"),
            agent("orbit-tests", .claudeDesktop, "orbit", "claude-sonnet-5-5", .tools, true, 0, now.addingTimeInterval(-6), parent: "orbit"),
            agent("quasar-worker", .deepseek, "quasar", "deepseek-v4-flash", .generating, true, speed * 0.2, now),
            agent("rocket", .codex, "rocket", "gpt-6.1-sol", .generating, false, speed * 0.12, now),
            agent("rocket-worker", .codex, "rocket", "gpt-6.1-sol-mini", .generating, true, speed * 0.06, now, parent: "rocket"),
            agent("lumen", .claudeCode, "lumen", "claude-haiku-5", .idle, false, 0, now.addingTimeInterval(-220)),
        ]
    }

    private static func agent(
        _ id: String, _ source: SourceKind, _ project: String, _ model: String,
        _ phase: AgentPhase, _ isSubagent: Bool, _ live: Double, _ activity: Date, parent: String? = nil
    ) -> AgentStatus {
        AgentStatus(
            id: "/preview/\(id).jsonl",
            source: source,
            project: project,
            model: model,
            phase: phase,
            isSubagent: isSubagent,
            live: Rate(output: live, total: live * 26),
            recentSpeed: live > 0 ? live : nil,
            lastActivity: activity,
            parentId: parent.map { "/preview/\($0).jsonl" }
        )
    }

    /// DeepSeek not installed and Claude Desktop turned off: the dashboard hides the first and dims the second.
    static func withPartialSources(_ snapshot: SpeedSnapshot) -> SpeedSnapshot {
        var snapshot = snapshot
        snapshot.sources = snapshot.sources.map { source in
            var source = source
            switch source.kind {
            case .deepseek: source.available = false
            case .claudeDesktop: source.enabled = false
            case .codex, .claudeCode, .opencode, .gemini, .omp, .antigravity, .workbuddy, .workbuddyAi, .zcode, .kimi, .grokBuild: return source
            }
            source.sessionCount = 0
            source.live = .zero
            source.today = TokenTotals()
            source.activeAgents = 0
            return source
        }
        snapshot.agents = snapshot.agents.filter { $0.source != .deepseek && $0.source != .claudeDesktop }
        snapshot.generatingCount = snapshot.agents.filter { $0.phase == .generating }.count
        snapshot.toolsCount = snapshot.agents.filter { $0.phase == .tools }.count
        return snapshot
    }

    private static func source(_ kind: SourceKind, _ live: Double, _ today: TokenTotals, _ active: Int) -> SourceStatus {
        // API-equivalent costs (METRICS 9): a blended price per output token, DeepSeek partly and Antigravity not priced.
        let microUsdPerToken: Double = kind == .deepseek ? 2.4 : kind == .gemini ? 9 : 21
        let knownShare: Double = kind == .antigravity ? 0 : kind == .deepseek ? 0.7 : 1
        let unpriced = kind == .antigravity ? today.requests : kind == .deepseek ? today.requests / 3 : 0
        return SourceStatus(
            kind: kind,
            enabled: true,
            rootPath: kind.transcriptFolder.defaultURL.path,
            available: true,
            sessionCount: sessionCount(kind.transcriptFolder),
            live: Rate(output: live, total: live * 26),
            today: today,
            activeAgents: active,
            liveCost: LiveCost(microUsdPerSecond: live * knownShare * microUsdPerToken, knownOutput: live * knownShare),
            todayCost: CostTally(costMicroUsd: Int64(Double(today.output) * knownShare * microUsdPerToken * 1.6),
                                 unpricedRequests: unpriced),
            unpricedModels: kind == .antigravity ? ["gemini-3.1-flash"] : kind == .deepseek ? ["deepseek-pro"] : []
        )
    }

    private static func sessionCount(_ folder: TranscriptFolder) -> Int {
        switch folder {
        case .codex: 128
        case .claude: 342
        case .dsh: 46
        case .opencode: 1
        case .gemini: 23
        case .omp: 17
        case .antigravity: 9
        case .workbuddy: 12
        case .workbuddyAi: 5
        case .zcode: 3
        case .kimi: 2
        case .grokBuild: 1
        }
    }
}
