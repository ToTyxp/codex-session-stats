import AgentSpeedCore
import Foundation
import UserNotifications

/// One system notification per new version (UPDATES section 4). Permission is requested the first time an update
/// is found; when it is denied the in-app indicators are enough. Clicking the notification opens the update window.
@MainActor
final class UpdateNotifier: NSObject, UNUserNotificationCenterDelegate {
    nonisolated private static let identifierPrefix = "update."

    var onOpen: (@MainActor () -> Void)?

    /// Notifications need a real app bundle; `swift run` builds have none.
    private var center: UNUserNotificationCenter? {
        guard Bundle.main.bundleURL.pathExtension == "app", Bundle.main.bundleIdentifier != nil else { return nil }
        return UNUserNotificationCenter.current()
    }

    func install() {
        center?.delegate = self
    }

    func announce(_ release: Release, title: String, body: String) {
        guard let center else { return }
        Task {
            guard await Self.isAllowed(center) else {
                UpdateLog.logger.notice("Notifications are not allowed; relying on in-app indicators")
                return
            }
            let content = UNMutableNotificationContent()
            content.title = title
            content.body = body
            let request = UNNotificationRequest(identifier: Self.identifierPrefix + release.tag, content: content, trigger: nil)
            do {
                try await center.add(request)
            } catch {
                UpdateLog.logger.error("Posting the update notification failed")
            }
        }
    }

    private static func isAllowed(_ center: UNUserNotificationCenter) async -> Bool {
        switch await center.notificationSettings().authorizationStatus {
        case .authorized, .provisional, .ephemeral:
            return true
        case .notDetermined:
            return (try? await center.requestAuthorization(options: [.alert, .sound])) ?? false
        case .denied:
            return false
        @unknown default:
            return false
        }
    }

    nonisolated func userNotificationCenter(
        _ center: UNUserNotificationCenter,
        didReceive response: UNNotificationResponse
    ) async {
        let isUpdate = response.notification.request.identifier.hasPrefix(Self.identifierPrefix)
        guard isUpdate, response.actionIdentifier == UNNotificationDefaultActionIdentifier else { return }
        await MainActor.run { [weak self] in self?.onOpen?() }
    }

    nonisolated func userNotificationCenter(
        _ center: UNUserNotificationCenter,
        willPresent notification: UNNotification
    ) async -> UNNotificationPresentationOptions {
        [.banner, .list]
    }
}
