using System.Windows.Media;
using System.Windows.Threading;
using AgentSpeed.Interop;
using AgentSpeed.Orb.Gpu;
using AgentSpeed.Presentation.Orb;

namespace AgentSpeed.Orb.Render;

/// <summary>Something the shared orb clock drives: a floating orb, a settings thumbnail or a style card.</summary>
internal interface IOrbFrameClient
{
    /// <summary>False while hidden, cloaked, occluded or faded out: no frames at all.</summary>
    bool IsActive { get; }

    /// <summary>Hovered or most recently interacted: keeps 60 fps when four or more orbs animate.</summary>
    bool IsPreferred { get; }

    /// <summary>The frame class after the last render.</summary>
    FrameClass Demand { get; }

    /// <summary>When static, the time (shared clock) of the next scheduled change, e.g. the idle sign of life.</summary>
    double? NextWake { get; }

    /// <summary>The highest class this client may use (thumbnails 15 fps, style cards 30 fps).</summary>
    FrameClass Cap { get; }

    void Render(double now);
}

/// <summary>
/// One frame clock for every orb (DESIGN 2a, Frame policy): one tick source while anything animates — the compositor clock
/// thread of the Direct3D orbs (<see cref="CompositorTicker"/>), or a single <see cref="CompositionTarget.Rendering"/>
/// subscription when the WPF renderer is in use — each client rendering on every k-th display tick for its frame class
/// (72 fps and 28.8 fps on a 144 Hz display), a single wake-up timer for scheduled changes, and nothing at all when every orb
/// is static. Four or more orbs in the 60 fps class drop to 30 fps except the focused one.
/// </summary>
internal sealed class OrbClock
{
    private readonly List<IOrbFrameClient> _clients = [];
    private readonly HashSet<IOrbFrameClient> _kicked = [];
    private readonly Func<double> _now;
    private readonly DispatcherTimer _wake;
    private bool _hooked;
    private long _tick;
    private TimeSpan _lastRenderingTime;
    private double _refreshHz = 60;
    private double _monitorHz = 60;
    private long _failedFrames;
    private long _lastFailureLog;
    private bool _rendering;
    private readonly double[] _intervals = new double[48];
    private int _intervalCount;
    private int _intervalIndex;
    private TimeSpan _previousTick;
    private CompositorTicker? _ticker;
    private long _lastTick = -1;

    public OrbClock(Dispatcher dispatcher, Func<double> now)
    {
        _now = now;
        _wake = new DispatcherTimer(DispatcherPriority.Render, dispatcher);
        _wake.Tick += (_, _) =>
        {
            _wake.Stop();
            var at = _now();
            foreach (var client in _clients)
            {
                if (client.IsActive && client.NextWake is { } wake && wake <= at + 0.02)
                {
                    _kicked.Add(client);
                }
            }

            Hook();
            Reschedule();
        };
        RefreshDisplay();
    }

    /// <summary>The app's clock (set once at start-up), for orb previews hosted in other windows.</summary>
    public static OrbClock? Current { get; set; }

    /// <summary>The shared clock (seconds).</summary>
    public double Now => _now();

    /// <summary>Display refresh rate used for the frame divisors.</summary>
    public double RefreshHz => _refreshHz;

    /// <summary>Frames rendered since start (all clients), for the benchmark harness.</summary>
    public long FramesRendered { get; private set; }

    /// <summary>Display ticks seen since start, for the benchmark harness.</summary>
    public long Ticks => _tick;

    /// <summary>What paces the frames (for the log).</summary>
    public string Source => _ticker?.Source ?? "WPF rendering";

    /// <summary>
    /// Paces the orbs by the compositor clock thread instead of WPF's render loop (the Direct3D orbs draw outside WPF, so
    /// hooking <see cref="CompositionTarget.Rendering"/> would only keep WPF composing for nothing).
    /// </summary>
    public void UseCompositorTicker()
    {
        if (_ticker is not null)
        {
            return;
        }

        var hooked = _hooked;
        Unhook();
        _ticker = new CompositorTicker(OnTicker) { RefreshHz = _monitorHz };
        _refreshHz = _monitorHz;
        if (hooked)
        {
            Hook();
        }
    }

    public void Register(IOrbFrameClient client)
    {
        if (!_clients.Contains(client))
        {
            _clients.Add(client);
        }

        Kick(client);
    }

    public void Unregister(IOrbFrameClient client)
    {
        _clients.Remove(client);
        _kicked.Remove(client);
        Reschedule();
    }

    /// <summary>Asks for a frame on the next display tick (new data, hover, a configuration change).</summary>
    public void Kick(IOrbFrameClient client)
    {
        if (!_clients.Contains(client))
        {
            return;
        }

        _kicked.Add(client);
        Hook();
        _ticker?.Wake();
    }

    /// <summary>Re-reads the refresh rate of the primary display (after a display change).</summary>
    public void RefreshDisplay()
    {
        if (Monitors.Primary() is { } primary && Monitors.RefreshRate(primary) is { } hz && hz >= 24)
        {
            _monitorHz = hz;
            _refreshHz = hz;
            _intervalCount = 0;
            if (_ticker is not null)
            {
                _ticker.RefreshHz = hz;
            }
        }
    }

