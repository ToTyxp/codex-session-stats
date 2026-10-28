using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Orb.Styles;
using AgentSpeed.Theme;

namespace AgentSpeed.Orb.Render.Styles;

/// <summary>
/// The drawings of the <c>needle</c> style for one size (DESIGN 2a.2). Geometry is built once per Configure and each
/// method records one cached layer. The needle, its glow, shadow, tip flare and wake are drawn pointing at 0°
/// (3 o'clock) around the disc center; a rotation places them.
/// </summary>
internal sealed class NeedleDrawings
{
    public const int TraceSegmentsPerDial = 48;

    // The prototype's 0.002 rad overlap between trace pieces.
    private const double TraceOverlapDegrees = 0.002 * StyleMath.DegreesPerRadian;

    // The lamp glow is 90 % of the lamp's opacity (NeedleLamp.GlowOpacity); the layer's opacity is the lamp's.
    private const double LampGlowShare = 0.9;

    // Where the blade's glow and shadow show (between the hub and the tip): their thin-shape profile uses its width there.
    private const double VisibleBladeRadius = 0.33;

    // The hub's inner rim light (prototype): vertical gradient just inside the chrome bezel.
    private static readonly Rgba DarkRimTop = Rgba.White.WithAlpha(0.2);
    private static readonly Rgba DarkRimBottom = Rgba.White.WithAlpha(0.03);
    private static readonly Rgba LightRimTop = Rgba.White;
    private static readonly Rgba LightRimBottom = Rgba.Black.WithAlpha(0.1);

    private readonly NeedlePaint _paint;
    private readonly double _s;
    private readonly Point _c;
    private readonly Geometry _band;
    private readonly Geometry _redZone;
    private readonly Geometry _blade;
    private readonly Geometry[] _trace = new Geometry[TraceSegmentsPerDial];
    private readonly Geometry[] _wakeClockwise = new Geometry[NeedleWake.MaxLayers];
    private readonly Geometry[] _wakeCounterclockwise = new Geometry[NeedleWake.MaxLayers];
    private readonly double _bladeWidth;

    public NeedleDrawings(OrbRenderContext ctx, NeedlePaint paint)
    {
        _paint = paint;
        _s = ctx.Size;
        _c = ctx.Center;
        _band = OrbBrushes.Arc(_c, NeedleGeometry.DialBandRadius * _s, StyleMath.DialDegrees(NeedleGeometry.DialBandStart),
            StyleMath.DialDegrees(NeedleGeometry.DialBandEnd));
        _redZone = OrbBrushes.Arc(_c, NeedleGeometry.RedZoneRadius * _s, StyleMath.DialDegrees(NeedleGeometry.RedZoneStart),
            StyleMath.DialDegrees(NeedleGeometry.RedZoneEnd));
        _blade = BuildBlade();
        var half = NeedleGeometry.NeedleBaseWidth / 2;
        _bladeWidth = 2 * (half + (NeedleGeometry.NeedleTipWidth / 2 - half) * VisibleBladeRadius / NeedleGeometry.NeedleTip) * _s;

        var traceRadius = NeedleGeometry.TraceRadius * _s;
        for (var k = 0; k < _trace.Length; k++)
        {
            _trace[k] = OrbBrushes.Arc(_c, traceRadius, StyleMath.DialDegrees(k / (double)TraceSegmentsPerDial),
                StyleMath.DialDegrees((k + 1) / (double)TraceSegmentsPerDial) + TraceOverlapDegrees);
        }

        var inner = NeedleGeometry.WakeInnerRadius * _s;
        var outer = NeedleGeometry.WakeOuterRadius * _s;
        for (var m = 0; m < NeedleWake.MaxLayers; m++)
        {
            var trail = NeedleWake.SliceDegrees * (m + 1);
            _wakeClockwise[m] = OrbBrushes.AnnularSector(_c, inner, outer, -trail, 0);
            _wakeCounterclockwise[m] = OrbBrushes.AnnularSector(_c, inner, outer, 0, trail);
        }
    }

    private static Rgba WindowText => SystemColors.WindowTextColor.ToRgba();

    private static Rgba Highlight => SystemColors.HighlightColor.ToRgba();

