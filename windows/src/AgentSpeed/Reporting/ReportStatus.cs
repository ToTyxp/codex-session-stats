using AgentSpeed.Core.Reporting;
using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Menus;

namespace AgentSpeed.Reporting;

public enum ReportStatusKind
{
    Off,
    NetworkOff,
    Never,
    Reporting,
    Idle,
    Quiet,
    Paused,
    PausedIndefinitely,
    Connecting,
    Registering,
    Retrying,
    RateLimited,
    Changed,
    Service,
    RegisterFailed,
    Review,
    Problem,
}

public enum ReportTone
{
    Green,
    Gray,
    Amber,
    Red,
}

/// <summary>A failed discovery fetch, as one plain sentence (DESIGN 11, Service; REPORTING 3.1).</summary>
public enum ReportServiceIssue
{
    NotSupported,
    Invalid,
    Unreachable,
    Insecure,
    Unavailable,
}

/// <summary>
/// The pane's status line (DESIGN 11, Status): always a localized string of the table, with a dot whose color never
/// carries meaning alone, and never a status code or error class.
/// </summary>
internal sealed record ReportStatus(ReportStatusKind Kind)
{
    public DateTimeOffset? At { get; init; }

    public ReportFailure? Failure { get; init; }

    public ReportServiceIssue? Issue { get; init; }

    public ReportProblem? Problem { get; init; }

    public ReportTone Tone => Kind switch
    {
        ReportStatusKind.Reporting => ReportTone.Green,
        ReportStatusKind.Off or ReportStatusKind.NetworkOff or ReportStatusKind.Never or ReportStatusKind.Idle or ReportStatusKind.Quiet
            or ReportStatusKind.Paused or ReportStatusKind.PausedIndefinitely => ReportTone.Gray,
        ReportStatusKind.Connecting or ReportStatusKind.Registering or ReportStatusKind.Retrying or ReportStatusKind.RateLimited
            or ReportStatusKind.RegisterFailed => ReportTone.Amber,
        ReportStatusKind.Service => Issue is ReportServiceIssue.NotSupported or ReportServiceIssue.Invalid ? ReportTone.Red : ReportTone.Amber,
        _ => ReportTone.Red,
    };

    /// <summary>Red: the user has to act (the dashboard indicator's attention dot).</summary>
    public bool NeedsUser => Tone == ReportTone.Red;

    /// <summary>The plain reason shown under the line (<c>report.error.*</c>), when there is one.</summary>
    public string? DetailKey => Kind is ReportStatusKind.Retrying or ReportStatusKind.RegisterFailed && Failure is { } failure
        ? ReportFailures.Key(failure)
        : null;

    public TrafficHelperDot Dot => Tone switch
    {
        ReportTone.Green => TrafficHelperDot.Success,
        ReportTone.Amber => TrafficHelperDot.Caution,
        ReportTone.Red => TrafficHelperDot.Critical,
        _ => TrafficHelperDot.Neutral,
    };

    public MenuStatusTone MenuTone => Tone switch
    {
        ReportTone.Green => MenuStatusTone.Green,
        ReportTone.Amber => MenuStatusTone.Amber,
        ReportTone.Red => MenuStatusTone.Red,
        _ => MenuStatusTone.Gray,
    };

    public string Text(Localizer loc, DateTimeOffset now) => Kind switch
    {
        ReportStatusKind.Off => loc["report.status.off"],
        ReportStatusKind.NetworkOff => loc["settings.network.offHint"],
        ReportStatusKind.Never => loc["report.status.never"],
        ReportStatusKind.Reporting => loc.Format("report.status.reporting", Ago(At ?? now, now, loc)),
        ReportStatusKind.Idle => loc["report.status.idle"],
        ReportStatusKind.Quiet => loc["report.status.quiet"],
        ReportStatusKind.Paused => loc.Format("report.status.paused", Time(At ?? now, loc)),
        ReportStatusKind.PausedIndefinitely => loc["report.status.pausedIndefinitely"],
        ReportStatusKind.Connecting => loc["report.status.connecting"],
        ReportStatusKind.Registering => loc["report.status.registering"],
        ReportStatusKind.Retrying => loc["report.status.retrying"],
        ReportStatusKind.RateLimited => loc.Format("report.status.rateLimited", Time(At ?? now, loc)),
        ReportStatusKind.Changed => loc["report.service.changed"],
        ReportStatusKind.Service => loc[IssueKey(Issue ?? ReportServiceIssue.Unavailable)],
        ReportStatusKind.RegisterFailed => loc["report.status.registerFailed"],
        ReportStatusKind.Review => loc["report.status.review"],
        _ => loc[ProblemKey(Problem ?? ReportProblem.Rejected)],
    };

    public static string IssueKey(ReportServiceIssue issue) => issue switch
    {
        ReportServiceIssue.NotSupported => "report.service.notSupported",
        ReportServiceIssue.Invalid => "report.service.invalid",
        ReportServiceIssue.Unreachable => "report.service.unreachable",
        ReportServiceIssue.Insecure => "report.service.insecure",
        _ => "report.service.unavailable",
    };

    public static ReportServiceIssue IssueOf(ReportFailure failure) => failure switch
    {
        ReportFailure.Unreachable => ReportServiceIssue.Unreachable,
        ReportFailure.Insecure => ReportServiceIssue.Insecure,
        _ => ReportServiceIssue.Unavailable,
    };

    public static string ProblemKey(ReportProblem problem) => problem switch
    {
        ReportProblem.ApiKey => "report.status.apiKey",
        ReportProblem.UsernameTaken => "report.status.usernameTaken",
        ReportProblem.UsernameRejected => "report.status.usernameRejected",
        ReportProblem.KeyMismatch => "report.status.keyMismatch",
        ReportProblem.Rejected => "report.status.rejected",
        ReportProblem.Forbidden => "report.status.forbidden",
        ReportProblem.Upgrade => "report.status.upgrade",
        _ => "report.status.gone",
    };

    /// <summary><c>time.secondsAgo</c> under a minute, then the local time of day.</summary>
    private static string Ago(DateTimeOffset at, DateTimeOffset now, Localizer loc)
    {
        var seconds = (int)Math.Max(0, Math.Floor((now - at).TotalSeconds));
        return seconds < 60 ? loc.Format("time.secondsAgo", seconds) : Time(at, loc);
    }

    public static string Time(DateTimeOffset at, Localizer loc) =>
        at.ToLocalTime().ToString(loc.Culture.DateTimeFormat.ShortTimePattern, loc.Culture);
}
