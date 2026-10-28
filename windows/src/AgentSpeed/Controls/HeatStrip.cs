using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Theme;

namespace AgentSpeed.Controls;

/// <summary>
/// The heat palette as a slim strip under the redline slider, with one thin tick per speed title at its heat position
/// (DESIGN section 5). One tick can be highlighted (the title under the pointer or keyboard focus); it eases in and out.
/// </summary>
internal sealed class HeatStrip : FrameworkElement
{
    public static readonly DependencyProperty TicksProperty = DependencyProperty.Register(
        nameof(Ticks), typeof(IReadOnlyList<double>), typeof(HeatStrip),
        new FrameworkPropertyMetadata(Array.Empty<double>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HighlightIndexProperty = DependencyProperty.Register(
        nameof(HighlightIndex), typeof(int), typeof(HeatStrip),
        new PropertyMetadata(-1, (d, e) => ((HeatStrip)d).OnHighlightChanged((int)e.OldValue, (int)e.NewValue)));

    public static readonly DependencyProperty TickBrushProperty = DependencyProperty.Register(
        nameof(TickBrush), typeof(Brush), typeof(HeatStrip),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HighlightBrushProperty = DependencyProperty.Register(
        nameof(HighlightBrush), typeof(Brush), typeof(HeatStrip),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty HighlightLevelProperty = DependencyProperty.Register(
        "HighlightLevel", typeof(double), typeof(HeatStrip),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double StripHeight = 4;
    private const double TickHeight = 10;
    private const double HighlightTickHeight = 14;

    private static readonly Brush Gradient = BuildGradient();

    private int _shownIndex = -1;

    public HeatStrip()
    {
        Height = HighlightTickHeight;
        SnapsToDevicePixels = true;
        IsHitTestVisible = false;
    }

    /// <summary>Tick positions, 0 (left) to 1 (right).</summary>
    public IReadOnlyList<double> Ticks
    {
        get => (IReadOnlyList<double>)GetValue(TicksProperty);
        set => SetValue(TicksProperty, value);
    }

    /// <summary>The tick to emphasise, or -1.</summary>
    public int HighlightIndex
    {
        get => (int)GetValue(HighlightIndexProperty);
        set => SetValue(HighlightIndexProperty, value);
    }

    public Brush TickBrush
    {
        get => (Brush)GetValue(TickBrushProperty);
        set => SetValue(TickBrushProperty, value);
    }

    public Brush HighlightBrush
    {
        get => (Brush)GetValue(HighlightBrushProperty);
        set => SetValue(HighlightBrushProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 2 || height <= 0)
        {
            return;
        }

        var middle = height / 2;
        dc.DrawRoundedRectangle(Gradient, null, new Rect(0, middle - StripHeight / 2, width, StripHeight), StripHeight / 2, StripHeight / 2);

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var tickPen = MediaColors.FrozenPen(TickBrush, 1);
        var level = (double)GetValue(HighlightLevelProperty);
        var ticks = Ticks;
        for (var i = 0; i < ticks.Count; i++)
        {
            var x = Snap(Math.Clamp(ticks[i], 0, 1) * width, dpi);
            if (i == _shownIndex && level > 0.001)
            {
                var tall = TickHeight + (HighlightTickHeight - TickHeight) * level;
                dc.PushOpacity(1 - level);
                dc.DrawLine(tickPen, new Point(x, middle - TickHeight / 2), new Point(x, middle + TickHeight / 2));
                dc.Pop();
                dc.PushOpacity(level);
                dc.DrawLine(MediaColors.FrozenPen(HighlightBrush, 2, PenLineCap.Round), new Point(x, middle - tall / 2), new Point(x, middle + tall / 2));
                dc.Pop();
            }
            else
            {
                dc.DrawLine(tickPen, new Point(x, middle - TickHeight / 2), new Point(x, middle + TickHeight / 2));
            }
        }
    }

    private void OnHighlightChanged(int previous, int next)
    {
        var fade = TimeSpan.FromMilliseconds(next >= 0 ? 150 : 200);
        if (next >= 0)
        {
            if (previous != next)
            {
                _shownIndex = next;
                BeginAnimation(HighlightLevelProperty, null);
                SetValue(HighlightLevelProperty, 0.0);
            }

            BeginAnimation(HighlightLevelProperty, new DoubleAnimation(1, fade) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        else
        {
            BeginAnimation(HighlightLevelProperty, new DoubleAnimation(0, fade) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }

    private static double Snap(double x, double dpi) => (Math.Round(x * dpi - 0.5) + 0.5) / dpi;

    private static Brush BuildGradient()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        // Sampled densely so the strip follows the palette's OKLab interpolation, not the brush's sRGB one.
        const int samples = 32;
        for (var i = 0; i <= samples; i++)
        {
            var position = i / (double)samples;
            brush.GradientStops.Add(new GradientStop(HeatPalette.Evaluate(position).ToColor(), position));
        }

        brush.Freeze();
        return brush;
    }
}
