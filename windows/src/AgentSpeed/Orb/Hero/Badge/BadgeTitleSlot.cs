using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Hero;
using AgentSpeed.Presentation.Hero.Badge;

namespace AgentSpeed.Orb.Hero.Badge;

/// <summary>How one title is filled this frame.</summary>
/// <param name="Gradient">Dark mode: a vertical gradient from <paramref name="Top"/> to <paramref name="Bottom"/> over the ink box.</param>
internal readonly record struct BadgeFill(Rgba Top, Rgba Bottom, bool Gradient);

/// <summary>The glow under one title: the two pre-blurred copies' opacities, or the two stroke widths without a blur.</summary>
internal readonly record struct BadgeGlowPaint(Rgba Color, double Near, double Far, double NearWidth, double FarWidth);

/// <summary>
/// One title on screen (the incoming or the outgoing one): its glyph outlines recorded once into a crisp visual and two
/// glow visuals (σ 1.6 and 4.2 units, pre-blurred by a fixed BlurEffect). Per frame only the shared transform, the
/// brush colors and the visual opacities change; the drawings are recorded again only when the text or the kind of
/// fill changes.
/// </summary>
internal sealed class BadgeTitleSlot
{
    private readonly MatrixTransform _transform = new();
    private readonly SolidColorBrush _solid = new();
    private readonly GradientStop _top = new(Colors.Transparent, 0);
    private readonly GradientStop _bottom = new(Colors.Transparent, 1);
    private readonly LinearGradientBrush _gradient;
    private readonly GradientStop _bandStart = new(Colors.Transparent, 0);
    private readonly GradientStop _bandPeak = new(Colors.Transparent, 0.5);
    private readonly GradientStop _bandEnd = new(Colors.Transparent, 1);
    private readonly LinearGradientBrush _band;
    private readonly SolidColorBrush _glow = new();
    private readonly Pen _nearPen;
    private readonly Pen _farPen;
    private (BadgeGlyphs? Glyphs, bool Gradient, bool Band) _crispKey;
    private (BadgeGlyphs? Glyphs, bool Strokes) _nearKey;
    private (BadgeGlyphs? Glyphs, bool Strokes) _farKey;

