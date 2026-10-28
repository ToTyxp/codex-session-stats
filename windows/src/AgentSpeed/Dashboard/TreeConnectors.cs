using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.Layout;
using AgentSpeed.Theme;

namespace AgentSpeed.Dashboard;

/// <summary>
/// One agent row's share of the parent-to-child connector lines. Rows stack without gaps, so the pieces join into a
/// line that drops from below a parent's text and branches into each of its direct children.
/// </summary>
internal sealed class TreeConnectors : FrameworkElement
{
    /// <summary>Indent of one tree level.</summary>
    public const double LevelWidth = 16;

    private const double LineInset = 2;
    private const double TextGap = 4;
    private const double Radius = 3;
    private const double DeeperReach = 5;

    public static readonly DependencyProperty GuidesProperty = DependencyProperty.Register(
        nameof(Guides), typeof(TreeGuides), typeof(TreeConnectors),
        new FrameworkPropertyMetadata(default(TreeGuides), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(TreeConnectors),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public TreeConnectors()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    public TreeGuides Guides
    {
        get => (TreeGuides)GetValue(GuidesProperty);
        set => SetValue(GuidesProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var guides = Guides;
        var height = ActualHeight;
        if (guides.IsEmpty || height <= 0)
        {
            return;
        }

        var ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var mid = Snap(height / 2, ppd);

        // One geometry for the whole row keeps the translucent stroke even where segments meet.
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            Add(context, guides.Level1, Snap(LineInset, ppd), LevelWidth - TextGap, mid, height);
            Add(context, guides.Level2, Snap(LevelWidth + LineInset, ppd), 2 * LevelWidth - TextGap, mid, height);
            Add(context, guides.Deeper, Snap(2 * LevelWidth - TextGap - DeeperReach, ppd), 2 * LevelWidth - TextGap, mid, height);
        }

        geometry.Freeze();
        dc.DrawGeometry(null, MediaColors.FrozenPen(Stroke, 1), geometry);
    }

    private static void Add(StreamGeometryContext context, TreeGuide guide, double x, double branchEnd, double mid, double height)
    {
        switch (guide)
        {
            case TreeGuide.Through:
                Line(context, new Point(x, 0), new Point(x, height));
                break;
            case TreeGuide.Branch:
                Line(context, new Point(x, 0), new Point(x, height));
                Line(context, new Point(x, mid), new Point(branchEnd, mid));
                break;
            case TreeGuide.Last:
                context.BeginFigure(new Point(x, 0), isFilled: false, isClosed: false);
                context.LineTo(new Point(x, mid - Radius), isStroked: true, isSmoothJoin: true);
                context.QuadraticBezierTo(new Point(x, mid), new Point(x + Radius, mid), isStroked: true, isSmoothJoin: true);
                context.LineTo(new Point(branchEnd, mid), isStroked: true, isSmoothJoin: true);
                break;
        }
    }

    private static void Line(StreamGeometryContext context, Point from, Point to)
    {
        context.BeginFigure(from, isFilled: false, isClosed: false);
        context.LineTo(to, isStroked: true, isSmoothJoin: false);
    }

    private static double Snap(double value, double ppd) => (Math.Round(value * ppd - 0.5) + 0.5) / ppd;
}
