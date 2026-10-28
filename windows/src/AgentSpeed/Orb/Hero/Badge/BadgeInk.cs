using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.Colors;

namespace AgentSpeed.Orb.Hero.Badge;

/// <summary>
/// Pooled, mutable pens, brushes and arc paths for the badge layer: each one is handed out once per frame and updated in
/// place, so a frame never allocates once the pools have grown to the badge's working set.
/// </summary>
internal sealed class BadgeInk
{
    private readonly List<PenSlot> _pens = [];
    private readonly List<PenSlot> _flatPens = [];
    private readonly List<SolidColorBrush> _brushes = [];
    private readonly List<FadeSlot> _fades = [];
    private readonly List<RadialSlot> _radials = [];
    private readonly List<ArcSlot> _arcs = [];
    private int _nextPen;
    private int _nextFlatPen;
    private int _nextBrush;
    private int _nextFade;
    private int _nextRadial;
    private int _nextArc;

    /// <summary>Starts a frame: every pool hands out its items again from the first one.</summary>
    public void BeginFrame()
    {
        _nextPen = 0;
        _nextFlatPen = 0;
        _nextBrush = 0;
        _nextFade = 0;
        _nextRadial = 0;
        _nextArc = 0;
    }

    public static Color ToColor(Rgba color, double alpha) => HeroInk.ToColor(color, alpha);

    /// <summary>A round-capped pen <paramref name="width"/> canvas units wide.</summary>
    public Pen Pen(Rgba color, double alpha, double width)
    {
        if (_nextPen == _pens.Count)
        {
            _pens.Add(new PenSlot());
        }

        return _pens[_nextPen++].Set(ToColor(color, alpha), width);
    }

    /// <summary>A flat-capped pen, for arc segments that join end to end.</summary>
    public Pen FlatPen(Rgba color, double alpha, double width)
    {
        if (_nextFlatPen == _flatPens.Count)
        {
            _flatPens.Add(new PenSlot(PenLineCap.Flat));
        }

        return _flatPens[_nextFlatPen++].Set(ToColor(color, alpha), width);
    }

    public SolidColorBrush Brush(Rgba color, double alpha)
    {
        if (_nextBrush == _brushes.Count)
        {
            _brushes.Add(new SolidColorBrush());
        }

        var brush = _brushes[_nextBrush++];
        var value = ToColor(color, alpha);
        if (brush.Color != value)
        {
            brush.Color = value;
        }

        return brush;
    }

    /// <summary>A stroke fading linearly from <paramref name="alpha"/> at <paramref name="head"/> to 0 at <paramref name="tail"/>.</summary>
    public Pen Fade(Rgba color, double alpha, double width, Point head, Point tail)
    {
        if (_nextFade == _fades.Count)
        {
            _fades.Add(new FadeSlot());
        }

        return _fades[_nextFade++].Set(ToColor(color, alpha), ToColor(color, 0), width, head, tail);
    }

    /// <summary>
    /// A radial gradient over the drawn ellipse: <paramref name="center"/> at 0, <paramref name="middle"/> at
    /// <paramref name="offset"/>, <paramref name="edge"/> at 1.
    /// </summary>
    public RadialGradientBrush Radial(Color center, Color middle, double offset, Color edge)
    {
        if (_nextRadial == _radials.Count)
        {
            _radials.Add(new RadialSlot());
        }

        return _radials[_nextRadial++].Set(center, middle, offset, edge);
    }

    /// <summary>An open arc on a circle, clockwise from <paramref name="startDegrees"/> (0 = 3 o'clock, y down).</summary>
    public Geometry Arc(Point center, double radius, double startDegrees, double sweepDegrees)
    {
        if (_nextArc == _arcs.Count)
        {
            _arcs.Add(new ArcSlot());
        }

        return _arcs[_nextArc++].Set(center, radius, startDegrees, Math.Clamp(sweepDegrees, 0, 359.9));
    }

    private sealed class PenSlot
    {
        private readonly SolidColorBrush _brush = new();
        private readonly Pen _pen;

        public PenSlot(PenLineCap cap = PenLineCap.Round)
        {
            _pen = new Pen(_brush, 1) { StartLineCap = cap, EndLineCap = cap, LineJoin = PenLineJoin.Round };
        }

        public Pen Set(Color color, double width)
        {
            if (_brush.Color != color)
            {
                _brush.Color = color;
            }

            if (_pen.Thickness != width)
            {
                _pen.Thickness = width;
            }

            return _pen;
        }
    }

    private sealed class FadeSlot
    {
        private readonly GradientStop _head = new(Colors.Transparent, 0);
        private readonly GradientStop _tail = new(Colors.Transparent, 1);
        private readonly LinearGradientBrush _brush;
        private readonly Pen _pen;

        public FadeSlot()
        {
            _brush = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, GradientStops = { _head, _tail } };
            _pen = new Pen(_brush, 1) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        }

        public Pen Set(Color head, Color tail, double width, Point from, Point to)
        {
            if (_head.Color != head)
            {
                _head.Color = head;
            }

            if (_tail.Color != tail)
            {
                _tail.Color = tail;
            }

            if (_brush.StartPoint != from)
            {
                _brush.StartPoint = from;
            }

            if (_brush.EndPoint != to)
            {
                _brush.EndPoint = to;
            }

            if (_pen.Thickness != width)
            {
                _pen.Thickness = width;
            }

            return _pen;
        }
    }

    private sealed class RadialSlot
    {
        private readonly GradientStop _center = new(Colors.Transparent, 0);
        private readonly GradientStop _middle = new(Colors.Transparent, 0.5);
        private readonly GradientStop _edge = new(Colors.Transparent, 1);

        public RadialSlot()
        {
            Brush = new RadialGradientBrush { GradientStops = { _center, _middle, _edge } };
        }

        public RadialGradientBrush Brush { get; }

        public RadialGradientBrush Set(Color center, Color middle, double offset, Color edge)
        {
            if (_center.Color != center)
            {
                _center.Color = center;
            }

            if (_middle.Color != middle)
            {
                _middle.Color = middle;
            }

            if (_middle.Offset != offset)
            {
                _middle.Offset = offset;
            }

            if (_edge.Color != edge)
            {
                _edge.Color = edge;
            }

            return Brush;
        }
    }

    private sealed class ArcSlot
    {
        private readonly PathFigure _figure;
        private readonly ArcSegment _segment = new() { SweepDirection = SweepDirection.Clockwise, IsStroked = true };
        private readonly PathGeometry _geometry;

        public ArcSlot()
        {
            _figure = new PathFigure { IsClosed = false, IsFilled = false, Segments = { _segment } };
            _geometry = new PathGeometry { Figures = { _figure } };
        }

        public Geometry Set(Point center, double radius, double startDegrees, double sweepDegrees)
        {
            var a0 = startDegrees * Math.PI / 180;
            var a1 = (startDegrees + sweepDegrees) * Math.PI / 180;
            var start = new Point(center.X + radius * Math.Cos(a0), center.Y + radius * Math.Sin(a0));
            var end = new Point(center.X + radius * Math.Cos(a1), center.Y + radius * Math.Sin(a1));
            var size = new Size(radius, radius);
            var large = sweepDegrees > 180;
            if (_figure.StartPoint != start)
            {
                _figure.StartPoint = start;
            }

            if (_segment.Point != end)
            {
                _segment.Point = end;
            }

            if (_segment.Size != size)
            {
                _segment.Size = size;
            }

            if (_segment.IsLargeArc != large)
            {
                _segment.IsLargeArc = large;
            }

            return _geometry;
        }
    }
}
