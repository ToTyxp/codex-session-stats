using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AgentSpeed.Controls;
using AgentSpeed.Core;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Cost;
using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Layout;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Settings;
using AgentSpeed.Presentation.Titles;
using AgentSpeed.Theme;

namespace AgentSpeed.Dashboard;

internal sealed class SourceRow : ObservableObject
{
    private string _name = string.Empty;
    private string _liveText = "0";
    private string _todayText = string.Empty;
    private string _status = string.Empty;
    private bool _dimmed;
    private bool _showSettingsLink;
    private bool _isListed;
    private string _accessibleName = string.Empty;
    private LiveValueKind _liveKind = LiveValueKind.Dash;
    private string _liveAccessibleName = string.Empty;

    public SourceRow(SourceKind kind)
    {
        Kind = kind;
        DotBrush = MediaColors.Frozen(HeatPalette.SourceColor(kind).ToColor());
    }

    public SourceKind Kind { get; }

    public Brush DotBrush { get; }

    public string Name { get => _name; set => Set(ref _name, value); }

    /// <summary>The last measurable speed; kept while a dash shows so the digits can fade out.</summary>
    public string LiveText { get => _liveText; set => Set(ref _liveText, value); }

    public LiveValueKind LiveKind { get => _liveKind; set => Set(ref _liveKind, value); }

    public string LiveAccessibleName { get => _liveAccessibleName; set => Set(ref _liveAccessibleName, value); }

    public string TodayText { get => _todayText; set => Set(ref _todayText, value); }

    public string Status { get => _status; set => Set(ref _status, value); }

    public bool IsDimmed { get => _dimmed; set => Set(ref _dimmed, value); }

    public bool ShowSettingsLink
    {
        get => _showSettingsLink;
        set
        {
            if (Set(ref _showSettingsLink, value))
            {
                OnPropertyChanged(nameof(ShowChevron));
            }
        }
    }

    /// <summary>The trailing chevron appears on hover unless the settings link takes the trailing edge.</summary>
    public bool ShowChevron => !_showSettingsLink;

    /// <summary>Name, today's output and live speed (or the status) for screen readers.</summary>
    public string AccessibleName { get => _accessibleName; set => Set(ref _accessibleName, value); }

    /// <summary>Only detected sources are listed on the dashboard.</summary>
    public bool IsListed { get => _isListed; set => Set(ref _isListed, value); }
}

internal sealed class AgentRow : ObservableObject
{
    private string _project = string.Empty;
    private string _detail = string.Empty;
    private AgentPhase _phase = AgentPhase.Idle;
    private Color _color = Colors.Gray;
    private string _liveText = string.Empty;
    private Thickness _indent;
    private TreeGuides _guides;
    private string _accessibleName = string.Empty;
    private LiveValueKind _liveKind = LiveValueKind.Dash;
    private string _liveAccessibleName = string.Empty;

    public string Project { get => _project; set => Set(ref _project, value); }

    public string Detail { get => _detail; set => Set(ref _detail, value); }

    public AgentPhase Phase { get => _phase; set => Set(ref _phase, value); }

    public Color Color { get => _color; set => Set(ref _color, value); }

    /// <summary>The last measurable speed; kept while the dash or hourglass shows so the digits can fade out.</summary>
    public string LiveText { get => _liveText; set => Set(ref _liveText, value); }

    public LiveValueKind LiveKind { get => _liveKind; set => Set(ref _liveKind, value); }

    /// <summary>The speed with its unit, or the phase name when no number is shown.</summary>
    public string LiveAccessibleName { get => _liveAccessibleName; set => Set(ref _liveAccessibleName, value); }

    /// <summary>Left margin for the row's depth in the agent tree.</summary>
    public Thickness Indent { get => _indent; set => Set(ref _indent, value); }

    public TreeGuides Guides { get => _guides; set => Set(ref _guides, value); }

    public string AccessibleName { get => _accessibleName; set => Set(ref _accessibleName, value); }

    /// <summary><c>traffic.agent.tooltip</c> while the agent has attributed traffic today (DESIGN 10); null otherwise.</summary>
    public string? TrafficTooltip { get => _trafficTooltip; set => Set(ref _trafficTooltip, value); }

