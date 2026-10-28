using System.Diagnostics;
using System.Windows.Threading;
using AgentSpeed.Core;
using AgentSpeed.Presentation.Hero;

namespace AgentSpeed.Services;

/// <summary>
/// Developer option <c>--hero-demo [tour|startups|idle]</c>: drives the hub (as a preview) through a hero vehicle scenario
/// of DESIGN 4c — idle, start-up, every upgrade, redline and overdrive, the downgrades and parking — on a loop.
/// </summary>
internal sealed class HeroTourDriver : IDisposable
{
    private static readonly (string Id, SourceKind Source, string Project, string Model, double Share)[] Agents =
    [
        ("tour-atlas", SourceKind.Codex, "atlas", "gpt-6.1-sol", 0.55),
        ("tour-nova", SourceKind.ClaudeCode, "nova", "claude-opus-5-5", 0.3),
        ("tour-quasar", SourceKind.DeepSeek, "quasar", "deepseek-flash", 0.15),
    ];

    private readonly SpeedHub _hub;
    private readonly HeroTourScenario _scenario;
    private readonly Func<double> _redline;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private readonly List<HistoryPoint> _history = [];
    private double _peak;

    public HeroTourDriver(SpeedHub hub, Dispatcher dispatcher, HeroTourScenario scenario, Func<double> redline)
    {
        _hub = hub;
        _scenario = scenario;
        _redline = redline;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, (_, _) => Publish(), dispatcher);
        _timer.Stop();
    }

    public void Start()
    {
        _hub.BeginPreview();
        _clock.Restart();
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
        var speed = HeroTour.Value(_scenario, _clock.Elapsed.TotalSeconds, _redline());
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

        var active = speed >= 0.5;
        var agents = Agents.Select(a =>
        {
            var live = active ? a.Share * speed : 0;
            return new AgentStatus(a.Id, a.Source, a.Project, a.Model, active ? AgentPhase.Generating : AgentPhase.Idle, false,
                new Rate(live, live * 24), live > 0 ? live : null, now);
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
            null,
            null,
            today,
            sources,
            agents,
            agents.Count(a => a.Phase == AgentPhase.Generating),
            0,
            _history.ToArray(),
            null,
            []));
    }
}
