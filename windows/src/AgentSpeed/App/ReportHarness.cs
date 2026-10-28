using System.Diagnostics;
using System.Windows.Threading;
using AgentSpeed.Core;
using AgentSpeed.Core.Reporting;
using AgentSpeed.Presentation.Settings;
using AgentSpeed.Reporting;

namespace AgentSpeed.App;

/// <summary>
/// <c>--report-e2e URL</c>: drives the real live reporting controller — the dashboard invitation (declined, then accepted),
/// consent, the DPAPI-protected device key, registration, reports while busy and idle, rename, key rotation, rate limiting,
/// pause, the API key, the network switch, turning off and Forget — against a local reference server
/// (<c>spec/tools/report_server.py</c>). The live monitor is not started; a synthetic engine feed publishes a snapshot every
/// 0.5 s. It refuses to run without an isolated settings file and key folder (<c>AGENT_SPEED_SETTINGS_FILE</c>,
/// <c>AGENT_SPEED_REPORT_DIR</c>) and the preset pointed at the same server (<c>AGENT_SPEED_REPORT_PRESET_URL</c>). Writes one
/// line per check to <c>report-e2e.log</c> next to the settings file and quits.
/// </summary>
internal static class ReportHarness
{
    private static readonly ReportCounters Counters = new();
    private static bool _busy;
    private static int _failures;
    private static StreamWriter? _log;

    public static async Task RunAsync(AppController app, ReportController report, string url)
    {
        var settingsFile = Environment.GetEnvironmentVariable("AGENT_SPEED_SETTINGS_FILE");
        if (string.IsNullOrEmpty(settingsFile) || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AGENT_SPEED_REPORT_DIR")))
        {
            AppLog.Warn("--report-e2e needs AGENT_SPEED_SETTINGS_FILE and AGENT_SPEED_REPORT_DIR; not running.");
            return;
        }

        _log = new StreamWriter(Path.Combine(Path.GetDirectoryName(settingsFile)!, "report-e2e.log"), append: false) { AutoFlush = true };
        var feed = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Normal, (_, _) => Publish(app), app.Dispatcher);
        feed.Start();
        try
        {
            await Run(app, report, url);
        }
        catch (Exception ex)
        {
            AppLog.Error("REPORT-E2E harness failed.", ex);
            Check($"harness ran without an exception ({ex.GetType().Name}: {ex.Message})", false);
        }
        finally
        {
            feed.Stop();
        }

        Write($"REPORT-E2E {(_failures == 0 ? "PASSED" : $"FAILED ({_failures})")}");
        _log.Dispose();
        app.Quit();
    }

