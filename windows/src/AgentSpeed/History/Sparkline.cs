using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.Chart;
using AgentSpeed.Presentation.History;

namespace AgentSpeed.History;

/// <summary>
/// A tiny line of one value per day; it breaks where a day has no value and ends in a dot, or marks the highlighted
/// day with a ringed dot instead.
/// </summary>
internal sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double?>), typeof(Sparkline),
        new FrameworkPropertyMetadata(Array.Empty<double?>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Index into <see cref="Values"/> of the day to mark; -1 for none.</summary>
    public static readonly DependencyProperty HighlightProperty = DependencyProperty.Register(
        nameof(Highlight), typeof(int), typeof(Sparkline), new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double?> Values
    {
        get => (IReadOnlyList<double?>)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public int Highlight
    {
        get => (int)GetValue(HighlightProperty);
        set => SetValue(HighlightProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (Values is not { Count: > 0 } raw || width <= 2 || height <= 2)
        {
            return;
        }

        const double inset = 2;
        var values = Bucket(raw, Math.Max(2, (int)((width - 2 * inset) / 2.5)));
        var max = values.Where(v => v is { } x && double.IsFinite(x)).Select(v => v!.Value).DefaultIfEmpty(0).Max();
        if (max <= 0)
        {
            return;
        }

        var step = values.Length > 1 ? (width - 2 * inset) / (values.Length - 1) : 0;
        Point At(int i) => new(inset + (values.Length > 1 ? i * step : (width - 2 * inset) / 2),
            height - inset - Math.Clamp(values[i]!.Value / max, 0, 1) * (height - 2 * inset));

        var stroke = Stroke;
        var pen = new Pen(stroke, 1.25) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        var fill = AreaBrush(stroke);

        foreach (var (start, count) in HistoryChartMath.Runs(values))
        {
            if (count == 1)
            {
                continue;
            }

            var points = Enumerable.Range(start, count).Select(At).ToArray();
            var segments = ChartMath.CatmullRom(points.Select(p => new ChartPoint(p.X, p.Y)).ToArray());
            var line = new StreamGeometry();
            var area = new StreamGeometry();
            using (var g = line.Open())
            using (var a = area.Open())
            {
                g.BeginFigure(points[0], false, false);
                a.BeginFigure(new Point(points[0].X, height), true, true);
                a.LineTo(points[0], false, false);
                foreach (var segment in segments)
                {
                    var c1 = new Point(segment.Control1.X, segment.Control1.Y);
                    var c2 = new Point(segment.Control2.X, segment.Control2.Y);
                    var end = new Point(segment.End.X, segment.End.Y);
                    g.BezierTo(c1, c2, end, true, true);
                    a.BezierTo(c1, c2, end, false, true);
                }

                a.LineTo(new Point(points[^1].X, height), false, false);
            }

            line.Freeze();
            area.Freeze();
            if (fill is not null)
            {
                dc.DrawGeometry(fill, null, area);
            }

            dc.DrawGeometry(null, pen, line);
        }

        var highlight = Highlight >= 0 && Highlight < raw.Count ? Highlight * values.Length / raw.Count : -1;
        if (highlight >= 0)
        {
            if (values[highlight] is { } h && double.IsFinite(h))
            {
                dc.DrawEllipse(HaloBrush(stroke), null, At(highlight), 4.5, 4.5);
                dc.DrawEllipse(stroke, null, At(highlight), 2.25, 2.25);
            }

            return;
        }

        for (var i = values.Length - 1; i >= 0; i--)
        {
            if (values[i] is { } v && double.IsFinite(v))
            {
                dc.DrawEllipse(stroke, null, At(i), 1.75, 1.75);
                break;
            }
        }
    }

    /// <summary>A soft ring of the line color around the highlighted dot.</summary>
    private static Brush HaloBrush(Brush stroke)
    {
        if (stroke is not SolidColorBrush solid)
        {
            return Brushes.Transparent;
        }

        var color = solid.Color;
        var brush = new SolidColorBrush(Color.FromArgb((byte)(color.A * solid.Opacity * 0.3), color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    /// <summary>Averages neighbouring days so a long range reads as a trend rather than a scribble.</summary>
    private static double?[] Bucket(IReadOnlyList<double?> values, int maxPoints)
    {
        if (values.Count <= maxPoints)
        {
            return values.ToArray();
        }

        var result = new double?[maxPoints];
        for (var b = 0; b < maxPoints; b++)
        {
            var from = b * values.Count / maxPoints;
            var to = Math.Max(from + 1, (b + 1) * values.Count / maxPoints);
            var sum = 0.0;
            var count = 0;
            for (var i = from; i < to; i++)
            {
                if (values[i] is { } v && double.IsFinite(v))
                {
                    sum += v;
                    count++;
                }
            }

            result[b] = count > 0 ? sum / count : null;
        }

        return result;
    }

    /// <summary>The line color fading out toward the baseline.</summary>
    private static Brush? AreaBrush(Brush stroke)
    {
        if (stroke is not SolidColorBrush solid)
        {
            return null;
        }

        var color = solid.Color;
        var alpha = color.A * solid.Opacity;
        var brush = new LinearGradientBrush(
            [
                new GradientStop(Color.FromArgb((byte)(alpha * 0.22), color.R, color.G, color.B), 0),
                new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1),
            ],
            new Point(0, 0), new Point(0, 1));
        brush.Freeze();
        return brush;
    }
}
