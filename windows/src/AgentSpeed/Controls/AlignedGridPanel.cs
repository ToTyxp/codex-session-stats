using System.Windows;
using System.Windows.Controls;
using AgentSpeed.Presentation.Layout;

namespace AgentSpeed.Controls;

/// <summary>
/// Lays its children out row by row in columns as wide as their widest child, using the first column count from
/// <see cref="ColumnChoices"/> that fits the available width; each child is stretched to its column.
/// </summary>
internal sealed class AlignedGridPanel : Panel
{
    public static readonly DependencyProperty HorizontalSpacingProperty = DependencyProperty.Register(
        nameof(HorizontalSpacing), typeof(double), typeof(AlignedGridPanel), new FrameworkPropertyMetadata(20.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty VerticalSpacingProperty = DependencyProperty.Register(
        nameof(VerticalSpacing), typeof(double), typeof(AlignedGridPanel), new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private AlignedGridLayout? _layout;
    private double[] _rowHeights = [];

    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    public IReadOnlyList<int> ColumnChoices { get; set; } = [4, 3, 2];

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = InternalChildren;
        var widths = new double[children.Count];
        for (var i = 0; i < children.Count; i++)
        {
            children[i].Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            widths[i] = children[i].DesiredSize.Width;
        }

        var layout = AlignedGrid.Choose(widths, availableSize.Width, ColumnChoices, HorizontalSpacing);
        _layout = layout;
        var rows = (children.Count + layout.Columns - 1) / layout.Columns;
        _rowHeights = new double[rows];
        for (var i = 0; i < children.Count; i++)
        {
            var row = i / layout.Columns;
            _rowHeights[row] = Math.Max(_rowHeights[row], children[i].DesiredSize.Height);
        }

        var height = _rowHeights.Sum() + VerticalSpacing * Math.Max(0, rows - 1);
        return new Size(layout.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_layout is not { } layout)
        {
            return finalSize;
        }

        var children = InternalChildren;
        var y = 0.0;
        for (var row = 0; row < _rowHeights.Length; row++)
        {
            var x = 0.0;
            for (var column = 0; column < layout.Columns; column++)
            {
                var index = row * layout.Columns + column;
                if (index >= children.Count)
                {
                    break;
                }

                children[index].Arrange(new Rect(x, y, layout.Widths[column], _rowHeights[row]));
                x += layout.Widths[column] + HorizontalSpacing;
            }

            y += _rowHeights[row] + VerticalSpacing;
        }

        return finalSize;
    }
}
