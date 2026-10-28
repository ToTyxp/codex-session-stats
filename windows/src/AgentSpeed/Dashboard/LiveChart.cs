using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using AgentSpeed.Core;
using AgentSpeed.Presentation.Chart;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Theme;

namespace AgentSpeed.Dashboard;

/// <summary>
/// Smooth live area chart (DESIGN section 4.2): heat-mapped gradient fill, measured line, faded and dashed
/// in-flight estimate, today's peak, and a hover crosshair.
/// </summary>
internal sealed class LiveChart : FrameworkElement
{
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(LiveChart),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush), typeof(Brush), typeof(LiveChart),
        new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BubbleBrushProperty = DependencyProperty.Register(
        nameof(BubbleBrush), typeof(Brush), typeof(LiveChart),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Typeface LabelFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface LabelStrongFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private IReadOnlyList<HistoryPoint> _history = [];
    private SpeedMetric _metric = SpeedMetric.Output;
    private double _redline = 400;
    private double _peak;
    private int _range = 120;
    private string _peakTemplate = "%@";
    private string _unit = "tok/s";
    private Point? _hover;

    public LiveChart()
    {
        ClipToBounds = false;
        Cursor = Cursors.Cross;
        Focusable = false;
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public Brush GridBrush
    {
        get => (Brush)GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public Brush BubbleBrush
    {
        get => (Brush)GetValue(BubbleBrushProperty);
        set => SetValue(BubbleBrushProperty, value);
    }

    public string Summary { get; private set; } = string.Empty;

    public void SetData(IReadOnlyList<HistoryPoint> history, SpeedMetric metric, double redline, double peak, int rangeSeconds,
        string peakTemplate, string unit, string summary)
    {
        _history = history;
        _metric = metric;
        _redline = redline;
        _peak = double.IsFinite(peak) ? peak : 0;
        _range = rangeSeconds;
        _peakTemplate = peakTemplate;
        _unit = unit;
        Summary = summary;
        InvalidateVisual();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ChartPeer(this);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _hover = e.GetPosition(this);
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 4 || height <= 4)
        {
            return;
        }

        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
        var ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var (start, count) = ChartMath.Window(_history.Count, _range);
        var measured = new double[Math.Max(count, 2)];
        var total = new double[Math.Max(count, 2)];
        var estimatedFrom = -1;
        for (var i = 0; i < count; i++)
        {
            var point = _history[start + i];
            var m = Clean(point.Measured.ValueFor(_metric));
            var e = Clean(point.Estimated.ValueFor(_metric));
            measured[i] = m;
            total[i] = m + e;
            if (e > 0.05 && estimatedFrom < 0)
            {
                estimatedFrom = i;
            }
        }

        if (count < 2)
        {
            measured[1] = measured[0];
            total[1] = total[0];
        }

        var dataMax = total.Max();
        var scale = ChartMath.ComputeScale(dataMax, _peak);
        const double top = 14;
        var plotHeight = height - top;
        var gridPen = MediaColors.FrozenPen(GridBrush, 1);
        var baseline = Snap(height - 0.5, ppd);
        dc.DrawLine(gridPen, new Point(0, baseline), new Point(width, baseline));

        var gradient = HeatGradient(scale.MaxValue, _redline, top, height, 1);
        var fill = HeatGradient(scale.MaxValue, _redline, top, height, 0.42);

        var totalPoints = Offset(ChartMath.Project(total, width, plotHeight, scale.MaxValue), top);
        var measuredPoints = Offset(ChartMath.Project(measured, width, plotHeight, scale.MaxValue), top);

        if (estimatedFrom >= 0)
        {
            dc.PushOpacity(0.45);
            dc.DrawGeometry(fill, null, Area(totalPoints, height));
            dc.Pop();
        }

        dc.DrawGeometry(fill, null, Area(measuredPoints, height));
        dc.DrawGeometry(null, MediaColors.FrozenPen(gradient, 1.5, PenLineCap.Round), Line(measuredPoints, 0));

        if (estimatedFrom >= 0)
        {
            var dashed = new Pen(gradient, 1.5) { DashStyle = new DashStyle([2, 2], 0), DashCap = PenLineCap.Round };
            dashed.Freeze();
            dc.PushOpacity(0.8);
            dc.DrawGeometry(null, dashed, Line(totalPoints, Math.Max(0, estimatedFrom - 1)));
            dc.Pop();
        }

        var label = new FormattedText(Format(scale.MaxValue), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelFace, 10,
            Foreground, ppd);
        dc.DrawText(label, new Point(0, 0));

        if (_peak > 0 && scale.PeakInRange)
        {
            var y = Snap(top + plotHeight * (1 - _peak / scale.MaxValue), ppd);
            var peakColor = HeatPalette.Sample(Presentation.Heat.HeatMath.Heat(_peak, _redline));
            var pen = new Pen(MediaColors.Frozen(peakColor.ToColor(0.8)), 1) { DashStyle = new DashStyle([3, 3], 0) };
            pen.Freeze();
            dc.DrawLine(pen, new Point(0, y), new Point(width, y));
            var text = Presentation.Localization.Localizer.FormatTemplate(_peakTemplate, CultureInfo.CurrentCulture, Format(_peak));
            var peakLabel = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelFace, 10, Foreground, ppd);
            var labelY = y - peakLabel.Height - 1 < 0 ? y + 2 : y - peakLabel.Height - 1;
            dc.DrawText(peakLabel, new Point(width - peakLabel.WidthIncludingTrailingWhitespace, labelY));
        }

        if (_hover is { } hover && count > 0)
        {
            DrawCrosshair(dc, hover, start, count, totalPoints, total, width, height, ppd);
        }
    }

    private void DrawCrosshair(DrawingContext dc, Point hover, int start, int count, Point[] points, double[] values,
        double width, double height, double ppd)
    {
        var index = ChartMath.IndexAt(hover.X, width, count);
        var point = points[Math.Min(index, points.Length - 1)];
        var x = Snap(point.X, ppd);
        dc.DrawLine(MediaColors.FrozenPen(Foreground, 1), new Point(x, 0), new Point(x, height));
        var heat = Presentation.Heat.HeatMath.Heat(values[index], _redline);
        dc.DrawEllipse(MediaColors.Frozen(HeatPalette.Sample(heat).ToColor()), MediaColors.FrozenPen(BubbleBrush, 1.5), point, 3.5, 3.5);

        var time = _history[start + index].Time.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var text = new FormattedText($"{time} · {SpeedFormat.Speed(values[index])} {_unit}", CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, LabelStrongFace, 11, Foreground, ppd);
        var bubbleWidth = text.WidthIncludingTrailingWhitespace + 12;
        var bubbleX = Math.Clamp(x - bubbleWidth / 2, 0, Math.Max(0, width - bubbleWidth));
        var rect = new Rect(bubbleX, -2, bubbleWidth, text.Height + 4);
        dc.DrawRoundedRectangle(BubbleBrush, MediaColors.FrozenPen(GridBrush, 1), rect, 4, 4);
        dc.DrawText(text, new Point(bubbleX + 6, 0));
    }

    private static LinearGradientBrush HeatGradient(double maxValue, double redline, double top, double height, double alpha)
    {
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, top),
            EndPoint = new Point(0, height),
        };
        foreach (var (offset, heat) in ChartMath.HeatGradient(maxValue, redline))
        {
            var a = alpha < 1 ? alpha * (1 - offset * 0.8) : alpha;
            brush.GradientStops.Add(new GradientStop(HeatPalette.Sample(heat).ToColor(a), offset));
        }

        brush.Freeze();
        return brush;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }

    private static Geometry Area(Point[] points, double bottom)
    {
        var segments = ChartMath.CatmullRom(points.Select(p => new ChartPoint(p.X, p.Y)).ToArray());
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(points[0].X, bottom), true, true);
            g.LineTo(points[0], false, false);
            foreach (var s in segments)
            {
                g.BezierTo(new Point(s.Control1.X, s.Control1.Y), new Point(s.Control2.X, s.Control2.Y), new Point(s.End.X, s.End.Y), false, true);
            }

            g.LineTo(new Point(points[^1].X, bottom), false, false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Geometry Line(Point[] points, int from)
    {
        var slice = points.Skip(from).Select(p => new ChartPoint(p.X, p.Y)).ToArray();
        var geometry = new StreamGeometry();
        if (slice.Length < 2)
        {
            geometry.Freeze();
            return geometry;
        }

        var segments = ChartMath.CatmullRom(slice);
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(slice[0].X, slice[0].Y), false, false);
            foreach (var s in segments)
            {
                g.BezierTo(new Point(s.Control1.X, s.Control1.Y), new Point(s.Control2.X, s.Control2.Y), new Point(s.End.X, s.End.Y), true, true);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static Point[] Offset(ChartPoint[] points, double top) =>
        points.Select(p => new Point(p.X, p.Y + top)).ToArray();

    private static double Clean(double value) => double.IsFinite(value) && value > 0 ? value : 0;

    private static double Snap(double value, double ppd) => (Math.Round(value * ppd - 0.5) + 0.5) / ppd;

    private static string Format(double value) => SpeedFormat.Speed(value);

    private sealed class ChartPeer(LiveChart owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;

        protected override string GetHelpTextCore() => ((LiveChart)Owner).Summary;
    }
}
