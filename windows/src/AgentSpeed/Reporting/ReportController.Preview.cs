using AgentSpeed.Core.Reporting;
using AgentSpeed.Presentation.Settings;

namespace AgentSpeed.Reporting;

/// <summary>The next report as the preview shows it (DESIGN 11, Consent sheet and Preview window).</summary>
/// <param name="NextIn">Seconds until the next report is due; null while quiet, paused or off.</param>
/// <param name="Assumed">Built before the service's document is known: only the body is meaningful.</param>
internal sealed record ReportPreview(
    ReportJson Body,
    string RequestLine,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    int? NextIn,
    bool Assumed)
{
    /// <summary>What <c>report.preview.copy</c> copies: the pretty-printed body.</summary>
    public string CopyText => Body.ToPrettyString();
}

/// <summary>
/// The live preview: the real body of the next report — the open window of the run, or, before a run exists, the window
/// since the preview was opened.
/// </summary>
internal sealed partial class ReportController
{
    /// <summary>Starts the preview's own window when no run is open.</summary>
    public void OpenPreview()
    {
        _previewOpenings++;
        if (_previewWindow is null)
        {
            _previewWindow = new ReportWindow(LastSample);
            _previewStartedAt = MonotonicMs;
        }
    }

    public void ClosePreview()
    {
        _previewOpenings = Math.Max(0, _previewOpenings - 1);
        if (_previewOpenings == 0)
        {
            _previewWindow = null;
        }
    }

    /// <summary>
    /// The next report for the consent sheet's <paramref name="draft"/>, or for the selected service: its identity and
    /// document, or, before the document is known (the dashboard invitation), the protocol's defaults.
    /// </summary>
    public ReportPreview? Preview(ReportConsentDraft? draft = null)
    {
        var now = MonotonicMs;
        var sample = LastSample;
        ReportDiscovery document;
        string deviceId;
        string username;
        IEnumerable<ReportSection> sections = Preferences.Sections;
        var assumed = false;
        if (draft is not null)
        {
            document = draft.Discovery;
            deviceId = draft.DeviceId;
            username = ReportUsername.Normalized(draft.Username);
            sections = draft.Sections;
        }
        else if (Discovery is { } stored)
        {
            document = stored;
            deviceId = Identity?.DeviceId ?? _previewDeviceId;
            username = FieldUsername;
        }
        else if (SelectedService is { } service && ReportDiscovery.Assumed(service, ServiceName ?? service.Host) is { } guess)
        {
            document = guess;
            deviceId = _previewDeviceId;
            username = FieldUsername;
            assumed = true;
        }
        else
        {
            return null;
        }

        var run = draft is null ? _run : null;
        var window = run?.Window ?? _previewWindow ?? new ReportWindow(sample);
        var start = run is not null ? run.Scheduler.WindowStart ?? now : _previewStartedAt ?? now;
        var seq = run is not null ? run.Scheduler.Seq + 1 : 1;
        var working = sample.IsWorking || sample.Live.Output > 0;
        var activity = !working && Mode == ReportMode.Idle ? ReportActivity.Idle : ReportActivity.Active;
        var envelope = new ReportEnvelope(Client, deviceId, username, _run?.Id ?? _previewRunId, seq, UnixMs + _clockCorrection * 1000,
            Math.Max(0, now - start), activity, _run?.Title.Level ?? 0);
        var (body, data) = ReportBody.Fitted(envelope, window.Content(sample), sample, Options(document, sections));
        var url = document.Api.Endpoint(ReportEndpoint.Reports);
        var headers = new List<KeyValuePair<string, string>>
        {
            new("Host", ReportUrls.Authority(url)),
            new("User-Agent", Client.UserAgent),
            new("Accept", "application/json"),
            new("Content-Type", "application/json"),
            new("Content-Length", data.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(ReportCanonical.DeviceHeader, deviceId),
            new(ReportCanonical.TimestampHeader, (UnixMs / 1000 + _clockCorrection).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(ReportCanonical.NonceHeader, "…"),
            new(ReportCanonical.SignatureHeader, "…"),
        };
        if (draft is not null ? draft.ApiKey.Length > 0 : _apiKey is not null)
        {
            headers.Add(new("Authorization", "Bearer ••••"));
        }

        int? nextIn = null;
        if (run is not null && !IsPaused && run.Scheduler.NextDueAt is { } due)
        {
            nextIn = (int)Math.Max(0, (due - now + 999) / 1000);
        }

        return new ReportPreview(body, $"POST {ReportUrls.RequestPath(url)} HTTP/1.1", headers, nextIn, assumed);
    }

    /// <summary>The preset's name and what the invitation says (DESIGN 11, Dashboard invitation): nothing is fetched.</summary>
    public (string Name, string Domain, string Username) InvitationText =>
        (ReportPreferences.CodexRadarName, PresetDomain ?? SelectedService?.Authority ?? string.Empty, FieldUsername);
}
