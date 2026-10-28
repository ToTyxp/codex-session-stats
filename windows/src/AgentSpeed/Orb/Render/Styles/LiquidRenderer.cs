using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Presentation.Orb;
using AgentSpeed.Presentation.Orb.Styles;
using AgentSpeed.Presentation.Settings;
using AgentSpeed.Theme;
using static AgentSpeed.Orb.Render.Styles.LiquidReactorShapes;

namespace AgentSpeed.Orb.Render.Styles;

/// <summary>
/// The <c>liquid</c> style (DESIGN 2a.3) on retained visuals. Only the surface moves every frame: the back wave and the
/// liquid body are one small re-recorded visual built from 48 surface samples and closed along the glass, so no
/// geometry clip (an offscreen mask per frame in WPF) is ever needed; the same geometry is filled again with the
/// spherical shade and the inner glow and strokes the meniscus. Static parts (volume shading, scrim, glass, caustic) are
/// cached visuals; the light spill and the gold band are circle-bounded bands re-recorded only when they move; particles
/// are splatted into a bitmap clipped to the glass and the meniscus glow into a half-resolution one. Nothing is
/// re-recorded while the surface rests (idle, Off). The dark appearance's additive lights are drawn in
/// <see cref="LiquidModel.LitOver"/> colors.
/// </summary>
internal sealed partial class LiquidRenderer : IOrbStyleRenderer
{
    private const int SurfaceSamples = 48;
    private const int CurrentSamples = 25;
    private const double R = LiquidGeometry.GlassRadius;


    private static readonly BodyVariant DarkBody = new(LiquidModel.DarkBodyTop, LiquidModel.DarkBodyBottom, false);
    private static readonly BodyVariant LightBody = new(LiquidModel.LightBodyTop, LiquidModel.LightBodyBottom, false);
    private static readonly LiquidModel DarkProbe = Probe(dark: true);
    private static readonly LiquidModel LightProbe = Probe(dark: false);

    private readonly LiquidReactorInk _ink = new();
    private readonly DrawingVisual _volume = CachedVisual();
    private readonly ContainerVisual _glassClip = new();
    private readonly DrawingVisual _spill = CachedVisual();
    private readonly DrawingVisual _bodyFill = new();
    private readonly DrawingVisual _scrim = CachedVisual();
    private readonly DrawingVisual _currents = new();
    private readonly DrawingVisual _gold = CachedVisual();
    private Brush? _shadeBrush;
    private readonly Dictionary<long, Brush> _glowBrushes = new();
    private readonly DrawingVisual _meniscus = new();
    private readonly ParticleBitmap _particles = new();
    private readonly ParticleBitmap _glowLayer = new();
    private readonly DrawingVisual _glass = CachedVisual();
    private readonly DrawingVisual _caustic = CachedVisual();

    private readonly double[] _xs = new double[SurfaceSamples];
    private readonly double[] _glowXs = new double[SurfaceSamples];
    private readonly double[] _glowYs = new double[SurfaceSamples];
    private readonly Point[] _surfaceTail = new Point[SurfaceSamples - 1];
    private readonly Point[] _currentTail = new Point[CurrentSamples - 1];
    private readonly Pen?[] _currentPens = new Pen?[3];
    private readonly Dictionary<int, Brush> _bodyBrushes = new();

    private OrbRenderContext? _context;
    private BodyVariant _body = DarkBody;
    private ReadoutPalette? _belowPalette;
    private uint _belowTitleKey;
    private bool _staticDirty = true;
    private bool _surfaceDirty = true;
    private long _inkKey = long.MinValue;
    private int _surfaceVersion = int.MinValue;
    private bool _dropletsDrawn;
    private bool _currentsDrawn;
    private Rgba _bodyTop;
    private Rgba _bodyBottom;
    private Brush? _backBrush;
    private Pen? _meniscusPen;
    private Rgba _meniscusGlow;
    private double _meniscusWidth;
    private double _meniscusSigma;

