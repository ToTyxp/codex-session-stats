using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Gauge;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Presentation.Orb;
using AgentSpeed.Presentation.Orb.Styles;
using AgentSpeed.Presentation.Particles;
using AgentSpeed.Presentation.Render;
using AgentSpeed.Theme;

namespace AgentSpeed.Orb.Render.Styles;

/// <summary>
/// The <c>led</c> style (DESIGN 2 / 2a.1) on retained visuals: the 36-segment gauge is one cached drawing re-recorded only
/// when its quantized fill, shimmer tick, flash or sweep changes; the comet is a cached arc that only rotates; the redline
/// edge glow only fades; sparks and embers are splatted into a particle bitmap.
/// </summary>
internal sealed class LedRenderer : IOrbStyleRenderer
{
    private readonly OrbScene _scene;
    private readonly DrawingVisual _gauge = new();
    private readonly DrawingVisual _edge = new();
    private readonly DrawingVisual _distortion = new();
    private readonly DrawingVisual _comet = new();
    private readonly RotateTransform _cometRotation = new();
    private readonly ParticleBitmap _particles = new();
    private OrbRenderContext? _context;
    private OrbBrushes? _shapes;
    private long _gaugeKey = long.MinValue;
    private int _edgeKey = -1;
    private int _cometKey = -1;
    private bool _distortionDrawn;

    public LedRenderer(OrbScene scene)
    {
        _scene = scene;
        Model = new LedModel(scene);
        _gauge.CacheMode = new BitmapCache(1);
        _edge.CacheMode = new BitmapCache(1);
        _comet.CacheMode = new BitmapCache(1);
        _comet.Transform = _cometRotation;
        Root.Children.Add(_gauge);
        Root.Children.Add(_edge);
        Root.Children.Add(_distortion);
        Root.Children.Add(_comet);
        Root.Children.Add(_particles.Visual);
    }

    public OrbStyle Style => OrbStyle.Led;

    public IOrbStyleModel Model { get; }

    public ContainerVisual Root { get; } = new();

    public ContainerVisual? Overlay => null;

    public OrbReadoutLayout Readout => OrbReadoutLayout.Led;

    public BodyVariant Body => BodyVariant.Standard;

    public bool SharedEffects => true;

    public double? ReadoutSplit => null;

    public ReadoutPalette? BelowPalette => null;

    public void Configure(OrbRenderContext context)
    {
        _context = context;
        _shapes = new OrbBrushes(context.Size);
        _particles.Configure(context.Bounds, context.Scale);
        _gaugeKey = long.MinValue;
        _edgeKey = -1;
        _cometKey = -1;
        _distortionDrawn = true;
        _cometRotation.CenterX = context.Center.X;
        _cometRotation.CenterY = context.Center.Y;
    }

    public void Render(OrbScene scene, in OrbFrame frame)
    {
        if (_context is not { } ctx || _shapes is not { } shapes)
        {
            return;
        }

        var gaugeKey = GaugeKey(scene, frame);
        if (gaugeKey != _gaugeKey)
        {
            _gaugeKey = gaugeKey;
            DrawGauge(scene, ctx, shapes, frame.Thumbnail);
        }


        RenderEdge(scene, ctx);
        RenderDistortion(scene, ctx);
        RenderComet(scene, ctx);
        if (!_particles.SkipThisFrame(frame.Dt))
        {
            RenderParticles(scene, ctx, frame.Thumbnail);
        }
    }

    private long GaugeKey(OrbScene scene, in OrbFrame frame)
    {
        var heat = scene.GaugeHeat;
        var fill = (long)Math.Round(heat * GaugeLayout.SegmentCount * 8);
        var shimmer = scene.Effects.Shimmer && scene.Tier == SpeedTier.Redline ? (long)Math.Floor(scene.Time * 12) & 0xFFFF : 0;
        var flash = (long)Math.Round(scene.Flash * 10);
        var sweep = scene.IdleSweep is { } p ? (long)Math.Round(p * 40) + 1 : 0;
        var ticks = (long)Math.Round(_scene.Redline);
        var hash = new HashCode();
        hash.Add(fill);
        hash.Add(shimmer);
        hash.Add(flash);
        hash.Add(sweep);
        hash.Add(ticks);
        hash.Add(frame.Thumbnail);
        return hash.ToHashCode();
    }

