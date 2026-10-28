using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Orb;
using AgentSpeed.Presentation.Orb.Styles;
using AgentSpeed.Presentation.Render;
using AgentSpeed.Theme;
using static AgentSpeed.Orb.Render.Styles.LiquidReactorShapes;

namespace AgentSpeed.Orb.Render.Styles;

/// <summary>
/// The <c>reactor</c> style (DESIGN 2a.4) on retained visuals. Stator, tracks and core window are drawn once; rotors A
/// and C are cached drawings re-recorded only when the model's color key or arc-length bucket changes and otherwise
/// only rotated; rotor B is a ring of cached coil visuals (a base and a hot copy each) whose opacities change per frame
/// inside one rotating container; the corona is a cached gradient that only fades. Lightning bolts are cached per slot,
/// re-recorded when a bolt re-randomizes (every 50 ms) and faded per frame; sparks are splatted into a bitmap.
/// </summary>
internal sealed partial class ReactorRenderer : IOrbStyleRenderer
{

    private static readonly BodyVariant DarkBody = new(Rgba.FromHex("#141824"), Rgba.FromHex("#06070B"), true);
    private static readonly BodyVariant LightBody = new(null, Rgba.FromHex("#E5E9F1"), true);

    private readonly LiquidReactorInk _ink = new();
    private readonly DrawingVisual _frame = CachedVisual();
    private readonly DrawingVisual _corona = CachedVisual();
    private readonly ParticleBitmap _particles = new();
    private readonly DrawingVisual _flare = new();
    private readonly DrawingVisual _core = CachedVisual();
    private readonly DrawingVisual _rim = CachedVisual();

    private OrbRenderContext? _context;
    private BodyVariant _body = DarkBody;
    private Geometry? _rimCircle;
    private bool _staticDirty = true;
    private uint _coronaKey = uint.MaxValue;
    private int _rimKey = int.MinValue;
    private bool _flareDrawn;

    public ReactorRenderer(OrbScene scene, uint seed)
    {
        _ = scene;
        Model = new ReactorModel(unchecked((int)seed));

        BuildRotors();
        Root.Children.Add(_frame);
        Root.Children.Add(_corona);
        Root.Children.Add(_rotorA);
        Root.Children.Add(_rotorB);
        Root.Children.Add(_rotorC);
        Root.Children.Add(_particles.Visual);
        Root.Children.Add(_flare);
        Root.Children.Add(_core);
        Root.Children.Add(_rim);
    }

    public OrbStyle Style => OrbStyle.Reactor;

    public ReactorModel Model { get; }

    IOrbStyleModel IOrbStyleRenderer.Model => Model;

    public ContainerVisual Root { get; } = new();

    public ContainerVisual? Overlay => null;

    public OrbReadoutLayout Readout => OrbReadoutLayout.Reactor;

    public BodyVariant Body => _body;

    public bool SharedEffects => true;

    public double? ReadoutSplit => null;

    public ReadoutPalette? BelowPalette => null;

    public void Configure(OrbRenderContext context)
    {
        _context = context;
        _body = context.Dark ? DarkBody : LightBody;
        _particles.Configure(context.Bounds, context.Scale);
        var c = context.Center;
        var core = ReactorGeometry.CoreRadius * context.Size;
        var circle = new EllipseGeometry(c, core, core);
        circle.Freeze();
        _rimCircle = circle;
        CenterRotors(c);
        _staticDirty = true;
        _coronaKey = uint.MaxValue;
        _rimKey = int.MinValue;
        _flareDrawn = true;
        InvalidateRotors();
    }

    public void Render(OrbScene scene, in OrbFrame frame)
    {
        if (_context is not { } ctx)
        {
            return;
        }

        var hc = ctx.HighContrast;
        var thumbnail = frame.Thumbnail;
        var effects = !hc && !thumbnail;
        if (_staticDirty)
        {
            _staticDirty = false;
            DrawFrame(ctx);
            DrawCore(ctx);
        }

        SetOpacity(_frame, thumbnail ? 0 : 1);
        RenderCorona(ctx, effects);
        RenderRotorA(ctx, hc, thumbnail);
        RenderRotorB(ctx, hc, thumbnail);

        RenderRotorC(ctx, hc);
        if (!_particles.SkipThisFrame(frame.Dt))
        {
            RenderSparks(ctx, effects);
        }
        RenderFlare(ctx, effects);
        RenderRim(ctx, hc, thumbnail);
    }

