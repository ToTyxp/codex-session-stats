using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AgentSpeed.Controls;

/// <summary>
/// A small indeterminate progress ring (the Fluent one): a quarter arc turning once per second while visible; with
/// Reduce Motion it stays still. Drawn in the foreground brush.
/// </summary>
internal sealed class ProgressRing : FrameworkElement
{
    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(ProgressRing),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThicknessProperty =
        DependencyProperty.Register(nameof(Thickness), typeof(double), typeof(ProgressRing),
            new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly RotateTransform _rotation = new();

    public ProgressRing()
    {
        RenderTransform = _rotation;
        RenderTransformOrigin = new Point(0.5, 0.5);
        IsVisibleChanged += (_, _) => UpdateAnimation();
        Focusable = false;
        IsHitTestVisible = false;
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= Thickness)
        {
            return;
        }

        var radius = (size - Thickness) / 2;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        drawingContext.PushOpacity(0.18);
        drawingContext.DrawEllipse(null, new Pen(Foreground, Thickness), center, radius, radius);
        drawingContext.Pop();
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(center.X + radius, center.Y);
        var figure = new PathFigure(start, [new ArcSegment(end, new Size(radius, radius), 0, false, SweepDirection.Clockwise, true)], false);
        var pen = new Pen(Foreground, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        drawingContext.DrawGeometry(null, pen, new PathGeometry([figure]));
    }

    private void UpdateAnimation()
    {
        if (IsVisible && !SystemParameters.ClientAreaAnimation)
        {
            _rotation.BeginAnimation(RotateTransform.AngleProperty, null);
            return;
        }

        _rotation.BeginAnimation(RotateTransform.AngleProperty, IsVisible
            ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever }
            : null);
    }
}