    public LiquidRenderer(OrbScene scene, uint seed)
    {
        _ = scene;
        Model = new LiquidModel(unchecked((int)seed));
        _glassClip.Children.Add(_spill);
        _glassClip.Children.Add(_bodyFill);
        _glassClip.Children.Add(_currents);
        // The prototype's scrim is not clipped to the liquid: it also darkens the glass behind a readout above the surface.
        _glassClip.Children.Add(_scrim);
        _glassClip.Children.Add(_gold);
        _glassClip.Children.Add(_glowLayer.Visual);
        _glassClip.Children.Add(_meniscus);
        _glassClip.Children.Add(_particles.Visual);
        Root.Children.Add(_volume);
        Root.Children.Add(_glassClip);
        Root.Children.Add(_glass);
        Root.Children.Add(_caustic);
    }

    public OrbStyle Style => OrbStyle.Liquid;

    public LiquidModel Model { get; }

    IOrbStyleModel IOrbStyleRenderer.Model => Model;

    public ContainerVisual Root { get; } = new();

    /// <summary>The glass highlights sit below the readout (prototype order), so there is no overlay.</summary>
    public ContainerVisual? Overlay => null;

    public OrbReadoutLayout Readout => OrbReadoutLayout.Liquid;

    public BodyVariant Body => _body;

    public bool SharedEffects => true;

    /// <summary>The surface height at the disc center (a flat split; DESIGN 2a.3 layer 10).</summary>
    public double? ReadoutSplit => _context is { HighContrast: false } ? Model.ReadoutSplitY : null;

    /// <summary>
    /// The readout under the surface. <see cref="OrbSurface"/> reads it in Configure; the light title (<c>heat(h)</c>
    /// darkened 55 %) is kept current here, so re-reading it per frame would track the heat.
    /// </summary>
    public ReadoutPalette? BelowPalette => _belowPalette;

    public void Configure(OrbRenderContext context)
    {
        _context = context;
        _body = context.Dark ? DarkBody : LightBody;
        _particles.Configure(context.Bounds, context.Scale);
        var (glassX, glassY) = _particles.ToPixels(context.Center.X, context.Center.Y);
        _particles.Raster?.SetClipCircle(glassX, glassY, R * context.Size * _particles.Scale);
        // The meniscus glow is a soft Gaussian: half the device resolution looks the same and costs a quarter.
        _glowLayer.Configure(context.Bounds, context.Scale / 2);
        var (glowX, glowY) = _glowLayer.ToPixels(context.Center.X, context.Center.Y);
        _glowLayer.Raster?.SetClipCircle(glowX, glowY, R * context.Size * _glowLayer.Scale);
        _glowBrushes.Clear();
        _spillY = double.NaN;
        _goldY = double.NaN;
        _staticDirty = true;
        _surfaceDirty = true;
        _inkKey = long.MinValue;
        _spillKey = uint.MaxValue;
        _causticKey = uint.MaxValue;
        _glowVersion = int.MinValue;
        _bodyBrushes.Clear();
        _belowPalette = BelowPaletteFor(context);
    }

    public void Render(OrbScene scene, in OrbFrame frame)
    {
        if (_context is not { } ctx)
        {
            return;
        }

        var m = Model;
        var hc = ctx.HighContrast;
        var thumbnail = frame.Thumbnail;
        var effects = !hc && !thumbnail;
        if (_staticDirty)
        {
            _staticDirty = false;
            DrawStatic(ctx);
        }

        if (m.ColorKey != _inkKey)
        {
            _inkKey = m.ColorKey;
            RefreshInk(ctx);
            _surfaceDirty = true;
        }

        RenderSpill(ctx, effects);
        SetOpacity(_scrim, hc ? 0 : m.ScrimOpacity);
        RenderGoldBand(ctx, effects);
        RenderSurface(ctx, hc, thumbnail);
        RenderGlow(effects, frame.Dt);
        RenderCaustic(ctx, effects);
        if (!_particles.SkipThisFrame(frame.Dt))
        {
            RenderParticles(ctx, effects);
        }
        TrackBelowTitle(ctx);
    }