    private void DrawGauge(OrbScene scene, OrbRenderContext ctx, OrbBrushes shapes, bool thumbnail)
    {
        using var dc = _gauge.RenderOpen();
        var s = ctx.Size;
        var c = ctx.Center;
        var heat = scene.GaugeHeat;
        var flash = scene.Flash;
        var shimmer = scene.Shimmer;
        var sweep = scene.IdleSweep;
        var hc = ctx.HighContrast;
        var tokens = ctx.Tokens;
        var unlit = hc ? SystemColors.GrayTextColor.ToRgba().WithAlpha(0.6) : tokens.UnlitSegment;
        var leading = GaugeLayout.LeadingIndex(heat);
        var brushes = ctx.Brushes;

        for (var i = 0; i < GaugeLayout.SegmentCount; i++)
        {
            var geometry = shapes.Segments[i];
            var unlitAlpha = 1.0;
            if (sweep is { } p)
            {
                var position = i / (double)(GaugeLayout.SegmentCount - 1);
                var distance = scene.Effects.SlideTransitions ? Math.Abs(position - (p * 1.3 - 0.15)) : 0;
                var strength = scene.Effects.SlideTransitions ? Math.Exp(-distance * distance * 90) : Math.Sin(Math.PI * p) * 0.6;
                unlitAlpha += 1.6 * strength;
            }

            dc.DrawGeometry(brushes.Get(unlit, unlitAlpha), null, geometry);
            var fill = GaugeLayout.SegmentFill(heat, i);
            if (fill <= 0.002)
            {
                continue;
            }

            Rgba color;
            if (hc)
            {
                color = SystemColors.HighlightColor.ToRgba();
            }
            else if (i == leading)
            {
                color = tokens.LeadingSegment(heat);
            }
            else
            {
                color = HeatPalette.Sample(GaugeLayout.SegmentHeat(i));
                if (flash > 0)
                {
                    color = OkLab.Mix(color, HeatPalette.WhiteHot, 0.25 * flash);
                }
            }

            dc.DrawGeometry(brushes.Get(color, fill * shimmer[i]), null, geometry);
        }

        if (!hc && leading >= 0 && !thumbnail)
        {
            var arc = GaugeLayout.Segment(leading);
            var center = OrbBrushes.Polar(c, arc.MidRadius * s, arc.MidDegrees);
            var bloomRadius = (GaugeLayout.SegmentLength * 0.5 + 0.03) * s * scene.Effects.Bloom;
            var bloomColor = ctx.Dark ? OkLab.Mix(HeatPalette.Sample(Q(heat)), HeatPalette.WhiteHot, 0.35) : HeatPalette.Sample(Q(heat));
            dc.PushOpacity(Math.Clamp(0.55 + 0.45 * GaugeLayout.SegmentFill(heat, leading), 0, 1) * (0.6 + 0.4 * heat));
            dc.DrawEllipse(shapes.Bloom(bloomColor), null, center, bloomRadius, bloomRadius);
            dc.Pop();
        }

        if (thumbnail)
        {
            return;
        }

        foreach (var (_, fraction) in GaugeLayout.Ticks(_scene.Redline))
        {
            var lit = heat >= fraction - 0.0001;
            var point = OrbBrushes.Polar(c, GaugeLayout.TickRadius * s, GaugeLayout.AngleForFraction(fraction));
            var color = hc ? SystemColors.WindowTextColor.ToRgba() : lit ? HeatPalette.Sample(fraction) : tokens.SecondaryText;
            var radius = Math.Max(0.9, 0.0085 * s);
            dc.DrawEllipse(brushes.Get(color, lit ? 0.95 : 0.55), null, point, radius, radius);
        }
    }

