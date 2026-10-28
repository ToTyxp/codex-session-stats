#if DEBUG
import AgentSpeedCore
import AppKit
import SwiftUI

@MainActor
enum ProfileCardPreviews {
    static func render(model: AppModel, into directory: URL) {
        let saved = model.settings.language
        defer {
            model.settings.language = saved; model.l10n.apply(saved)
            model.report.profile.reset(); model.report.preview(.off, username: "")
        }
        for (name, language, _) in ReportPreviews.languages {
            model.settings.language = language; model.l10n.apply(language)
            let checks = checkRouting(model: model)
            try? (checks.map { ($0.1 ? "PASS " : "FAIL ") + $0.0 }.joined(separator: "\n") + "\n")
                .write(to: directory.appendingPathComponent("profile-card-checks-\(name).txt"), atomically: true, encoding: .utf8)
            for dark in [false, true] {
                let appearance = dark ? "dark" : "light"
                for width in [520.0, 680.0] {
                    prepare(model, bound: true)
                    PreviewSnapshot.writeHosted(
                        Form { ReportProfileSection(model: model, loadsProfileOnAppearance: false) }
                            .settingsForm().frame(width: width, height: 350).appEnvironment(model),
                        dark: dark, to: directory.appendingPathComponent("profile-card-\(name)-\(appearance)-\(Int(width)).png"))
                }
                prepare(model, bound: false)
                PreviewSnapshot.writeHosted(
                    Form { ReportProfileSection(model: model, loadsProfileOnAppearance: false) }
                        .settingsForm().frame(width: 520, height: 350).appEnvironment(model),
                    dark: dark, to: directory.appendingPathComponent("profile-card-device-\(name)-\(appearance).png"))
                prepare(model, bound: true)
                model.report.profile.preview = try? sampleAvatar(pending: true)
                model.report.profile.failureKey = "profile.failed"
                let previewHash = model.report.profile.preview?.sha256
                PreviewSnapshot.writeHosted(
                    Form { ReportProfileSection(model: model, loadsProfileOnAppearance: false) }
                        .settingsForm().frame(width: 520, height: 430).appEnvironment(model),
                    dark: dark, to: directory.appendingPathComponent("profile-card-pending-\(name)-\(appearance).png"))
                if model.report.profile.preview?.sha256 != previewHash {
                    try? "FAIL preview lost while rendering\n".write(to: directory.appendingPathComponent("profile-card-preview-failed.txt"), atomically: true, encoding: .utf8)
                }
                for registered in [false, true] {
                    model.report.profile.reset()
                    model.report.preview(registered ? .reporting : .off, username: "Zoe")
                    PreviewSnapshot.writeHosted(
                        Form { ReportProfileSection(model: model, loadsProfileOnAppearance: false) }
                            .settingsForm().frame(width: 520, height: 310).appEnvironment(model),
                        dark: dark, to: directory.appendingPathComponent("profile-card-\(registered ? "legacy" : "setup")-\(name)-\(appearance).png"))
                }
                prepare(model, bound: true)
                PreviewSnapshot.writeHosted(ReportDeviceManagementSheet(model: model).appEnvironment(model),
                                            dark: dark, to: directory.appendingPathComponent("profile-devices-\(name)-\(appearance).png"))
            }
        }
    }

    private static func prepare(_ model: AppModel, bound: Bool) {
        model.report.profile.reset()
        model.report.preview(.reporting, username: "Zoe")
        var document = model.report.discovery!
        document.extensions = .init(profileAvatar: true, sharedIdentity: true)
        model.report.serviceState = .ready(document)
        let shared = bound ? "\"11111111-1111-4111-8111-111111111111\"" : "null"
        let name = bound ? "Zoe Studio" : "Zoe"
        let avatar = try? sampleAvatar()
        model.report.profile.avatar = avatar
        model.report.profile.snapshot = ReportProfileSnapshot.parse(Data("""
            {"sharedUserId":\(shared),"displayName":"\(name)","avatar":{
              "url":"https://example.com/avatar.png","sha256":"\(avatar?.sha256 ?? String(repeating: "0", count: 64))",
              "contentType":"image/png","width":256,"height":256},"devices":[
              {"deviceId":"22222222-2222-4222-8222-222222222222","platform":"macOS","isCurrent":true,"online":true},
              {"deviceId":"44444444-4444-4444-8444-444444444444","platform":"Windows","isCurrent":false,"online":false}]}
            """.utf8))
    }

    private static func checkRouting(model: AppModel) -> [(String, Bool)] {
        model.report.profile.reset()
        model.report.preview(.off, username: "Zoe")
        var results = [("registration remains editable without profile capability", ReportProfileNameTarget.resolve(model.report) == .registration)]
        model.report.preview(.reporting, username: "Zoe")
        results.append(("legacy registered service keeps original device name", ReportProfileNameTarget.resolve(model.report) == .device))
        prepare(model, bound: false)
        results.append(("unbound profile uses device rename", ReportProfileNameTarget.resolve(model.report) == .device))
        model.report.profile.snapshot = nil
        results.append(("unknown shared identity waits for profile", ReportProfileNameTarget.resolve(model.report) == .loading))
        prepare(model, bound: true)
        results.append(("bound profile uses shared display name", ReportProfileNameTarget.resolve(model.report) == .shared))
        return results
    }

    private static func sampleAvatar(pending: Bool = false) throws -> ReportAvatarImage {
        let image = NSImage(size: NSSize(width: 256, height: 256), flipped: false) { rect in
            NSColor(calibratedRed: pending ? 0.68 : 0.2, green: pending ? 0.32 : 0.4, blue: pending ? 0.16 : 0.66, alpha: 1).setFill()
            NSBezierPath(rect: rect).fill()
            NSColor.white.withAlphaComponent(0.88).setFill()
            NSBezierPath(ovalIn: NSRect(x: 86, y: 128, width: 84, height: 84)).fill()
            NSBezierPath(ovalIn: NSRect(x: 50, y: 20, width: 156, height: 94)).fill()
            return true
        }
        guard let data = image.tiffRepresentation, let bitmap = NSBitmapImageRep(data: data),
              let png = bitmap.representation(using: .png, properties: [:]) else { throw ReportAvatarImage.Failure.invalid }
        return try ReportAvatarImage.prepare(data: png)
    }
}
#endif
