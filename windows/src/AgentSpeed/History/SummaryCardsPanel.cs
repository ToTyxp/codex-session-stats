using System.Windows;
using System.Windows.Controls;

namespace AgentSpeed.History;

/// <summary>
/// Lays the summary cards out in rows (DESIGN 9: all five in one row when wide, else 3 + 2): the cards of a row share its
/// width equally and take the height of the tallest one.
/// </summary>
internal sealed class SummaryCardsPanel : Panel
{
    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(
        nameof(Rows), typeof(IReadOnlyList<int>), typeof(SummaryCardsPanel),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>How many cards each row holds, top first; null puts every card in one row.</summary>
    public IReadOnlyList<int>? Rows
    {
        get => (IReadOnlyList<int>?)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 0;
        var height = 0.0;
        var widest = 0.0;
        foreach (var (start, count) in Spans())
        {
            var cell = count > 0 && width > 0 ? width / count : double.PositiveInfinity;
            var row = 0.0;
            var sum = 0.0;
            for (var i = start; i < start + count; i++)
            {
                var child = InternalChildren[i];
                child.Measure(new Size(cell, double.PositiveInfinity));
                row = Math.Max(row, child.DesiredSize.Height);
                sum += child.DesiredSize.Width;
            }

            height += row;
            widest = Math.Max(widest, sum);
        }

        return new Size(width > 0 ? width : widest, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var y = 0.0;
        foreach (var (start, count) in Spans())
        {
            var cell = count > 0 ? finalSize.Width / count : 0;
            var row = 0.0;
            for (var i = start; i < start + count; i++)
            {
                row = Math.Max(row, InternalChildren[i].DesiredSize.Height);
            }

            for (var i = start; i < start + count; i++)
            {
                InternalChildren[i].Arrange(new Rect((i - start) * cell, y, cell, row));
            }

            y += row;
        }

        return finalSize;
    }

    /// <summary>The children of each row as (first index, count); cards beyond the given rows join the last one.</summary>
    private IEnumerable<(int Start, int Count)> Spans()
    {
        var total = InternalChildren.Count;
        var rows = Rows is { Count: > 0 } given ? given : [total];
        var start = 0;
        for (var r = 0; r < rows.Count && start < total; r++)
        {
            var count = r == rows.Count - 1 ? total - start : Math.Clamp(rows[r], 0, total - start);
            yield return (start, count);
            start += count;
        }
    }
}
