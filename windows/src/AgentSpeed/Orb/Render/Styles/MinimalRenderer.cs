using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.Orb;
using AgentSpeed.Presentation.Orb.Styles;
using AgentSpeed.Presentation.Settings;
using AgentSpeed.Theme;

namespace AgentSpeed.Orb.Render.Styles;

/// <summary>
/// The <c>minimal</c> style (DESIGN 2a.5) on retained visuals, back to front: track, ring glow (a narrow and a wide
/// copy), progress ring and head dot. The ring and its glow re-record only when the model's geometry or color keys
/// change; per frame the glow breathes by crossfading its two copies, and the head dot only rotates and fades.
/// </summary>
internal sealed class MinimalRenderer : IOrbStyleRenderer
{
    private const long Unset = long.MinValue;

    private static readonly BodyVariant NoLens = new(null, null, Lens: false);

    private readonly MinimalModel _model = new();
    private readonly NeedlePaint _paint = new();
    private readonly DrawingVisual _track = Cached();
    private readonly DrawingVisual _glowNarrow = Cached();
    private readonly DrawingVisual _glowWide = Cached();
    private readonly DrawingVisual _ring = Cached();
    private readonly DrawingVisual _headDot = Cached();
    private readonly RotateTransform _headRotation = new();
    private OrbRenderContext? _context;
    private MinimalDrawings? _drawings;
    private long _trackKey = Unset;
    private long _ringGeometry = Unset;
    private long _ringColor = Unset;
    private long _ringFlags = Unset;
    private long _glowKey = Unset;
    private long _dotKey = Unset;
    private double _headAngle = double.NaN;

    public MinimalRenderer(OrbScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _headDot.Transform = _headRotation;
        Root.Children.Add(_track);
        Root.Children.Add(_glowNarrow);
        Root.Children.Add(_glowWide);
        Root.Children.Add(_ring);
        Root.Children.Add(_headDot);
    }

    public OrbStyle Style => OrbStyle.Minimal;

    public IOrbStyleModel Model => _model;

    public ContainerVisual Root { get; } = new();

    public ContainerVisual? Overlay => null;

    public OrbReadoutLayout Readout => OrbReadoutLayout.Minimal;

    public BodyVariant Body => NoLens;

    public bool SharedEffects => false;

    public double? ReadoutSplit => null;

    public ReadoutPalette? BelowPalette => null;

    public void Configure(OrbRenderContext context)
    {
        _context = context;
        _paint.Clear();
        _drawings = new MinimalDrawings(context, _paint);
        _headRotation.CenterX = context.Center.X;
        _headRotation.CenterY = context.Center.Y;
        _trackKey = _ringGeometry = _ringColor = _ringFlags = _glowKey = _dotKey = Unset;
        _headAngle = double.NaN;
    }

    public void Render(OrbScene scene, in OrbFrame frame)
    {
        if (_context is not { } ctx || _drawings is not { } drawings)
        {
            return;
        }

        var m = _model;
        var hc = ctx.HighContrast || m.HighContrast;
        var width = m.RingWidth * ctx.Size;
        var widthKey = (long)Math.Round(m.RingWidth * 100000);
        var trackKey = (long)NeedlePaint.Pack(m.TrackColor) << 20 | widthKey << 1 | (hc ? 1L : 0);
        if (trackKey != _trackKey)
        {
            _trackKey = trackKey;
            using var dc = _track.RenderOpen();
            drawings.Track(dc, m.TrackColor, width, hc);
        }

        if (!m.RingVisible)
        {
            SetOpacity(_ring, 0);
            SetOpacity(_glowNarrow, 0);
            SetOpacity(_glowWide, 0);
            SetOpacity(_headDot, 0);
            return;
        }

        var flags = (hc ? 1L : 0) | (frame.Thumbnail ? 2L : 0);
        if (m.GeometryKey != _ringGeometry || m.ColorKey != _ringColor || flags != _ringFlags)
        {
            _ringGeometry = m.GeometryKey;
            _ringColor = m.ColorKey;
            _ringFlags = flags;
            using var dc = _ring.RenderOpen();
            drawings.Ring(dc, m, hc, frame.Thumbnail);
        }

        SetOpacity(_ring, 1);
        RenderGlow(drawings, m, ctx.Size, width, widthKey, !hc && !frame.Thumbnail, frame.Effects.Level == EffectsIntensity.Max);
        RenderHeadDot(drawings, m, ctx.Size, hc);
    }

    private void RenderGlow(MinimalDrawings drawings, MinimalModel m, double size, double width, long widthKey, bool allowed, bool max)
    {
        if (!allowed || !(m.RingGlowSigma > 0) || m.RingGlowOpacity <= 0.002)
        {
            SetOpacity(_glowNarrow, 0);
            SetOpacity(_glowWide, 0);
            return;
        }

        // A narrow and a wide copy crossfade to follow σ between the steady glow and the breath's peak plus a blink.
        var scale = max ? MinimalModel.MaxGlowScale : 1;
        var narrow = MinimalModel.GlowSigma * scale;
        var wide = (MinimalModel.BreathGlowSigma + MinimalModel.BlinkGlowSigma) * scale;
        // The glow's soft end hides a 0.5° step, so it re-records less often than the ring.
        var lap = Math.Round(Math.Clamp(m.Lap1EndDegrees - StyleMath.TopDegrees, 0, 360) * 2) / 2;
        var color = NeedlePaint.Coarse(m.HeadColor);
        var key = (long)Math.Round(lap * 2) << 40 | NeedlePaint.CoarseKey(color) << 16 | widthKey << 1 | (max ? 1L : 0);
        if (key != _glowKey)
        {
            _glowKey = key;
            using (var dc = _glowNarrow.RenderOpen())
            {
                drawings.Glow(dc, lap, width, color, narrow * size);
            }

            using (var dc = _glowWide.RenderOpen())
            {
                drawings.Glow(dc, lap, width, color, wide * size);
            }
        }

        var t = StyleMath.Clamp01((m.RingGlowSigma - narrow) / (wide - narrow));
        SetOpacity(_glowNarrow, m.RingGlowOpacity * (1 - t));
        SetOpacity(_glowWide, m.RingGlowOpacity * t);
    }

    private void RenderHeadDot(MinimalDrawings drawings, MinimalModel m, double size, bool hc)
    {
        if (!m.HeadDotVisible)
        {
            SetOpacity(_headDot, 0);
            return;
        }

        var color = hc ? SystemColors.HighlightTextColor.ToRgba() : NeedlePaint.Coarse(m.HeadDotColor);
        var key = NeedlePaint.CoarseKey(color) << 20 | (long)Math.Round(m.HeadDotRadius * 100000) << 1 | (hc ? 1L : 0);
        if (key != _dotKey)
        {
            _dotKey = key;
            using var dc = _headDot.RenderOpen();
            drawings.HeadDot(dc, color, m.HeadDotRadius * size);
        }

        var angle = m.HeadDegrees;
        if (angle != _headAngle)
        {
            _headAngle = angle;
            _headRotation.Angle = angle;
        }

        SetOpacity(_headDot, m.HeadDotOpacity);
    }

    private static DrawingVisual Cached() => new() { CacheMode = new BitmapCache(1) };

    private static void SetOpacity(ContainerVisual visual, double opacity)
    {
        if (Math.Abs(visual.Opacity - opacity) > 0.002 || (opacity == 0) != (visual.Opacity == 0))
        {
            visual.Opacity = opacity;
        }
    }
}
