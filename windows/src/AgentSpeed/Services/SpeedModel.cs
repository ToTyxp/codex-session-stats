using System.Diagnostics;
using System.Windows.Threading;
using AgentSpeed.Core;
using AgentSpeed.Presentation.Heat;

namespace AgentSpeed.Services;

/// <summary>
/// UI-thread view of the hub: the selected metric's live value, the hysteresis-filtered effect tier that drives every
/// effect, and the speed title every surface shows as its label.
/// </summary>
internal sealed class SpeedModel : IDisposable
{
    private readonly SpeedHub _hub;
    private readonly SpeedPresenter _presenter = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _deadline;

    public SpeedModel(SpeedHub hub, Dispatcher dispatcher)
    {
        _hub = hub;
        _deadline = new DispatcherTimer(DispatcherPriority.Normal, dispatcher);
        _deadline.Tick += (_, _) =>
        {
            _deadline.Stop();
            if (_presenter.Tick(Now))
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }

            ScheduleDeadline();
        };

        _hub.SnapshotChanged += OnSnapshot;
        _hub.EventRaised += OnEvent;
    }

    public event EventHandler? Changed;

    public event EventHandler<SpeedEvent>? EventRaised;

    /// <summary>Monotonic seconds shared by every animation.</summary>
    public double Now => _clock.Elapsed.TotalSeconds;

    public SpeedSnapshot Snapshot => _presenter.Snapshot;

    public double Value => _presenter.Value;

    public SpeedTier Tier => _presenter.Tier;

    public SpeedTitle Title => _presenter.Title;

    public double Redline => _presenter.Redline;

    public SpeedMetric Metric => _presenter.Metric;

    public bool IsPreviewing => _hub.IsPreviewing;

    public double ValueFor(Rate rate) => _presenter.ValueFor(rate);

    public void Configure(SpeedMetric metric, double redline)
    {
        _presenter.Configure(metric, redline, Now);
        Changed?.Invoke(this, EventArgs.Empty);
        ScheduleDeadline();
    }

    public void Dispose()
    {
        _deadline.Stop();
        _hub.SnapshotChanged -= OnSnapshot;
        _hub.EventRaised -= OnEvent;
    }

    private void OnSnapshot(object? sender, SpeedSnapshot snapshot)
    {
        _presenter.Update(snapshot, Now);
        Changed?.Invoke(this, EventArgs.Empty);
        ScheduleDeadline();
    }

    private void OnEvent(object? sender, SpeedEvent speedEvent) => EventRaised?.Invoke(this, speedEvent);

    private void ScheduleDeadline()
    {
        _deadline.Stop();
        if (_presenter.NextDeadline is not { } deadline)
        {
            return;
        }

        var wait = Math.Max(0.02, deadline - Now + 0.01);
        _deadline.Interval = TimeSpan.FromSeconds(wait);
        _deadline.Start();
    }
}
