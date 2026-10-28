import AgentSpeedCore
import AppKit
import SwiftUI

/// Speed-title renders for `--render-previews`: every title on the floating orb (smallest and default size) and the
/// dashboard hero at the longest titles, in each language, light and dark.
@MainActor
enum TitlePreviews {
    static let orbSizes: [CGFloat] = [96, 148]
    static let heroTitles: [SpeedTitle] = [.idle, .walk, .ebike, .sportsCar, .bulletTrain, .maglev, .fighterJet, .hypersonic, .rocket]

    static func render(model: AppModel, into directory: URL) {
        let original = model.settings.language
        defer { setLanguage(original, model: model) }
        for language in [LanguagePreference.zhHant, .zhHans, .en, .ja] {
            setLanguage(language, model: model)
            for dark in [false, true] {
                let scheme = dark ? "dark" : "light"
                for size in orbSizes {
                    let images = SpeedTitle.allCases.compactMap { title -> NSImage? in
                        let speed = speed(for: title, redline: model.settings.redline)
                        let tier = LiveGauge.tier(for: speed, redline: model.settings.redline)
                        guard let image = SurfacePreviews.orbImage(model: model, size: size, dark: dark, speed: speed, tier: tier) else {
                            return nil
                        }
                        return SurfacePreviews.pointImage(image)
                    }
                    let rows = stride(from: 0, to: images.count, by: 7).map { Array(images[$0..<min($0 + 7, images.count)]) }
                    SurfacePreviews.writeSheet(rows, dark: dark, to: directory.appendingPathComponent(
                        "titles-orb-\(language.rawValue)-\(Int(size))-\(scheme).png"
                    ))
                }
                let heroes = heroTitles.compactMap { title -> NSImage? in
                    let speed = speed(for: title, redline: model.settings.redline)
                    PreviewRenderer.publish(model, speed: speed)
                    PreviewRenderer.snapGauge(model, to: speed, now: Clock.now - 3)
                    return heroImage(model: model, dark: dark)
                }
                SurfacePreviews.writeColumn(heroes, to: directory.appendingPathComponent("titles-hero-\(language.rawValue)-\(scheme).png"))
                renderLadder(model: model, dark: dark, to: directory.appendingPathComponent("titles-ladder-\(language.rawValue)-\(scheme).png"))
            }
        }
    }

    /// Settings → General → Redline at the default redline, the minimum and the maximum, for each metric, with one
    /// title highlighted as if hovered. The line count must match across the three redlines of a metric.
    private static func renderLadder(model: AppModel, dark: Bool, to url: URL) {
        let settings = model.settings
        let originalMetric = settings.metric
        let originalOutput = settings.redlineOutput
        let originalTotal = settings.redlineTotal
        defer {
            settings.metric = originalMetric
            settings.redlineOutput = originalOutput
            settings.redlineTotal = originalTotal
        }
        var images: [NSImage] = []
        for metric in SpeedMetric.allCases {
            settings.metric = metric
            let range = SettingsLimits.redlineRange(for: metric)
            for redline in [SettingsLimits.defaultRedline(for: metric), range.lowerBound, range.upperBound] {
                settings.redline = redline
                let highlighted: SpeedTitle? = redline == range.upperBound ? .maglev : nil
                let view = Form {
                    Section {
                        RedlineControl(settings: settings, l10n: model.l10n, highlightedTitle: highlighted)
                    } footer: {
                        RedlineFooter(settings: settings, l10n: model.l10n, highlightedTitle: .constant(highlighted))
                    }
                }
                .settingsForm()
                .frame(width: SettingsPaneView.width)
                .fixedSize(horizontal: false, vertical: true)
                .appEnvironment(model)
                if let image = hostedImage(view, dark: dark) { images.append(image) }
            }
        }
        SurfacePreviews.writeColumn(images, to: url)
    }

    /// A speed inside the title's band at the current redline.
    static func speed(for title: SpeedTitle, redline: Double) -> Double {
        switch title {
        case .idle: 0
        case .walk: 2
        default: title.enterSpeed(redline: redline) * 1.08
        }
    }

    private static func setLanguage(_ language: LanguagePreference, model: AppModel) {
        model.settings.language = language
        model.l10n.apply(language)
    }

    private static func heroImage(model: AppModel, dark: Bool) -> NSImage? {
        let view = HeroSection(model: model)
            .padding(.horizontal, DashboardLayout.padding)
            .padding(.vertical, Spacing.s)
            .frame(width: DashboardController.width, alignment: .leading)
            .appEnvironment(model)
            .environment(\.orbStillTime, Clock.now)
        return hostedImage(view, dark: dark)
    }

    private static func hostedImage<V: View>(_ content: V, dark: Bool) -> NSImage? {
        let view = content
            .environment(\.colorScheme, dark ? .dark : .light)
            .background(Color(nsColor: .windowBackgroundColor))
        let appearance = NSAppearance(named: dark ? .darkAqua : .aqua)
        let host = NSHostingView(rootView: view)
        host.appearance = appearance
        host.frame = CGRect(origin: .zero, size: host.fittingSize)
        let window = NSWindow(contentRect: host.frame, styleMask: [.borderless], backing: .buffered, defer: false)
        window.appearance = appearance
        window.contentView = host
        host.layoutSubtreeIfNeeded()
        host.displayIfNeeded()
        RunLoop.main.run(until: Date().addingTimeInterval(0.1))
        guard let rep = host.bitmapImageRepForCachingDisplay(in: host.bounds) else { return nil }
        host.cacheDisplay(in: host.bounds, to: rep)
        let image = NSImage(size: CGSize(width: rep.pixelsWide, height: rep.pixelsHigh))
        image.addRepresentation(rep)
        return image
    }
}
