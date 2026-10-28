using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.Colors;

namespace AgentSpeed.Orb.Hero;

/// <summary>
/// Frozen brushes and round-capped pens for the vehicle layer, keyed by quantized color, alpha and width, so a steady
/// frame reuses every one of them.
/// </summary>
internal sealed class HeroInk
{
    private const int MaxEntries = 4096;

    private readonly Dictionary<uint, SolidColorBrush> _brushes = new();
    private readonly Dictionary<(uint Color, int Width), Pen> _pens = new();
    private readonly Dictionary<uint, RadialGradientBrush> _flashes = new();
    private readonly List<MatrixTransform> _transforms = [];
    private readonly List<FadeStroke> _fades = [];
    private int _nextTransform;
    private int _nextFade;

    /// <summary>Starts a frame: pooled transforms and fade pens are handed out again from the first one.</summary>
    public void BeginFrame()
    {
        _nextTransform = 0;
        _nextFade = 0;
    }

    /// <summary>A pooled transform set to <paramref name="matrix"/>; each one is used once per frame.</summary>
    public MatrixTransform Transform(in Matrix matrix)
    {
        if (_nextTransform < _transforms.Count)
        {
            var pooled = _transforms[_nextTransform++];
            if (pooled.Matrix != matrix)
            {
                pooled.Matrix = matrix;
            }

            return pooled;
        }

        var transform = new MatrixTransform(matrix);
        _transforms.Add(transform);
        _nextTransform++;
        return transform;
    }

    /// <summary>A pooled stroke fading linearly from <paramref name="alpha"/> at <paramref name="from"/> to 0 at <paramref name="to"/>.</summary>
    public Pen Fade(Rgba color, double alpha, double width, Point from, Point to)
    {
        if (_nextFade == _fades.Count)
        {
            _fades.Add(new FadeStroke());
        }

        return _fades[_nextFade++].Set(ToColor(color, alpha), ToColor(color, 0), Math.Round(width * 100) / 100, from, to);
    }

    public SolidColorBrush Brush(Rgba color, double alpha = 1)
    {
        var key = Key(color, alpha);
        if (!_brushes.TryGetValue(key, out var brush))
        {
            if (_brushes.Count > MaxEntries)
            {
                _brushes.Clear();
            }

            brush = new SolidColorBrush(Color.FromArgb((byte)(key >> 24), (byte)(key >> 16), (byte)(key >> 8), (byte)key));
            brush.Freeze();
            _brushes[key] = brush;
        }

        return brush;
    }

    /// <summary>A pen <paramref name="width"/> canvas units wide (quantized to 1/100), or null when invisible.</summary>
    public Pen? Pen(Rgba color, double alpha, double width)
    {
        if (alpha <= 0.004 || width <= 0.005)
        {
            return null;
        }

        var key = (Key(color, alpha), (int)Math.Round(width * 100));
        if (!_pens.TryGetValue(key, out var pen))
        {
            if (_pens.Count > MaxEntries)
            {
                _pens.Clear();
            }

            pen = new Pen(Brush(color, alpha), key.Item2 / 100.0)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            pen.Freeze();
            _pens[key] = pen;
        }

        return pen;
    }

    /// <summary>A horizontal light band 18 units wide centered on <paramref name="x"/> (the idle sweep).</summary>
    public static Brush Band(Rgba color, double alpha, double x)
    {
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(x - 9, 0),
            EndPoint = new Point(x + 9, 0),
            GradientStops =
            {
                new GradientStop(ToColor(color, 0), 0),
                new GradientStop(ToColor(color, alpha), 0.5),
                new GradientStop(ToColor(color, 0), 1),
            },
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>The upgrade flash: <paramref name="center"/> color fading through the accent to transparent.</summary>
    public RadialGradientBrush Flash(Rgba center, Rgba hot)
    {
        var key = Key(center, 1) ^ (Key(hot, 1) * 2654435761u);
        if (!_flashes.TryGetValue(key, out var brush))
        {
            if (_flashes.Count > 256)
            {
                _flashes.Clear();
            }

            brush = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(ToColor(center, 1), 0),
                    new GradientStop(ToColor(hot, 0.45), 0.45),
                    new GradientStop(ToColor(hot, 0), 1),
                },
            };
            brush.Freeze();
            _flashes[key] = brush;
        }

        return brush;
    }

    public void Clear()
    {
        _brushes.Clear();
        _pens.Clear();
        _flashes.Clear();
    }

    public static Color ToColor(Rgba color, double alpha) =>
        Color.FromArgb((byte)Math.Clamp((int)Math.Round(alpha * color.A * 255), 0, 255), color.R8, color.G8, color.B8);

    /// <summary>A mutable gradient pen whose ends and colors are updated in place.</summary>
    private sealed class FadeStroke
    {
        private readonly LinearGradientBrush _brush;
        private readonly GradientStop _head = new(Colors.Transparent, 0);
        private readonly GradientStop _tail = new(Colors.Transparent, 1);
        private readonly Pen _pen;

        public FadeStroke()
        {
            _brush = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, GradientStops = { _head, _tail } };
            _pen = new Pen(_brush, 1) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
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

    private static uint Key(Rgba color, double alpha)
    {
        var a = (uint)Math.Clamp((int)Math.Round(Math.Clamp(alpha, 0, 1) * color.A * 255), 0, 255);
        return a << 24 | (uint)color.R8 << 16 | (uint)color.G8 << 8 | color.B8;
    }
}