    /// <summary>Brushes and pens that change only with the model's <see cref="LiquidModel.ColorKey"/>.</summary>
    private void RefreshInk(OrbRenderContext ctx)
    {
        var m = Model;
        var s = ctx.Size;
        _bodyBrushes.Clear();
        _meniscusWidth = m.MeniscusWidth * s;
        if (ctx.HighContrast)
        {
            var highlight = SystemColors.HighlightColor.ToRgba();
            _bodyTop = highlight.WithAlpha(0.35);
            _bodyBottom = _bodyTop;
            _backBrush = null;
            _meniscusPen = _ink.Pen(highlight, _meniscusWidth, PenLineCap.Round);
            _meniscusGlow = Rgba.Transparent;
            Array.Clear(_currentPens);
            return;
        }

        // Reduce Transparency: the liquid is 15 % more opaque.
        var solid = ctx.SolidBody ? 0.15 : 0;
        _bodyTop = m.BodyTopColor.WithAlpha(m.BodyTopColor.A + solid);
        _bodyBottom = m.BodyBottomColor.WithAlpha(m.BodyBottomColor.A + solid);
        var back = m.BackWaveColor;
        _backBrush = _ink.Brush(back.WithAlpha(back.A + solid * 0.55));
        _meniscusPen = _ink.Pen(m.MeniscusColor, _meniscusWidth, PenLineCap.Round);
        _meniscusGlow = m.MeniscusGlowColor;
        _glowVersion = int.MinValue;
        _meniscusSigma = m.MeniscusGlowSigma * s;
        var widths = LiquidGeometry.CurrentWidths;
        var blur = LiquidGeometry.CurrentBlur * s;
        for (var k = 0; k < 3; k++)
        {
            var color = m.LitOver(m.CurrentColor(k), 0, m.CurrentY(k, 0.5));
            var width = widths[k] * s;
            // At about 10 % alpha a σ 0.006 S blur reads the same as one slightly wider translucent stroke.
            _currentPens[k] = _ink.Pen(color.MultiplyAlpha(0.85), width + blur, PenLineCap.Round);
        }
    }

    /// <summary>
    /// Layers 4, 5 (body), 6 and 8: re-recorded only when the surface moved or a color changed. The front geometry fills
    /// the body, clips the liquid's shading and currents, and strokes the meniscus (only its surface edge is stroked).
    /// </summary>
    private void RenderSurface(OrbRenderContext ctx, bool hc, bool thumbnail)
    {
        var m = Model;
        var effects = !hc && !thumbnail;
        var droplets = effects && m.Droplets.Length > 0;
        if (!_surfaceDirty && m.SurfaceVersion == _surfaceVersion && !droplets && !_dropletsDrawn)
        {
            return;
        }

        _surfaceDirty = false;
        _surfaceVersion = m.SurfaceVersion;
        var s = ctx.Size;
        var c = ctx.Center;
        // The glow in the particle layer reaches 3 σ past the stroke's edge; the samples cover the glass that far.
        var glowReach = effects && _meniscusGlow.A > 0.004 ? 3 * _meniscusSigma / s : 0;
        var margin = LiquidSurfaceSampling.Margin(m.WaveAmplitude, m.ChopAmplitude, m.RippleCount > 0)
            + glowReach + _meniscusWidth / s;
        LiquidSurfaceSampling.FillSampleX(_xs, LiquidSurfaceSampling.VisibleHalfWidth(m.MeanSurfaceY, margin));

        var front = SurfaceGeometry(c, s, back: false);
        using (var dc = _bodyFill.RenderOpen())
        {
            if (effects && m.BackWaveVisible && _backBrush is not null)
            {
                dc.DrawGeometry(_backBrush, null, SurfaceGeometry(c, s, back: true));
            }

            dc.DrawGeometry(BodyBrush(ctx), null, front);
            if (effects && _shadeBrush is not null)
            {
                dc.DrawGeometry(_shadeBrush, null, front);
            }

            if (effects && GlowBrush(ctx) is { } glow)
            {
                dc.DrawGeometry(glow, null, front);
            }
        }

        using (var dc = _meniscus.RenderOpen())
        {
            if (_meniscusPen is not null)
            {
                dc.DrawGeometry(null, _meniscusPen, front);
            }

            if (droplets)
            {
                DrawDroplets(dc, c, s);
            }
        }

        _dropletsDrawn = droplets;
        RenderCurrents(ctx, effects && m.CurrentsVisible);
    }

