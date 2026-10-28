using System.Windows;
using System.Windows.Media;

namespace AgentSpeed.Controls;

/// <summary>
/// Gauge glyph in the inherited foreground color. With no heat it rests: a muted track with the needle at zero and a
/// hub, so empty states read as an instrument rather than a loading spinner (macOS RestingGauge).
/// </summary>
internal sealed class GaugeGlyph : FrameworkElement
{
    public static readonly DependencyProperty HeatProperty = DependencyProperty.Register(
        nameof(Heat), typeof(double), typeof(GaugeGlyph), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = System.Windows.Documents.TextElement.ForegroundProperty.AddOwner(
        typeof(GaugeGlyph), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    public double Heat
    {
        get => (double)GetValue(HeatProperty);
        set => SetValue(HeatProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var color = Foreground is SolidColorBrush solid ? solid.Color : Colors.Gray;
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        var stroke = Math.Max(1.5, Math.Min(ActualWidth, ActualHeight) / 9);
        if (Heat <= 0)
        {
            GaugeGlyphPainter.DrawResting(drawingContext, bounds, color, stroke);
            return;
        }

        GaugeGlyphPainter.Draw(drawingContext, bounds, Heat, color, monochrome: false, strokeThickness: stroke, trackAlpha: 0.45);
    }
}
