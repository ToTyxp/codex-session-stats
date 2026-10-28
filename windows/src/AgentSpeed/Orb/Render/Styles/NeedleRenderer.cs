using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.Orb;
using AgentSpeed.Presentation.Orb.Styles;
using AgentSpeed.Presentation.Render;

namespace AgentSpeed.Orb.Render.Styles;

/// <summary>
/// The <c>needle</c> style (DESIGN 2a.2) on retained visuals, back to front: dial band with stop pins and lamp housings,
/// red-zone glow, red zone, graduations, trace, five lamps, wake, needle shadow, needle (glow, blade with spine, tip
/// flare), peg sparks and hub. Every layer is a cached drawing re-recorded only when its change key does; per frame the
/// wake, shadow and needle only rotate, the red zone, its glow, the lamps, the needle glow and the flare only fade, and
/// the sparks are splatted into a particle bitmap.
/// </summary>
internal sealed class NeedleRenderer : IOrbStyleRenderer
{
    // The trace follows the needle without its tremble and re-records only once the needle is this far from the drawn
    // end; the blade (about ±1.5° wide at the trace radius) covers the difference.
    private const double TraceSlackDegrees = 1;

    private const double GlowSigmaStep = 0.0005;
    private const double SparkTailAlpha = 0.3;
    private const long Unset = long.MinValue;

    private readonly NeedleModel _model;
    private readonly NeedlePaint _paint = new();
    private readonly DrawingVisual _dial = Cached();
    private readonly DrawingVisual _redGlow = Cached();
    private readonly DrawingVisual _redZone = Cached();
    private readonly DrawingVisual _ticks = Cached();
    private readonly DrawingVisual _trace = Cached();
    private readonly DrawingVisual[] _lamps = new DrawingVisual[NeedleGeometry.LampCount];
    private readonly DrawingVisual _wake = Cached();
    private readonly DrawingVisual _shadow = Cached();
    private readonly ContainerVisual _needle = new();
    private readonly DrawingVisual _glow = Cached();
    private readonly DrawingVisual _blade = Cached();
    private readonly DrawingVisual _flare = Cached();
    private readonly ParticleBitmap _sparks = new();
    private readonly DrawingVisual _hub = Cached();
    private readonly RotateTransform _wakeRotation = new();
    private readonly RotateTransform _shadowRotation = new();
    private readonly TranslateTransform _shadowOffset = new();
    private readonly RotateTransform _needleRotation = new();
    private OrbRenderContext? _context;
    private NeedleDrawings? _drawings;
    private long _dialKey = Unset;
    private long _redKey = Unset;
    private long _redGlowKey = Unset;
    private long _tickKey = Unset;
    private long _traceKey = Unset;
    private long _lampKey = Unset;
    private long _wakeKey = Unset;
    private long _shadowKey = Unset;
    private long _glowKey = Unset;
    private long _bladeKey = Unset;
    private long _flareKey = Unset;
    private long _hubKey = Unset;
    private double _traceEnd;
    private double _angle = double.NaN;
    private double _wakeAngle = double.NaN;

    public NeedleRenderer(OrbScene scene, uint seed)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _model = new NeedleModel(seed);
        _wake.Transform = _wakeRotation;
        _shadow.Transform = new TransformGroup { Children = { _shadowRotation, _shadowOffset } };
        _needle.Transform = _needleRotation;
        _needle.Children.Add(_glow);
        _needle.Children.Add(_blade);
        _needle.Children.Add(_flare);

        Root.Children.Add(_dial);
        Root.Children.Add(_redGlow);
        Root.Children.Add(_redZone);
        Root.Children.Add(_ticks);
        Root.Children.Add(_trace);
        for (var i = 0; i < _lamps.Length; i++)
        {
            _lamps[i] = Cached();
            Root.Children.Add(_lamps[i]);
        }

