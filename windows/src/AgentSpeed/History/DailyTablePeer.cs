using System.Windows;
using System.Windows.Automation.Peers;

namespace AgentSpeed.History;

/// <summary>Exposes the daily table as a list of rows, each named with every column label and value.</summary>
internal sealed class DailyTablePeer(DailyTable owner) : FrameworkElementAutomationPeer(owner)
{
    private readonly Dictionary<DateOnly, RowPeer> _rows = new();

    private DailyTable Table => (DailyTable)Owner;

    public void RaiseFocusChanged(int index)
    {
        if (ListenerExists(AutomationEvents.AutomationFocusChanged) && index >= 0 && index < Table.Rows.Count)
        {
            Row(Table.Rows[index].Day).RaiseAutomationEvent(AutomationEvents.AutomationFocusChanged);
        }
    }

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;

    protected override string GetClassNameCore() => nameof(DailyTable);

    protected override List<AutomationPeer> GetChildrenCore()
    {
        var children = new List<AutomationPeer>(Table.Rows.Count);
        var live = new HashSet<DateOnly>();
        foreach (var row in Table.Rows)
        {
            live.Add(row.Day);
            children.Add(Row(row.Day));
        }

        foreach (var stale in _rows.Keys.Where(k => !live.Contains(k)).ToArray())
        {
            _rows.Remove(stale);
        }

        return children;
    }

    private RowPeer Row(DateOnly day)
    {
        if (!_rows.TryGetValue(day, out var peer))
        {
            peer = new RowPeer(Table, day);
            _rows[day] = peer;
        }

        return peer;
    }

    private sealed class RowPeer(DailyTable table, DateOnly day) : AutomationPeer
    {
        private int Index => table.IndexOf(day) ?? -1;

        public override object GetPattern(PatternInterface patternInterface) => null!;

        protected override string GetAcceleratorKeyCore() => string.Empty;

        protected override string GetAccessKeyCore() => string.Empty;

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;

        protected override string GetAutomationIdCore() => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        protected override Rect GetBoundingRectangleCore()
        {
            var index = Index;
            if (index < 0 || PresentationSource.FromVisual(table) is null)
            {
                return Rect.Empty;
            }

            var bounds = table.RowBounds(index);
            return new Rect(table.PointToScreen(bounds.TopLeft), table.PointToScreen(bounds.BottomRight));
        }

        protected override List<AutomationPeer> GetChildrenCore() => [];

        protected override string GetClassNameCore() => "DailyTableRow";

        protected override Point GetClickablePointCore()
        {
            var bounds = GetBoundingRectangleCore();
            return bounds.IsEmpty ? new Point(double.NaN, double.NaN) : new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        }

        protected override string GetHelpTextCore() => table.AccessibleHelp(Index);

        protected override string GetItemStatusCore() => string.Empty;

        protected override string GetItemTypeCore() => string.Empty;

        protected override AutomationPeer GetLabeledByCore() => null!;

        protected override string GetNameCore() => table.AccessibleRow(Index);

        protected override AutomationOrientation GetOrientationCore() => AutomationOrientation.None;

        protected override bool HasKeyboardFocusCore() => table.IsKeyboardFocused && table.Current == Index;

        protected override bool IsContentElementCore() => true;

        protected override bool IsControlElementCore() => true;

        protected override bool IsEnabledCore() => true;

        protected override bool IsKeyboardFocusableCore() => false;

        protected override bool IsOffscreenCore() => !table.IsVisible;

        protected override bool IsPasswordCore() => false;

        protected override bool IsRequiredForFormCore() => false;

        protected override void SetFocusCore()
        {
        }
    }
}
