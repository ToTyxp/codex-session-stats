import AgentSpeedCore
import SwiftUI

/// Settings → 網路流量 (DESIGN 10, Settings → Network Traffic): measuring, tools, the speed unit, the menu bar block and
/// the per-agent measurement status. Everything applies live; turning measuring off stops the sampler at once.
struct TrafficPane: View {
    let model: AppModel

    var body: some View {
        @Bindable var settings = model.settings
        let l10n = model.l10n
        let enabled = settings.trafficEnabled
        Form {
            Section {
                SettingToggle(title: l10n("settings.traffic.enabled"), subtitle: l10n("settings.traffic.enabled.hint"),
                              isOn: $settings.trafficEnabled)
                if model.report.reportsTraffic {
                    Notice(text: l10n("report.traffic.note", model.report.destinationHost ?? ""),
                           symbol: "dot.radiowaves.left.and.right")
                }
            }

            Section {
                SettingToggle(title: l10n("settings.traffic.includeTools"), subtitle: l10n("settings.traffic.includeTools.hint"),
                              isOn: $settings.trafficIncludeTools)
                SegmentedRow(title: l10n("settings.traffic.unit"), selection: $settings.trafficUnit, segmentWidth: 116, count: 2) {
                    Text(l10n("settings.traffic.unit.bytes")).tag(TrafficUnit.bytes)
                    Text(l10n("settings.traffic.unit.bits")).tag(TrafficUnit.bits)
                }
                BarTrafficPicker(model: model)
            }
            .disabled(!enabled)

            if enabled {
                Section {
                    TrafficStatusRow(model: model)
                } header: {
                    Text(l10n("traffic.status"))
                }
                .transition(.opacity)
            }
        }
        .settingsForm()
        .animation(.easeOut(duration: 0.2), value: enabled)
    }
}

/// `settings.bar.traffic` (DESIGN 10): the menu bar's traffic block; the same control in 選單列 and 網路流量.
struct BarTrafficPicker: View {
    let model: AppModel

    var body: some View {
        @Bindable var settings = model.settings
        let l10n = model.l10n
        Picker(selection: $settings.barTraffic) {
            ForEach(BarTraffic.allCases) { option in
                Text(l10n(option.titleKey)).tag(option)
            }
        } label: {
            RowTitle(l10n("settings.bar.traffic"))
        }
        .pickerStyle(.menu)
    }
}

/// The per-agent measurement status (DESIGN 10, `traffic.status`): working, with how it is read, or the reason it is
/// not and a retry button. The dot always comes with its text.
private struct TrafficStatusRow: View {
    let model: AppModel

    var body: some View {
        let l10n = model.l10n
        let state = model.traffic.snapshot.processState
        let failed = state.unavailableReason != nil
        HStack(alignment: .firstTextBaseline, spacing: Spacing.s) {
            Circle()
                .fill(failed ? Color.orange : Color.green)
                .frame(width: 8, height: 8)
                .alignmentGuide(.firstTextBaseline) { $0[VerticalAlignment.center] + 4 }
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 3) {
                RowTitle(failed ? l10n("traffic.unavailable.sampler") : l10n("traffic.status.ok"))
                if !failed {
                    Footnote(l10n("traffic.status.macos"))
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            if failed {
                Button(l10n("traffic.retry")) { model.retryTraffic() }
                    .buttonStyle(.borderless)
                    .font(.system(size: 12))
            }
        }
        .accessibilityElement(children: .combine)
        .animation(Motion.fade, value: failed)
    }
}
