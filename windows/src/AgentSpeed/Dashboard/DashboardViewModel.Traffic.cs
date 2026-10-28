using System.Collections.ObjectModel;
using System.Windows.Media;
using AgentSpeed.Controls;
using AgentSpeed.Core;
using AgentSpeed.Core.Traffic;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Theme;

namespace AgentSpeed.Dashboard;

/// <summary>A source row of the Network section: dot, name, today's pair and the live rates as two stacked lines.</summary>
internal sealed class TrafficRowItem : ObservableObject
{
    private string _name = string.Empty;
    private string _todayText = string.Empty;
    private TrafficFormat.Parts? _rx;
    private TrafficFormat.Parts? _tx;
    private bool _bits;
    private string _accessibleName = string.Empty;

    public TrafficRowItem(SourceKind source)
    {
        Source = source;
        DotBrush = MediaColors.Frozen(MediaColors.ToColor(HeatPalette.SourceColor(source)));
    }

    public SourceKind Source { get; }

    public Brush DotBrush { get; }

    public string Name { get => _name; set => Set(ref _name, value); }

    public string TodayText { get => _todayText; set => Set(ref _todayText, value); }

    public TrafficFormat.Parts? Rx { get => _rx; set => Set(ref _rx, value); }

    public TrafficFormat.Parts? Tx { get => _tx; set => Set(ref _tx, value); }

    public bool Bits { get => _bits; set => Set(ref _bits, value); }

    public string AccessibleName { get => _accessibleName; set => Set(ref _accessibleName, value); }
}

internal sealed partial class DashboardViewModel
{
    private TrafficSnapshot _traffic = TrafficSnapshot.Off;
    private bool _trafficIncludeTools = true;
    private bool _trafficBits;
    private bool _trafficShown;
    private bool _trafficExpanded;
    private TrafficFormat.Parts? _trafficMachineRx;
    private TrafficFormat.Parts? _trafficMachineTx;
    private string _trafficMachineSpoken = string.Empty;
    private string? _trafficUnavailable;
    private string? _trafficHelperButton;
    private string _trafficCaption = string.Empty;
    private bool _trafficHasRows;
    private bool _trafficCanExpand;
    private string _trafficExpandText = string.Empty;
    private Localizer? _trafficLoc;

    /// <summary>The section shows while measuring is on (DESIGN 10, Dashboard).</summary>
    public bool TrafficShown { get => _trafficShown; private set => Set(ref _trafficShown, value); }

    public TrafficFormat.Parts? TrafficMachineRx { get => _trafficMachineRx; private set => Set(ref _trafficMachineRx, value); }

    public TrafficFormat.Parts? TrafficMachineTx { get => _trafficMachineTx; private set => Set(ref _trafficMachineTx, value); }

    public bool TrafficBits { get => _trafficBits; private set => Set(ref _trafficBits, value); }

    public string TrafficMachineSpoken { get => _trafficMachineSpoken; private set => Set(ref _trafficMachineSpoken, value); }

    /// <summary>Why the rows are replaced; null when they show.</summary>
    public string? TrafficUnavailable { get => _trafficUnavailable; private set => Set(ref _trafficUnavailable, value); }

    /// <summary>The borderless helper button (install or update) under the reason; null when none applies.</summary>
    public string? TrafficHelperButton { get => _trafficHelperButton; private set => Set(ref _trafficHelperButton, value); }

    public string TrafficCaption { get => _trafficCaption; private set => Set(ref _trafficCaption, value); }

    public bool TrafficHasRows { get => _trafficHasRows; private set => Set(ref _trafficHasRows, value); }

    public bool TrafficCanExpand { get => _trafficCanExpand; private set => Set(ref _trafficCanExpand, value); }

    public string TrafficExpandText { get => _trafficExpandText; private set => Set(ref _trafficExpandText, value); }

    public ObservableCollection<TrafficRowItem> TrafficRows { get; } = new();

    /// <summary>The latest traffic snapshot (also the agents' attributed traffic).</summary>
    public TrafficSnapshot Traffic => _traffic;

    /// <summary>Applies one traffic sample and the display settings (once per second while the dashboard shows).</summary>
    public void UpdateTraffic(TrafficSnapshot snapshot, bool enabled, bool includeTools, bool bits, Localizer loc)
    {
        _traffic = snapshot;
        _trafficLoc = loc;
        _trafficIncludeTools = includeTools;
        _trafficBits = bits;
        TrafficShown = enabled && snapshot.Enabled;
        if (!TrafficShown)
        {
            return;
        }

        var view = DashboardTraffic.From(snapshot, includeTools, bits, loc);
        OnPropertyChanged(nameof(TrafficBits));
        TrafficMachineRx = view.MachineRx;
        TrafficMachineTx = view.MachineTx;
        TrafficMachineSpoken = view.MachineSpoken.Length > 0 ? $"{loc["traffic.machine"]}, {view.MachineSpoken}" : loc["traffic.machine"];
        TrafficUnavailable = view.Unavailable;
        TrafficHelperButton = view.HelperButton;
        TrafficCaption = view.Caption;
        var visible = _trafficExpanded ? view.Rows : view.Rows.Take(DashboardTraffic.CollapsedRows).ToList();
        while (TrafficRows.Count > visible.Count)
        {
            TrafficRows.RemoveAt(TrafficRows.Count - 1);
        }

        for (var i = 0; i < visible.Count; i++)
        {
            var row = visible[i];
            if (i == TrafficRows.Count || TrafficRows[i].Source != row.Source)
            {
                if (i < TrafficRows.Count)
                {
                    TrafficRows[i] = new TrafficRowItem(row.Source);
                }
                else
                {
                    TrafficRows.Add(new TrafficRowItem(row.Source));
                }
            }

            var item = TrafficRows[i];
            item.Name = row.Name;
            item.TodayText = row.TodayText;
            item.Rx = row.Rx;
            item.Tx = row.Tx;
            item.Bits = bits;
            item.AccessibleName = row.AccessibleName;
        }

        TrafficHasRows = view.Rows.Count > 0 && view.Unavailable is null;
        TrafficCanExpand = view.Rows.Count > DashboardTraffic.CollapsedRows;
        TrafficExpandText = _trafficExpanded ? loc["agents.collapse"] : loc.Format("traffic.expand", view.Rows.Count - DashboardTraffic.CollapsedRows);
    }

    /// <summary><c>traffic.expand</c> shows the remaining sources in place (and collapses them again).</summary>
    public void ToggleTrafficRows()
    {
        _trafficExpanded = !_trafficExpanded;
        if (_trafficLoc is { } loc)
        {
            UpdateTraffic(_traffic, TrafficShown, _trafficIncludeTools, _trafficBits, loc);
        }
    }

    /// <summary>The agent tree's attributed traffic today (tools per the setting), for the row's suffix and tooltip.</summary>
    private TrafficBytes? AgentTraffic(string agentId) =>
        _traffic.Enabled && _traffic.Agents.TryGetValue(agentId, out var traffic) && traffic.Today.Total(_trafficIncludeTools) is { IsZero: false } bytes
            ? bytes
            : null;
}