    /// <summary>Layers 2–3: the stator ring with its notches and the unlit tracks under rotors A and C.</summary>
    private void DrawFrame(OrbRenderContext ctx)
    {
        var m = Model;
        var s = ctx.Size;
        var c = ctx.Center;
        var hc = ctx.HighContrast;
        var text = SystemColors.WindowTextColor.ToRgba();
        using var dc = _frame.RenderOpen();
        dc.DrawEllipse(null, _ink.Pen(hc ? text : m.StatorColor, Math.Max(1, ReactorGeometry.StatorWidth * s)), c,
            ReactorGeometry.StatorRadius * s, ReactorGeometry.StatorRadius * s);
        var notches = m.NotchCount;
        var notchWidth = Math.Max(0.8, ReactorGeometry.NotchWidth * s);
        for (var i = 0; i < notches; i++)
        {
            var angle = 360.0 * i / notches;
            var color = hc ? text.WithAlpha(notches == 4 || i % 6 == 0 ? 1 : 0.5) : m.NotchColor(i);
            dc.DrawLine(_ink.Pen(color, notchWidth), OrbBrushes.Polar(c, ReactorGeometry.NotchInner * s, angle),
                OrbBrushes.Polar(c, ReactorGeometry.NotchOuter * s, angle));
        }

        var track = hc ? SystemColors.GrayTextColor.ToRgba().WithAlpha(0.6) : m.TrackColor;
        dc.DrawEllipse(null, _ink.Pen(track, ReactorGeometry.RotorAWidth * s), c, ReactorGeometry.RotorARadius * s,
            ReactorGeometry.RotorARadius * s);
        dc.DrawEllipse(null, _ink.Pen(track, ReactorGeometry.RotorCWidth * s), c, ReactorGeometry.RotorCRadius * s,
            ReactorGeometry.RotorCRadius * s);
    }

    /// <summary>Layer 10 disc: the core window's radial gradient (high contrast: the window color).</summary>
    private void DrawCore(OrbRenderContext ctx)
    {
        var m = Model;
        var s = ctx.Size;
        var c = ctx.Center;
        var radius = ReactorGeometry.CoreRadius * s;
        Brush fill;
        if (ctx.HighContrast)
        {
            fill = SystemColors.WindowBrush;
        }
        else
        {
            var brush = new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                Center = c,
                GradientOrigin = new Point(c.X, c.Y + ReactorGeometry.CoreGradientOffsetY * s),
                RadiusX = radius,
                RadiusY = radius,
                GradientStops =
                {
                    new GradientStop(m.CoreTopColor.ToColor(), ReactorGeometry.CoreGradientInner / ReactorGeometry.CoreRadius),
                    new GradientStop(m.CoreBottomColor.ToColor(), 1),
                },
            };
            brush.Freeze();
            fill = brush;
        }

