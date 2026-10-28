using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AgentSpeed.Core.Traffic;

namespace AgentSpeed.Dashboard;

/// <summary>
/// One traffic rate in fixed cells (DESIGN 10, Formatting): the arrow, the number right-aligned in a cell as wide as
/// <c>8.88</c> with tabular digits, the unit left-aligned in a cell as wide as <c>MB/s</c> (bits: <c>Mb/s</c>), so the
/// readout never changes width or position. A missing value reads a <c>–</c> in the dash brush.
/// </summary>
internal sealed class TrafficRateText : FrameworkElement
{
    public static readonly DependencyProperty DownProperty = Register(nameof(Down), true, FrameworkPropertyMetadataOptions.AffectsRender);

    public static readonly DependencyProperty PartsProperty = DependencyProperty.Register(nameof(Parts), typeof(TrafficFormat.Parts?),
        typeof(TrafficRateText), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BitsProperty = Register(nameof(Bits), false, FrameworkPropertyMetadataOptions.AffectsMeasure);

    public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(TrafficRateText),
        new FrameworkPropertyMetadata(11.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty NumberBrushProperty = BrushProperty(nameof(NumberBrush));

    public static readonly DependencyProperty SecondaryBrushProperty = BrushProperty(nameof(SecondaryBrush));

    public static readonly DependencyProperty DashBrushProperty = BrushProperty(nameof(DashBrush));

    public static readonly DependencyProperty NumberWeightProperty = DependencyProperty.Register(nameof(NumberWeight), typeof(FontWeight),
        typeof(TrafficRateText), new FrameworkPropertyMetadata(FontWeights.Medium, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    private const double ArrowGap = 3;
    private const double UnitGap = 3;
    private static readonly FontFamily Family = new("Segoe UI Variable Text, Segoe UI");

    public bool Down
    {
        get => (bool)GetValue(DownProperty);
        set => SetValue(DownProperty, value);
    }

    public TrafficFormat.Parts? Parts
    {
        get => (TrafficFormat.Parts?)GetValue(PartsProperty);
        set => SetValue(PartsProperty, value);
    }

    public bool Bits
    {
        get => (bool)GetValue(BitsProperty);
        set => SetValue(BitsProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public Brush NumberBrush
    {
        get => (Brush)GetValue(NumberBrushProperty);
        set => SetValue(NumberBrushProperty, value);
    }

    public Brush SecondaryBrush
    {
        get => (Brush)GetValue(SecondaryBrushProperty);
        set => SetValue(SecondaryBrushProperty, value);
    }

    public Brush DashBrush
    {
        get => (Brush)GetValue(DashBrushProperty);
        set => SetValue(DashBrushProperty, value);
    }

    public FontWeight NumberWeight
    {
        get => (FontWeight)GetValue(NumberWeightProperty);
        set => SetValue(NumberWeightProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var (arrow, number, unit, height) = Cells();
        return new Size(arrow + ArrowGap + number + UnitGap + unit, height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var (arrowCell, numberCell, _, _) = Cells();
        var arrow = Text(Down ? "↓" : "↑", SecondaryBrush, FontWeights.Normal, ppd);
        var baseline = Math.Round(arrow.Baseline * ppd) / ppd;
        dc.DrawText(arrow, new Point(0, baseline - arrow.Baseline));
        var numberRight = arrowCell + ArrowGap + numberCell;
        if (Parts is not { } parts)
        {
            var dash = Text("–", DashBrush, NumberWeight, ppd);
            dc.DrawText(dash, new Point(Snap(numberRight - dash.WidthIncludingTrailingWhitespace, ppd), baseline - dash.Baseline));
            return;
        }

        var digit = DigitCell(ppd);
        var x = numberRight - Tabular(parts.Number, digit, ppd);
        foreach (var c in parts.Number)
        {
            var glyph = Text(c.ToString(), NumberBrush, NumberWeight, ppd);
            var cell = char.IsAsciiDigit(c) ? digit : glyph.WidthIncludingTrailingWhitespace;
            dc.DrawText(glyph, new Point(Snap(x + (cell - glyph.WidthIncludingTrailingWhitespace) / 2, ppd), baseline - glyph.Baseline));
            x += cell;
        }

        var unit = Text(parts.Unit, SecondaryBrush, FontWeights.Normal, ppd);
        dc.DrawText(unit, new Point(Snap(numberRight + UnitGap, ppd), baseline - unit.Baseline));
    }

    private (double Arrow, double Number, double Unit, double Height) Cells()
    {
        var ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var arrow = Math.Max(Text("↓", Brushes.Black, FontWeights.Normal, ppd).WidthIncludingTrailingWhitespace,
            Text("↑", Brushes.Black, FontWeights.Normal, ppd).WidthIncludingTrailingWhitespace);
        var digit = DigitCell(ppd);
        var number = Tabular("8.88", digit, ppd);
        var unitText = Text(Bits ? "Mb/s" : "MB/s", Brushes.Black, FontWeights.Normal, ppd);
        return (arrow, number, unitText.WidthIncludingTrailingWhitespace, unitText.Height);
    }

    private double DigitCell(double ppd)
    {
        var cell = 0.0;
        for (var d = '0'; d <= '9'; d++)
        {
            cell = Math.Max(cell, Text(d.ToString(), Brushes.Black, NumberWeight, ppd).WidthIncludingTrailingWhitespace);
        }

        return cell;
    }

    private double Tabular(string text, double digit, double ppd)
    {
        var width = 0.0;
        foreach (var c in text)
        {
            width += char.IsAsciiDigit(c) ? digit : Text(c.ToString(), Brushes.Black, NumberWeight, ppd).WidthIncludingTrailingWhitespace;
        }

        return width;
    }

    private FormattedText Text(string text, Brush brush, FontWeight weight, double ppd) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(Family, FontStyles.Normal, weight, FontStretches.Normal),
            FontSize, brush ?? Brushes.Gray, ppd);

    private static double Snap(double value, double ppd) => Math.Round(value * ppd) / ppd;

    private static DependencyProperty Register(string name, bool value, FrameworkPropertyMetadataOptions options) =>
        DependencyProperty.Register(name, typeof(bool), typeof(TrafficRateText), new FrameworkPropertyMetadata(value, options));

    private static DependencyProperty BrushProperty(string name) =>
        DependencyProperty.Register(name, typeof(Brush), typeof(TrafficRateText),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
}