    /// <summary>Dial band (2), stop pins (6) and lamp housings (7): static apart from appearance and level of detail.</summary>
    public void Dial(DrawingContext dc, NeedlePalette palette, bool hc, bool pins, bool housings)
    {
        if (!hc)
        {
            dc.DrawGeometry(null, _paint.Pen(palette.DialBand, 1, NeedleGeometry.DialBandWidth * _s), _band);
        }

        if (pins)
        {
            var brush = _paint.Brush(hc ? WindowText : palette.Pin);
            var radius = NeedleGeometry.PinSize * _s;
            dc.DrawEllipse(brush, null, DialPoint(NeedleGeometry.PinRadius, NeedleGeometry.ZeroPin), radius, radius);
            dc.DrawEllipse(brush, null, DialPoint(NeedleGeometry.PinRadius, NeedleGeometry.PegPin), radius, radius);
        }

        if (!housings)
        {
            return;
        }

        var size = NeedleGeometry.LampSize * _s;
        foreach (var degrees in NeedleGeometry.LampDegrees)
        {
            var center = OrbBrushes.Polar(_c, NeedleGeometry.LampRingRadius * _s, degrees);
            if (hc)
            {
                dc.DrawEllipse(null, _paint.Pen(SystemColors.GrayTextColor.ToRgba(), 1, 1), center, size - 0.5, size - 0.5);
            }
            else
            {
                dc.DrawEllipse(_paint.Brush(palette.LampHousing), null, center, size, size);
            }
        }
    }

    /// <summary>The red zone (3) at full opacity; the layer's opacity carries the pulse.</summary>
    public void RedZone(DrawingContext dc, NeedlePalette palette, bool hc) =>
        dc.DrawGeometry(null, _paint.Pen(hc ? Highlight : palette.RedZone.WithAlpha(1), 1, NeedleGeometry.RedZoneWidth * _s), _redZone);

    /// <summary>The red-zone glow at full strength (σ in S); the layer's opacity carries the model's glow opacity.</summary>
    public void RedZoneGlow(DrawingContext dc, NeedlePalette palette, double sigma) =>
        _paint.GlowStroke(dc, _redZone, NeedleGeometry.RedZoneWidth * _s, palette.RedZone.WithAlpha(1), 1, sigma * _s, core: true,
            PenLineCap.Round);

    /// <summary>The graduations (4): glows of the lit majors first, then every visible tick.</summary>
    public void Ticks(DrawingContext dc, ReadOnlySpan<NeedleTick> ticks, bool hc)
    {
        if (!hc)
        {
            foreach (ref readonly var tick in ticks)
            {
                if (tick.Visible && tick.GlowSigma > 0 && tick.GlowOpacity > 0)
                {
                    var (from, to) = TickEnds(tick);
                    _paint.GlowLine(dc, from, to, tick.Width * _s, tick.Color.WithAlpha(1), tick.GlowOpacity, tick.GlowSigma * _s);
                }
            }
        }

        var text = WindowText;
        foreach (ref readonly var tick in ticks)
        {
            if (tick.Visible)
            {
                var (from, to) = TickEnds(tick);
                dc.DrawLine(_paint.Pen(hc ? text : tick.Color, 1, tick.Width * _s, PenLineCap.Round), from, to);
            }
        }
    }

    /// <summary>
    /// The trace (5) from n = 0 to <paramref name="end"/> on a fixed grid of 1/48 (reused geometry), each piece in the
    /// color of its end position, its opacity rising linearly from <paramref name="startAlpha"/> to
    /// <paramref name="endAlpha"/> at the needle.
    /// </summary>
    public void Trace(DrawingContext dc, NeedleModel model, double end, double startAlpha, double endAlpha, bool hc)
    {
        if (!(end > 0))
        {
            return;
        }

        var width = NeedleGeometry.TraceWidth * _s;
        var steps = end * TraceSegmentsPerDial;
        var full = (int)Math.Floor(steps + 1e-9);
        var count = full + (steps - full > 1e-6 ? 1 : 0);
        var text = WindowText;
        for (var k = 0; k < count; k++)
        {
            var p1 = Math.Min(end, (k + 1) / (double)TraceSegmentsPerDial);
            var geometry = k < full && k < _trace.Length
                ? _trace[k]
                : OrbBrushes.Arc(_c, NeedleGeometry.TraceRadius * _s, StyleMath.DialDegrees(k / (double)TraceSegmentsPerDial),
                    StyleMath.DialDegrees(end));
            if (hc)
            {
                dc.DrawGeometry(null, _paint.Pen(text, 1, width), geometry);
                continue;
            }

            var alpha = startAlpha + (endAlpha - startAlpha) * (p1 / end);
            dc.DrawGeometry(null, _paint.Pen(model.TraceColor(p1), Math.Round(alpha * 64) / 64, width), geometry);
        }
    }

