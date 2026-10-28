using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Gauge;
using AgentSpeed.Theme;

namespace AgentSpeed.Orb;

/// <summary>Frozen brushes and geometries for one orb size and appearance; rebuilt only when those change.</summary>
internal sealed class OrbBrushes
{
    private const int AuraBuckets = 48;

    private readonly RadialGradientBrush?[] _aura = new RadialGradientBrush?[AuraBuckets];
    private readonly Dictionary<int, RadialGradientBrush> _blooms = new();

    public OrbBrushes(double diameter)
    {
        Diameter = diameter;
        var c = Center;
        Segments = new Geometry[GaugeLayout.SegmentCount];
        for (var i = 0; i < Segments.Length; i++)
        {
            var arc = GaugeLayout.Segment(i);
            Segments[i] = AnnularSector(c, arc.InnerRadius * diameter, arc.OuterRadius * diameter, arc.StartDegrees, arc.EndDegrees);
        }
    }

    public double Diameter { get; }

    public Point Center => new(Diameter * 0.75, Diameter * 0.75);

    public Geometry[] Segments { get; }

    public BrushCache Solid { get; } = new();

    public RadialGradientBrush Aura(double heat)
    {
        var bucket = HeatPalette.Bucket(heat, AuraBuckets);
        if (_aura[bucket] is { } cached)
        {
            return cached;
        }

        var color = HeatPalette.Sample(bucket / (double)(AuraBuckets - 1));
        var brush = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
            GradientStops =
            {
                new GradientStop(color.ToColor(1), 0),
                new GradientStop(color.ToColor(1), 0.6),
                new GradientStop(color.ToColor(0.55), 0.74),
                new GradientStop(color.ToColor(0.18), 0.88),
                new GradientStop(color.ToColor(0), 1),
            },
        };
        brush.Freeze();
        _aura[bucket] = brush;
        return brush;
    }

    /// <summary>Soft radial bloom (the "blurred copy" of a lit element) without a per-frame blur effect.</summary>
    public RadialGradientBrush Bloom(Rgba color)
    {
        var key = color.R8 << 16 | color.G8 << 8 | color.B8;
        if (_blooms.TryGetValue(key, out var brush))
        {
            return brush;
        }

        brush = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(color.ToColor(0.85), 0),
                new GradientStop(color.ToColor(0.35), 0.45),
                new GradientStop(color.ToColor(0), 1),
            },
        };
        brush.Freeze();
        if (_blooms.Count > 256)
        {
            _blooms.Clear();
        }

        _blooms[key] = brush;
        return brush;
    }

    public static Geometry AnnularSector(Point center, double inner, double outer, double startDegrees, double endDegrees)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            var large = endDegrees - startDegrees > 180;
            g.BeginFigure(Polar(center, outer, startDegrees), isFilled: true, isClosed: true);
            g.ArcTo(Polar(center, outer, endDegrees), new Size(outer, outer), 0, large, SweepDirection.Clockwise, true, false);
            g.LineTo(Polar(center, inner, endDegrees), true, false);
            g.ArcTo(Polar(center, inner, startDegrees), new Size(inner, inner), 0, large, SweepDirection.Counterclockwise, true, false);
        }

        geometry.Freeze();
        return geometry;
    }

    public static Geometry Arc(Point center, double radius, double startDegrees, double endDegrees)
    {
        var sweep = Math.Clamp(endDegrees - startDegrees, 0, 359.99);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(Polar(center, radius, startDegrees), false, false);
            g.ArcTo(Polar(center, radius, startDegrees + sweep), new Size(radius, radius), 0, sweep > 180,
                SweepDirection.Clockwise, true, false);
        }

        geometry.Freeze();
        return geometry;
    }

    public static Point Polar(Point center, double radius, double degrees)
    {
        var v = GaugeLayout.Polar(radius, degrees);
        return new Point(center.X + v.X, center.Y + v.Y);
    }
}
