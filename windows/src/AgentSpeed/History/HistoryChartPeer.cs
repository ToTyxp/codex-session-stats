using System.Windows;
using System.Windows.Automation.Peers;

namespace AgentSpeed.History;

/// <summary>Exposes the chart as a list of days with data, each named with every value of that day.</summary>
internal sealed class HistoryChartPeer(HistoryChart owner) : FrameworkElementAutomationPeer(owner)
{
    private readonly Dictionary<DateOnly, DayPeer> _days = new();

    private HistoryChart Chart => (HistoryChart)Owner;

    public void RaiseFocusChanged(int index)
    {
        if (!ListenerExists(AutomationEvents.AutomationFocusChanged))
        {
            return;
        }

        if (Chart.Range.DayCount > index && index >= 0 && Chart.Bars.HasData(index))
        {
            Day(index).RaiseAutomationEvent(AutomationEvents.AutomationFocusChanged);
        }
    }

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;

    protected override string GetClassNameCore() => nameof(HistoryChart);

    protected override List<AutomationPeer> GetChildrenCore()
    {
        var children = new List<AutomationPeer>();
        var live = new HashSet<DateOnly>();
        foreach (var index in Chart.IndicesWithData())
        {
            var peer = Day(index);
            live.Add(peer.Date);
            children.Add(peer);
        }

        foreach (var stale in _days.Keys.Where(k => !live.Contains(k)).ToArray())
        {
            _days.Remove(stale);
        }

        return children;
    }

    private DayPeer Day(int index)
    {
        var date = Chart.Range.DayAt(index);
        if (!_days.TryGetValue(date, out var peer))
        {
            peer = new DayPeer(Chart, date);
            _days[date] = peer;
        }

        return peer;
    }

    private sealed class DayPeer(HistoryChart chart, DateOnly date) : AutomationPeer
    {
        public DateOnly Date { get; } = date;

        private int Index => chart.Range.Contains(Date) ? chart.Range.IndexOf(Date) : -1;

        public override object GetPattern(PatternInterface patternInterface) => null!;

        protected override string GetAcceleratorKeyCore() => string.Empty;

        protected override string GetAccessKeyCore() => string.Empty;

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;

        protected override string GetAutomationIdCore() => Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        protected override Rect GetBoundingRectangleCore()
        {
            var index = Index;
            if (index < 0 || PresentationSource.FromVisual(chart) is null)
            {
                return Rect.Empty;
            }

            var slot = chart.SlotBounds(index);
            if (slot.IsEmpty)
            {
                return Rect.Empty;
            }

            return new Rect(chart.PointToScreen(slot.TopLeft), chart.PointToScreen(slot.BottomRight));
        }

        protected override List<AutomationPeer> GetChildrenCore() => [];

        protected override string GetClassNameCore() => "HistoryChartDay";

        protected override Point GetClickablePointCore()
        {
            var bounds = GetBoundingRectangleCore();
            return bounds.IsEmpty ? new Point(double.NaN, double.NaN) : new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        }

        protected override string GetHelpTextCore() => string.Empty;

        protected override string GetItemStatusCore() => string.Empty;

        protected override string GetItemTypeCore() => string.Empty;

        protected override AutomationPeer GetLabeledByCore() => null!;

        protected override string GetNameCore() => Index >= 0 ? chart.AccessibleName(Index) : string.Empty;

        protected override AutomationOrientation GetOrientationCore() => AutomationOrientation.None;

        protected override bool HasKeyboardFocusCore() => chart.IsKeyboardFocused && chart.KeyIndex == Index;

        protected override bool IsContentElementCore() => true;

        protected override bool IsControlElementCore() => true;

        protected override bool IsEnabledCore() => true;

        protected override bool IsKeyboardFocusableCore() => false;

        protected override bool IsOffscreenCore() => !chart.IsVisible;

        protected override bool IsPasswordCore() => false;

        protected override bool IsRequiredForFormCore() => false;

        protected override void SetFocusCore()
        {
        }
    }
}