    /// <summary>One shift light (7): glow (dark) and lamp at full opacity; the layer's opacity is the lamp's.</summary>
    public void Lamp(DrawingContext dc, in NeedleLamp lamp, bool hc)
    {
        var center = new Point(_c.X + lamp.X * _s, _c.Y + lamp.Y * _s);
        var radius = NeedleGeometry.LampSize * NeedleGeometry.LampLitScale * _s;
        if (hc)
        {
            dc.DrawEllipse(_paint.Brush(Highlight), null, center, radius, radius);
            return;
        }

        if (lamp.GlowSigma > 0)
        {
            var glow = _paint.BlurredDisc(lamp.Color.WithAlpha(1), LampGlowShare, radius, lamp.GlowSigma * _s, out var outer);
            dc.DrawEllipse(glow, null, center, outer, outer);
        }

        dc.DrawEllipse(_paint.Brush(lamp.Color.WithAlpha(1)), null, center, radius, radius);
    }

    /// <summary>
    /// The wake (8) trailing a needle at 0° by <paramref name="slices"/> × 2.5°, behind it for a clockwise turn
    /// (<paramref name="clockwise"/>), at the model's head opacity at the needle; the layer's opacity scales it.
    /// </summary>
    public void Wake(DrawingContext dc, int slices, bool clockwise, Rgba color)
    {
        Span<NeedleWakeLayer> layers = stackalloc NeedleWakeLayer[NeedleWake.MaxLayers];
        var n = NeedleWake.Layers(slices, NeedleModel.WakeOpacity, layers);
        var wedges = clockwise ? _wakeClockwise : _wakeCounterclockwise;
        var opaque = color.WithAlpha(1);
        for (var k = 0; k < n; k++)
        {
            var index = Math.Clamp((int)Math.Round(layers[k].TrailDegrees / NeedleWake.SliceDegrees) - 1, 0, wedges.Length - 1);
            dc.DrawGeometry(_paint.Brush(opaque, layers[k].Alpha), null, wedges[index]);
        }
    }

    /// <summary>Light only: the blade's soft drop shadow at 0° (a transform adds the screen-space offset).</summary>
    public void Shadow(DrawingContext dc, NeedlePalette palette)
    {
        var sigma = palette.NeedleShadowSigma * _s;
        var color = palette.NeedleShadow;
        var below = _paint.GlowOutline(dc, _blade, _bladeWidth, color, 1, sigma);
        var inside = color.A * NeedleGlow.StrokeProfile(_bladeWidth / sigma, 0);
        var fill = below >= 1 ? 0 : Math.Max(0, (inside - below) / (1 - below));
        dc.DrawGeometry(_paint.Brush(color.WithAlpha(1), fill), null, _blade);
    }

    /// <summary>Dark only: the blade's glow at full strength (σ in S); the layer's opacity is the model's glow opacity.</summary>
    public void Glow(DrawingContext dc, Rgba color, double sigma) =>
        _paint.GlowOutline(dc, _blade, _bladeWidth, color.WithAlpha(1), 1, sigma * _s);

    /// <summary>The blade (9) in <paramref name="fill"/> with its bright spine.</summary>
    public void Blade(DrawingContext dc, Rgba fill, Rgba spine, bool hc)
    {
        if (hc)
        {
            dc.DrawGeometry(_paint.Brush(WindowText), null, _blade);
            return;
        }

        dc.DrawGeometry(_paint.Brush(fill), null, _blade);
        var tip = NeedleGeometry.NeedleTip - NeedleGeometry.NeedleTipWidth / 2;
        dc.DrawLine(_paint.Pen(spine, 1, Math.Max(0.6, NeedleGeometry.SpineWidth * _s), PenLineCap.Round), Local(NeedleGeometry.SpineStart),
            Local(tip));
    }

