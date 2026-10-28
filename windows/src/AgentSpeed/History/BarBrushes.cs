using System.Windows;
using System.Windows.Media;

namespace AgentSpeed.History;

/// <summary>
/// The fills of history bars in a source color: solid, a soft vertical gradient (one source), or hatched and lighter for
/// today's figures, which are still in progress. Frozen and cached per color.
/// </summary>
internal static class BarBrushes
{
    private static readonly Dictionary<(Color, bool, bool), Brush> Cache = new();

    public static Brush For(Color color, bool gradient, bool hatched)
    {
        if (Cache.TryGetValue((color, gradient, hatched), out var cached))
        {
            return cached;
        }

        Brush brush;
        if (hatched)
        {
            var tile = new DrawingGroup();
            using (var g = tile.Open())
            {
                g.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(color.A * 0.38), color.R, color.G, color.B)), null, new Rect(0, 0, 6, 6));
                var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(color.A * 0.9), color.R, color.G, color.B)), 1.4);
                g.DrawLine(pen, new Point(-1.5, 7.5), new Point(7.5, -1.5));
                g.DrawLine(pen, new Point(-1.5, 1.5), new Point(1.5, -1.5));
                g.DrawLine(pen, new Point(4.5, 7.5), new Point(7.5, 4.5));
            }

            brush = new DrawingBrush(tile)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 6, 6),
                ViewportUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, 6, 6),
                ViewboxUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None,
            };
        }
        else if (gradient)
        {
            brush = new LinearGradientBrush(
                [new GradientStop(color, 0), new GradientStop(Color.FromArgb((byte)(color.A * 0.62), color.R, color.G, color.B), 1)],
                new Point(0, 0), new Point(0, 1));
        }
        else
        {
            brush = new SolidColorBrush(color);
        }

        brush.Freeze();
        Cache[(color, gradient, hatched)] = brush;
        return brush;
    }
}