        Root.Children.Add(_wake);
        Root.Children.Add(_shadow);
        Root.Children.Add(_needle);
        Root.Children.Add(_sparks.Visual);
        Root.Children.Add(_hub);
    }

    public OrbStyle Style => OrbStyle.Needle;

    public IOrbStyleModel Model => _model;

    public ContainerVisual Root { get; } = new();

    public ContainerVisual? Overlay => null;

    public OrbReadoutLayout Readout => OrbReadoutLayout.Needle;

    public BodyVariant Body => BodyVariant.Standard;

    public bool SharedEffects => true;

    public double? ReadoutSplit => null;

    public ReadoutPalette? BelowPalette => null;

    public void Configure(OrbRenderContext context)
    {
        _context = context;
        _paint.Clear();
        _drawings = new NeedleDrawings(context, _paint);
        _sparks.Configure(SparkBounds(context), context.Scale);
        var c = context.Center;
        _wakeRotation.CenterX = _shadowRotation.CenterX = _needleRotation.CenterX = c.X;
        _wakeRotation.CenterY = _shadowRotation.CenterY = _needleRotation.CenterY = c.Y;
        var palette = NeedlePalette.LightPalette;
        _shadowOffset.X = palette.NeedleShadowOffsetX * context.Size;
        _shadowOffset.Y = palette.NeedleShadowOffsetY * context.Size;
        _dialKey = _redKey = _redGlowKey = _tickKey = _traceKey = _lampKey = Unset;
        _wakeKey = _shadowKey = _glowKey = _bladeKey = _flareKey = _hubKey = Unset;
        _angle = double.NaN;
        _wakeAngle = double.NaN;
    }

    public void Render(OrbScene scene, in OrbFrame frame)
    {
        if (_context is not { } ctx || _drawings is not { } drawings)
        {
            return;
        }

        var m = _model;
        var hc = ctx.HighContrast || m.HighContrast;
        var palette = m.Palette;
        RenderDial(drawings, m, palette, hc);
        RenderRedZone(drawings, m, palette, hc);
        RenderTicks(drawings, m, hc);
        RenderTrace(drawings, m, palette.Dark, hc);
        RenderLamps(drawings, m, hc);
        RenderNeedle(drawings, m, palette, hc);
        RenderWake(drawings, m, hc);
        RenderSparks(ctx, m, hc || frame.Thumbnail);
        RenderHub(drawings, m, palette, hc);
    }

    private void RenderDial(NeedleDrawings drawings, NeedleModel m, NeedlePalette palette, bool hc)
    {
        var key = Bits(palette.Dark, hc, m.ShowStopPins, m.ShowLamps);
        if (key != _dialKey)
        {
            _dialKey = key;
            using var dc = _dial.RenderOpen();
            drawings.Dial(dc, palette, hc, m.ShowStopPins, m.ShowLamps);
        }
    }

    private void RenderRedZone(NeedleDrawings drawings, NeedleModel m, NeedlePalette palette, bool hc)
    {
        var key = Bits(palette.Dark, hc);
        if (key != _redKey)
        {
            _redKey = key;
            using var dc = _redZone.RenderOpen();
            drawings.RedZone(dc, palette, hc);
        }

        SetOpacity(_redZone, hc ? 1 : m.RedZoneOpacity);

        var glowing = !hc && m.RedZoneGlowSigma > 0 && m.RedZoneGlowOpacity > 0.002;
        if (glowing)
        {
            var glowKey = m.RedZoneGlowKey << 1 | Bits(palette.Dark);
            if (glowKey != _redGlowKey)
            {
                _redGlowKey = glowKey;
                using var dc = _redGlow.RenderOpen();
                drawings.RedZoneGlow(dc, palette, m.RedZoneGlowSigma);
            }
        }

        SetOpacity(_redGlow, glowing ? m.RedZoneGlowOpacity : 0);
    }

    private void RenderTicks(NeedleDrawings drawings, NeedleModel m, bool hc)
    {
        var key = m.TickKey << 1 | Bits(hc);
        if (key != _tickKey)
        {
            _tickKey = key;
            using var dc = _ticks.RenderOpen();
            drawings.Ticks(dc, m.Ticks, hc);
        }
    }

    private void RenderTrace(NeedleDrawings drawings, NeedleModel m, bool dark, bool hc)
    {
        if (!m.TraceVisible)
        {
            SetOpacity(_trace, 0);
            return;
        }

        // The spring's position is the displayed needle without the tremble (and with the new-peak sweep handled below).
        var end = m.PeakSweep is null ? Math.Clamp(m.SpringDial, NeedleGeometry.ZeroStop, NeedleGeometry.Peg) : m.TraceEnd;
        var start = m.TraceOpacityStart;
        var finish = m.TraceOpacityEnd;
        var key = (long)Math.Round(StyleMath.Clamp01(start) * 255) << 10 | (long)Math.Round(StyleMath.Clamp01(finish) * 255) << 2
            | Bits(dark, hc);
        if (key != _traceKey || Math.Abs(end - _traceEnd) * StyleMath.DialSweepDegrees > TraceSlackDegrees)
        {
            _traceKey = key;
            _traceEnd = end;
            using var dc = _trace.RenderOpen();
            drawings.Trace(dc, m, end, start, finish, hc);
        }

        SetOpacity(_trace, 1);
    }

    private void RenderLamps(NeedleDrawings drawings, NeedleModel m, bool hc)
    {
        var lamps = m.Lamps;
        if (!m.ShowLamps)
        {
            foreach (var lamp in _lamps)
            {
                SetOpacity(lamp, 0);
            }

            return;
        }

        var key = m.LampKey << 1 | Bits(hc);
        if (key != _lampKey)
        {
            _lampKey = key;
            for (var i = 0; i < _lamps.Length; i++)
            {
                using var dc = _lamps[i].RenderOpen();
                drawings.Lamp(dc, lamps[i], hc);
            }
        }

        for (var i = 0; i < _lamps.Length; i++)
        {
            SetOpacity(_lamps[i], lamps[i].Opacity);
        }
    }

    private void RenderNeedle(NeedleDrawings drawings, NeedleModel m, NeedlePalette palette, bool hc)
    {
        var dark = palette.Dark;
        var angle = m.NeedleDegrees;
        if (angle != _angle)
        {
            _angle = angle;
            _needleRotation.Angle = angle;
        }

        var fill = NeedlePaint.Coarse(m.NeedleFill);
        var spine = NeedlePaint.Coarse(m.Spine);
        var bladeKey = (NeedlePaint.CoarseKey(fill) << 24 | NeedlePaint.CoarseKey(spine)) << 2 | Bits(dark, hc);
        if (bladeKey != _bladeKey)
        {
            _bladeKey = bladeKey;
            using var dc = _blade.RenderOpen();
            drawings.Blade(dc, fill, spine, hc);
        }

        var glowing = !hc && m.NeedleGlowSigma > 0 && m.NeedleGlowOpacity > 0.002;
        if (glowing)
        {
            var color = NeedlePaint.Coarse(m.NeedleGlowColor);
            var steps = (long)Math.Round(m.NeedleGlowSigma / GlowSigmaStep);
            var glowKey = NeedlePaint.CoarseKey(color) << 12 | steps;
            if (glowKey != _glowKey)
            {
                _glowKey = glowKey;
                using var dc = _glow.RenderOpen();
                drawings.Glow(dc, color, steps * GlowSigmaStep);
            }
        }

        SetOpacity(_glow, glowing ? m.NeedleGlowOpacity : 0);

        var flaring = !hc && m.FlareSpotOpacity > 0.002;
        if (flaring && Bits(dark) != _flareKey)
        {
            _flareKey = Bits(dark);
            using var dc = _flare.RenderOpen();
            drawings.Flare(dc, palette);
        }

        SetOpacity(_flare, flaring ? m.FlareSpotOpacity : 0);

        var shadowed = !hc && m.NeedleShadowVisible;
        if (shadowed)
        {
            if (_shadowKey != 1)
            {
                _shadowKey = 1;
                using var dc = _shadow.RenderOpen();
                drawings.Shadow(dc, palette);
            }

            if (_shadowRotation.Angle != angle)
            {
                _shadowRotation.Angle = angle;
            }
        }

        SetOpacity(_shadow, shadowed ? m.NeedleFill.A : 0);
    }

    private void RenderWake(NeedleDrawings drawings, NeedleModel m, bool hc)
    {
        if (hc || !m.WakeVisible)
        {
            SetOpacity(_wake, 0);
            return;
        }

        var head = m.WakeHeadDegrees;
        var slices = NeedleWake.SliceCount(Math.Abs(head - m.WakeTailDegrees));
        var clockwise = m.WakeTailDegrees < head;
        var color = NeedlePaint.Coarse(m.WakeColor);
        var key = NeedlePaint.CoarseKey(color) << 8 | (long)slices << 1 | Bits(clockwise);
        if (key != _wakeKey)
        {
            _wakeKey = key;
            using var dc = _wake.RenderOpen();
            drawings.Wake(dc, slices, clockwise, color);
        }

        if (head != _wakeAngle)
        {
            _wakeAngle = head;
            _wakeRotation.Angle = head;
        }

        SetOpacity(_wake, StyleMath.Clamp01(m.WakeHeadOpacity / NeedleModel.WakeOpacity));
    }

    private void RenderSparks(OrbRenderContext ctx, NeedleModel m, bool suppressed)
    {
        var sparks = m.Sparks;
        if (sparks.Length == 0 && !_sparks.HasContent)
        {
            return;
        }

        if (_sparks.Begin() is not { } raster)
        {
            return;
        }

        if (!suppressed)
        {
            var s = ctx.Size;
            var c = ctx.Center;
            var px = _sparks.Scale;
            var blend = m.Dark ? SpriteBlend.Additive : SpriteBlend.Over;
            foreach (ref readonly var p in sparks)
            {
                var (hx, hy) = _sparks.ToPixels(c.X + p.X * s, c.Y + p.Y * s);
                var (tx, ty) = _sparks.ToPixels(c.X + (p.X + p.StreakX) * s, c.Y + (p.Y + p.StreakY) * s);
                raster.Streak(tx, ty, hx, hy, p.Width * px, p.Color, p.Alpha, SparkTailAlpha, blend);
            }
        }

        _sparks.End();
    }

    private void RenderHub(NeedleDrawings drawings, NeedleModel m, NeedlePalette palette, bool hc)
    {
        var key = Bits(palette.Dark, hc, m.ShowHub);
        if (key != _hubKey)
        {
            _hubKey = key;
            using var dc = _hub.RenderOpen();
            if (m.ShowHub)
            {
                drawings.Hub(dc, palette, hc);
            }
        }
    }

    /// <summary>
    /// Where peg sparks can be: born at the contact point (r 0.405 S, lower right) heading down along the clockwise
    /// tangent ±30° and falling, they stay within x 0 … 0.27 S and y 0.29 … 0.75 S of c (simulated over the model's
    /// ranges). The bitmap covers that with a wide margin instead of the whole 1.5 S window.
    /// </summary>
    private static Rect SparkBounds(OrbRenderContext ctx)
    {
        var s = ctx.Size;
        var c = ctx.Center;
        var bounds = new Rect(c.X - 0.25 * s, c.Y + 0.2 * s, 0.75 * s, 0.55 * s);
        bounds.Intersect(ctx.Bounds);
        return bounds;
    }

    private static DrawingVisual Cached() => new() { CacheMode = new BitmapCache(1) };

    private static long Bits(bool a, bool b = false, bool c = false, bool d = false) =>
        (a ? 1L : 0) | (b ? 2L : 0) | (c ? 4L : 0) | (d ? 8L : 0);

    private static void SetOpacity(ContainerVisual visual, double opacity)
    {
        if (Math.Abs(visual.Opacity - opacity) > 0.002 || (opacity == 0) != (visual.Opacity == 0))
        {
            visual.Opacity = opacity;
        }
    }
}
