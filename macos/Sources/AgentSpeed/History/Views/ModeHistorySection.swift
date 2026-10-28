import AgentSpeedCore
import AppKit
import SwiftUI
import UniformTypeIdentifiers

struct ModeHistorySection: View {
    let model: HistoryViewModel
    @State private var expanded = false
    @State private var kind = ExecutionModeKind.serviceTier
    @State private var exportFailed = false

    init(model: HistoryViewModel, expanded: Bool = false) {
        self.model = model
        _expanded = State(initialValue: expanded)
    }

    private var days: [DailyUsage] {
        model.store.days.filter { day in
            (model.source == nil || day.source == model.source)
                && DayKey(day.day).map(model.content.range.contains) == true
        }
    }

    private var rows: [Row] {
        var groups: [String: Row] = [:]
        for day in days {
            for usage in day.modeBreakdown where usage.attribution.kind == kind {
                let key = day.source.rawValue + "/" + usage.id
                if var row = groups[key] {
                    row.requests += usage.requests
                    row.output += usage.output
                    row.measuredOutput += usage.measuredOutput
                    row.measuredMs += usage.measuredMs
                    groups[key] = row
                } else { groups[key] = Row(id: key, source: day.source, usage: usage) }
            }
        }
        return groups.values.sorted { $0.requests == $1.requests ? $0.id < $1.id : $0.requests > $1.requests }
    }

    var body: some View {
        let l10n = model.l10n
        let breakdown = rows
        DisclosureGroup(isExpanded: $expanded) {
            VStack(alignment: .leading, spacing: 12) {
                HStack {
                    Picker(l10n("mode.label"), selection: $kind) {
                        ForEach(ExecutionModeKind.allCases, id: \.self) { value in
                            Text(l10n("mode.kind." + value.rawValue)).tag(value)
                        }
                    }
                    .labelsHidden()
                    .frame(maxWidth: 180)
                    Spacer()
                    Button { export() } label: {
                        Label(l10n("mode.exportCsv"), systemImage: "square.and.arrow.up")
                    }
                    .buttonStyle(.borderless)
                    .help(l10n("mode.exportCsv"))
                }
                ForEach(breakdown) { row in
                    HStack(alignment: .firstTextBaseline, spacing: 12) {
                        VStack(alignment: .leading, spacing: 3) {
                            Text(ExecutionModeText.value(row.attribution.mode, l10n))
                                .font(.system(.body, design: .rounded).weight(.medium))
                            Text([l10n(row.source.titleKey), row.model,
                                  row.attribution.evidence == "unknown" ? l10n("mode.unknown") : l10n("mode.evidence." + row.attribution.evidence)]
                                .filter { !$0.isEmpty }.joined(separator: " · "))
                                .font(.caption)
                                .foregroundStyle(.secondary)
                                .lineLimit(1)
                                .truncationMode(.middle)
                        }
                        Spacer(minLength: 10)
                        VStack(alignment: .trailing, spacing: 3) {
                            Text(row.requests.formatted())
                                .monospacedDigit()
                            Text(l10n("history.table.requests"))
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }
                        .frame(width: 86, alignment: .trailing)
                        VStack(alignment: .trailing, spacing: 3) {
                            Text(row.output.formatted()).monospacedDigit()
                            Text(l10n("history.table.output")).font(.caption).foregroundStyle(.secondary)
                        }
                        .frame(width: 96, alignment: .trailing)
                        VStack(alignment: .trailing, spacing: 3) {
                            Text(row.speed.map { String(format: "%.1f", locale: l10n.locale, $0) + " tok/s" } ?? "—")
                                .monospacedDigit()
                            Text(l10n("history.table.speed"))
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }
                        .frame(width: 112, alignment: .trailing)
                    }
                    .accessibilityElement(children: .combine)
                    if row.id != breakdown.last?.id { Divider().opacity(0.5) }
                }
            }
            .padding(.top, 12)
        } label: {
            Text(l10n("mode.history"))
                .font(HistoryFont.sectionTitle)
        }
        .padding(16)
        .background(.primary.opacity(0.035), in: RoundedRectangle(cornerRadius: 12))
        .alert(l10n("mac.history.export.failed"), isPresented: $exportFailed) {
            Button(l10n("mac.update.ok"), role: .cancel) {}
        }
    }

    private func export() {
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.commaSeparatedText]
        panel.canCreateDirectories = true
        panel.nameFieldStringValue = "agent-speed-modes-\(model.content.range.start)-\(model.content.range.end).csv"
        let data = ExecutionModeCSV.build(days, kind: kind)
        panel.begin { response in
            MainActor.assumeIsolated {
                guard response == .OK, let url = panel.url else { return }
                do { try data.write(to: url, options: .atomic) } catch { exportFailed = true }
            }
        }
    }

    private struct Row: Identifiable {
        let id: String
        let source: SourceKind
        let model: String
        let attribution: ExecutionModeAttribution
        var requests: Int
        var output: Int
        var measuredOutput: Int
        var measuredMs: Int64
        var speed: Double? { measuredMs > 0 ? Double(measuredOutput) / (Double(measuredMs) / 1000) : nil }

        init(id: String, source: SourceKind, usage: ExecutionModeUsage) {
            self.id = id; self.source = source; model = usage.model; attribution = usage.attribution
            requests = usage.requests; output = usage.output; measuredOutput = usage.measuredOutput; measuredMs = usage.measuredMs
        }
    }
}