    private string? _trafficTooltip;
}

/// <summary>One source of today's cost in the ⓘ popup: colour dot, name, amount and a thin share bar.</summary>
internal sealed record TodayCostRow(Brush DotBrush, string Name, string AmountText, double Share, string AccessibleName);

/// <summary>Text and rows for the dashboard, refreshed from snapshots (≤ 4 Hz while visible).</summary>
internal sealed partial class DashboardViewModel : ObservableObject
{
    /// <summary>Fixed agent row height, so the scrolling viewport is exactly a whole number of rows.</summary>
    public const double AgentRowHeight = 40;

    private string _valueText = "0";
    private string _unitText = "tok/s";
    private string _titleText = string.Empty;
    private Brush _titleBrush = Brushes.Gray;
    private string _contextText = string.Empty;
    private bool _isLoading;
    private bool _isNoSources;
    private bool _isRunning = true;
    private double _loadingProgress;
    private bool _hasAgents;
    private bool _hasListedSources;
    private bool _agentsExpanded;
    private bool _holdAllAgents;
    private bool _canToggleAgents;
    private string _agentsToggleText = string.Empty;
    private string _agentsToggleAccessibleName = string.Empty;
    private double _agentsViewportHeight;
    private double _collapsedAgentsHeight;
    private int _collapsedAgentCount;
    private bool _agentsScroll;
    private IReadOnlyList<AgentStatus> _lastAgents = [];
    private SpeedMetric _lastMetric;
    private Localizer? _lastLoc;
    private string _outputText = "0";
    private string _inputText = "0";
    private string _requestsText = "0";
    private string _averageText = "—";
    private string _peakText = "0";
    private string _peakTimeText = string.Empty;
    private string _warningText = string.Empty;
    private bool _hasEstimate;
    private bool _reduceMotion;
    private string _heroAccessibleName = string.Empty;

    public DashboardViewModel()
    {
        foreach (var kind in Enum.GetValues<SourceKind>())
        {
            Sources.Add(new SourceRow(kind));
        }
    }

    public ObservableCollection<SourceRow> Sources { get; } = new();

    public ObservableCollection<AgentRow> Agents { get; } = new();

    public string ValueText { get => _valueText; private set => Set(ref _valueText, value); }

    public string UnitText { get => _unitText; private set => Set(ref _unitText, value); }

    public string TitleText { get => _titleText; private set => Set(ref _titleText, value); }

    public Brush TitleBrush { get => _titleBrush; private set => Set(ref _titleBrush, value); }

    public string ContextText { get => _contextText; private set => Set(ref _contextText, value); }

    public bool IsLoading { get => _isLoading; private set => Set(ref _isLoading, value); }

    public bool IsNoSources { get => _isNoSources; private set => Set(ref _isNoSources, value); }

    public bool IsRunning { get => _isRunning; private set => Set(ref _isRunning, value); }

    public double LoadingProgress { get => _loadingProgress; private set => Set(ref _loadingProgress, value); }

    public bool HasAgents { get => _hasAgents; private set => Set(ref _hasAgents, value); }

    /// <summary>At least one source produced tokens today (DESIGN section 4, item 3).</summary>
    public bool HasListedSources { get => _hasListedSources; private set => Set(ref _hasListedSources, value); }

    /// <summary>The remembered choice to show every agent (DESIGN section 4, item 4).</summary>
    public bool AgentsExpanded
    {
        get => _agentsExpanded;
        set
        {
            if (Set(ref _agentsExpanded, value))
            {
                RefreshAgents();
            }
        }
    }

    /// <summary>Keeps every row while the list animates closed; the collapsed rows replace them when it settles.</summary>
    public bool HoldAllAgents
    {
        get => _holdAllAgents;
        set
        {
            if (Set(ref _holdAllAgents, value))
            {
                RefreshAgents();
            }
        }
    }

    /// <summary>More agents exist than the collapsed list shows, so the disclosure row is offered.</summary>
    public bool CanToggleAgents { get => _canToggleAgents; private set => Set(ref _canToggleAgents, value); }

    /// <summary><c>+4 個 agent</c> while collapsed, <c>收合</c> while expanded.</summary>
    public string AgentsToggleText { get => _agentsToggleText; private set => Set(ref _agentsToggleText, value); }

