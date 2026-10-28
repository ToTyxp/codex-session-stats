import AgentSpeedCore
import SwiftUI

/// `report.sections` (DESIGN 11, What's sent): one switch per optional section. A section the service does not list is
/// off and disabled; the traffic switch waits for traffic measuring.
struct ReportSectionsSection: View {
    let model: AppModel

    var body: some View {
        let l10n = model.l10n
        let report = model.report
        Section {
            ReportSectionToggles(model: model, selection: Binding(
                get: { report.preferences.sections },
                set: { new in
                    for section in ReportSection.allCases where new.contains(section) != report.preferences.sections.contains(section) {
                        report.setSection(section, on: new.contains(section))
                    }
                }
            ), accepted: report.discovery?.sections)
            HStack {
                Spacer()
                Button(l10n("report.preview")) { ReportPreviewWindow.show(model: model) }
                    .disabled(report.selectedService == nil)
            }
        } header: {
            Text(l10n("report.sections"))
        } footer: {
            SectionFootnote(l10n("report.sections.hint"))
        }
    }
}

/// The four section switches, shared by the pane and the consent sheet.
struct ReportSectionToggles: View {
    let model: AppModel
    @Binding var selection: Set<ReportSection>
    /// The sections the service lists; nil while its document is unknown (every switch stays usable).
    let accepted: Set<ReportSection>?

    var body: some View {
        let l10n = model.l10n
        let trafficOn = model.settings.trafficEnabled
        ForEach(ReportSection.allCases, id: \.self) { section in
            let listed = accepted?.contains(section) ?? true
            let isTraffic = section == .traffic
            Toggle(isOn: Binding(
                get: { listed && selection.contains(section) },
                set: { on in
                    if on { selection.insert(section) } else { selection.remove(section) }
                }
            )) {
                VStack(alignment: .leading, spacing: 2) {
                    RowTitle(l10n(section.titleKey))
                    if !listed {
                        Footnote(l10n("report.section.notAccepted"))
                    } else if isTraffic, !trafficOn {
                        Footnote(l10n("report.section.trafficOff"))
                    }
                }
            }
            .disabled(!listed || (isTraffic && !trafficOn))
        }
    }
}

extension ReportSection {
    var titleKey: String { "report.section.\(rawValue)" }
}
