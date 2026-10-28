using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Orb.Styles;
using AgentSpeed.Theme;

namespace AgentSpeed.Orb.Render.Styles;

/// <summary>
/// Frozen solid brushes and pens of the liquid and reactor renderers, keyed by the exact 8-bit color and a 1/256 DIP
/// width, so the faint glows (a few percent alpha) keep their precision and steady frames allocate nothing.
/// </summary>
internal sealed class LiquidReactorInk
{
    private const int MaxBrushes = 2048;
    private const int MaxPens = 1024;

    private readonly Dictionary<uint, SolidColorBrush> _brushes = new();
    private readonly Dictionary<(uint Color, int Width, PenLineCap Cap), Pen> _pens = new();

    public SolidColorBrush Brush(Rgba color)
    {
        var key = Pack(color);
        if (_brushes.TryGetValue(key, out var brush))
        {
            return brush;
        }

        if (_brushes.Count >= MaxBrushes)
        {
            _brushes.Clear();
        }

        brush = MediaColors.Frozen(color.ToColor());
        _brushes[key] = brush;
        return brush;
    }

    public Pen Pen(Rgba color, double width, PenLineCap cap = PenLineCap.Flat)
    {
        var key = (Pack(color), (int)Math.Round(Math.Max(0, width) * 256), cap);
        if (_pens.TryGetValue(key, out var pen))
        {
            return pen;
        }

        if (_pens.Count >= MaxPens)
        {
            _pens.Clear();
        }

        pen = MediaColors.FrozenPen(Brush(color), key.Item2 / 256.0, cap);
        _pens[key] = pen;
        return pen;
    }

    public static uint Pack(Rgba c) => (uint)c.A8 << 24 | (uint)c.R8 << 16 | (uint)c.G8 << 8 | c.B8;

    /// <summary>The opaque color as 24 bits (a cache key for visuals whose alpha is applied as opacity).</summary>
    public static uint PackRgb(Rgba c) => (uint)c.R8 << 16 | (uint)c.G8 << 8 | c.B8;

    /// <summary>True when two colors differ by at most <paramref name="tolerance"/> per 8-bit channel.</summary>
    public static bool Near(Rgba a, Rgba b, int tolerance = 2) =>
        Math.Abs(a.R8 - b.R8) <= tolerance && Math.Abs(a.G8 - b.G8) <= tolerance && Math.Abs(a.B8 - b.B8) <= tolerance
        && Math.Abs(a.A8 - b.A8) <= tolerance;
}

/// <summary>Drawing helpers shared by the liquid and reactor renderers (retained, frozen, no blur effects).</summary>
internal static class LiquidReactorShapes
{
    /// <summary>Glow spread (× σ beyond the stroke width) and relative alpha of the three strokes of a soft glow.</summary>
    private static ReadOnlySpan<double> GlowSpread => [4.0, 2.2, 0.8];

    private static ReadOnlySpan<double> GlowWeight => [0.10, 0.16, 0.22];

    /// <summary>The two-stroke glow for content re-recorded every frame.</summary>
    private static ReadOnlySpan<double> LiteSpread => [3.0, 1.2];

    private static ReadOnlySpan<double> LiteWeight => [0.13, 0.2];

    public static BitmapCache DeviceCache() => new(1) { SnapsToDevicePixels = false };

    public static DrawingVisual CachedVisual() => new() { CacheMode = DeviceCache() };

    public static void SetOpacity(Visual visual, double opacity)
    {
        if (visual is ContainerVisual container)
        {
            opacity = Math.Clamp(double.IsFinite(opacity) ? opacity : 0, 0, 1);
            if (Math.Abs(container.Opacity - opacity) > 0.002 || (opacity == 0 && container.Opacity != 0))
            {
                container.Opacity = opacity;
            }
        }
    }

    public static void SetOffset(TranslateTransform transform, double x, double y)
    {
        if (Math.Abs(transform.X - x) > 0.001)
        {
            transform.X = x;
        }

        if (Math.Abs(transform.Y - y) > 0.001)
        {
            transform.Y = y;
        }
    }

    public static void SetAngle(RotateTransform transform, double degrees)
    {
        if (Math.Abs(transform.Angle - degrees) > 0.0005)
        {
            transform.Angle = degrees;
        }
    }

