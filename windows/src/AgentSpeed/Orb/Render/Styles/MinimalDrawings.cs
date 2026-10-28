using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Orb.Styles;
using AgentSpeed.Theme;

namespace AgentSpeed.Orb.Render.Styles;

/// <summary>
/// The drawings of the <c>minimal</c> style for one size (DESIGN 2a.5): the track, the progress ring (the first lap's
/// conic gradient as pieces on a fixed 7.5° grid whose geometry is built once, the second lap's head shadow and the
/// second lap), the ring glow and the head dot (drawn at 0°; a rotation places it).
/// </summary>
internal sealed class MinimalDrawings
{
    // Each gradient piece reaches this far into the next one: the colors are opaque, so this only hides the
    // anti-aliasing seams between pieces.
    private const double OverlapDegrees = 0.6;

    private const double FullCircle = 359.99;

    private readonly NeedlePaint _paint;
    private readonly double _s;
    private readonly Point _c;
    private readonly double _r;
    private readonly Geometry[] _pieces = new Geometry[MinimalArcs.LapSegments];
    private readonly Geometry _circle;

    public MinimalDrawings(OrbRenderContext ctx, NeedlePaint paint)
    {
        _paint = paint;
        _s = ctx.Size;
        _c = ctx.Center;
        _r = MinimalModel.TrackRadius * _s;
        for (var k = 0; k < _pieces.Length; k++)
        {
            var from = StyleMath.TopDegrees + k * MinimalArcs.StepDegrees;
            _pieces[k] = OrbBrushes.Arc(_c, _r, from, from + MinimalArcs.StepDegrees + OverlapDegrees);
        }

        var circle = new EllipseGeometry(_c, _r, _r);
        circle.Freeze();
        _circle = circle;
    }

    public void Track(DrawingContext dc, Rgba color, double width, bool hc) =>
        dc.DrawGeometry(null, _paint.Pen(hc ? SystemColors.GrayTextColor.ToRgba() : color, 1, width), _circle);

    /// <summary>The progress ring of the model's current frame (round caps; no shadow in thumbnails).</summary>
    public void Ring(DrawingContext dc, MinimalModel m, bool hc, bool thumbnail)
    {
        var width = m.RingWidth * _s;
        var lap1 = Math.Clamp(m.Lap1EndDegrees - StyleMath.TopDegrees, 0, 360);
        var lap2 = m.HasSecondLap ? Math.Clamp(m.Lap2EndDegrees - StyleMath.TopDegrees, 0, 360) : 0;
        if (hc)
        {
            // The second lap in the text color: in the highlight color it would vanish into the first.
            dc.DrawGeometry(null, _paint.Pen(SystemColors.HighlightColor.ToRgba(), 1, width, PenLineCap.Round), Lap(lap1));
            if (lap2 > 0)
            {
                dc.DrawGeometry(null, _paint.Pen(SystemColors.WindowTextColor.ToRgba(), 1, width, PenLineCap.Round), Lap(lap2));
            }

            return;
        }

        var cap = width / 2;
        dc.DrawEllipse(_paint.Brush(m.StartColor), null, OnRing(StyleMath.TopDegrees), cap, cap);
        var count = MinimalArcs.SegmentCount(lap1);
        for (var i = 0; i < count; i++)
        {
            var piece = MinimalArcs.Segment(StyleMath.TopDegrees, lap1, i);
            var geometry = piece.Full && i < _pieces.Length ? _pieces[i] : OrbBrushes.Arc(_c, _r, piece.FromDegrees, piece.ToDegrees);
            dc.DrawGeometry(null, _paint.Pen(NeedlePaint.Coarse(m.Lap1ColorAt(piece.Fraction)), 1, width), geometry);
        }

        dc.DrawEllipse(_paint.Brush(m.HeadColor), null, OnRing(StyleMath.TopDegrees + lap1), cap, cap);
        if (lap2 <= 0)
        {
            return;
        }

        if (m.ShadowVisible && !thumbnail)
        {
            var shadow = OrbBrushes.Arc(_c, _r, m.ShadowStartDegrees, m.ShadowEndDegrees);
            _paint.GlowStroke(dc, shadow, width, m.ShadowColor, 1, m.ShadowSigma * _s, core: true, PenLineCap.Round);
        }

        dc.DrawGeometry(null, _paint.Pen(m.HeadColor, 1, width, PenLineCap.Round), Lap(lap2));
    }

    /// <summary>The ring glow at full strength along the first lap (<paramref name="lap"/> degrees); σ in DIP.</summary>
    public void Glow(DrawingContext dc, double lap, double width, Rgba color, double sigma) =>
        _paint.GlowStroke(dc, Lap(lap), width, color.WithAlpha(1), 1, sigma, core: false, PenLineCap.Round);

    /// <summary>The head dot at 0° on the ring.</summary>
    public void HeadDot(DrawingContext dc, Rgba color, double radius) =>
        dc.DrawEllipse(_paint.Brush(color), null, OnRing(0), radius, radius);

    private Geometry Lap(double sweep) =>
        sweep >= FullCircle ? _circle : OrbBrushes.Arc(_c, _r, StyleMath.TopDegrees, StyleMath.TopDegrees + sweep);

    private Point OnRing(double degrees) => OrbBrushes.Polar(_c, _r, degrees);
}
