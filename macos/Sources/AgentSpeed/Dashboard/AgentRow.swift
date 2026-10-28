import AgentSpeedCore
import SwiftUI

/// One agent: phase badge, project, source and model, live speed. The phase in words is in the tooltip and for VoiceOver.
struct AgentRow: View {
    let model: AppModel
    let agent: AgentStatus
    /// Visual nesting level in the parent tree; roots are 0.
    let depth: Int
    /// Snapshot time, the reference for "x minutes ago".
    let now: Date

    @Environment(\.colorScheme) private var colorScheme

    var body: some View {
        let l10n = model.l10n
        let sourceName = l10n(agent.source.titleKey)
        let project = agent.project.isEmpty ? sourceName : agent.project
        // Model first: when space runs out the source name truncates, and the badge color still carries it.
        let detail = [agent.model, sourceName].compactMap { $0?.isEmpty == false ? $0 : nil }.joined(separator: " · ")
        // Attributed traffic today (DESIGN 10, Agents): the line ends with `· ↓ 2.10 MB`, kept whole while the model and
        // source names give way; both directions in the tooltip.
        let traffic = agentTraffic
        let trafficSuffix = traffic.flatMap { $0.rx > 0 ? " · ↓ " + TrafficFormat.total($0.rx) : nil }
        let speed = model.value(of: agent.live)
        let status = status(l10n)

        HStack(spacing: Spacing.s) {
            PhaseBadge(
                phase: agent.phase,
                tint: agent.source.tint(dark: colorScheme == .dark),
                animated: !model.accessibility.reduceMotion
            )
            .anchorPreference(key: BadgeAnchors.self, value: .bounds) { [agent.id: $0] }

            HStack(alignment: .firstTextBaseline, spacing: 6) {
                Text(project)
                    .font(DashboardFont.rowTitle)
                    .lineLimit(1)
                    .layoutPriority(1)
                    .accessibilityLabel(agent.isSubagent ? "\(project), \(l10n("agents.subagent"))" : project)
                HStack(spacing: 0) {
                    Text(detail)
                        .lineLimit(1)
                        .truncationMode(.tail)
                    if let trafficSuffix {
                        Text(trafficSuffix)
                            .lineLimit(1)
                            .fixedSize()
                    }
                }
                .font(DashboardFont.detail)
                .foregroundStyle(.dashboardSecondary)
            }

            Text(ExecutionModeText.summary(agent.modes, l10n))
                .font(.caption2.weight(.medium))
                .foregroundStyle(.secondary)
                .lineLimit(1)
                .padding(.horizontal, 6)
                .padding(.vertical, 3)
                .background(.primary.opacity(0.055), in: Capsule())
                .help(ExecutionModeText.detail(agent.modes, l10n))
                .accessibilityLabel(l10n("mode.label") + ": " + ExecutionModeText.summary(agent.modes, l10n))

            Spacer(minLength: Spacing.xs)

            LiveSpeedValue(
                speed: speed,
                phase: agent.phase,
                l10n: l10n,
                reduceMotion: model.accessibility.reduceMotion
            )
        }
        .frame(height: DashboardLayout.rowHeight)
        .padding(.leading, CGFloat(depth) * DashboardLayout.subagentIndent)
        .contentShape(Rectangle())
        .help((traffic.map { "\(project) · \(status)\n" + l10n("traffic.agent.tooltip", TrafficText(model: model).pair($0)) }
            ?? "\(project) · \(status)") + "\n" + ExecutionModeText.detail(agent.modes, l10n))
        .accessibilityElement(children: .combine)
        .accessibilityValue(status)
    }

    /// Today's traffic attributed to this agent's tree (roots only; sub-agents run inside their parent's process).
    private var agentTraffic: TrafficBytes? {
        guard model.settings.trafficEnabled, agent.parentId == nil,
              let traffic = model.traffic.snapshot.agents[agent.id] else { return nil }
        let bytes = traffic.today.total(includeTools: model.settings.trafficIncludeTools)
        return bytes.isZero ? nil : bytes
    }

    private func status(_ l10n: Localizer) -> String {
        switch agent.phase {
        case .generating: return l10n("phase.generating")
        case .tools: return l10n("phase.tools")
        case .idle:
            let seconds = Int(now.timeIntervalSince(agent.lastActivity).rounded(.down))
            let ago: String
            if seconds < 10 {
                ago = l10n("time.justNow")
            } else if seconds < 60 {
                ago = l10n("time.secondsAgo", seconds)
            } else {
                ago = l10n("time.minutesAgo", seconds / 60)
            }
            return "\(l10n("phase.idle")) · \(ago)"
        }
    }
}
