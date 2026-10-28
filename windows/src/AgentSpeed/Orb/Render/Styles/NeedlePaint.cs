using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Orb.Styles;
using AgentSpeed.Theme;

namespace AgentSpeed.Orb.Render.Styles;

/// <summary>
/// Frozen brushes, pens and glow drawings of the <c>needle</c> and <c>minimal</c> renderers. Brushes are keyed by the
/// exact 8-bit color because glow tails need finer alpha steps than <see cref="BrushCache"/>'s 31. Used only while a
/// cached layer is re-recorded; bounded, and cleared on Configure.
/// </summary>
internal sealed class NeedlePaint
{
    private const int Capacity = 2048;
    private const int DiscStops = 16;

    private readonly Dictionary<uint, SolidColorBrush> _brushes = new();
    private readonly Dictionary<(uint Color, long Thickness, PenLineCap Cap), Pen> _pens = new();
    private readonly Dictionary<(uint Color, long Radius, long Sigma), RadialGradientBrush> _discs = new();

    public static uint Pack(Rgba color, double alpha = 1)
    {
        var a = (uint)Math.Clamp((int)Math.Round(color.A * alpha * 255), 0, 255);
        return a << 24 | (uint)color.R8 << 16 | (uint)color.G8 << 8 | color.B8;
    }

    /// <summary>A color with each channel rounded to 6 bits, for change keys that ignore invisible steps.</summary>
    public static Rgba Coarse(Rgba color) =>
        new(Round6(color.R), Round6(color.G), Round6(color.B), Round6(color.A));

    /// <summary>The 24-bit key of a <see cref="Coarse"/> color.</summary>
    public static long CoarseKey(Rgba color) =>
        (long)Math.Round(Math.Clamp(color.R, 0, 1) * 63) << 18 | (long)Math.Round(Math.Clamp(color.G, 0, 1) * 63) << 12
        | (long)Math.Round(Math.Clamp(color.B, 0, 1) * 63) << 6 | (long)Math.Round(Math.Clamp(color.A, 0, 1) * 63);

    public SolidColorBrush Brush(Rgba color, double alpha = 1)
    {
        var key = Pack(color, alpha);
        if (_brushes.TryGetValue(key, out var brush))
        {
            return brush;
        }

        Trim(_brushes);
        brush = MediaColors.Frozen(Color.FromArgb((byte)(key >> 24), color.R8, color.G8, color.B8));
        _brushes[key] = brush;
        return brush;
    }

    public Pen Pen(Rgba color, double alpha, double thickness, PenLineCap cap = PenLineCap.Flat)
    {
        var key = (Pack(color, alpha), (long)Math.Round(thickness * 256), cap);
        if (_pens.TryGetValue(key, out var pen))
        {
            return pen;
        }

        Trim(_pens);
        pen = MediaColors.FrozenPen(Brush(color, alpha), Math.Max(0.01, thickness), cap);
        _pens[key] = pen;
        return pen;
    }

    /// <summary>
    /// A disc of <paramref name="radius"/> (DIP) blurred by σ <paramref name="sigma"/>, as a radial gradient to fill a
    /// circle of <paramref name="outerRadius"/>; the color's alpha times <paramref name="alpha"/> is its full opacity.
    /// </summary>
    public RadialGradientBrush BlurredDisc(Rgba color, double alpha, double radius, double sigma, out double outerRadius)
    {
        sigma = Math.Max(0.05, sigma);
        outerRadius = radius + NeedleGlow.Reach * sigma + sigma;
        var key = (Pack(color, alpha), (long)Math.Round(radius * 64), (long)Math.Round(sigma * 64));
        if (_discs.TryGetValue(key, out var brush))
        {
            return brush;
        }

        Trim(_discs);
        var opacity = color.A * alpha;
        var stops = new GradientStopCollection(DiscStops + 1);
        var from = Math.Max(0, radius - 3 * sigma);
        if (from > 0)
        {
            stops.Add(new GradientStop(color.WithAlpha(opacity * NeedleGlow.BlurredDisc(radius / sigma, 0)).ToColor(), 0));
        }

        for (var i = 0; i < DiscStops; i++)
        {
            var rho = from + (outerRadius - from) * i / (DiscStops - 1);
            var value = i == DiscStops - 1 ? 0 : NeedleGlow.BlurredDisc(radius / sigma, rho / sigma);
            stops.Add(new GradientStop(color.WithAlpha(opacity * value).ToColor(), rho / outerRadius));
        }

        brush = new RadialGradientBrush(stops);
        brush.Freeze();
        _discs[key] = brush;
        return brush;
    }

