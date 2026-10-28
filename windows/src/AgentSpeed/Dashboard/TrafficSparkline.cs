using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using AgentSpeed.Core.Traffic;
using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Theme;

namespace AgentSpeed.Dashboard;

/// <summary>
/// The last 60 s of this computer's traffic (DESIGN 10, Dashboard 2): mirrored around a hairline midline — download as
/// an area above it (fill 16 %, 1 pt line 55 %), upload below it (fill 10 %, line 40 %) — on one linear scale whose
/// top is the window's largest value rounded up to a 1-2-5 step (at least 10 KB/s). A scale change eases over 0.3 s
/// (Reduce Motion: jumps). Redrawn once per second; hover shows a crosshair with the time and both rates.
/// </summary>
internal sealed class TrafficSparkline : FrameworkElement
{
    public const int WindowSeconds = 60;
    private const double EaseSeconds = 0.3;

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(TrafficSparkline),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush), typeof(Brush), typeof(TrafficSparkline),
        new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BubbleBrushProperty = DependencyProperty.Register(
        nameof(BubbleBrush), typeof(Brush), typeof(TrafficSparkline),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Typeface BubbleFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);

    private IReadOnlyList<TrafficPoint> _points = [];
    private bool _bits;
    private double _scale = 10_000;
    private double _scaleFrom = 10_000;
    private double _scaleTo = 10_000;
    private TimeSpan _easeStart;
    private bool _easing;
    private Point? _hover;

    public TrafficSparkline()
    {
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

    public bool ReduceMotion { get; set; }

    /// <summary><c>traffic.a11y.chart</c>: both peaks of the window as words.</summary>
    public string Summary { get; private set; } = string.Empty;

    /// <summary>The last 60 one-second buckets (oldest first) and how rates read.</summary>
    public void SetData(IReadOnlyList<TrafficPoint> history, bool bits, string summary)
    {
        _points = history.Count > WindowSeconds ? history.Skip(history.Count - WindowSeconds).ToArray() : history;
        _bits = bits;
        Summary = summary;
        var peak = 0.0;
        foreach (var point in _points)
        {
            peak = Math.Max(peak, Math.Max(point.Rate.Rx, point.Rate.Tx));
        }

        var target = DashboardTraffic.ScaleTop(peak);
        if (target != _scaleTo)
        {
            _scaleFrom = _scale;
            _scaleTo = target;
            if (ReduceMotion || _points.Count < 2 || !IsVisible)
            {
                _scale = target;
            }
            else
            {
                _easeStart = TimeSpan.MinValue;
                if (!_easing)
                {
                    _easing = true;
                    CompositionTarget.Rendering += OnRendering;
                }
            }
        }

        InvalidateVisual();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SparklinePeer(this);

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
        var mid = Math.Round(height / 2 * ppd) / ppd;
        var half = mid - 1;
        dc.DrawLine(MediaColors.FrozenPen(GridBrush, 1 / ppd), new Point(0, mid), new Point(width, mid));
        if (_points.Count < 2)
        {
            return;
        }

        var color = (Foreground as SolidColorBrush)?.Color ?? Colors.Gray;
        Plot(dc, p => p.Rate.Rx, true, width, mid, half, color, 0.16, 0.55);
        Plot(dc, p => p.Rate.Tx, false, width, mid, half, color, 0.10, 0.40);
        if (_hover is { } hover)
        {
            Crosshair(dc, hover, width, height, ppd);
        }
    }

    private void Plot(DrawingContext dc, Func<TrafficPoint, double> value, bool up, double width, double mid, double half, Color color,
        double fillAlpha, double lineAlpha)
    {
        var end = _points[^1].Time;
        var line = new StreamGeometry();
        var fill = new StreamGeometry();
        using (var l = line.Open())
        using (var f = fill.Open())
        {
            for (var i = 0; i < _points.Count; i++)
            {
                var point = _points[i];
                var v = Math.Clamp(value(point) / Math.Max(_scale, 1), 0, 1);
                var p = new Point(X(point.Time, end, width), up ? mid - v * half : mid + v * half);
                if (i == 0)
                {
                    l.BeginFigure(p, false, false);
                    f.BeginFigure(new Point(p.X, mid), true, true);
                }
                else
                {
                    l.LineTo(p, true, true);
                }

                f.LineTo(p, false, true);
            }

            f.LineTo(new Point(X(end, end, width), mid), false, false);
        }

        line.Freeze();
        fill.Freeze();
        dc.DrawGeometry(MediaColors.Frozen(Color.FromArgb((byte)(color.A * fillAlpha), color.R, color.G, color.B)), null, fill);
        var pen = new Pen(MediaColors.Frozen(Color.FromArgb((byte)(color.A * lineAlpha), color.R, color.G, color.B)), 1) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, line);
    }

    private void Crosshair(DrawingContext dc, Point hover, double width, double height, double ppd)
    {
        var end = _points[^1].Time;
        var time = end.AddSeconds(-WindowSeconds * (1 - Math.Clamp(hover.X / width, 0, 1)));
        var nearest = _points.MinBy(p => Math.Abs((p.Time - time).TotalSeconds));
        var x = Math.Round(X(nearest.Time, end, width) * ppd) / ppd;
        dc.DrawLine(MediaColors.FrozenPen(Foreground, 1), new Point(x, 0), new Point(x, height));
        var rx = TrafficFormat.RateParts(nearest.Rate.Rx, _bits).Text;
        var tx = TrafficFormat.RateParts(nearest.Rate.Tx, _bits).Text;
        var stamp = nearest.Time.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var text = new FormattedText($"{stamp}   ↓ {rx}   ↑ {tx}", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, BubbleFace, 10,
            Foreground, ppd);
        var bubbleWidth = text.WidthIncludingTrailingWhitespace + 14;
        var bubbleX = Math.Clamp(x - bubbleWidth / 2, 0, Math.Max(0, width - bubbleWidth));
        var rect = new Rect(bubbleX, -text.Height - 8, bubbleWidth, text.Height + 4);
        dc.DrawRoundedRectangle(BubbleBrush, MediaColors.FrozenPen(GridBrush, 1), rect, rect.Height / 2, rect.Height / 2);
        dc.DrawText(text, new Point(bubbleX + 7, rect.Y + 2));
    }

    private static double X(DateTimeOffset time, DateTimeOffset end, double width) =>
        (1 - (end - time).TotalSeconds / WindowSeconds) * width;

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = e is RenderingEventArgs r ? r.RenderingTime : TimeSpan.Zero;
        if (_easeStart == TimeSpan.MinValue)
        {
            _easeStart = now;
        }

        var t = Math.Clamp((now - _easeStart).TotalSeconds / EaseSeconds, 0, 1);
        var eased = t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
        _scale = _scaleFrom + (_scaleTo - _scaleFrom) * eased;
        InvalidateVisual();
        if (t >= 1)
        {
            _scale = _scaleTo;
            _easing = false;
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    private sealed class SparklinePeer(TrafficSparkline owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;

        protected override string GetNameCore() => ((TrafficSparkline)Owner).Summary;
    }
}