    /// <summary>
    /// The cheap stand-in for a Gaussian glow of σ <paramref name="sigma"/> around a stroke of <paramref name="width"/>:
    /// three (<paramref name="lite"/>: two, for content re-recorded every frame) widening translucent strokes behind it,
    /// their alphas scaled so the glow carries about the stroke's energy.
    /// </summary>
    public static void Glow(DrawingContext dc, LiquidReactorInk ink, Geometry geometry, Rgba color, double width, double sigma,
        PenLineCap cap = PenLineCap.Round, bool lite = false)
    {
        if (color.A < 0.004 || !(sigma > 0) || !(width > 0))
        {
            return;
        }

        var spread = lite ? LiteSpread : GlowSpread;
        var weight = lite ? LiteWeight : GlowWeight;
        var scale = GlowScale(width, sigma, spread, weight);
        for (var k = 0; k < spread.Length; k++)
        {
            dc.DrawGeometry(null, ink.Pen(color.MultiplyAlpha(weight[k] * scale), width + spread[k] * sigma, cap), geometry);
        }
    }

    /// <summary>A stroke blurred by σ (no glow core of its own): three nested translucent strokes, peak ≈ 0.75.</summary>
    public static void BlurredStroke(DrawingContext dc, LiquidReactorInk ink, Geometry geometry, Rgba color, double width, double sigma,
        PenLineCap cap = PenLineCap.Round)
    {
        if (color.A < 0.004 || !(width > 0))
        {
            return;
        }

        sigma = Math.Max(0, sigma);
        dc.DrawGeometry(null, ink.Pen(color.MultiplyAlpha(0.3), width + 3 * sigma, cap), geometry);
        dc.DrawGeometry(null, ink.Pen(color.MultiplyAlpha(0.35), width + 1.4 * sigma, cap), geometry);
        dc.DrawGeometry(null, ink.Pen(color.MultiplyAlpha(0.4), Math.Max(width * 0.5, width - sigma), cap), geometry);
    }

    /// <summary>Glow strokes for a gradient arc: the glow color with the arc's alpha ramp from tail to head.</summary>
    public static void ArcGlow(DrawingContext dc, Point center, double radius, double tailDegrees, double lengthDegrees, Geometry arc,
        Rgba color, double tailAlphaRatio, double width, double sigma)
    {
        if (color.A < 0.004 || !(sigma > 0) || !(width > 0))
        {
            return;
        }

        var scale = GlowScale(width, sigma, GlowSpread, GlowWeight);
        for (var k = 0; k < GlowSpread.Length; k++)
        {
            var head = color.MultiplyAlpha(GlowWeight[k] * scale);
            var brush = ArcBrush(center, radius, tailDegrees, lengthDegrees, head.MultiplyAlpha(tailAlphaRatio), head, 2);
            dc.DrawGeometry(null, MediaColors.FrozenPen(brush, width + GlowSpread[k] * sigma, PenLineCap.Round), arc);
        }
    }

    /// <summary>
    /// A frozen linear gradient along the chord of an arc (clockwise from <paramref name="tailDegrees"/>), with stops
    /// placed so the color runs evenly along the arc (<see cref="ReactorArcGradient"/>); colors mix in OKLab.
    /// </summary>
    public static Brush ArcBrush(Point center, double radius, double tailDegrees, double lengthDegrees, Rgba tail, Rgba head, int steps)
    {
        var length = Math.Clamp(lengthDegrees, 0, ReactorArcGradient.MaxLengthDegrees);
        if (length < 0.5)
        {
            return MediaColors.Frozen(head.ToColor());
        }

        steps = Math.Max(1, steps);
        var stops = new GradientStopCollection(steps + 1);
        for (var i = 0; i <= steps; i++)
        {
            var u = i / (double)steps;
            stops.Add(new GradientStop(OkLab.Mix(tail, head, u).ToColor(), ReactorArcGradient.ChordOffset(u, length)));
        }

        var brush = new LinearGradientBrush(stops)
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = OrbBrushes.Polar(center, radius, tailDegrees),
            EndPoint = OrbBrushes.Polar(center, radius, tailDegrees + length),
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>A stroke-only arc, clockwise from <paramref name="startDegrees"/> over <paramref name="sweepDegrees"/>.</summary>
    public static Geometry Arc(Point center, double radius, double startDegrees, double sweepDegrees) =>
        OrbBrushes.Arc(center, radius, startDegrees, startDegrees + Math.Max(0.01, sweepDegrees));

    private static double GlowScale(double width, double sigma, ReadOnlySpan<double> spread, ReadOnlySpan<double> weight)
    {
        var energy = 0.0;
        for (var k = 0; k < spread.Length; k++)
        {
            energy += (width + spread[k] * sigma) * weight[k];
        }

        return Math.Clamp(width / energy, 0.45, 1);
    }
}