    /// <summary>
    /// The surface at the sampled x (S) as one figure: the sampled edge (stroked) and, unstroked, out to the overscan
    /// at the end heights and around below the glass. An open filled figure fills as if closed.
    /// </summary>
    private StreamGeometry SurfaceGeometry(Point c, double s, bool back)
    {
        var m = Model;
        var radius = R * s;
        var first = default(Point);
        for (var i = 0; i < _xs.Length; i++)
        {
            var x = _xs[i] * s;
            var y = (back ? m.BackSurfaceY(_xs[i]) : m.SurfaceY(_xs[i])) * s;
            if (!back)
            {
                (_glowXs[i], _glowYs[i]) = _glowLayer.ToPixels(c.X + x, c.Y + y);
            }

            // Points outside the glass move radially onto it, so the path never leaves the glass.
            var d = Math.Sqrt(x * x + y * y);
            if (d > radius)
            {
                x *= radius / d;
                y *= radius / d;
            }

            var point = new Point(c.X + x, c.Y + y);
            if (i == 0)
            {
                first = point;
            }
            else
            {
                _surfaceTail[i - 1] = point;
            }
        }

        var last = _surfaceTail[^1];
        var start = Math.Atan2(last.Y - c.Y, last.X - c.X);
        var end = Math.Atan2(first.Y - c.Y, first.X - c.X);
        var sweep = end - start;
        while (sweep < 0)
        {
            sweep += 2 * Math.PI;
        }

        // Ends of the surface that stop inside the glass go straight out to it.
        var lastOnGlass = OnGlass(c, last, radius);
        var firstOnGlass = OnGlass(c, first, radius);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(first, isFilled: true, isClosed: true);
            // Only the part of the surface clear of the glass is stroked, so the round caps end at the wall instead of
            // the stroke bending along it.
            var limit = radius - _meniscusWidth / 2;
            var previous = first;
            var previousInside = Inside(c, previous, limit);
            foreach (var point in _surfaceTail)
            {
                var inside = Inside(c, point, limit);
                if (inside != previousInside)
                {
                    g.LineTo(CrossingAt(c, previous, point, limit), isStroked: previousInside, isSmoothJoin: true);
                }

                g.LineTo(point, isStroked: inside, isSmoothJoin: true);
                previous = point;
                previousInside = inside;
            }

            if (lastOnGlass != last)
            {
                g.LineTo(lastOnGlass, isStroked: false, isSmoothJoin: false);
            }

            var startOnGlass = Math.Atan2(lastOnGlass.Y - c.Y, lastOnGlass.X - c.X);
            var endOnGlass = Math.Atan2(firstOnGlass.Y - c.Y, firstOnGlass.X - c.X);
            var arc = endOnGlass - startOnGlass;
            while (arc < 0)
            {
                arc += 2 * Math.PI;
            }

            g.ArcTo(firstOnGlass, new Size(radius, radius), 0, arc > Math.PI, SweepDirection.Clockwise, isStroked: false, isSmoothJoin: false);
        }