    /// <summary>
    /// The glow of an open stroke (an arc or line along <paramref name="path"/>, <paramref name="width"/> DIP wide):
    /// stacked widening strokes in <paramref name="color"/> whose composite is a Gaussian blur of σ
    /// <paramref name="sigma"/> (DIP) at <paramref name="opacity"/>. <paramref name="core"/> also matches the inside.
    /// </summary>
    public void GlowStroke(DrawingContext dc, Geometry path, double width, Rgba color, double opacity, double sigma, bool core,
        PenLineCap cap)
    {
        if (!(sigma > 0) || !(opacity * color.A > 0.002))
        {
            return;
        }

        Span<NeedleGlowLayer> layers = stackalloc NeedleGlowLayer[NeedleGlow.MaxLayers];
        var n = NeedleGlow.StrokeLayers(width / sigma, opacity * color.A, core, layers);
        var opaque = color.WithAlpha(1);
        for (var j = 0; j < n; j++)
        {
            var thickness = width + 2 * layers[j].Extent * sigma;
            if (thickness > 0.05 && layers[j].Alpha > 0.001)
            {
                dc.DrawGeometry(null, Pen(opaque, layers[j].Alpha, thickness, cap), path);
            }
        }
    }

    /// <summary><see cref="GlowStroke"/> for a straight round-capped line.</summary>
    public void GlowLine(DrawingContext dc, Point from, Point to, double width, Rgba color, double opacity, double sigma)
    {
        if (!(sigma > 0) || !(opacity * color.A > 0.002))
        {
            return;
        }

        Span<NeedleGlowLayer> layers = stackalloc NeedleGlowLayer[NeedleGlow.MaxLayers];
        var n = NeedleGlow.StrokeLayers(width / sigma, opacity * color.A, false, layers);
        var opaque = color.WithAlpha(1);
        for (var j = 0; j < n; j++)
        {
            if (layers[j].Alpha > 0.001)
            {
                dc.DrawLine(Pen(opaque, layers[j].Alpha, width + 2 * layers[j].Extent * sigma, PenLineCap.Round), from, to);
            }
        }
    }

    /// <summary>
    /// The outer glow of a filled shape about <paramref name="width"/> DIP across (its outline is stroked with the
    /// layers, so only the part outside the shape is exact); returns the composite reached at the outline.
    /// </summary>
    public double GlowOutline(DrawingContext dc, Geometry shape, double width, Rgba color, double opacity, double sigma)
    {
        if (!(sigma > 0) || !(opacity * color.A > 0.002))
        {
            return 0;
        }

        Span<NeedleGlowLayer> layers = stackalloc NeedleGlowLayer[NeedleGlow.MaxLayers];
        var n = NeedleGlow.StrokeLayers(width / sigma, opacity * color.A, false, layers);
        var opaque = color.WithAlpha(1);
        for (var j = 0; j < n; j++)
        {
            if (layers[j].Alpha > 0.001)
            {
                dc.DrawGeometry(null, Pen(opaque, layers[j].Alpha, 2 * layers[j].Extent * sigma), shape);
            }
        }

        return NeedleGlow.Composite(layers[..n]);
    }

    public void Clear()
    {
        _brushes.Clear();
        _pens.Clear();
        _discs.Clear();
    }

    private static double Round6(double value) => Math.Round(Math.Clamp(value, 0, 1) * 63) / 63;

    private static void Trim<TKey, TValue>(Dictionary<TKey, TValue> cache)
        where TKey : notnull
    {
        if (cache.Count >= Capacity)
        {
            cache.Clear();
        }
    }
}