    /// <summary>The pulse flare's radial spot at the tip at full strength; the layer's opacity is the model's.</summary>
    public void Flare(DrawingContext dc, NeedlePalette palette)
    {
        var spot = new RadialGradientBrush(palette.FlareSpot.ToColor(), palette.FlareSpot.ToColor(0));
        spot.Freeze();
        var radius = NeedleGeometry.FlareSpotRadius * _s;
        dc.DrawEllipse(spot, null, Local(NeedleGeometry.NeedleTip), radius, radius);
    }

    /// <summary>The hub (11): soft drop shadow, gradient disc, inner rim light and the 135° chrome bezel.</summary>
    public void Hub(DrawingContext dc, NeedlePalette palette, bool hc)
    {
        var r = NeedleGeometry.HubRadius * _s;
        if (hc)
        {
            dc.DrawEllipse(SystemColors.WindowBrush, _paint.Pen(WindowText, 1, 1.5), _c, r, r);
            return;
        }

        var shadow = _paint.BlurredDisc(palette.HubShadow, 1, r, NeedleGeometry.HubShadowSigma * _s, out var shadowRadius);
        dc.DrawEllipse(shadow, null, new Point(_c.X, _c.Y + NeedleGeometry.HubShadowOffsetY * _s), shadowRadius, shadowRadius);

        var fill = new LinearGradientBrush(palette.HubTop.ToColor(), palette.HubBottom.ToColor(), 90);
        fill.Freeze();
        dc.DrawEllipse(fill, null, _c, r, r);

        var dark = palette.Dark;
        var rim = new LinearGradientBrush((dark ? DarkRimTop : LightRimTop).ToColor(), (dark ? DarkRimBottom : LightRimBottom).ToColor(), 90);
        rim.Freeze();
        var inset = r - 0.004 * _s;
        dc.DrawEllipse(null, MediaColors.FrozenPen(rim, Math.Max(1, 0.008 * _s)), _c, inset, inset);

        var bezel = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop(palette.BezelStart.ToColor(), 0),
                new GradientStop(palette.BezelMiddle.ToColor(), 0.5),
                new GradientStop(palette.BezelEnd.ToColor(), 1),
            },
        };
        bezel.Freeze();
        dc.DrawEllipse(null, MediaColors.FrozenPen(bezel, Math.Max(1.2, NeedleGeometry.HubBezelWidth * _s)), _c, r, r);
    }

    private Geometry BuildBlade()
    {
        var tail = NeedleGeometry.NeedleTail * _s;
        var tip = NeedleGeometry.NeedleTip * _s;
        var pivot = NeedleGeometry.NeedleBaseWidth * _s / 2;
        var end = NeedleGeometry.NeedleTipWidth * _s / 2;
        var counterweight = pivot * NeedleGeometry.NeedleTailWidthScale;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(Offset(tail, counterweight), isFilled: true, isClosed: true);
            g.LineTo(Offset(0, pivot), true, true);
            g.LineTo(Offset(tip, end), true, true);
            g.ArcTo(Offset(tip, -end), new Size(end, end), 0, false, SweepDirection.Counterclockwise, true, true);
            g.LineTo(Offset(0, -pivot), true, true);
            g.LineTo(Offset(tail, -counterweight), true, true);
        }

        geometry.Freeze();
        return geometry;
    }

    private (Point From, Point To) TickEnds(in NeedleTick tick) =>
        (OrbBrushes.Polar(_c, tick.InnerRadius * _s, tick.Degrees), OrbBrushes.Polar(_c, tick.OuterRadius * _s, tick.Degrees));

    private Point DialPoint(double radius, double n) => OrbBrushes.Polar(_c, radius * _s, StyleMath.DialDegrees(n));

    private Point Local(double radius) => new(_c.X + radius * _s, _c.Y);

    private Point Offset(double x, double y) => new(_c.X + x, _c.Y + y);
}
