import AgentSpeedCore
import AppKit

/// Runs automatic checks on the UPDATES section 3 schedule while `autoCheckUpdates` is on: 30 s after launch, then
/// every 6 h, waiting out the rest of 6 h after the last successful check of an earlier run.
@MainActor
final class UpdateScheduler {
    private let controller: UpdateController
    private let settings: AppSettings
    private let launchedAt = Date()
    private var lastAttempt: Date?
    private var timer: Task<Void, Never>?
    private var settingLoop: ObservationLoop?
    private var wakeObserver: NSObjectProtocol?
    private var isEnabled = false

    init(controller: UpdateController, settings: AppSettings) {
        self.controller = controller
        self.settings = settings
    }

    func start() {
        guard controller.checksAutomatically else {
            UpdateLog.logger.notice("Automatic update checks are off for development version \(self.controller.currentVersion, privacy: .public)")
            return
        }
        // Only the setting is observed here, so a check that updates `lastChecked` never restarts its own timer.
        settingLoop = ObservationLoop { [weak self] in
            guard let self else { return }
            // With the network master switch off, automatic checks have no effect (NETWORK 2).
            let enabled = self.settings.autoCheckUpdates && self.settings.networkEnabled
            guard enabled != self.isEnabled else { return }
            self.isEnabled = enabled
            Task { @MainActor [weak self] in self?.reschedule() }
        }
        wakeObserver = NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didWakeNotification,
            object: nil,
            queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.reschedule() }
        }
    }

    func stop() {
        settingLoop?.cancel()
        settingLoop = nil
        timer?.cancel()
        timer = nil
        if let wakeObserver { NSWorkspace.shared.notificationCenter.removeObserver(wakeObserver) }
        wakeObserver = nil
    }

    /// Re-arms the timer from wall-clock dates, so sleep and clock changes are accounted for.
    private func reschedule() {
        timer?.cancel()
        timer = nil
        guard isEnabled else { return }
        let now = Date()
        let due = UpdateSchedule.nextCheck(
            launchedAt: launchedAt,
            lastSuccess: controller.lastChecked,
            lastAttempt: lastAttempt,
            now: now
        )
        let delay = UpdateSchedule.delay(until: due, now: now)
        timer = Task { [weak self] in
            try? await Task.sleep(for: .seconds(delay))
            guard !Task.isCancelled else { return }
            self?.fire()
        }
    }

    private func fire() {
        lastAttempt = Date()
        Task { [weak self] in
            guard let self else { return }
            await self.controller.check(manual: false)
            self.reschedule()
        }
    }
}
