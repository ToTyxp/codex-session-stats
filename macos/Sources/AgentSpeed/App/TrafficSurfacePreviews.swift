import AgentSpeedCore
import AppKit
import SwiftUI

/// `--render-previews` group `traffic` (surfaces): the dashboard's Network section (busy, unavailable, tools excluded),
/// Settings → 網路流量 (working and failed), and the menu bar with the traffic block (DESIGN 10).
@MainActor
enum TrafficSurfacePreviews {
    static func render(model: AppModel, into directory: URL) {
        let redline = model.settings.redline
        model.settings.trafficEnabled = true
        PreviewRenderer.publish(model, speed: redline * 0.3)
        TrafficPreviewData.publish(into: model.traffic)
        for dark in [false, true] {
            let scheme = dark ? "dark" : "light"
            dashboard(model, dark: dark, to: directory.appendingPathComponent("dashboard-traffic-\(scheme).png"))
            settingsPane(model, dark: dark, to: directory.appendingPathComponent("settings-traffic-\(scheme).png"))
        }
        model.settings.trafficIncludeTools = false
        model.settings.trafficUnit = .bits
        dashboard(model, dark: true, to: directory.appendingPathComponent("dashboard-traffic-no-tools-bits-dark.png"))
        model.settings.trafficIncludeTools = true
        model.settings.trafficUnit = .bytes
        // A disabled source shows no traffic (DESIGN 10); this computer's values stay.
        let enabled = model.settings.enabledSources
        model.settings.enabledSources = enabled.subtracting([.claudeCode])
        dashboard(model, dark: false, to: directory.appendingPathComponent("dashboard-traffic-disabled-light.png"))
        model.settings.enabledSources = enabled
        let language = model.settings.language
        model.settings.language = .en
        model.l10n.apply(.en)
        dashboard(model, dark: false, to: directory.appendingPathComponent("dashboard-traffic-en-light.png"))
        model.traffic.publish(TrafficPreviewData.snapshot(processState: .unavailable(.sampler)))
        dashboard(model, dark: false, to: directory.appendingPathComponent("dashboard-traffic-unavailable-en-light.png"))
        settingsPane(model, dark: false, to: directory.appendingPathComponent("settings-traffic-failed-en-light.png"))
        model.settings.language = language
        model.l10n.apply(language)
        TrafficPreviewData.publish(into: model.traffic)
        menuBar(model, into: directory)
    }

    private static func dashboard(_ model: AppModel, dark: Bool, to url: URL) {
        PreviewSnapshot.writeHosted(
            DashboardView(model: model).appEnvironment(model).environment(\.orbStillTime, Clock.now),
            dark: dark, to: url
        )
    }

    private static func settingsPane(_ model: AppModel, dark: Bool, to url: URL) {
        PreviewSnapshot.writeHosted(
            SettingsPaneView(pane: .traffic, model: model).appEnvironment(model).environment(\.orbStillTime, Clock.now),
            dark: dark, to: url
        )
    }

    /// The item at three values with the block off, for this computer and for the agents (bytes and bits).
    private static func menuBar(_ model: AppModel, into directory: URL) {
        let redline = model.settings.redline
        let modes: [(BarTraffic, TrafficUnit, BarDisplayMode)] = [
            (.off, .bytes, .glyphValue), (.machine, .bytes, .glyphValue), (.agents, .bytes, .glyphValue),
            (.machine, .bits, .value), (.machine, .bytes, .glyph),
        ]
        for dark in [false, true] {
            var images: [NSImage] = []
            for (traffic, unit, mode) in modes {
                model.settings.barTraffic = traffic
                model.settings.trafficUnit = unit
                model.settings.barMode = mode
                for value in [0.0, 45, 1280] {
                    model.store.publishLive(PreviewData.snapshot(speed: value, redline: redline))
                    PreviewRenderer.snapGauge(model, to: value, now: Clock.now - 3)
                    let state = StatusItemState(model: model, time: Clock.now)
                    let size = CGSize(width: 190, height: state.layout.height)
                    let image = NSImage(size: size, flipped: true) { rect in
                        (dark ? NSColor(white: 0.16, alpha: 1) : NSColor(white: 0.93, alpha: 1)).setFill()
                        rect.fill()
                        NSColor.systemRed.withAlphaComponent(0.35).setFill()
                        CGRect(x: 10 + state.layout.length, y: 0, width: 1, height: rect.height).fill()
                        NSGraphicsContext.current?.cgContext.translateBy(x: 10, y: 0)
                        StatusItemArtist.draw(state, dark: dark, in: CGRect(origin: .zero, size: CGSize(width: state.layout.length,
                                                                                                      height: state.layout.height)))
                        return true
                    }
                    images.append(image)
                }
            }
            SurfacePreviews.writeColumn(images.map { scaled($0, by: 3) }, to: directory.appendingPathComponent("menubar-traffic-\(dark ? "dark" : "light").png"))
        }
        model.settings.barTraffic = .off
        model.settings.trafficUnit = .bytes
        model.settings.barMode = .glyphValue
    }

    private static func scaled(_ image: NSImage, by scale: CGFloat) -> NSImage {
        let size = CGSize(width: image.size.width * scale, height: image.size.height * scale)
        return NSImage(size: size, flipped: false) { rect in
            image.draw(in: rect)
            return true
        }
    }
}