    /// <summary>Re-evaluates hooks and timers after a client's demand changed outside a frame.</summary>
    public void Reschedule()
    {
        if (_rendering)
        {
            return;
        }

        var needsTicks = _kicked.Count > 0;
        double? wake = null;
        var divisors = 0UL;
        foreach (var client in _clients)
        {
            if (!client.IsActive)
            {
                continue;
            }

            var effective = Effective(client);
            if (effective != FrameClass.None)
            {
                needsTicks = true;
                divisors |= 1UL << Math.Clamp(FramePolicy.Divisor(effective, _refreshHz), 1, 63);
            }
            else if (client.NextWake is { } at && (wake is null || at < wake))
            {
                wake = at;
            }
        }

        // The clock thread wakes the UI only on ticks some orb is due on (28.8 of 144 per second while cruising).
        _ticker?.SetDivisors(divisors);
        if (needsTicks)
        {
            Hook();
        }
        else
        {
            Unhook();
        }

        _wake.Stop();
        if (!needsTicks && wake is { } next)
        {
            _wake.Interval = TimeSpan.FromSeconds(Math.Clamp(next - _now(), 0.005, 3600));
            _wake.Start();
        }
    }

    private int SixtyCount()
    {
        var count = 0;
        foreach (var client in _clients)
        {
            if (client.IsActive && Capped(client, client.Demand) == FrameClass.Fps60)
            {
                count++;
            }
        }

        return count;
    }

    private static FrameClass Capped(IOrbFrameClient client, FrameClass requested) =>
        requested > client.Cap ? client.Cap : requested;

    private FrameClass Effective(IOrbFrameClient client) =>
        FramePolicy.Crowded(Capped(client, client.Demand), SixtyCount(), client.IsPreferred);

    /// <summary>
    /// The divisors apply to the rate at which WPF actually delivers display ticks, which can be lower than the monitor's
    /// refresh rate (WPF may compose every other vsync); the median of recent tick intervals estimates it.
    /// </summary>
    private void MeasureTick(TimeSpan renderingTime)
    {
        var interval = (renderingTime - _previousTick).TotalSeconds;
        _previousTick = renderingTime;
        if (interval <= 0.002 || interval > 0.05)
        {
            return;
        }

        _intervals[_intervalIndex] = interval;
        _intervalIndex = (_intervalIndex + 1) % _intervals.Length;
        _intervalCount = Math.Min(_intervalCount + 1, _intervals.Length);
        if (_intervalCount < _intervals.Length || _intervalIndex != 0)
        {
            return;
        }

        Span<double> sorted = stackalloc double[_intervals.Length];
        _intervals.CopyTo(sorted);
        sorted.Sort();
        var measured = 1 / sorted[sorted.Length / 2];
        // Snap to a whole divisor of the monitor rate (144 → 72, 48, 36); keep the monitor rate when ticks keep up.
        var divisor = Math.Max(1, (int)Math.Round(_monitorHz / measured));
        _refreshHz = _monitorHz / divisor;
    }

    private void Hook()
    {
        if (_hooked)
        {
            return;
        }

        _hooked = true;
        _wake.Stop();
        _previousTick = TimeSpan.Zero;
        if (_ticker is not null)
        {
            _ticker.Start();
            return;
        }

        CompositionTarget.Rendering += OnRendering;
    }

    private void Unhook()
    {
        if (!_hooked)
        {
            return;
        }

        _hooked = false;
        if (_ticker is not null)
        {
            _ticker.Stop();
            return;
        }

        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (e is RenderingEventArgs args)
        {
            if (args.RenderingTime == _lastRenderingTime)
            {
                return;
            }

            MeasureTick(args.RenderingTime);
            _lastRenderingTime = args.RenderingTime;
        }

        Tick(_tick + 1);
    }

    private void OnTicker(long count)
    {
        if (_hooked)
        {
            Tick(Math.Max(count, _tick + 1));
        }
    }

    private void Tick(long tick)
    {
        _lastTick = _tick;
        _tick = tick;
        var now = _now();
        var sixty = SixtyCount();
        _rendering = true;
        try
        {
            for (var i = 0; i < _clients.Count; i++)
            {
                var client = _clients[i];
                if (!client.IsActive)
                {
                    _kicked.Remove(client);
                    continue;
                }

                var frameClass = FramePolicy.Crowded(Capped(client, client.Demand), sixty, client.IsPreferred);
                var divisor = FramePolicy.Divisor(frameClass, _refreshHz);
                var due = _kicked.Remove(client) || (divisor > 0 && _tick / divisor != _lastTick / divisor);
                if (!due)
                {
                    continue;
                }

                try
                {
                    client.Render(now);
                    FramesRendered++;
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                {
                    // A failing frame repeats every tick: log the first and then at most every 30 s.
                    _failedFrames++;
                    var stamp = Environment.TickCount64;
                    if (_lastFailureLog == 0 || stamp - _lastFailureLog >= 30_000)
                    {
                        _lastFailureLog = stamp;
                        App.AppLog.Warn($"An orb frame failed ({_failedFrames} so far).", ex);
                    }
                }
            }
        }
        finally
        {
            _rendering = false;
        }

        Reschedule();
    }
}
