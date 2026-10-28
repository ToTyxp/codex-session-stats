import AgentSpeedCore
import SwiftUI

/// The pane's status line (DESIGN 11, Status): always a localized string of the table, with a dot whose color never
/// carries meaning alone, and never a status code or error class.
enum ReportStatus: Equatable {
    case off
    case networkOff
    case never
    case reporting(last: Date)
    case idle
    case quiet
    case paused(until: Date)
    case pausedIndefinitely
    case connecting
    case registering
    case retrying(reason: ReportFailure)
    case rateLimited(until: Date)
    case changed
    /// Setting up after the invitation: the discovery fetch failed.
    case service(ReportServiceIssue)
    /// Setting up after the invitation: registration failed for a reason that passes (REPORTING 7.3).
    case registerFailed(ReportFailure)
    /// The invitation did not cover the service's document; the consent sheet finishes it (REPORTING 3.3).
    case review
    case problem(ReportProblem)

    enum Tone: Equatable {
        case green, gray, amber, red
    }

    var tone: Tone {
        switch self {
        case .reporting: .green
        case .off, .networkOff, .never, .idle, .quiet, .paused, .pausedIndefinitely: .gray
        case .connecting, .registering, .retrying, .rateLimited, .registerFailed: .amber
        case .service(let issue): issue.isPermanent ? .red : .amber
        case .changed, .review, .problem: .red
        }
    }

    /// Red: the user has to act (the dashboard indicator's attention dot).
    var needsUser: Bool { tone == .red }

    /// The plain reason shown under the line (`report.error.*`), when there is one.
    var detail: ReportFailure? {
        switch self {
        case .retrying(let reason): reason
        case .registerFailed(let reason): reason
        default: nil
        }
    }

    @MainActor
    func text(_ l10n: Localizer, now: Date) -> String {
        switch self {
        case .off: return l10n("report.status.off")
        case .networkOff: return l10n("settings.network.offHint")
        case .never: return l10n("report.status.never")
        case .reporting(let last):
            let seconds = max(0, Int(now.timeIntervalSince(last).rounded(.down)))
            let when = seconds < 60
                ? l10n("time.secondsAgo", seconds)
                : last.formatted(Date.RelativeFormatStyle(presentation: .named, unitsStyle: .wide).locale(l10n.locale))
            return l10n("report.status.reporting", when)
        case .idle: return l10n("report.status.idle")
        case .quiet: return l10n("report.status.quiet")
        case .paused(let until): return l10n("report.status.paused", Self.time(until, l10n))
        case .pausedIndefinitely: return l10n("report.status.pausedIndefinitely")
        case .connecting: return l10n("report.status.connecting")
        case .registering: return l10n("report.status.registering")
        case .retrying: return l10n("report.status.retrying")
        case .rateLimited(let until): return l10n("report.status.rateLimited", Self.time(until, l10n))
        case .changed: return l10n("report.service.changed")
        case .service(let issue): return l10n(issue.key)
        case .registerFailed: return l10n("report.status.registerFailed")
        case .review: return l10n("report.status.review")
        case .problem(let problem): return l10n(problem.statusKey)
        }
    }

    var color: Color {
        switch tone {
        case .green: .green
        case .gray: Color(nsColor: .tertiaryLabelColor)
        case .amber: .orange
        case .red: .red
        }
    }

    @MainActor
    static func time(_ date: Date, _ l10n: Localizer) -> String {
        date.formatted(Date.FormatStyle(date: .omitted, time: .shortened).locale(l10n.locale))
    }
}

/// A failed discovery fetch, as one plain sentence (DESIGN 11, Service; REPORTING 3.1).
enum ReportServiceIssue: Equatable {
    case notSupported, invalid, unreachable, insecure, unavailable

    init(_ failure: ReportFailure) {
        switch failure {
        case .unreachable: self = .unreachable
        case .insecure: self = .insecure
        case .unavailable, .response, .key: self = .unavailable
        }
    }

    var key: String {
        switch self {
        case .notSupported: "report.service.notSupported"
        case .invalid: "report.service.invalid"
        case .unreachable: "report.service.unreachable"
        case .insecure: "report.service.insecure"
        case .unavailable: "report.service.unavailable"
        }
    }

    /// The service itself does not offer the API: trying again soon will not help (a red octagon).
    var isPermanent: Bool { self == .notSupported || self == .invalid }

    var symbol: String { isPermanent ? "xmark.octagon.fill" : "exclamationmark.triangle.fill" }
}

extension ReportProblem {
    var statusKey: String {
        switch self {
        case .apiKey: "report.status.apiKey"
        case .usernameTaken: "report.status.usernameTaken"
        case .usernameRejected: "report.status.usernameRejected"
        case .keyMismatch: "report.status.keyMismatch"
        case .rejected: "report.status.rejected"
        case .forbidden: "report.status.forbidden"
        case .upgrade: "report.status.upgrade"
        case .gone: "report.status.gone"
        }
    }
}