    public string AgentsToggleAccessibleName
    {
        get => _agentsToggleAccessibleName;
        private set => Set(ref _agentsToggleAccessibleName, value);
    }

    /// <summary>Height of the agents viewport for the rows shown: up to 12 rows, beyond that the list scrolls.</summary>
    public double AgentsViewportHeight { get => _agentsViewportHeight; private set => Set(ref _agentsViewportHeight, value); }

    /// <summary>Height the viewport settles at once collapsed, the target of the closing animation.</summary>
    public double CollapsedAgentsHeight { get => _collapsedAgentsHeight; private set => Set(ref _collapsedAgentsHeight, value); }

    /// <summary>Rows the collapsed list shows; the rows after them are the ones expanding reveals.</summary>
    public int CollapsedAgentCount { get => _collapsedAgentCount; private set => Set(ref _collapsedAgentCount, value); }

    /// <summary>The shown rows exceed the viewport.</summary>
    public bool AgentsScroll { get => _agentsScroll; private set => Set(ref _agentsScroll, value); }

    public string OutputText { get => _outputText; private set => Set(ref _outputText, value); }

    public string InputText { get => _inputText; private set => Set(ref _inputText, value); }

    public string RequestsText { get => _requestsText; private set => Set(ref _requestsText, value); }

    public string AverageText { get => _averageText; private set => Set(ref _averageText, value); }

    public string PeakText { get => _peakText; private set => Set(ref _peakText, value); }

    public string PeakTimeText { get => _peakTimeText; private set => Set(ref _peakTimeText, value); }

    public string WarningText { get => _warningText; private set => Set(ref _warningText, value); }

    public bool HasWarning => !string.IsNullOrEmpty(_warningText);

    public bool HasEstimate { get => _hasEstimate; private set => Set(ref _hasEstimate, value); }

    public bool ReduceMotion { get => _reduceMotion; set => Set(ref _reduceMotion, value); }

    public string HeroAccessibleName { get => _heroAccessibleName; private set => Set(ref _heroAccessibleName, value); }

    /// <summary>The General title theme the hero's title reads in (DESIGN 1, Where the theme applies).</summary>
    public TitleTheme TitleTheme { get; set; } = TitleTheme.Vehicles;

    /// <summary>The level of <see cref="TitleText"/>: a new level slides, a new text on the same level crossfades.</summary>
    public int TitleLevelIndex { get => _titleLevel; private set => Set(ref _titleLevel, value); }

    /// <summary>Title changes slide (motion allowed); otherwise they keep the stagger as a fade.</summary>
    public bool TitleSlides { get => _titleSlides; set => Set(ref _titleSlides, value); }

    private int _titleLevel;
    private bool _titleSlides = true;
    private bool _heroIsMoney;
    private bool _heroAtLeast;
    private bool _heroMuted;
    private string _todayCostText = string.Empty;
    private string _todayCostAccessibleName = string.Empty;
    private IReadOnlyList<TodayCostRow> _todayCostRows = [];
    private string? _todayCostUnpriced;
    private string? _todayCostExcluded;
    private string? _todayCostRateLine;
    private string _currencyCode = "USD";

    /// <summary>Formats every money value (DESIGN 9); null shows no cost.</summary>
    public CostDisplay? Cost { get; set; }

    /// <summary><c>settings.liveUnit</c>: the hero and the source rows show tok/s or money.</summary>
    public LiveUnit LiveUnit { get; set; }

    /// <summary>The hero shows the money unit (its value and unit slide when this changes).</summary>
    public bool HeroIsMoney { get => _heroIsMoney; private set => Set(ref _heroIsMoney, value); }

    /// <summary>Part of the live money value has no known price: a small <c>≥</c> precedes it.</summary>
    public bool HeroAtLeast { get => _heroAtLeast; private set => Set(ref _heroAtLeast, value); }

    /// <summary>Nothing is priced: the hero's <c>–</c> reads in the secondary color.</summary>
    public bool HeroMuted { get => _heroMuted; private set => Set(ref _heroMuted, value); }

