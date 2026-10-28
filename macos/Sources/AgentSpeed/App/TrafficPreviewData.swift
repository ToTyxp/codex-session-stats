import AgentSpeedCore
import Foundation

/// Deterministic traffic sample data for `--render-previews` and the screenshot harness (METRICS 10, DESIGN 10).
enum TrafficPreviewData {
    /// A busy minute: a tool download through Claude Code, Codex streaming, Claude Desktop quiet.
    static func snapshot(now: Date = Date(), processState: ProcessTrafficState = .running(interval: 1)) -> TrafficSnapshot {
        let second = floor(now.timeIntervalSince1970)
        let history: [TrafficPoint] = (0..<300).map { index in
            let t = Double(index)
            // A tool download: ramps up, holds with some wobble, tails off.
            let burst = 2_400_000 * Easing.smoothstep(246, 254, t) * (1 - Easing.smoothstep(278, 290, t)) * (0.8 + 0.2 * sin(t / 2.1))
            let rx = 180_000 + 120_000 * (0.5 + 0.5 * sin(t / 11)) + 60_000 * max(0, sin(t / 2.3)) + burst
            let tx = 22_000 + 14_000 * (0.5 + 0.5 * sin(t / 7 + 1)) + (index % 17 == 0 ? 260_000 : 0)
            return TrafficPoint(time: Date(timeIntervalSince1970: second - Double(299 - index)), rate: TrafficRate(rx: rx, tx: tx))
        }
        let sources: [SourceKind: SourceTraffic] = [
            .claudeCode: SourceTraffic(
                source: .claudeCode,
                today: TrafficSplit(own: TrafficBytes(rx: 48_200_000, tx: 3_100_000), tools: TrafficBytes(rx: 412_000_000, tx: 6_400_000)),
                unassigned: TrafficBytes(rx: 1_200_000, tx: 90_000),
                liveWithTools: TrafficRate(rx: 1_280_000, tx: 64_000), liveOwn: TrafficRate(rx: 24_000, tx: 1_200)
            ),
            .codex: SourceTraffic(
                source: .codex,
                today: TrafficSplit(own: TrafficBytes(rx: 86_400_000, tx: 12_800_000), tools: TrafficBytes(rx: 9_200_000, tx: 410_000)),
                liveWithTools: TrafficRate(rx: 8_600, tx: 1_100), liveOwn: TrafficRate(rx: 8_600, tx: 1_100)
            ),
            .claudeDesktop: SourceTraffic(
                source: .claudeDesktop,
                today: TrafficSplit(own: TrafficBytes(rx: 6_100_000, tx: 820_000)),
                liveWithTools: TrafficRate(rx: 120, tx: 40), liveOwn: TrafficRate(rx: 120, tx: 40)
            ),
            .antigravity: SourceTraffic(
                source: .antigravity,
                today: TrafficSplit(own: TrafficBytes(rx: 2_400_000, tx: 310_000)),
                liveWithTools: TrafficRate(rx: 3_200, tx: 412_000), liveOwn: TrafficRate(rx: 3_200, tx: 412_000)
            ),
            .gemini: SourceTraffic(source: .gemini, today: TrafficSplit(own: TrafficBytes(rx: 940_000, tx: 120_000)),
                                   liveWithTools: .zero, liveOwn: .zero),
        ]
        let agents: [String: AgentTraffic] = [
            "/preview/nova.jsonl": AgentTraffic(agentId: "/preview/nova.jsonl",
                                                today: TrafficSplit(own: TrafficBytes(rx: 2_100_000, tx: 210_000),
                                                                    tools: TrafficBytes(rx: 410_000_000, tx: 6_100_000))),
            "/preview/rocket.jsonl": AgentTraffic(agentId: "/preview/rocket.jsonl",
                                                  today: TrafficSplit(own: TrafficBytes(rx: 31_000_000, tx: 4_200_000))),
            "/preview/atlas.jsonl": AgentTraffic(agentId: "/preview/atlas.jsonl",
                                                 today: TrafficSplit(own: TrafficBytes(rx: 12_400_000, tx: 1_900_000))),
        ]
        return TrafficSnapshot(
            timestamp: now, enabled: true,
            machineLive: history.last?.rate,
            machineToday: TrafficBytes(rx: 3_820_000_000, tx: 412_000_000), machineSampledMs: 9 * 3_600_000,
            machineHistory: history,
            processState: processState, processSampledMs: 9 * 3_600_000,
            sources: sources, agents: agents,
            dayStart: Calendar.current.startOfDay(for: now)
        )
    }

    /// Stored days: measuring began `recordedDays` days ago (an earlier stretch is not recorded); the first day is
    /// partial, today is in progress.
    static func days(endingAt end: Date = Date(), recordedDays: Int = 40, calendar: Calendar = .current) -> (days: [TrafficDay], since: String) {
        let formatter = DateFormatter()
        formatter.calendar = calendar
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.dateFormat = "yyyy-MM-dd"
        var rows: [TrafficDay] = []
        var since = ""
        for offset in stride(from: recordedDays - 1, through: 0, by: -1) {
            guard let date = calendar.date(byAdding: .day, value: -offset, to: end) else { continue }
            let day = formatter.string(from: date)
            if offset == recordedDays - 1 { since = day }
            let weekday = calendar.component(.weekday, from: date)
            let weekend = weekday == 1 || weekday == 7
            let wave = 0.6 + 0.4 * sin(Double(offset) / 3.1) + (weekend ? -0.35 : 0)
            let scale = max(0.15, wave)
            let sampled: Int64 = offset == recordedDays - 1 ? 5 * 3_600_000 : offset == 0 ? 9 * 3_600_000 : 86_400_000
            func bytes(_ base: Double) -> Int64 { Int64(base * scale) }
            rows.append(TrafficDay(day: day, key: TrafficDay.machineKey, rx: bytes(1_400_000_000), tx: bytes(260_000_000), sampledMs: sampled))
            rows.append(TrafficDay(day: day, key: SourceKind.claudeCode.rawValue, rx: bytes(60_000_000), tx: bytes(4_000_000),
                                   rxTools: bytes(offset % 5 == 0 ? 900_000_000 : 120_000_000), txTools: bytes(8_000_000), sampledMs: sampled))
            rows.append(TrafficDay(day: day, key: SourceKind.codex.rawValue, rx: bytes(110_000_000), tx: bytes(16_000_000),
                                   rxTools: bytes(14_000_000), txTools: bytes(600_000), sampledMs: sampled))
            if offset % 3 != 1 {
                rows.append(TrafficDay(day: day, key: SourceKind.claudeDesktop.rawValue, rx: bytes(9_000_000), tx: bytes(1_100_000),
                                       sampledMs: sampled))
            }
            if offset < 12 {
                rows.append(TrafficDay(day: day, key: SourceKind.antigravity.rawValue, rx: bytes(24_000_000), tx: bytes(3_000_000),
                                       sampledMs: sampled))
            }
        }
        return (rows, since)
    }

    /// Publishes the sample traffic into `store`.
    @MainActor
    static func publish(into store: TrafficStore, now: Date = Date(), processState: ProcessTrafficState = .running(interval: 1)) {
        store.publish(snapshot(now: now, processState: processState))
        let stored = days(endingAt: now)
        store.publish(days: stored.days, since: stored.since)
    }
}