        using var dc = _core.RenderOpen();
        dc.DrawEllipse(fill, null, c, radius, radius);
    }

    /// <summary>Layer 10 rim: re-recorded when its color or glow changes.</summary>
    private void RenderRim(OrbRenderContext ctx, bool hc, bool thumbnail)
    {
        var m = Model;
        var rim = hc ? SystemColors.WindowTextColor.ToRgba() : m.RimColor;
        var glow = hc || thumbnail ? Rgba.Transparent : m.RimGlowColor;
        var sigma = m.RimGlowSigma;
        var key = HashCode.Combine(LiquidReactorInk.Pack(rim), LiquidReactorInk.Pack(glow), (int)Math.Round(sigma * 4000));
        if (key == _rimKey || _rimCircle is not { } circle)
        {
            return;
        }

        _rimKey = key;
        var s = ctx.Size;
        var width = m.RimWidth * s;
        using var dc = _rim.RenderOpen();
        Glow(dc, _ink, circle, glow, width, sigma * s);
        dc.DrawGeometry(null, _ink.Pen(rim, width), circle);
    }

    /// <summary>Layer 4: a cached gradient ring around the core window; only its opacity changes per frame.</summary>
    private void RenderCorona(OrbRenderContext ctx, bool effects)
    {
        var m = Model;
        var opacity = m.CoronaOpacity;
        if (!effects || opacity <= 0.004)
        {
            SetOpacity(_corona, 0);
            return;
        }

        var color = m.CoronaColor;
        var key = LiquidReactorInk.PackRgb(color);
        if (key != _coronaKey)
        {
            _coronaKey = key;
            var brush = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(color.ToColor(), 0),
                    new GradientStop(color.ToColor(), ReactorGeometry.CoronaInner / ReactorGeometry.CoronaOuter),
                    new GradientStop(color.ToColor(0), 1),
                },
            };
            brush.Freeze();
            var radius = ReactorGeometry.CoronaOuter * ctx.Size;
            using var dc = _corona.RenderOpen();
            dc.DrawEllipse(brush, null, ctx.Center, radius, radius);
        }

        SetOpacity(_corona, opacity);
    }

    private void SplatBolt(SpriteRaster raster, ReadOnlySpan<ReactorPoint> path, Point c, double s, double px, Rgba color, double width,
        double alpha, SpriteBlend blend)
    {
        for (var i = 1; i < path.Length; i++)
        {
            var (x0, y0) = _particles.ToPixels(c.X + path[i - 1].X * s, c.Y + path[i - 1].Y * s);
            var (x1, y1) = _particles.ToPixels(c.X + path[i].X * s, c.Y + path[i].Y * s);
            raster.Streak(x0, y0, x1, y1, width, color, alpha, 1, blend);
        }
    }

    /// <summary>
    /// Lightning and its sparks in the particle bitmap (additive in dark): a bolt is three strokes of its 8-segment path
    /// splatted as streaks, cheaper than re-rendering cached bolt visuals at every re-randomization.
    /// </summary>
    private void RenderSparks(OrbRenderContext ctx, bool effects)
    {
        var sparks = Model.Sparks;
        var bolts = Model.Bolts;
        var any = effects && (sparks.Length > 0 || bolts.Length > 0);
        if (!any && !_particles.HasContent)
        {
            return;
        }

        if (_particles.Begin() is not { } raster)
        {
            return;
        }

        if (any)
        {
            var s = ctx.Size;
            var c = ctx.Center;
            var px = _particles.Scale;
            var blend = ctx.Dark ? SpriteBlend.Additive : SpriteBlend.Over;
            foreach (ref readonly var p in sparks)
            {
                var (x, y) = _particles.ToPixels(c.X + p.X * s, c.Y + p.Y * s);
                raster.Dot(x, y, Math.Max(0.7, p.Radius * s * px * 1.6), p.Color, p.Alpha, DotShape.Soft, blend);
            }

            var m = Model;
            foreach (var bolt in bolts)
            {
                var path = m.BoltPath(bolt);
                SplatBolt(raster, path, c, s, px, m.BoltColor.MultiplyAlpha(0.28), ReactorGeometry.BoltGlowWidth * s * px, bolt.Alpha, blend);
                SplatBolt(raster, path, c, s, px, m.BoltColor.MultiplyAlpha(0.75), ReactorGeometry.BoltBodyWidth * s * px, bolt.Alpha, blend);
                SplatBolt(raster, path, c, s, px, m.BoltCoreColor, Math.Max(0.6, ReactorGeometry.BoltCoreWidth * s) * px, bolt.Alpha, blend);
            }
        }

        _particles.End();
    }

    /// <summary>Layer 9: the pulse flare ring, re-recorded per frame only while it runs (0.32 s).</summary>
    private void RenderFlare(OrbRenderContext ctx, bool effects)
    {
        var m = Model;
        if (!effects || !m.FlareVisible)
        {
            if (_flareDrawn)
            {
                _flareDrawn = false;
                using var clear = _flare.RenderOpen();
            }

            return;
        }

        _flareDrawn = true;
        var s = ctx.Size;
        var color = m.FlareColor;
        // Quantized so successive flares reuse the cached pens.
        color = color.WithAlpha(Math.Round(color.A * 64) / 64);
        var width = Math.Max(0.25, Math.Round(m.FlareWidth * s * 8) / 8);
        var radius = m.FlareRadius * s;
        using var dc = _flare.RenderOpen();
        dc.DrawEllipse(null, _ink.Pen(color, width), ctx.Center, radius, radius);
    }
}