        _ = sweep;
        geometry.Freeze();
        return geometry;
    }

    private static bool Inside(Point c, Point p, double limit)
    {
        var dx = p.X - c.X;
        var dy = p.Y - c.Y;
        return dx * dx + dy * dy <= limit * limit;
    }

    /// <summary>Where the segment from <paramref name="a"/> to <paramref name="b"/> crosses the circle of <paramref name="limit"/>.</summary>
    private static Point CrossingAt(Point c, Point a, Point b, double limit)
    {
        var ax = a.X - c.X;
        var ay = a.Y - c.Y;
        var vx = b.X - a.X;
        var vy = b.Y - a.Y;
        var qa = vx * vx + vy * vy;
        if (qa < 1e-12)
        {
            return a;
        }

        var qb = 2 * (ax * vx + ay * vy);
        var qc = ax * ax + ay * ay - limit * limit;
        var root = Math.Sqrt(Math.Max(0, qb * qb - 4 * qa * qc));
        var t0 = (-qb - root) / (2 * qa);
        var t = t0 is >= 0 and <= 1 ? t0 : Math.Clamp((-qb + root) / (2 * qa), 0, 1);
        return new Point(a.X + vx * t, a.Y + vy * t);
    }

    /// <summary>The point on the glass straight outward (horizontally) from an end of the surface.</summary>
    private static Point OnGlass(Point c, Point p, double radius)
    {
        radius = Math.Max(0, radius);
        var dy = Math.Clamp(p.Y - c.Y, -radius, radius);
        var dx = Math.Sqrt(Math.Max(0, radius * radius - dy * dy));
        return new Point(c.X + (p.X >= c.X ? dx : -dx), c.Y + dy);
    }

    /// <summary>The inner glow (layer 5) as a brush for the liquid geometry, cached by center height and color.</summary>
    private Brush? GlowBrush(OrbRenderContext ctx)
    {
        var color = Model.InnerGlowColor;
        if (color.A < 0.004)
        {
            return null;
        }

        color = Model.LitOver(color, 0, Model.InnerGlowCenterY, LiquidLightStage.InnerGlow);

        var s = ctx.Size;
        var bucket = (long)Math.Round(Model.InnerGlowCenterY * 200);
        var key = bucket << 32 | LiquidReactorInk.Pack(color.WithAlpha(Math.Round(color.A * 48) / 48));
        if (_glowBrushes.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (_glowBrushes.Count >= 96)
        {
            _glowBrushes.Clear();
        }

        var center = new Point(ctx.Center.X, ctx.Center.Y + bucket / 200.0 * s);
        var radius = LiquidGeometry.InnerGlowRadius * s;
        var alpha = Math.Round(color.A * 48) / 48;
        var brush = new RadialGradientBrush(color.WithAlpha(alpha).ToColor(), color.ToColor(0))
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = center,
            GradientOrigin = center,
            RadiusX = radius,
            RadiusY = radius,
        };
        brush.Freeze();
        _glowBrushes[key] = brush;
        return brush;
    }

    /// <summary>The part of the glass between two heights (absolute), for the light spill and the gold band.</summary>
    private static StreamGeometry? GlassBand(Point c, double radius, double top, double bottom)
    {
        top = Math.Max(top, c.Y - radius + 0.01);
        bottom = Math.Min(bottom, c.Y + radius - 0.01);
        if (bottom - top < 0.05)
        {
            return null;
        }

        var dxTop = Math.Sqrt(Math.Max(0, radius * radius - (top - c.Y) * (top - c.Y)));
        var dxBottom = Math.Sqrt(Math.Max(0, radius * radius - (bottom - c.Y) * (bottom - c.Y)));
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(c.X - dxTop, top), isFilled: true, isClosed: true);
            g.LineTo(new Point(c.X + dxTop, top), false, false);
            g.ArcTo(new Point(c.X + dxBottom, bottom), new Size(radius, radius), 0, false, SweepDirection.Clockwise, false, false);
            g.LineTo(new Point(c.X - dxBottom, bottom), false, false);
            g.ArcTo(new Point(c.X - dxTop, top), new Size(radius, radius), 0, false, SweepDirection.Clockwise, false, false);
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>The body gradient (layer 5) runs from <see cref="LiquidModel.BodyGradientTopY"/> to the glass bottom.</summary>
    private Brush BodyBrush(OrbRenderContext ctx)
    {
        if (ctx.HighContrast)
        {
            return _ink.Brush(_bodyTop);
        }

        var bucket = (int)Math.Round(Model.BodyGradientTopY * 400);
        if (_bodyBrushes.TryGetValue(bucket, out var cached))
        {
            return cached;
        }

        if (_bodyBrushes.Count >= 64)
        {
            _bodyBrushes.Clear();
        }

        var c = ctx.Center;
        var s = ctx.Size;
        var brush = new LinearGradientBrush(_bodyTop.ToColor(), _bodyBottom.ToColor(), new Point(c.X, c.Y + bucket / 400.0 * s),
            new Point(c.X, c.Y + R * s))
        {
            MappingMode = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        _bodyBrushes[bucket] = brush;
        return brush;
    }

    /// <summary>Layer 6: three luminous filaments, kept under the surface and inside the glass.</summary>
    private void RenderCurrents(OrbRenderContext ctx, bool visible)
    {
        if (!visible || _currentPens[0] is null)
        {
            if (_currentsDrawn)
            {
                _currentsDrawn = false;
                using var clear = _currents.RenderOpen();
            }

            return;
        }

        _currentsDrawn = true;
        var m = Model;
        var s = ctx.Size;
        var c = ctx.Center;
        using var dc = _currents.RenderOpen();
        for (var k = 0; k < 3; k++)
        {
            var first = default(Point);
            for (var i = 0; i < CurrentSamples; i++)
            {
                var u = i / (double)(CurrentSamples - 1);
                var x = -R + 2 * R * u;
                var y = Math.Max(m.CurrentY(k, u), m.SurfaceY(x) + 0.008);
                var point = new Point(c.X + x * s, c.Y + y * s);
                if (i == 0)
                {
                    first = point;
                }
                else
                {
                    _currentTail[i - 1] = point;
                }
            }

            // Like the prototype's clip to the liquid: only the part of the filament inside the glass is stroked.
            var pen = _currentPens[k]!;
            var limit = R * s - pen.Thickness / 2;
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(first, isFilled: false, isClosed: false);
                var previous = first;
                var previousInside = Inside(c, previous, limit);
                foreach (var point in _currentTail)
                {
                    var inside = Inside(c, point, limit);
                    if (inside != previousInside)
                    {
                        g.LineTo(CrossingAt(c, previous, point, limit), isStroked: previousInside, isSmoothJoin: true);
                    }

                    g.LineTo(point, isStroked: inside, isSmoothJoin: true);
                    previous = point;
                    previousInside = inside;
                }
            }

            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }
    }

    /// <summary>Falling pulse droplets: a teardrop, its tip 2.2 × the radius above the round end.</summary>
    private void DrawDroplets(DrawingContext dc, Point c, double s)
    {
        foreach (ref readonly var d in Model.Droplets)
        {
            var r = d.Radius * s;
            var x = c.X + d.X * s;
            var y = c.Y + d.Y * s;
            var tip = new Point(x, y - 2.2 * r);
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(tip, isFilled: true, isClosed: true);
                g.QuadraticBezierTo(new Point(x + 1.1 * r, y), new Point(x, y + r), isStroked: false, isSmoothJoin: true);
                g.QuadraticBezierTo(new Point(x - 1.1 * r, y), tip, isStroked: false, isSmoothJoin: true);
            }

            geometry.Freeze();
            dc.DrawGeometry(_ink.Brush(Model.LitOver(d.Color.WithAlpha(d.Alpha), d.X, d.Y)), null, geometry);
        }
    }

    private ReadoutPalette? BelowPaletteFor(OrbRenderContext ctx)
    {
        if (ctx.HighContrast)
        {
            return null;
        }

        var probe = ctx.Dark ? DarkProbe : LightProbe;
        // The live model has the same appearance once it has run a frame with it; its title then follows the heat.
        var title = Model.ReadoutBelowValueColor == probe.ReadoutBelowValueColor ? Model.ReadoutBelowTitleColor : probe.ReadoutBelowTitleColor;
        _belowTitleKey = LiquidReactorInk.Pack(title);
        Rgba? shadow = probe.ReadoutBelowShadowColor.A > 0 ? probe.ReadoutBelowShadowColor : null;
        return new ReadoutPalette(probe.ReadoutBelowValueColor, probe.ReadoutBelowValueColor, probe.ReadoutBelowUnitColor, title, shadow,
            shadow is null ? 0 : LiquidModel.ReadoutBelowShadowSigma, false, probe.AgentDotHaloColor);
    }

    private void TrackBelowTitle(OrbRenderContext ctx)
    {
        if (ctx.Dark || _belowPalette is null)
        {
            return;
        }

        var title = Model.ReadoutBelowTitleColor;
        var key = LiquidReactorInk.Pack(title);
        if (key != _belowTitleKey)
        {
            _belowTitleKey = key;
            _belowPalette = _belowPalette with { TitleOverride = title };
        }
    }

    private static LiquidModel Probe(bool dark)
    {
        var model = new LiquidModel(1);
        model.Snap(new OrbFrame(0, 0, 0, 0, 0, 0, SpeedTier.Idle, 148, new EffectsPolicy(EffectsIntensity.Standard), dark));
        return model;
    }
}
