using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.Hero;
using AgentSpeed.Presentation.Layout;

namespace AgentSpeed.Orb.Hero;

/// <summary>
/// Every geometry the vehicle layer draws, in canvas units: frozen paths built once from the art, and a few mutable
/// polylines (rig limbs, contrails) whose points are updated in place each frame.
/// </summary>
internal sealed class HeroShapes
{
    public const int ContrailPoints = 24;

    private readonly Geometry?[][] _parts;
    private readonly Geometry?[][] _spokeArcs;
    private readonly Dictionary<(int Vehicle, int Slot), MutablePolyline> _limbs = new();
    private readonly Dictionary<(int Vehicle, int Emitter), MutablePolyline> _contrails = new();

    public HeroShapes(VehicleArt art)
    {
        _parts = new Geometry?[art.Vehicles.Count][];
        _spokeArcs = new Geometry?[art.Vehicles.Count][];
        for (var v = 0; v < art.Vehicles.Count; v++)
        {
            var parts = art.Vehicles[v].Parts;
            _parts[v] = new Geometry?[parts.Count];
            _spokeArcs[v] = new Geometry?[parts.Count];
            for (var p = 0; p < parts.Count; p++)
            {
                var part = parts[p];
                if (part.Path is { } path)
                {
                    _parts[v][p] = Build(path, filled: part.Role is PartRole.Flame or PartRole.FlameCore);
                }

                if (part.Role == PartRole.Spoke && part.Spin is { } spin)
                {
                    _spokeArcs[v][p] = SpokeArcs(spin);
                }
            }
        }

        Clouds = art.Scenery.Clouds.Select(c => Build(c, filled: false)).ToArray();
        var cy = art.Scenery.HorizonCenterY;
        var r = art.Scenery.HorizonRadius;
        Horizon = Arc(new Point(50, cy), r, Math.PI * 1.15, Math.PI * 1.85);
        HorizonGlow = Arc(new Point(50, cy + 1.4), r, Math.PI * 1.2, Math.PI * 1.8);
        Diamond = Polygon(new Point(-0.9, 0), new Point(0, -1), new Point(0.9, 0), new Point(0, 1));
        Pop = PopShape();
    }

    public IReadOnlyList<Geometry> Clouds { get; }

    public Geometry Horizon { get; }

    public Geometry HorizonGlow { get; }

    /// <summary>Unit diamond (half-width 0.9, half-height 1) for the shock diamonds.</summary>
    public Geometry Diamond { get; }

    /// <summary>Exhaust flame pop of length 1 along +x (scale x by the length).</summary>
    public Geometry Pop { get; }

    public Geometry? Part(int vehicle, int part) => _parts[vehicle][part];

    /// <summary>The two motion arcs of a spinning spoke set, at rotation 0 about its pivot.</summary>
    public Geometry? SpokeArcs(int vehicle, int part) => _spokeArcs[vehicle][part];

    /// <summary>A rig limb's polyline with its points updated to <paramref name="points"/>.</summary>
    public Geometry Limb(int vehicle, int slot, ReadOnlySpan<PointD> points) =>
        Polyline(_limbs, (vehicle, slot), points);

    /// <summary>A contrail polyline with its points updated (<see cref="ContrailPoints"/> points).</summary>
    public Geometry Contrail(int vehicle, int emitter, ReadOnlySpan<PointD> points) =>
        Polyline(_contrails, (vehicle, emitter), points);

    private static Geometry Polyline(Dictionary<(int, int), MutablePolyline> cache, (int, int) key, ReadOnlySpan<PointD> points)
    {
        if (!cache.TryGetValue(key, out var line) || line.Count != points.Length)
        {
            line = new MutablePolyline(points.Length);
            cache[key] = line;
        }

        line.Set(points);
        return line.Geometry;
    }

    public static StreamGeometry Build(PathData path, bool filled)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            var segments = path.Segments;
            for (var i = 0; i < segments.Count; i++)
            {
                var s = segments[i];
                switch (s.Verb)
                {
                    case PathVerb.Move:
                        g.BeginFigure(P(s.P1), filled, FigureCloses(segments, i));
                        break;
                    case PathVerb.Line:
                        g.LineTo(P(s.P1), true, true);
                        break;
                    case PathVerb.Quad:
                        g.QuadraticBezierTo(P(s.P1), P(s.P2), true, true);
                        break;
                    case PathVerb.Cubic:
                        g.BezierTo(P(s.P1), P(s.P2), P(s.P3), true, true);
                        break;
                }
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static bool FigureCloses(IReadOnlyList<Presentation.Hero.PathSegment> segments, int move)
    {
        for (var i = move + 1; i < segments.Count; i++)
        {
            if (segments[i].Verb == PathVerb.Move)
            {
                return false;
            }

            if (segments[i].Verb == PathVerb.Close)
            {
                return true;
            }
        }

        return false;
    }

    private static Geometry SpokeArcs(SpinSpec spin)
    {
        var c = new Point(spin.Pivot.X, spin.Pivot.Y);
        var r = spin.Radius - 2;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            AddArc(g, c, 0.72 * r, 0, 2.1);
            AddArc(g, c, 0.45 * r, Math.PI, Math.PI + 2.1);
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>A clockwise (y down) arc from <paramref name="start"/> to <paramref name="end"/> radians.</summary>
    private static Geometry Arc(Point center, double radius, double start, double end)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            AddArc(g, center, radius, start, end);
        }

        geometry.Freeze();
        return geometry;
    }

    private static void AddArc(StreamGeometryContext g, Point center, double radius, double start, double end)
    {
        g.BeginFigure(new Point(center.X + radius * Math.Cos(start), center.Y + radius * Math.Sin(start)), false, false);
        g.ArcTo(new Point(center.X + radius * Math.Cos(end), center.Y + radius * Math.Sin(end)), new Size(radius, radius), 0,
            end - start > Math.PI, SweepDirection.Clockwise, true, true);
    }

    private static Geometry Polygon(params Point[] points)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(points[0], false, true);
            for (var i = 1; i < points.Length; i++)
            {
                g.LineTo(points[i], true, true);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static Geometry PopShape()
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(0, -0.9), true, true);
            g.QuadraticBezierTo(new Point(0.6, -0.9), new Point(1, 0), true, true);
            g.QuadraticBezierTo(new Point(0.6, 0.9), new Point(0, 0.9), true, true);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Point P(PointD p) => new(p.X, p.Y);

    /// <summary>An open polyline whose points change in place (no new geometry per frame).</summary>
    private sealed class MutablePolyline
    {
        private readonly PathFigure _figure;
        private readonly PolyLineSegment _segment;

        public MutablePolyline(int count)
        {
            Count = count;
            _segment = new PolyLineSegment(new Point[Math.Max(1, count - 1)], true) { IsSmoothJoin = true };
            _figure = new PathFigure(default, [_segment], false) { IsFilled = false };
            Geometry = new PathGeometry([_figure]);
        }

        public int Count { get; }

        public PathGeometry Geometry { get; }

        public void Set(ReadOnlySpan<PointD> points)
        {
            var start = P(points[0]);
            if (_figure.StartPoint != start)
            {
                _figure.StartPoint = start;
            }

            var list = _segment.Points;
            for (var i = 1; i < points.Length; i++)
            {
                var point = P(points[i]);
                if (list[i - 1] != point)
                {
                    list[i - 1] = point;
                }
            }
        }
    }
}
