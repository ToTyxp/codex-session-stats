using System.Windows.Media;
using System.Windows.Threading;
using AgentSpeed.Presentation.Hero;
using AgentSpeed.Presentation.Orb;

namespace AgentSpeed.Orb;

/// <summary>
/// Drives animation frames: display-synced (CompositionTarget.Rendering) while something animates, a 10 fps (or
/// 15 fps) timer for slow ambient effects, a single wake-up for scheduled ones, and nothing at all otherwise.
/// </summary>
internal sealed class FrameDriver
{
    private static readonly TimeSpan SlowInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MediumInterval = TimeSpan.FromSeconds(1 / HeroStage.ParkedFrameRate);

    private readonly Action _frame;
    private readonly Func<FrameDemand> _demand;
    private readonly Func<double?> _nextWake;
    private readonly Func<double> _now;
    private readonly DispatcherTimer _slow;
    private readonly DispatcherTimer _wake;
    private bool _hooked;
    private bool _enabled = true;
    private TimeSpan _lastRenderingTime;

    public FrameDriver(Dispatcher dispatcher, Action frame, Func<FrameDemand> demand, Func<double?> nextWake, Func<double> now)
    {
        _frame = frame;
        _demand = demand;
        _nextWake = nextWake;
        _now = now;
        _slow = new DispatcherTimer(SlowInterval, DispatcherPriority.Render, (_, _) => Step(), dispatcher);
        _slow.Stop();
        _wake = new DispatcherTimer(DispatcherPriority.Render, dispatcher);
        _wake.Tick += (_, _) =>
        {
            _wake.Stop();
            Step();
        };
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            if (value)
            {
                Kick();
            }
            else
            {
                StopAll();
            }
        }
    }

    /// <summary>Renders a frame now and keeps going for as long as the scene asks.</summary>
    public void Kick()
    {
        if (!_enabled)
        {
            return;
        }

        if (_hooked)
        {
            return;
        }

        Step();
    }

    public void StopAll()
    {
        Unhook();
        _slow.Stop();
        _wake.Stop();
    }

    private void Step()
    {
        if (!_enabled)
        {
            StopAll();
            return;
        }

        _frame();
        Schedule();
    }

    private void Schedule()
    {
        var demand = _demand();
        switch (demand)
        {
            case FrameDemand.Full:
                _slow.Stop();
                _wake.Stop();
                Hook();
                break;
            case FrameDemand.Slow or FrameDemand.Medium:
                Unhook();
                _wake.Stop();
                var interval = demand == FrameDemand.Medium ? MediumInterval : SlowInterval;
                if (_slow.Interval != interval)
                {
                    _slow.Interval = interval;
                }

                if (!_slow.IsEnabled)
                {
                    _slow.Start();
                }

                break;
            default:
                Unhook();
                _slow.Stop();
                if (_nextWake() is { } at)
                {
                    var delay = Math.Clamp(at - _now(), 0.016, 3600);
                    _wake.Stop();
                    _wake.Interval = TimeSpan.FromSeconds(delay);
                    _wake.Start();
                }
                else
                {
                    _wake.Stop();
                }

                break;
        }
    }

    private void Hook()
    {
        if (_hooked)
        {
            return;
        }

        _hooked = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void Unhook()
    {
        if (!_hooked)
        {
            return;
        }

        _hooked = false;
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

            _lastRenderingTime = args.RenderingTime;
        }

        if (!_enabled)
        {
            StopAll();
            return;
        }

        _frame();
        if (_demand() != FrameDemand.Full)
        {
            Schedule();
        }
    }
}
