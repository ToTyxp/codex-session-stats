using System.Windows;
using System.Windows.Media;

namespace AgentSpeed.History;

/// <summary>A source's color dot; with several colors (all sources) a small pie of equal slices.</summary>
internal sealed class SourceDot : FrameworkElement
{
    public static readonly DependencyProperty ColorsProperty = DependencyProperty.Register(
        nameof(Colors), typeof(IReadOnlyList<Color>), typeof(SourceDot),
        new FrameworkPropertyMetadata(Array.Empty<Color>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<Color> Colors
    {
        get => (IReadOnlyList<Color>)GetValue(ColorsProperty);
        set => SetValue(ColorsProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(10, 10);

    protected override void OnRender(DrawingContext dc)
    {
        var colors = Colors;
        var size = Math.Min(ActualWidth, ActualHeight);
        if (colors is null || colors.Count == 0 || size <= 0)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = size / 2;
        if (colors.Count == 1)
        {
            dc.DrawEllipse(Brush(colors[0]), null, center, radius, radius);
            return;
        }

        var sweep = 360.0 / colors.Count;
        for (var i = 0; i < colors.Count; i++)
        {
            dc.DrawGeometry(Brush(colors[i]), null, Wedge(center, radius, -90 + i * sweep, sweep));
        }
    }

    private static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Geometry Wedge(Point center, double radius, double startDegrees, double sweepDegrees)
    {
        static Point At(Point c, double r, double degrees)
        {
            var radians = degrees * Math.PI / 180;
            return new Point(c.X + r * Math.Cos(radians), c.Y + r * Math.Sin(radians));
        }

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(center, true, true);
            g.LineTo(At(center, radius, startDegrees), false, false);
            g.ArcTo(At(center, radius, startDegrees + sweepDegrees), new Size(radius, radius), 0, sweepDegrees > 180,
                SweepDirection.Clockwise, false, false);
        }

        geometry.Freeze();
        return geometry;
    }
}
