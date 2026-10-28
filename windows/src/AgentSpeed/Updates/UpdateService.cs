using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using AgentSpeed.App;
using AgentSpeed.Presentation.Settings;
using AgentSpeed.Presentation.Updates;
using AgentSpeed.Services;

namespace AgentSpeed.Updates;

/// <summary>
/// Update state shared by the menu, dashboard, settings and update window; the automatic schedule; and the install
/// flow (UPDATES sections 3–5). Use from the UI thread only.
/// </summary>
internal sealed class UpdateService : IDisposable
{
    private readonly SettingsService _settings;
    private readonly UpdateClient _client;
    private readonly DispatcherTimer _timer;
    private readonly SemanticVersion? _current;
    private readonly CancellationTokenSource _lifetime = new();
    private DateTimeOffset _due;
    private Task? _check;
    private bool _checkIsManual;
    private bool _installing;
    private bool _disposed;

    public UpdateService(Dispatcher dispatcher, SettingsService settings, string currentVersion)
    {
        _settings = settings;
        CurrentVersion = currentVersion;
        _current = SemanticVersion.ParseOrNull(currentVersion);
        Source = UpdateSource.FromOverride(Environment.GetEnvironmentVariable(UpdateSource.FeedOverrideVariable), out var rejected);
        if (rejected)
        {
            AppLog.Warn($"{UpdateSource.FeedOverrideVariable} is not an http(s) URL; checking GitHub instead.");
        }
        else if (Source.IsOverride)
        {
            AppLog.Warn($"Update feed overridden by {UpdateSource.FeedOverrideVariable} (host {Source.FeedUrl.Host}); the host allow-list is off.");
        }

        _client = new UpdateClient(Source, currentVersion);
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
        _timer.Tick += OnTick;
    }

    public event EventHandler? Changed;

    /// <summary>An automatic check found a version the user has not been notified about yet.</summary>
    public event EventHandler<ReleaseInfo>? NotificationDue;

    /// <summary>The new version is installed and running; this process must quit now.</summary>
    public event EventHandler? RelaunchRequested;

    public string CurrentVersion { get; }

    public UpdateSource Source { get; }

    public UpdateStatus Status { get; private set; } = UpdateStatus.Idle;

    /// <summary>The newer release found by the last successful check, unless skipped; drives the menu item and the pill.</summary>
    public ReleaseInfo? Available { get; private set; }

    public Uri ReleasePage => Source.ReleasePage(Status.Release ?? Available) ?? new Uri(AppPaths.ReleasesUrl, UriKind.Absolute);

    public void Start()
    {
        if (_current is null)
        {
            AppLog.Warn($"Version '{CurrentVersion}' is not a semantic version; updates are never offered.");
        }
        else if (_current.Value.IsDevelopment)
        {
            AppLog.Info("Development build (0.0.0): updates are checked only when asked.");
        }

        Reschedule();
    }

    /// <summary>NETWORK 1: with the master switch off nothing is requested, automatic or manual.</summary>
    public bool NetworkEnabled => _settings.Current.NetworkEnabled;

    public void OnSettingsChanged(AppSettings previous, AppSettings current)
    {
        if (previous.AutoCheckUpdates != current.AutoCheckUpdates || previous.NetworkEnabled != current.NetworkEnabled)
        {
            Reschedule();
        }
    }

    /// <summary>
    /// Manual checks always run and report their outcome in <see cref="Status"/>; automatic checks change it only on
    /// success and otherwise just log. A manual request during an automatic check joins it.
    /// </summary>
    public async Task CheckAsync(bool manual)
    {
        if (_disposed || _installing || !NetworkEnabled)
        {
            return;
        }

        if (manual)
        {
            _checkIsManual = true;
            SetStatus(new UpdateStatus(UpdatePhase.Checking));
        }

        var check = _check ??= RunCheckAsync();
        try
        {
            await check;
        }
        finally
        {
            if (ReferenceEquals(_check, check))
            {
                _check = null;
            }
        }
    }

    /// <summary>Brings the known available release to the front (for the update window).</summary>
    public void Present()
    {
        if (Available is { } release && !Status.IsBusy && Status.Phase != UpdatePhase.InstallFailed)
        {
            SetStatus(new UpdateStatus(UpdatePhase.Available, release));
        }
    }

    public void Skip()
    {
        if (_installing || (Status.Release ?? Available) is not { } release)
        {
            return;
        }

        AppLog.Info($"Update {release.Tag} skipped by the user.");
        _settings.Update(s => s with { SkippedVersion = release.Tag });
        Available = null;
        SetStatus(UpdateStatus.Idle);
    }