    /// <summary>今日費用: today's billable cost (amount format, <c>≥</c> / <c>–</c> as needed).</summary>
    public string TodayCostText { get => _todayCostText; private set => Set(ref _todayCostText, value); }

    public string TodayCostAccessibleName { get => _todayCostAccessibleName; private set => Set(ref _todayCostAccessibleName, value); }

    public IReadOnlyList<TodayCostRow> TodayCostRows { get => _todayCostRows; private set => Set(ref _todayCostRows, value); }

    public string? TodayCostUnpriced { get => _todayCostUnpriced; private set => Set(ref _todayCostUnpriced, value); }

    public string? TodayCostExcluded { get => _todayCostExcluded; private set => Set(ref _todayCostExcluded, value); }

    /// <summary>The display currency; money values crossfade when it changes (DESIGN 9 Currency).</summary>
    public string CurrencyCode { get => _currencyCode; private set => Set(ref _currencyCode, value); }

    /// <summary>The rate line (<c>rates.status</c>) for a non-USD currency.</summary>
    public string? TodayCostRateLine { get => _todayCostRateLine; private set => Set(ref _todayCostRateLine, value); }

    /// <param name="title">The speed title shown under the hero value.</param>
    public void Update(SpeedSnapshot snapshot, double value, SpeedTitle title, double redline, SpeedMetric metric,
        Localizer loc, bool darkTheme)
    {
        var unit = loc["unit.tokS"];
        ValueText = SpeedFormat.Speed(value);
        UnitText = unit;
        TitleLevelIndex = (int)title;
        TitleText = TitleTheme.Label(title, loc);
        var heat = HeatMath.Heat(value, redline);
        var titleColor = title == SpeedTitle.Idle
            ? HeatPalette.Stops[0].Color
            : darkTheme ? HeatPalette.Sample(Math.Max(heat, 0.08)) : OrbTokens.Light.TierLabel(Math.Max(heat, 0.08));
        TitleBrush = MediaColors.Frozen(titleColor.ToColor());
        ContextText = DisplayText.AgentContext(snapshot, loc);
        HeroAccessibleName = DisplayText.Accessibility(value, TitleTheme.Text(title, loc), loc);
        var money = LiveUnit == LiveUnit.Cost ? Cost?.Live(snapshot) : null;
        HeroIsMoney = money is not null;
        HeroAtLeast = money?.AtLeast ?? false;
        HeroMuted = money?.Unknown ?? false;
        if (money is not null)
        {
            // The money unit (DESIGN 9): the billable live cost rate; heat and title keep following tokens.
            ValueText = money.Number;
            UnitText = money.Unit;
            HeroAccessibleName = Cost!.LiveAccessibility(money, TitleTheme.Text(title, loc));
        }

        UpdateTodayCost(snapshot, loc);

        IsLoading = snapshot.State.Kind == MonitorStateKind.Starting;
        LoadingProgress = Math.Clamp(snapshot.State.Progress, 0, 1);
        IsNoSources = snapshot.State.Kind == MonitorStateKind.NoSources;
        IsRunning = snapshot.State.Kind == MonitorStateKind.Running;
        HasEstimate = snapshot.LiveEstimatedShare > 0.001 || snapshot.History.Count > 0 && snapshot.History[^1].Estimated.ValueFor(metric) > 0.05;

        UpdateSources(snapshot, metric, loc, unit);
        _lastAgents = snapshot.Agents;
        _lastMetric = metric;
        _lastLoc = loc;
        RefreshAgents();

        var today = snapshot.Today;
        OutputText = SpeedFormat.Tokens(today.Output);
        InputText = SpeedFormat.Tokens(today.Input);
        RequestsText = today.Requests.ToString("N0", loc.Culture);
        AverageText = snapshot.AverageToday is { } average ? $"{SpeedFormat.Speed(average)} {unit}" : "—";
        var peak = snapshot.PeakToday.ValueFor(metric);
        PeakText = $"{SpeedFormat.Speed(peak)} {unit}";
        PeakTimeText = snapshot.PeakTodayAt is { } at && peak > 0
            ? at.ToLocalTime().ToString("t", loc.Culture)
            : string.Empty;

        var warnings = snapshot.Warnings.Select(w => DisplayText.Warning(w, loc)).Where(t => !string.IsNullOrEmpty(t)).ToArray();
        var warning = string.Join(Environment.NewLine, warnings);
        if (Set(ref _warningText, warning, nameof(WarningText)))
        {
            OnPropertyChanged(nameof(HasWarning));
        }
    }