    private static async Task Run(AppController app, ReportController report, string url)
    {
        var apiKey = Environment.GetEnvironmentVariable("AGENT_SPEED_REPORT_E2E_API_KEY");
        await Until("the engine feed runs", 5, () => report.Status.Kind == ReportStatusKind.Off);
        if (apiKey is null)
        {
            await Invitation(report, url);
        }

        report.SelectPreset(ReportPreset.Custom);
        report.CommitCustomUrl(url);
        await Until("discovery", 10, () => report.ServiceState.Kind == ReportServiceStateKind.Ready);

        var expectedName = report.DefaultUsername(report.UsernameRules);
        report.SetEnabled(true);
        await Until($"consent dialog opens with the fitted computer name ({expectedName})", 10, () => report.Consent?.Username == expectedName);
        Check("nothing is created before Start", report.Identity is null && KeyFiles(report).Length == 0);
        if (apiKey is not null)
        {
            report.StartReporting();
            await Until("Start without the required API key asks for it", 3, () => report.Consent?.ApiKeyError == "report.apiKey.required");
            Check("still nothing created or registered", report.Identity is null && KeyFiles(report).Length == 0);
            report.EditConsent(d => d with { ApiKey = apiKey });
        }

        report.StartReporting();
        await Until("registered and on", 10, () => report.IsOn && report.Identity?.Username == expectedName);
        var deviceId = report.Identity?.DeviceId ?? string.Empty;
        Check("device key stored DPAPI-protected (not plaintext)", IsDpapiBlob(Path.Combine(report.KeyStore.Directory, deviceId + ".key")));
        Check("the stored key opens again with the same fingerprint", report.KeyStore.LoadKey(deviceId)?.PublicKey.Fingerprint == report.Fingerprint);
        if (apiKey is not null)
        {
            Check("API key stored DPAPI-protected", IsDpapiBlob(Path.Combine(report.KeyStore.Directory, deviceId + ".apikey")));
            Check("the protected API key opens again", report.KeyStore.LoadApiKey(deviceId) == apiKey);
            Check("the API key is shown masked only", report.MaskedApiKey == "••••" + apiKey[^4..]);
        }

        if (Environment.GetEnvironmentVariable("AGENT_SPEED_REPORT_BENCH") is not null)
        {
            await Bench(report);
        }

        _busy = true;
        await Until("reports delivered while busy", 20, () => report.LastDeliveryAt is not null && report.Mode == ReportMode.Active);
        var first = report.LastDeliveryAt;
        await Until("a second active report", 8, () => report.LastDeliveryAt != first);

        report.CommitUsername("racer-e2e01");
        await Until("rename", 10, () => report.RenameState == ReportRenameKind.Updated && report.Identity?.Username == "racer-e2e01");
        var renamed = report.LastDeliveryAt;
        await Until("report under the new name", 10, () => report.LastDeliveryAt != renamed);

        var oldFingerprint = report.Fingerprint;
        report.RotateKeyIfDue(force: true);
        await Until("key rotated", 10, () => !report.HasPendingKey && report.Fingerprint != oldFingerprint);
        Check("the rotated key replaced the stored one", report.KeyStore.LoadKey(deviceId)?.PublicKey.Fingerprint == report.Fingerprint
            && !File.Exists(Path.Combine(report.KeyStore.Directory, deviceId + ".pending.key")));
        var rotated = report.LastDeliveryAt;
        await Until("report signed with the rotated key", 10, () => report.LastDeliveryAt != rotated && report.Problem is null);

        // Leaving reports of quick pause/resume cycles exceed the server's burst of 3: it answers 429 with Retry-After.
        for (var i = 0; i < 6 && report.Status.Kind != ReportStatusKind.RateLimited; i++)
        {
            report.Pause(TimeSpan.FromHours(1));
            await Task.Delay(150);
            report.Resume();
            await Task.Delay(350);
        }

        await Until("rate limited: the status says when it resumes", 5, () => report.Status.Kind == ReportStatusKind.RateLimited);
        var limitedDelivery = report.LastDeliveryAt;
        var until = report.RateLimitedUntil ?? DateTimeOffset.UtcNow;
        await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, (until - DateTimeOffset.UtcNow).TotalSeconds - 0.5)));
        Check("rate limited: nothing sent during the wait", report.LastDeliveryAt == limitedDelivery);
        await Until("rate limited: reporting resumes after the wait", 15, () => report.LastDeliveryAt != limitedDelivery
            && report.Status.Kind == ReportStatusKind.Reporting);

        report.Pause(TimeSpan.FromHours(1));
        Check("paused", report.Status.Kind == ReportStatusKind.Paused);
        await Task.Delay(1000);
        report.Resume();

        if (apiKey is not null)
        {
            report.ClearApiKey();
            await Until("without its API key the service refuses: the status asks for one", 15,
                () => report.Status is { Kind: ReportStatusKind.Problem, Problem: ReportProblem.ApiKey });
            Check("saving the key again works", report.SetApiKey(apiKey));
            var keyed = report.LastDeliveryAt;
            await Until("reporting resumes with the key", 15, () => report.LastDeliveryAt != keyed && report.Problem is null);
        }

        app.UpdateSettings(s => s with { NetworkEnabled = false });
        var offDelivery = report.LastDeliveryAt;
        var requestsOff = report.RequestCount;
        await Task.Delay(7000);
        Check("network off: nothing sent", report.LastDeliveryAt == offDelivery && report.RequestCount == requestsOff
            && report.Status.Kind == ReportStatusKind.NetworkOff);
        app.UpdateSettings(s => s with { NetworkEnabled = true });
        await Until("network back on: reporting resumes", 15, () => report.LastDeliveryAt != offDelivery);

        _busy = false;
        await Until("idle after 15 quiet seconds", 20, () => report.Mode == ReportMode.Idle);
        var idleStart = report.LastDeliveryAt;
        await Until("idle heartbeat", 70, () => report.LastDeliveryAt != idleStart);

        _busy = true;
        await Until("busy again", 10, () => report.Mode == ReportMode.Active);
        var beforeOff = report.LastDeliveryAt;
        report.SetEnabled(false);
        await Until("turning off sends a leaving report", 5, () => report.LastDeliveryAt != beforeOff);
        Check("turned off, registration kept", !report.Preferences.Enabled && report.Identity?.IsRegistered == true);
        _busy = false;

        report.ForgetDevice();
        await Until("forgotten on the service", 10, () => report.ForgetState == ReportForgetKind.Done);
        Check("keys deleted locally", KeyFiles(report).Length == 0 && report.Identity is null);
    }

    /// <summary>
    /// The dashboard invitation for the preset, pointed at the local server: declining never touches the network and creates
    /// nothing; accepting is the consent that fetches, creates the key, registers and reports.
    /// </summary>
    private static async Task Invitation(ReportController report, string url)
    {
        if (report.SelectedService?.Text != ReportServiceUrl.Parse(url)?.Text)
        {
            Check("the preset points at the local server (AGENT_SPEED_REPORT_PRESET_URL)", false);
            return;
        }

        Check("invitation offered on a fresh install", report.InvitationOffered);
        Check("showing it touches nothing", report.RequestCount == 0 && report.ServiceState.Kind == ReportServiceStateKind.Unknown);
        report.DeclineInvitation();
        await Task.Delay(3000);
        Check("declined: answered and never offered again", report.Preferences.Invite == ReportInviteAnswer.Declined && !report.InvitationOffered);
        Check("declined: no request, no key, nothing stored", report.RequestCount == 0 && KeyFiles(report).Length == 0
            && report.Preferences.Identities.IsEmpty && !report.Preferences.Enabled);

        report.ResetInvitation();
        Check("offered again once the answer is reset", report.InvitationOffered && report.RequestCount == 0);
        var expected = report.DefaultUsername(ReportUsernameRules.Standard);
        report.AcceptInvitation();
        Check("accepted: the done line shows and the indicator waits", report.InviteJustAccepted && !report.IndicatorShown);
        await Until($"accepted: registered as the fitted computer name ({expected})", 10, () => report.IsOn && report.Identity?.Username == expected);
        Check("accepted: answered, the default sections kept", report.Preferences.Invite == ReportInviteAnswer.Accepted
            && report.Preferences.Sections.SequenceEqual(ReportPreferences.DefaultSections));
        _busy = true;
        await Until("accepted: reports delivered while busy", 20, () => report.LastDeliveryAt is not null && report.Status.Tone == ReportTone.Green);
        _busy = false;
        await Until("accepted: the indicator appears after the done line", 5, () => report.IndicatorShown);
        Check($"accepted: requests went to the local server only ({report.RequestCount})", report.RequestCount >= 3);
        report.ForgetDevice();
        await Until("invitation device forgotten on the service", 10, () => report.ForgetState == ReportForgetKind.Done);
        report.DismissForgetResult();
        Check("forgotten: off, keys deleted, invitation stays answered", !report.Preferences.Enabled && KeyFiles(report).Length == 0
            && report.Preferences.Invite == ReportInviteAnswer.Accepted && !report.InvitationOffered);
    }

    /// <summary>
    /// The reporting loop's cost: per-tick and per-report CPU in isolation, then the whole process over 30 s with reporting
    /// off, on and busy, and on and idle (the synthetic feed is the same in every phase).
    /// </summary>
    private static async Task Bench(ReportController report)
    {
        _busy = true;
        await Task.Delay(1000);
        var ticks = 2000;
        var thread = Stopwatch.StartNew();
        for (var i = 0; i < ticks; i++)
        {
            report.BenchTick();
        }

        var perTick = thread.Elapsed.TotalMilliseconds * 1000 / ticks;
        var signer = new SoftwareReportSigner();
        var message = new byte[300];
        var sign = Stopwatch.StartNew();
        for (var i = 0; i < 200; i++)
        {
            signer.Sign(message);
        }

        var perSign = sign.Elapsed.TotalMilliseconds / 200;
        var preview = Stopwatch.StartNew();
        for (var i = 0; i < 200; i++)
        {
            _ = report.Preview();
        }

        var perBody = preview.Elapsed.TotalMilliseconds * 1000 / 200;
        Write(FormattableString.Invariant($"BENCH tick {perTick:0.0} µs, body (preview build) {perBody:0.0} µs, ES256 signature {perSign:0.000} ms (wall time on the UI thread)"));

        async Task Phase(string name, bool enabled, bool busy)
        {
            if (report.Preferences.Enabled != enabled)
            {
                report.SetEnabled(enabled);
            }

            _busy = busy;
            await Task.Delay(5000);
            var process = Process.GetCurrentProcess();
            var cpu = process.TotalProcessorTime;
            var wall = Stopwatch.StartNew();
            await Task.Delay(30_000);
            process.Refresh();
            var percent = (process.TotalProcessorTime - cpu).TotalMilliseconds / wall.Elapsed.TotalMilliseconds * 100;
            Write(FormattableString.Invariant($"BENCH {name}: {percent:0.00} % of one core (whole process, 30 s)"));
        }

        await Phase("reporting off, agents busy", enabled: false, busy: true);
        await Phase("reporting on, agents busy", enabled: true, busy: true);
        await Phase("reporting on, agents idle", enabled: true, busy: false);
        _busy = false;
    }

    /// <summary>One synthetic engine tick: an agent of Claude Code generating at 240 tok/s while busy, idle otherwise.</summary>
    private static void Publish(AppController app)
    {
        var key = new ReportModelKey("claude-opus-5-5", SourceKind.ClaudeCode);
        if (_busy)
        {
            Counters.Add(new ReportUsage(1, 120, 40_000, 36_000, 0, 0, 120, 900, 1800), key);
        }

        var live = _busy ? new Rate(240, 40_000) : Rate.Zero;
        var sources = Enum.GetValues<SourceKind>()
            .Select(kind => new SourceStatus(kind, true, string.Empty, true, 1, kind == SourceKind.ClaudeCode ? live : Rate.Zero,
                TokenTotals.Zero, kind == SourceKind.ClaudeCode && _busy ? 1 : 0)
            {
                LiveCost = kind == SourceKind.ClaudeCode && _busy ? new LiveCost(3600, 240) : LiveCost.Zero,
            })
            .ToList();
        var facts = new ReportLiveFacts(
            new Dictionary<SourceKind, ReportAgentCounts> { [SourceKind.ClaudeCode] = new(_busy ? 1 : 0, 0, 1) },
            _busy ? new Dictionary<ReportModelKey, int> { [key] = 1 } : new Dictionary<ReportModelKey, int>(),
            Counters.Copy(),
            Epoch: 1);
        app.PublishSynthetic(SpeedSnapshot.Empty with
        {
            Timestamp = DateTimeOffset.UtcNow,
            State = MonitorState.Running,
            Live = live,
            Sources = sources,
            LiveCost = _busy ? new LiveCost(3600, 240) : LiveCost.Zero,
            Report = facts,
        });
    }

    /// <summary>A DPAPI blob: version 1 and the provider GUID df9d8cd0-1501-11d1-8c7a-00c04fc297eb.</summary>
    private static bool IsDpapiBlob(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        ReadOnlySpan<byte> header = [0x01, 0x00, 0x00, 0x00, 0xD0, 0x8C, 0x9D, 0xDF, 0x01, 0x15, 0xD1, 0x11, 0x8C, 0x7A, 0x00, 0xC0, 0x4F, 0xC2, 0x97, 0xEB];
        var bytes = File.ReadAllBytes(path);
        return bytes.AsSpan().StartsWith(header);
    }

    private static string[] KeyFiles(ReportController report) =>
        Directory.Exists(report.KeyStore.Directory)
            ? Directory.GetFiles(report.KeyStore.Directory).Where(f => !Path.GetFileName(f).StartsWith('.')).ToArray()
            : [];

    private static async Task Until(string name, double seconds, Func<bool> condition)
    {
        var deadline = Stopwatch.StartNew();
        while (!condition() && deadline.Elapsed.TotalSeconds < seconds)
        {
            await Task.Delay(200);
        }

        Check(name, condition());
    }

    private static void Check(string name, bool ok)
    {
        if (!ok)
        {
            _failures++;
        }

        Write($"{(ok ? "PASS" : "FAIL")} {name}");
    }

    private static void Write(string line)
    {
        _log?.WriteLine(line);
        AppLog.Info("REPORT-E2E " + line);
    }
}