    public async Task InstallAsync()
    {
        var release = Status.Phase is UpdatePhase.Available or UpdatePhase.InstallFailed ? Status.Release : null;
        release ??= Available;
        if (_disposed || _installing || release is null || !NetworkEnabled)
        {
            return;
        }

        _installing = true;
        PreparedUpdate? prepared = null;
        AppLog.Info($"Update: installing {release.Tag} over {CurrentVersion}.");
        try
        {
            var target = ExecutableSwap.TargetPath()
                ?? throw new UpdateException(UpdateFailure.Invalid, "Only the published single-file executable can update itself.");
            var plan = UpdatePackage.Plan(release, RuntimeInformation.ProcessArchitecture, Source);
            EnsureWritable(target);

            SetStatus(new UpdateStatus(UpdatePhase.Downloading, release));
            prepared = await new UpdatePreparer(_client).PrepareAsync(
                plan, (phase, progress) => SetStatus(new UpdateStatus(phase, release, progress)), _lifetime.Token);

            SetStatus(new UpdateStatus(UpdatePhase.Installing, release, 1));
            var executable = prepared.Executable;
            var current = CurrentVersion;
            await Task.Run(() => ExecutableSwap.Apply(executable, target, release.Version, current, Environment.ProcessId));

            AppLog.Info($"Update: {release.Tag} installed; quitting so the new version can take over.");
            prepared.Dispose();
            prepared = null;
            SetStatus(new UpdateStatus(UpdatePhase.Relaunching, release, 1));
            RelaunchRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (UpdateException ex)
        {
            Fail(release, ex.Failure, ex);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException && _disposed)
        {
            AppLog.Info("Update: install cancelled because the app is quitting.");
        }
        catch (Exception ex)
        {
            // Never leave the window stuck in a progress state.
            Fail(release, ex is UnauthorizedAccessException ? UpdateFailure.Permission
                : ex is HttpRequestException ? UpdateFailure.Network : UpdateFailure.Invalid, ex);
        }
        finally
        {
            prepared?.Dispose();
            _installing = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _lifetime.Cancel();
        _client.Dispose();
        _lifetime.Dispose();
    }

    private void Fail(ReleaseInfo release, UpdateFailure failure, Exception exception)
    {
        AppLog.Warn($"Update: installing {release.Tag} failed ({failure}): {UpdateLog.Describe(exception)}");
        if (!_disposed)
        {
            SetStatus(new UpdateStatus(UpdatePhase.InstallFailed, release, 0, failure));
        }
    }

    /// <summary>Fails early, before any download, when the install folder cannot be written.</summary>
    private static void EnsureWritable(string target)
    {
        var probe = Path.Combine(Path.GetDirectoryName(target)!, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UpdateException(UpdateFailure.Permission, "The install folder is not writable.", ex);
        }
    }

    private async Task RunCheckAsync()
    {
        ReleaseInfo? latest = null;
        UpdateException? error = null;
        try
        {
            latest = await _client.FetchLatestAsync(_lifetime.Token);
        }
        catch (UpdateException ex)
        {
            error = ex;
        }
        catch (Exception ex) when (_disposed && ex is OperationCanceledException or ObjectDisposedException)
        {
            return;
        }
        catch (Exception ex)
        {
            error = new UpdateException(UpdateFailure.Network, "The check failed unexpectedly.", ex);
        }

        var manual = _checkIsManual;
        _checkIsManual = false;
        if (_disposed)
        {
            return;
        }

        var kind = manual ? "manual" : "automatic";
        if (error is not null)
        {
            AppLog.Warn($"Update check ({kind}) failed: {UpdateLog.Describe(error)}");
            if (manual && !_installing)
            {
                SetStatus(new UpdateStatus(UpdatePhase.CheckFailed, Failure: error.Failure));
            }

            return;
        }

        _settings.Update(s => s with { LastUpdateCheck = DateTimeOffset.UtcNow });
        ApplyResult(latest, manual, kind);
    }

    private void ApplyResult(ReleaseInfo? latest, bool manual, string kind)
    {
        var settings = _settings.Current;
        var offer = _current is { } current ? UpdateSchedule.Evaluate(current, latest, settings.SkippedVersion, manual) : UpdateOffer.UpToDate;
        AppLog.Info($"Update check ({kind}): current {CurrentVersion}, latest {latest?.Tag ?? "none"} -> {offer}.");
        Available = offer == UpdateOffer.Available ? latest : null;
        if (_installing)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!manual && Status.Phase == UpdatePhase.InstallFailed && Available is { } failed && Status.Release?.Version == failed.Version)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        SetStatus(Available is { } release ? new UpdateStatus(UpdatePhase.Available, release) : new UpdateStatus(UpdatePhase.UpToDate));
        if (Available is { } found && UpdateSchedule.ShouldNotify(found, settings.NotifiedVersion))
        {
            _settings.Update(s => s with { NotifiedVersion = found.Tag });
            if (!manual)
            {
                NotificationDue?.Invoke(this, found);
            }
        }
    }

    private void SetStatus(UpdateStatus status)
    {
        Status = status;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool AutomaticChecksAllowed =>
        !_disposed && NetworkEnabled && UpdateSchedule.AutomaticChecksAllowed(_settings.Current.AutoCheckUpdates, _current);

    private void Reschedule()
    {
        _timer.Stop();
        if (!AutomaticChecksAllowed)
        {
            return;
        }

        _due = UpdateSchedule.FirstCheck(DateTimeOffset.UtcNow, _settings.Current.LastUpdateCheck);
        Arm();
    }

    private void Arm()
    {
        _timer.Interval = UpdateSchedule.TimerStep(DateTimeOffset.UtcNow, _due);
        _timer.Start();
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        if (!AutomaticChecksAllowed)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (UpdateSchedule.IsDue(now, _due))
        {
            // Failures and rate limits wait for the next interval as well (UPDATES section 3).
            _due = UpdateSchedule.NextCheck(now);
            try
            {
                await CheckAsync(manual: false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                AppLog.Error("The automatic update check failed unexpectedly.", ex);
            }
        }

        if (AutomaticChecksAllowed && !_timer.IsEnabled)
        {
            Arm();
        }
    }
}