    private void RenderEdge(OrbScene scene, OrbRenderContext ctx)
    {
        var glow = scene.EdgeGlow;
        var heat = scene.GaugeHeat;
        var visible = !ctx.HighContrast && glow > 0.01 && heat > 0.02;
        var key = visible ? (int)Math.Round(Q(heat) * 63) * 64 + (int)Math.Round(Q(scene.Overdrive) * 63) : -1;
        if (key != _edgeKey)
        {
            _edgeKey = key;
            using var dc = _edge.RenderOpen();
            if (visible)
            {
                var s = ctx.Size;
                var end = GaugeLayout.AngleForFraction(heat);
                var color = HeatPalette.Highlight(Q(heat), Q(scene.Overdrive));
                var arc = OrbBrushes.Arc(ctx.Center, (GaugeLayout.OuterRadius + 0.012) * s, GaugeLayout.StartDegrees, end);
                dc.DrawGeometry(null, MediaColors.FrozenPen(ctx.Brushes.Get(color, 0.35), 0.05 * s, PenLineCap.Round), arc);
                dc.DrawGeometry(null, MediaColors.FrozenPen(ctx.Brushes.Get(color, 0.6), 0.016 * s, PenLineCap.Round), arc);
            }
        }

        SetOpacity(_edge, visible ? Math.Clamp(glow, 0, 1) : 0);
    }

    private void RenderDistortion(OrbScene scene, OrbRenderContext ctx)
    {
        var active = scene.Effects.HeatDistortion && scene.Tier == SpeedTier.Redline && !ctx.HighContrast;
        if (!active)
        {
            if (_distortionDrawn)
            {
                _distortionDrawn = false;
                using var clear = _distortion.RenderOpen();
            }

            return;
        }

        _distortionDrawn = true;
        using var dc = _distortion.RenderOpen();
        var s = ctx.Size;
        var t = scene.Time;
        var color = HeatPalette.Highlight(Q(scene.GaugeHeat), Q(scene.Overdrive));
        for (var k = 0; k < 3; k++)
        {
            var radius = (0.515 + 0.03 * k + 0.008 * Math.Sin(t * 5.3 + k * 2.1)) * s;
            dc.DrawEllipse(null, MediaColors.FrozenPen(ctx.Brushes.Get(color, 0.05 + 0.03 * Math.Sin(t * 3.7 + k)), 0.012 * s), ctx.Center,
                radius, radius * (1 + 0.01 * Math.Sin(t * 4.1 + k)));
        }
    }

    private void RenderComet(OrbScene scene, OrbRenderContext ctx)
    {
        var opacity = scene.CometOpacity;
        if (opacity <= 0.005 || ctx.HighContrast)
        {
            SetOpacity(_comet, 0);
            return;
        }

        var key = (int)Math.Round(Q(scene.Heat) * 63) * 2 + (ctx.Dark ? 1 : 0);
        if (key != _cometKey)
        {
            _cometKey = key;
            DrawComet(scene, ctx);
        }

        var head = scene.CometDegrees;
        _cometRotation.Angle = head;
        SetOpacity(_comet, Math.Clamp(opacity * GapFade(head - 6), 0, 1));
    }

    /// <summary>The comet drawn with its head at 0° (3 o'clock); a rotation places it.</summary>
    private void DrawComet(OrbScene scene, OrbRenderContext ctx)
    {
        using var dc = _comet.RenderOpen();
        var s = ctx.Size;
        var c = ctx.Center;
        var heat = Q(scene.Heat);
        var color = HeatPalette.Sample(heat);
        var hot = ctx.Dark ? HeatPalette.WhiteHot : OkLab.Darken(color, 0.1);
        const int pieces = 16;
        const double length = 40;
        var radius = GaugeLayout.CometRadius * s;
        for (var i = 0; i < pieces; i++)
        {
            var t0 = i / (double)pieces;
            var t1 = (i + 1) / (double)pieces;
            var a0 = -length + length * t0;
            var a1 = -length + length * t1 + 0.4;
            var pieceColor = OkLab.Mix(color, hot, Math.Max(0, (t1 - 0.6) / 0.4));
            var alpha = Math.Pow(t1, 1.6);
            var thickness = (0.006 + 0.012 * t1 * t1) * s;
            dc.DrawGeometry(null, MediaColors.FrozenPen(ctx.Brushes.Get(pieceColor, alpha), thickness), OrbBrushes.Arc(c, radius, a0, a1));
        }

        var point = OrbBrushes.Polar(c, radius, 0);
        var glow = 0.035 * s * scene.Effects.Bloom;
        dc.DrawEllipse(_shapes!.Bloom(hot), null, point, glow, glow);
    }