    public BadgeTitleSlot()
    {
        _gradient = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, GradientStops = { _top, _bottom } };
        _band = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, GradientStops = { _bandStart, _bandPeak, _bandEnd } };
        _nearPen = new Pen(_glow, 1) { LineJoin = PenLineJoin.Round };
        _farPen = new Pen(_glow, 1) { LineJoin = PenLineJoin.Round };
    }

    public DrawingVisual Crisp { get; } = new();

    public DrawingVisual GlowNear { get; } = new();

    public DrawingVisual GlowFar { get; } = new();

    /// <summary>Uses the blurred copies (<paramref name="near"/>, <paramref name="far"/>) or, when null, the stroke fallback.</summary>
    public void SetEffects(Effect? near, Effect? far)
    {
        GlowNear.Effect = near;
        GlowFar.Effect = far;
        _nearKey = default;
        _farKey = default;
    }

    /// <param name="band">The sheen or the idle light band, with its color.</param>
    /// <param name="strokes">Glow drawn as two wider strokes (no blur effect).</param>
    public void Show(BadgeGlyphs glyphs, in BadgeTitleItem item, in BadgeFill fill, (BadgeBand Band, Rgba Color)? band,
        in BadgeGlowPaint glow, bool strokes)
    {
        var s = item.Scale;
        var matrix = new Matrix(s, 0, 0, s, 50 - 50 * s, 50 - 50 * s + item.Dy);
        if (_transform.Matrix != matrix)
        {
            _transform.Matrix = matrix;
        }

        if (fill.Gradient)
        {
            Set(_top, BadgeInk.ToColor(fill.Top, 1));
            Set(_bottom, BadgeInk.ToColor(fill.Bottom, 1));
            var start = new Point(50, glyphs.InkTop);
            var end = new Point(50, glyphs.InkBottom);
            if (_gradient.StartPoint != start)
            {
                _gradient.StartPoint = start;
            }

            if (_gradient.EndPoint != end)
            {
                _gradient.EndPoint = end;
            }
        }
        else
        {
            var color = BadgeInk.ToColor(fill.Top, 1);
            if (_solid.Color != color)
            {
                _solid.Color = color;
            }
        }

        if (band is { } b)
        {
            SetBand(b.Band, b.Color);
        }

        var crispKey = (glyphs, fill.Gradient, band is not null);
        if (crispKey != _crispKey)
        {
            _crispKey = crispKey;
            using var dc = Crisp.RenderOpen();
            dc.PushTransform(_transform);
            dc.DrawGeometry(fill.Gradient ? _gradient : _solid, null, glyphs.Geometry);
            if (band is not null)
            {
                dc.DrawGeometry(_band, null, glyphs.Geometry);
            }

            dc.Pop();
        }

        SetOpacity(Crisp, HeroMath.Clamp01(item.Alpha));

        var glowColor = BadgeInk.ToColor(glow.Color, 1);
        if (_glow.Color != glowColor)
        {
            _glow.Color = glowColor;
        }

        if (strokes)
        {
            if (_nearPen.Thickness != glow.NearWidth)
            {
                _nearPen.Thickness = glow.NearWidth;
            }

            if (_farPen.Thickness != glow.FarWidth)
            {
                _farPen.Thickness = glow.FarWidth;
            }
        }

        var near = HeroMath.Clamp01(glow.Near * item.Alpha);
        var far = HeroMath.Clamp01(glow.Far * item.Alpha);
        RecordGlow(GlowNear, ref _nearKey, near > 0.003 ? glyphs : null, strokes ? _nearPen : null, strokes);
        RecordGlow(GlowFar, ref _farKey, far > 0.003 ? glyphs : null, strokes ? _farPen : null, strokes);
        SetOpacity(GlowNear, near);
        SetOpacity(GlowFar, far);
    }

    public void Hide()
    {
        if (_crispKey.Glyphs is not null)
        {
            _crispKey = default;
            using (Crisp.RenderOpen())
            {
            }
        }

        RecordGlow(GlowNear, ref _nearKey, null, null, false);
        RecordGlow(GlowFar, ref _farKey, null, null, false);
    }

    private void RecordGlow(DrawingVisual visual, ref (BadgeGlyphs? Glyphs, bool Strokes) key, BadgeGlyphs? glyphs, Pen? pen, bool strokes)
    {
        var next = (glyphs, glyphs is not null && strokes);
        if (next == key)
        {
            return;
        }

        key = next;
        using var dc = visual.RenderOpen();
        if (glyphs is null)
        {
            return;
        }

        dc.PushTransform(_transform);
        dc.DrawGeometry(_glow, pen, glyphs.Geometry);
        dc.Pop();
    }

    /// <summary>
    /// The band's profile is <c>max(0, 1 − |dx| / halfWidth)</c> with <c>dx = x − (X + (y − 50) slope)</c>: a linear
    /// gradient across the band, perpendicular to its edges.
    /// </summary>
    private void SetBand(in BadgeBand band, Rgba color)
    {
        var k = band.HalfWidth / (1 + band.Slope * band.Slope);
        var start = new Point(band.X - k, 50 + k * band.Slope);
        var end = new Point(band.X + k, 50 - k * band.Slope);
        if (_band.StartPoint != start)
        {
            _band.StartPoint = start;
        }

        if (_band.EndPoint != end)
        {
            _band.EndPoint = end;
        }

        var clear = BadgeInk.ToColor(color, 0);
        Set(_bandStart, clear);
        Set(_bandPeak, BadgeInk.ToColor(color, band.Alpha));
        Set(_bandEnd, clear);
    }

    private static void Set(GradientStop stop, Color color)
    {
        if (stop.Color != color)
        {
            stop.Color = color;
        }
    }

    private static void SetOpacity(DrawingVisual visual, double opacity)
    {
        if (Math.Abs(visual.Opacity - opacity) > 0.0005)
        {
            visual.Opacity = opacity;
        }
    }
}
