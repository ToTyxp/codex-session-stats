import AgentSpeedCore
import AppKit
import SwiftUI

/// `report.preview` (DESIGN 11, Preview window): a 560 × 640 panel with the request line and headers, the body of
/// the next report, the countdown to it and Copy JSON. The signature is shown as `…`, an API key as `Bearer ••••`.
@MainActor
enum ReportPreviewWindow {
    private static var panel: NSPanel?
    private static var closeObserver: NSObjectProtocol?

    static func show(model: AppModel) {
        NSApp.activate()
        if let panel {
            panel.makeKeyAndOrderFront(nil)
            return
        }
        let host = NSHostingController(rootView: ReportPreviewPanel(model: model).appEnvironment(model))
        host.sizingOptions = []
        let panel = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 560, height: 640),
                            styleMask: [.titled, .closable, .resizable, .utilityWindow], backing: .buffered, defer: true)
        panel.contentViewController = host
        panel.setContentSize(NSSize(width: 560, height: 640))
        panel.contentMinSize = NSSize(width: 460, height: 420)
        panel.title = model.l10n("report.preview.title")
        panel.isReleasedWhenClosed = false
        panel.hidesOnDeactivate = false
        panel.center()
        model.report.openPreview()
        closeObserver = NotificationCenter.default.addObserver(
            forName: NSWindow.willCloseNotification, object: panel, queue: .main
        ) { _ in
            MainActor.assumeIsolated {
                model.report.closePreview()
                if let observer = closeObserver { NotificationCenter.default.removeObserver(observer) }
                closeObserver = nil
                Self.panel = nil
            }
        }
        self.panel = panel
        panel.makeKeyAndOrderFront(nil)
    }
}

private struct ReportPreviewPanel: View {
    let model: AppModel

    var body: some View {
        let l10n = model.l10n
        TimelineView(.periodic(from: .now, by: 1)) { _ in
            let preview = model.report.preview()
            VStack(alignment: .leading, spacing: Spacing.s) {
                HStack(alignment: .firstTextBaseline) {
                    Text(l10n("report.preview.title"))
                        .font(.system(size: 13, weight: .semibold))
                    Spacer()
                    if let next = preview?.nextIn {
                        Text(l10n("report.preview.next", next))
                            .font(.system(size: 11).monospacedDigit())
                            .foregroundStyle(.secondary)
                            .contentTransition(.numericText(countsDown: true))
                    }
                }
                ReportPreviewBox(model: model, preview: preview, showsRequest: true, maxHeight: .infinity)
            }
            .padding(Spacing.m)
        }
        .frame(minWidth: 460, minHeight: 420)
    }
}