    private void RenderParticles(OrbScene scene, OrbRenderContext ctx, bool thumbnail)
    {
        var particles = scene.Particles.Particles;
        if (particles.Length == 0 && !_particles.HasContent)
        {
            return;
        }

        if (_particles.Begin() is not { } raster)
        {
            return;
        }

        if (!thumbnail && !ctx.HighContrast)
        {
            var s = ctx.Size;
            var c = ctx.Center;
            var dark = ctx.Dark;
            var blend = dark ? SpriteBlend.Additive : SpriteBlend.Over;
            var px = _particles.Scale;
            var sizeScale = ctx.Tokens.ParticleSizeScale * Math.Clamp(s / 148, 0.75, 1.4);
            foreach (ref readonly var p in particles)
            {
                var progress = p.Progress;
                var color = ParticleColor(p, progress, dark);
                var alpha = (p.Kind == ParticleKind.Spark
                    ? Math.Pow(1 - progress, 1.3)
                    : Math.Sin(Math.PI * Math.Min(1, progress * 1.15)) * 0.9) * ReadoutZone.Fade(p.X, p.Y);
                var radius = p.Size * 0.5 * sizeScale * (p.Kind == ParticleKind.Ember ? 1 - 0.35 * progress : 1);
                var (x, y) = _particles.ToPixels(c.X + p.X * s, c.Y + p.Y * s);
                if (dark)
                {
                    // The spark and its halo in one splat (the halo reaches 2 × the radius instead of 2.4 ×: same look,
                    // a third fewer pixels at the redline).
                    raster.Dot(x, y, radius * 2.0 * px, color, alpha, DotShape.Spark, blend);
                }
                else
                {
                    raster.Dot(x, y, radius * 1.25 * px, color, alpha, DotShape.Soft, blend);
                }
            }
        }

        _particles.End();
    }

    private static readonly Rgba[] ParticleColors = BuildParticleColors();

    /// <summary>Particle colors from a table (24 heat × 8 age steps per kind and appearance): no OKLab math per frame.</summary>
    private static Rgba ParticleColor(in Particle p, double progress, bool dark)
    {
        var heat = (int)Math.Round(Math.Clamp(p.Heat, 0, 1) * 23);
        var step = (int)Math.Round(Math.Clamp(progress, 0, 1) * 7);
        var kind = p.Kind == ParticleKind.Spark ? 0 : 1;
        return ParticleColors[((dark ? 0 : 2) + kind) * 192 + heat * 8 + step];
    }

    private static Rgba[] BuildParticleColors()
    {
        var table = new Rgba[4 * 192];
        for (var variant = 0; variant < 4; variant++)
        {
            var dark = variant < 2;
            var spark = variant % 2 == 0;
            for (var h = 0; h < 24; h++)
            {
                var heat = h / 23.0;
                var baseColor = HeatPalette.Sample(heat);
                var hot = dark ? HeatPalette.WhiteHot : HeatPalette.LightSparkHot;
                for (var k = 0; k < 8; k++)
                {
                    var step = k / 7.0;
                    table[variant * 192 + h * 8 + k] = spark
                        ? OkLab.Mix(hot, baseColor, Math.Min(1, step * 1.4))
                        : OkLab.Mix(HeatPalette.Highlight(heat, 0.3), baseColor, step);
                }
            }
        }

        return table;
    }

    private static void SetOpacity(Visual visual, double opacity)
    {
        if (visual is ContainerVisual container && Math.Abs(container.Opacity - opacity) > 0.002)
        {
            container.Opacity = opacity;
        }
    }

    private static double Q(double value) => Math.Round(Math.Clamp(value, 0, 1) * 63) / 63;

    private static double GapFade(double degrees)
    {
        var a = ((degrees % 360) + 360) % 360;
        return Easing.SmoothStep(38, 62, Math.Abs(a - 90));
    }
}
