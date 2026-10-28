using System.Windows;
using System.Windows.Controls;
using AgentSpeed.Presentation.History;

namespace AgentSpeed.History;

/// <summary>Lays the header buttons out on the table's column spans, child i on span i.</summary>
internal sealed class TableHeaderPanel : Panel
{
    private IReadOnlyList<ColumnSpan> _spans = [];

    public IReadOnlyList<ColumnSpan> Spans
    {
        get => _spans;
        set
        {
            _spans = value;
            InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = 0.0;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var width = i < _spans.Count ? _spans[i].Width : 0;
            InternalChildren[i].Measure(new Size(width, availableSize.Height));
            height = Math.Max(height, InternalChildren[i].DesiredSize.Height);
        }

        var right = _spans.Count > 0 ? _spans[^1].Right : 0;
        return new Size(double.IsInfinity(availableSize.Width) ? right : 0, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var span = i < _spans.Count ? _spans[i] : new ColumnSpan(0, 0);
            InternalChildren[i].Arrange(new Rect(span.X, 0, span.Width, finalSize.Height));
        }

        return finalSize;
    }
}