    private void UpdateSources(SpeedSnapshot snapshot, SpeedMetric metric, Localizer loc, string unit)
    {
        foreach (var row in Sources)
        {
            row.Name = loc[DisplayText.SourceKey(row.Kind)];
            var status = snapshot.Sources.FirstOrDefault(s => s.Kind == row.Kind);
            if (status is null)
            {
                row.LiveKind = LiveValueKind.Dash;
                row.LiveAccessibleName = loc["phase.idle"];
                row.TodayText = string.Empty;
                row.Status = string.Empty;
                row.IsDimmed = false;
                row.ShowSettingsLink = false;
                row.IsListed = false;
                row.AccessibleName = row.Name;
                continue;
            }

            row.IsListed = SourceDetection.ProducedToday(status);

            var live = status.Live.ValueFor(metric);
            if (DisplayText.IsNegligibleSpeed(live))
            {
                row.LiveKind = LiveValueKind.Dash;
                row.LiveAccessibleName = loc["phase.idle"];
            }
            else
            {
                row.LiveText = $"{SpeedFormat.Speed(live)} {unit}";
                row.LiveKind = LiveValueKind.Number;
                row.LiveAccessibleName = row.LiveText;
            }

            var costText = string.Empty;
            var costSpoken = string.Empty;
            if (Cost is { } cost)
            {
                (costText, costSpoken) = SourceCost.Today(status, cost);
                if (LiveUnit == LiveUnit.Cost)
                {
                    if (SourceCost.Live(status, cost) is { } money)
                    {
                        row.LiveText = $"{money.NumberText} {money.Unit}";
                        row.LiveKind = LiveValueKind.Number;
                        row.LiveAccessibleName = money.SpokenAmount;
                    }
                    else
                    {
                        row.LiveKind = LiveValueKind.Dash;
                        row.LiveAccessibleName = loc["phase.idle"];
                    }
                }
            }

            row.TodayText = costText.Length > 0
                ? $"{SpeedFormat.Tokens(status.Today.Output)} · {costText}"
                : SpeedFormat.Tokens(status.Today.Output);
            if (!status.Enabled)
            {
                row.Status = loc["source.disabled"];
                row.IsDimmed = true;
                row.ShowSettingsLink = true;
            }
            else if (!status.Available)
            {
                row.Status = loc["source.notFound"];
                row.IsDimmed = true;
                row.ShowSettingsLink = true;
            }
            else
            {
                row.Status = string.Empty;
                row.IsDimmed = false;
                row.ShowSettingsLink = false;
            }

            row.AccessibleName = row.IsDimmed
                ? $"{row.Name}, {row.Status}"
                : costSpoken.Length > 0
                    ? $"{row.Name}, {loc["today.output"]} {SpeedFormat.Tokens(status.Today.Output)}, {loc["today.cost"]} {costSpoken}, {row.LiveAccessibleName}"
                    : $"{row.Name}, {loc["today.output"]} {row.TodayText}, {row.LiveAccessibleName}";
        }

        HasListedSources = Sources.Any(r => r.IsListed);
    }

    private void UpdateTodayCost(SpeedSnapshot snapshot, Localizer loc)
    {
        if (Cost is not { } cost)
        {
            return;
        }

        CurrencyCode = cost.Currency.Code;
        var summary = TodayCostSummary.From(snapshot, cost);
        TodayCostText = summary.AmountText;
        TodayCostAccessibleName = $"{loc["today.cost"]} ({loc["cost.apiEquivalent"]}) {summary.Spoken}";
        var rows = summary.Parts
            .Select(p =>
            {
                var name = loc[DisplayText.SourceKey(p.Source)];
                return new TodayCostRow(Sources.First(r => r.Kind == p.Source).DotBrush, name, p.AmountText, p.Share,
                    $"{name}, {cost.SpokenTally(p.Cost, p.Requests)}");
            })
            .ToList();
        if (!rows.SequenceEqual(TodayCostRows))
        {
            TodayCostRows = rows;
        }

        TodayCostUnpriced = summary.UnpricedText;
        TodayCostExcluded = summary.ExcludedText;
        TodayCostRateLine = summary.RateLine;
    }

