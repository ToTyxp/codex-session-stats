using AgentSpeed.TrafficHelper.Counting;
using AgentSpeed.TrafficHelper.Etw;
using AgentSpeed.TrafficHelper.Install;
using AgentSpeed.TrafficHelper.Native;
using AgentSpeed.TrafficHelper.Pipe;
using AgentSpeed.TrafficHelper.Text;

namespace AgentSpeed.TrafficHelper.Service;

/// <summary>
/// The running service (WINDOWS-SERVICE 3): pipe first (so a squatted name stops it before anything else), then the
/// ETW session (an orphaned one is stopped first) and the process listing; once a second it expires exited entries,
/// refreshes the listing every 30 s (sooner while orphan bytes wait), records lost events, restarts a session that
/// died, and stops itself 120 s after the last client left.
/// </summary>
internal sealed class ServiceRuntime : IDisposable
{
    private const long ListingIntervalMs = 30_000;
    private const long OrphanListingIntervalMs = 2_000;
    private const long QueryIntervalMs = 5_000;
    private const long SessionRetryMs = 30_000;

    private readonly nint _stopEvent;
    private readonly TrafficTable _table = new();
    private readonly HelperStats _stats = new();
    private readonly ProcessListReader _listing = new();
    private readonly TraceSink _sink;
    private TraceSession? _session;
    private TraceConsumer? _consumer;
    private long _lastListingMs = long.MinValue / 2;
    private long _lastQueryMs;
    private long _lastSessionAttemptMs = long.MinValue / 2;

    public ServiceRuntime()
    {
        _stopEvent = Kernel32.CreateEventW(0, 1, 0, 0);
        _sink = new TraceSink(new TrafficEvents(_table, _stats));
    }

    /// <summary>Asks <see cref="Run"/> to return (STOP or SHUTDOWN from the SCM).</summary>
    public void RequestStop() => Kernel32.SetEvent(_stopEvent);

    /// <summary>Runs until stopped or idle; returns the Win32 exit code for SERVICE_STOPPED.</summary>
    public uint Run(Action running)
    {
        if (_stopEvent == 0)
        {
            return Kernel32.LastError;
        }

        var epoch = (ulong)Kernel32.FileTimeNow();
        var serviceSid = ServiceIdentity.Sid();
        if (serviceSid is null)
        {
            return Win32Error.InvalidParameter;
        }

        var allowedUsers = HelperRegistry.ReadAllowedUsers();
        using var descriptor = SecurityDescriptor.FromSddl(SecurityText.PipeSddl(serviceSid, allowedUsers), out var error);
        if (descriptor is null)
        {
            return error;
        }

        // Without it every client fails verification and reports helperDenied, which is the safe failure.
        ProcessAccess.GrantQueryLimited(allowedUsers);

        var idle = new IdleTracker(Environment.TickCount64);
        var handler = new RequestHandler(_table, _stats, epoch, static () => (ulong)Kernel32.FileTimeNow());
        using var pipes = new PipeServer(descriptor, handler, idle, _stopEvent);
        error = pipes.Start();
        if (error != 0)
        {
            return error;
        }

        StartTracing(Environment.TickCount64);
        RefreshListing(Environment.TickCount64);
        running();

        while (Kernel32.WaitForSingleObject(_stopEvent, 1_000) == Kernel32.WaitTimeout)
        {
            var now = Environment.TickCount64;
            _table.Expire(now);
            if (now - _lastListingMs >= ListingIntervalMs || (_table.HasOrphans && now - _lastListingMs >= OrphanListingIntervalMs))
            {
                RefreshListing(now);
            }

            if (now - _lastQueryMs >= QueryIntervalMs)
            {
                QuerySession(now);
            }

            if (_consumer is not { IsRunning: true } && now - _lastSessionAttemptMs >= SessionRetryMs)
            {
                StopTracing();
                StartTracing(now);
            }

            if (idle.ShouldStop(now))
            {
                break;
            }
        }

        Kernel32.SetEvent(_stopEvent);
        StopTracing();
        pipes.Stop(500);
        return 0;
    }

    public void Dispose()
    {
        StopTracing();
        if (_stopEvent != 0)
        {
            Kernel32.CloseHandle(_stopEvent);
        }
    }

    private void StartTracing(long now)
    {
        _lastSessionAttemptMs = now;
        var session = new TraceSession();
        if (session.Start() != 0)
        {
            _stats.SetHealthy(false);
            return;
        }

        var consumer = new TraceConsumer();
        if (consumer.Start(_sink) != 0)
        {
            session.Stop();
            _stats.SetHealthy(false);
            return;
        }

        _session = session;
        _consumer = consumer;
        _stats.SetHealthy(true);
    }

    private void StopTracing()
    {
        if (_session is { } session && session.TryQueryLost(out var lost))
        {
            _stats.SetCurrentSessionLost(lost);
        }

        _session?.Stop();
        _consumer?.Stop(1_000);
        if (_session is not null)
        {
            _stats.SessionEnded();
        }

        _session = null;
        _consumer = null;
        _stats.SetHealthy(false);
    }

    private void QuerySession(long now)
    {
        _lastQueryMs = now;
        if (_session is { } session && session.TryQueryLost(out var lost))
        {
            _stats.SetCurrentSessionLost(lost);
        }

        _stats.SetHealthy(_consumer is { IsRunning: true });
    }

    private void RefreshListing(long now)
    {
        _lastListingMs = now;
        var taken = (ulong)Kernel32.FileTimeNow();
        if (_listing.Read() is { } processes)
        {
            _table.Reconcile(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(processes), taken, now);
        }
    }
}
