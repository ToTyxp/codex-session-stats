import AgentSpeedCore
import Foundation

/// The live preview (DESIGN 11, Consent sheet and Preview window): the real body of the next report — the open
/// window of the run, or, before a run exists, the window since the preview was opened.
extension ReportController {
    struct Preview: Equatable {
        var body: ReportJSON
        var requestLine: String
        var headers: [(name: String, value: String)]
        /// Seconds until the next report is due; nil while quiet, paused or off.
        var nextIn: Int?
        /// Built before the service's document is known (DESIGN 11, Preview window): the body only is meaningful.
        var assumed = false

        static func == (lhs: Preview, rhs: Preview) -> Bool {
            lhs.body == rhs.body && lhs.requestLine == rhs.requestLine && lhs.nextIn == rhs.nextIn
                && lhs.assumed == rhs.assumed
                && lhs.headers.map(\.name) == rhs.headers.map(\.name) && lhs.headers.map(\.value) == rhs.headers.map(\.value)
        }

        /// What `report.preview.copy` copies: the pretty-printed body.
        var copyText: String { body.prettyText }
    }

    /// Starts the preview's own window when no run is open.
    func openPreview() {
        previewOpenings += 1
        if previewWindow == nil {
            previewWindow = ReportWindow(lastSample)
            previewStartedAt = ReportClock.monotonicMs
        }
    }

    func closePreview() {
        previewOpenings = max(0, previewOpenings - 1)
        if previewOpenings == 0 { previewWindow = nil }
    }

    /// The next report for the consent sheet's `draft`, or for the selected service: its identity and document, or,
    /// before the document is known (the dashboard invitation), the protocol's defaults.
    func preview(draft: ConsentDraft? = nil) -> Preview? {
        let now = ReportClock.monotonicMs
        let sample = lastSample
        let document: ReportDiscovery
        let deviceId: String
        let username: String
        var sections = preferences.sections
        var assumed = false
        if let draft {
            document = draft.discovery
            deviceId = draft.deviceId
            username = ReportUsername.normalized(draft.username)
            sections = draft.sections
        } else if let stored = discovery {
            document = stored
            deviceId = identity?.deviceId ?? previewDeviceId
            username = fieldUsername
        } else if let service = selectedService,
                  let guess = ReportDiscovery.assumed(for: service, name: serviceName ?? service.host) {
            document = guess
            deviceId = previewDeviceId
            username = fieldUsername
            assumed = true
        } else {
            return nil
        }
        let runOpen = draft == nil && run != nil
        let window = runOpen ? run!.window : (previewWindow ?? ReportWindow(sample))
        let start = runOpen ? (run!.scheduler.windowStart ?? now) : (previewStartedAt ?? now)
        let seq = runOpen ? run!.scheduler.seq + 1 : 1
        let working = sample.facts.agents.values.contains { $0.generating + $0.tools > 0 } || sample.live.output > 0
        let activity: ReportScheduler.Activity = !working && mode == .idle ? .idle : .active
        let envelope = ReportEnvelope(
            client: client, deviceId: deviceId, username: username, runId: run?.id ?? previewRunId, seq: seq,
            sentAtMs: ReportClock.unixMs + clockCorrection * 1000, windowMs: max(0, now - start), activity: activity,
            titleLevel: run?.title.level ?? 0)
        var options = options(for: document)
        options.sections = sections.intersection(document.sections)
        let (body, data) = ReportBody.fitted(envelope, window.content(sample), sample, options)
        let url = document.api.appending(endpoint: ReportEndpoint.reports)
        var headers: [(name: String, value: String)] = [
            ("Host", ReportURLs.authority(of: url)),
            ("User-Agent", userAgent),
            ("Accept", "application/json"),
            ("Content-Type", "application/json"),
            ("Content-Length", String(data.count)),
            (ReportCanonical.deviceHeader, deviceId),
            (ReportCanonical.timestampHeader, String(ReportClock.unixMs / 1000 + clockCorrection)),
            (ReportCanonical.nonceHeader, "…"),
            (ReportCanonical.signatureHeader, "…"),
        ]
        if draft.map({ !$0.apiKey.isEmpty }) ?? (apiKey != nil) { headers.append(("Authorization", "Bearer ••••")) }
        var nextIn: Int?
        if draft == nil, runOpen, !preferences.isPaused, let due = run?.scheduler.nextDueAt {
            nextIn = Int(max(0, (due - now + 999) / 1000))
        }
        return Preview(body: body, requestLine: "POST \(ReportURLs.requestPath(of: url)) HTTP/1.1", headers: headers,
                       nextIn: nextIn, assumed: assumed)
    }
}