    private void RefreshAgents()
    {
        if (_lastLoc is { } loc)
        {
            UpdateAgents(_lastAgents, _lastMetric, loc);
        }
    }

    private void UpdateAgents(IReadOnlyList<AgentStatus> agents, SpeedMetric metric, Localizer loc)
    {
        var list = AgentList.Arrange(agents);
        var showAll = _agentsExpanded || _holdAllAgents;
        var rows = list.For(showAll).Rows;
        while (Agents.Count > rows.Count)
        {
            Agents.RemoveAt(Agents.Count - 1);
        }

        while (Agents.Count < rows.Count)
        {
            Agents.Add(new AgentRow());
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var agent = rows[i].Agent;
            var row = Agents[i];
            var source = loc[DisplayText.SourceKey(agent.Source)];
            row.Project = string.IsNullOrWhiteSpace(agent.Project) ? source : agent.Project;
            var detail = string.IsNullOrWhiteSpace(agent.Model) ? source : $"{source} · {agent.Model}";
            detail = agent.IsSubagent ? $"{loc["agents.subagent"]} · {detail}" : detail;
            detail += " · " + ExecutionModeText.Summary(agent.Modes, loc);
            if (AgentTraffic(agent.Id) is { } traffic)
            {
                // DESIGN 10: the agent's attributed download today ends its secondary line; the tooltip has both directions.
                var text = new TrafficText(loc, _trafficBits);
                row.Detail = $"{detail} · ↓ {Core.Traffic.TrafficFormat.Total(traffic.Rx)}";
                row.TrafficTooltip = loc.Format("traffic.agent.tooltip", text.Pair(traffic));
            }
            else
            {
                row.Detail = detail;
                row.TrafficTooltip = null;
            }

            var modeDetail = ExecutionModeText.Detail(agent.Modes, loc);
            row.TrafficTooltip = row.TrafficTooltip is { } trafficTip ? modeDetail + Environment.NewLine + trafficTip : modeDetail;

            row.Phase = agent.Phase;
            row.Color = HeatPalette.SourceColor(agent.Source).ToColor();
            var live = agent.Live.ValueFor(metric);
            var phase = loc[DisplayText.PhaseKey(agent.Phase)];
            if (DisplayText.IsNegligibleSpeed(live))
            {
                row.LiveKind = agent.Phase == AgentPhase.Tools ? LiveValueKind.Waiting : LiveValueKind.Dash;
                row.LiveAccessibleName = phase;
            }
            else
            {
                row.LiveText = SpeedFormat.Speed(live);
                row.LiveKind = LiveValueKind.Number;
                row.LiveAccessibleName = $"{row.LiveText} {loc["unit.tokS"]}";
            }

            row.Indent = new Thickness(rows[i].Indent * TreeConnectors.LevelWidth, 0, 0, 0);
            row.Guides = rows[i].Guides;
            row.AccessibleName = row.LiveKind == LiveValueKind.Number
                ? string.Create(CultureInfo.InvariantCulture, $"{row.Project}, {row.Detail}, {phase}, {row.LiveAccessibleName}")
                : string.Create(CultureInfo.InvariantCulture, $"{row.Project}, {row.Detail}, {phase}");
        }

        HasAgents = rows.Count > 0;
        CanToggleAgents = list.CanExpand;
        AgentsToggleText = _agentsExpanded ? loc["agents.collapse"] : DisplayText.ExpandAgents(list.Collapsed.Hidden, loc);
        AgentsToggleAccessibleName = _agentsExpanded ? AgentsToggleText : DisplayText.ExpandAgentsAccessibility(list.Total, loc);
        CollapsedAgentCount = list.Collapsed.Rows.Count;
        AgentsViewportHeight = list.ViewportHeightFor(showAll, AgentRowHeight);
        CollapsedAgentsHeight = list.ViewportHeightFor(expanded: false, AgentRowHeight);
        AgentsScroll = showAll && list.ExpandedScrolls;
    }
}
