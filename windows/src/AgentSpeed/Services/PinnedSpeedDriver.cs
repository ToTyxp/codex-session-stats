using System.Windows.Threading;
using AgentSpeed.Core;

namespace AgentSpeed.Services;

/// <summary>
/// Developer option <c>--demo-speed 24,96,400</c>: holds the hub (as a preview) at each speed in turn for a few seconds,
/// with a small fixed set of agents in every phase, so speed titles and the zero-speed glyphs can be inspected.
/// </summary>
internal sealed class PinnedSpeedDriver : IDisposable
{
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(3.5);

    private static readonly (string Id, SourceKind Source, string Project, string Model, AgentPhase Phase, double Share)[] Agents =
    [
        ("pinned-atlas", SourceKind.Codex, "atlas", "gpt-6.1-sol", AgentPhase.Generating, 0.6),
        ("pinned-quasar", SourceKind.DeepSeek, "quasar", "deepseek-flash", AgentPhase.Generating, 0.4),
        ("pinned-nova", SourceKind.ClaudeCode, "nova", "claude-opus-5-5", AgentPhase.Tools, 0),
        ("pinned-orbit", SourceKind.ClaudeDesktop, "orbit", "claude-opus-5-5", AgentPhase.Idle, 0),
    ];

    private readonly SpeedHub _hub;
    private readonly IReadOnlyList<double> _speeds;
    private readonly DispatcherTimer _timer;
    private readonly List<HistoryPoint> _history = [];
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;
    private double _peak;

    public PinnedSpeedDriver(SpeedHub hub, Dispatcher dispatcher, IReadOnlyList<double> speeds)
    {
        _hub = hub;
        _speeds = speeds;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Publish(), dispatcher);
        _timer.Stop();
    }

    public void Start()
    {
        if (_speeds.Count == 0)
        {
            return;
        }

        _hub.BeginPreview();
        Publish();
        _timer.Start();
    }

    public void Dispose()
    {
        if (_timer.IsEnabled)
        {
            _timer.Stop();
            _hub.EndPreview();
        }
    }

    private void Publish()
    {
        var now = DateTimeOffset.Now;
        var index = (int)((now - _startedAt).Ticks / Step.Ticks % _speeds.Count);
        var speed = _speeds[index];
        _peak = Math.Max(_peak, speed);

        var second = new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, now.Offset);
        if (_history.Count == 0 || _history[^1].Time < second)
        {
            _history.Add(new HistoryPoint(second, new Rate(speed, speed * 24), Rate.Zero));
            if (_history.Count > 300)
            {
                _history.RemoveAt(0);
            }
        }

        var agents = Agents.Select(a =>
        {
            var live = a.Share * speed;
            return new AgentStatus(a.Id, a.Source, a.Project, a.Model, a.Phase, false, new Rate(live, live * 24),
                live > 0 ? live : null, now);
        }).ToArray();
        var today = new TokenTotals(120, 4_800_000, 4_100_000, 0, 96_000, 0);
        var sources = Agents.Select(a => a.Source).Distinct().Select(kind =>
        {
            var live = agents.Where(a => a.Source == kind).Sum(a => a.Live.Output);
            return new SourceStatus(kind, true, string.Empty, true, 3, new Rate(live, live * 24), today,
                agents.Count(a => a.Source == kind && a.Phase == AgentPhase.Generating));
        }).ToArray();

        _hub.PublishPreview(new SpeedSnapshot(
            now,
            MonitorState.Running,
            new Rate(speed, speed * 24),
            0,
            new Rate(_peak, _peak * 24),
            _peak > 0 ? _startedAt : null,
            _peak > 0 ? _peak * 0.6 : null,
            today,
            sources,
            agents,
            agents.Count(a => a.Phase == AgentPhase.Generating),
            agents.Count(a => a.Phase == AgentPhase.Tools),
            _history.ToArray(),
            null,
            []));
    }
}
