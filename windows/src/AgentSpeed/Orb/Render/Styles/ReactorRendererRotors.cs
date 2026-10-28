using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Orb.Styles;
using AgentSpeed.Theme;
using static AgentSpeed.Orb.Render.Styles.LiquidReactorShapes;

namespace AgentSpeed.Orb.Render.Styles;

/// <summary>The three rotors of the reactor style (DESIGN 2a.4 layers 5–7).</summary>
internal sealed partial class ReactorRenderer
{
    /// <summary>Gradient stops along an arc (the prototype draws 14 / 10 segments).</summary>
    private const int ArcSteps = 6;

    private readonly DrawingVisual _rotorA = CachedVisual();
    private readonly RotateTransform _rotationA = new();
    private readonly ContainerVisual _rotorB = new();
    private readonly RotateTransform _rotationB = new();
    private readonly DrawingVisual[] _coilBase = new DrawingVisual[ReactorModel.MaxCoils];
    private readonly DrawingVisual[] _coilHot = new DrawingVisual[ReactorModel.MaxCoils];
    private readonly DrawingVisual _rotorC = CachedVisual();
    private readonly RotateTransform _rotationC = new();

    private long _arcAColorKey = long.MinValue;
    private int _arcABucket = -1;
    private long _arcCColorKey = long.MinValue;
    private int _arcCBucket = -1;
    private bool _coilsDirty = true;
    private int _coilCount;
    private Rgba _coilBaseColor;
    private Rgba _coilHotColor;

    private void BuildRotors()
    {
        _rotorA.Transform = _rotationA;
        _rotorB.Transform = _rotationB;
        _rotorC.Transform = _rotationC;
        for (var i = 0; i < ReactorModel.MaxCoils; i++)
        {
            _coilBase[i] = CachedVisual();
            _coilHot[i] = CachedVisual();
            _coilBase[i].Opacity = 0;
            _coilHot[i].Opacity = 0;
            _rotorB.Children.Add(_coilBase[i]);
            _rotorB.Children.Add(_coilHot[i]);
        }
    }

    private void CenterRotors(Point c)
    {
        Center(_rotationA, c);
        Center(_rotationB, c);
        Center(_rotationC, c);

        static void Center(RotateTransform rotation, Point c)
        {
            rotation.CenterX = c.X;
            rotation.CenterY = c.Y;
        }
    }

    private void InvalidateRotors()
    {
        _arcAColorKey = long.MinValue;
        _arcCColorKey = long.MinValue;
        _coilsDirty = true;
    }

    /// <summary>Rotor A: three arcs 120° apart, drawn with arc 0's head at 0° and rotated to the model's angle.</summary>
    private void RenderRotorA(OrbRenderContext ctx, bool hc, bool thumbnail)
    {
        var m = Model;
        if (m.ColorKey != _arcAColorKey || m.ArcABucket != _arcABucket)
        {
            _arcAColorKey = m.ColorKey;
            _arcABucket = m.ArcABucket;
            var s = ctx.Size;
            var glow = !hc && !thumbnail && ctx.Dark ? m.ArcAGlowColor : Rgba.Transparent;
            using var dc = _rotorA.RenderOpen();
            DrawRotorArcs(dc, ctx, ReactorGeometry.RotorAArcs, ReactorGeometry.RotorARadius * s, m.ArcAWidth * s, _arcABucket,
                m.ArcATailColor, m.ArcAHeadColor, glow, m.ArcAGlowSigma * s);
        }

        SetAngle(_rotationA, m.RotorADegrees);
    }

    /// <summary>Rotor C: two thin arcs 180° apart, no glow.</summary>
    private void RenderRotorC(OrbRenderContext ctx, bool hc)
    {
        var m = Model;
        if (m.ColorKey != _arcCColorKey || m.ArcCBucket != _arcCBucket)
        {
            _arcCColorKey = m.ColorKey;
            _arcCBucket = m.ArcCBucket;
            var s = ctx.Size;
            using var dc = _rotorC.RenderOpen();
            DrawRotorArcs(dc, ctx, ReactorGeometry.RotorCArcs, ReactorGeometry.RotorCRadius * s, m.ArcCWidth * s, _arcCBucket,
                m.ArcCTailColor, m.ArcCHeadColor, Rgba.Transparent, 0);
        }

        SetAngle(_rotationC, m.RotorCDegrees);
    }

    /// <summary>
    /// <paramref name="count"/> arcs evenly spaced, heads at 360° k / count, each a single stroke whose chord gradient
    /// runs from the tail color to the head color; high contrast draws them in the text color with highlighted heads.
    /// </summary>
    private void DrawRotorArcs(DrawingContext dc, OrbRenderContext ctx, int count, double radius, double width, int lengthDegrees,
        Rgba tail, Rgba head, Rgba glow, double sigma)
    {
        var c = ctx.Center;
        var length = Math.Clamp(lengthDegrees, 1, ReactorArcGradient.MaxLengthDegrees);
        var text = SystemColors.WindowTextColor.ToRgba();
        var highlight = SystemColors.HighlightColor.ToRgba();
        var tailRatio = head.A > 0.001 ? Math.Clamp(tail.A / head.A, 0, 1) : 1;
        for (var k = 0; k < count; k++)
        {
            var headDegrees = 360.0 * k / count;
            var tailDegrees = headDegrees - length;
            var arc = Arc(c, radius, tailDegrees, length);
            if (ctx.HighContrast)
            {
                var lead = Math.Min(length, Math.Max(4, length * 0.25));
                dc.DrawGeometry(null, _ink.Pen(text, width), arc);
                dc.DrawGeometry(null, _ink.Pen(highlight, width), Arc(c, radius, headDegrees - lead, lead));
                continue;
            }

            if (glow.A > 0.004)
            {
                ArcGlow(dc, c, radius, tailDegrees, length, arc, glow, tailRatio, width, sigma);
            }

            var brush = ArcBrush(c, radius, tailDegrees, length, tail, head, ArcSteps);
            dc.DrawGeometry(null, MediaColors.FrozenPen(brush, width), arc);
        }
    }

    /// <summary>
    /// Rotor B: every coil is a cached base and hot visual drawn once per color; a frame sets their opacities
    /// (<c>opacity × (1 − hot)</c> and <c>opacity × hot</c>) and rotates the ring.
    /// </summary>
    private void RenderRotorB(OrbRenderContext ctx, bool hc, bool thumbnail)
    {
        if (thumbnail)
        {
            SetOpacity(_rotorB, 0);
            return;
        }

        SetOpacity(_rotorB, 1);
        var m = Model;
        var n = m.CoilCount;
        var baseColor = hc ? SystemColors.WindowTextColor.ToRgba() : m.CoilColor;
        var hotColor = hc ? SystemColors.HighlightColor.ToRgba() : m.CoilHotColor;
        if (_coilsDirty || n != _coilCount || !LiquidReactorInk.Near(baseColor, _coilBaseColor, 3)
            || !LiquidReactorInk.Near(hotColor, _coilHotColor, 3))
        {
            _coilsDirty = false;
            _coilCount = n;
            _coilBaseColor = baseColor;
            _coilHotColor = hotColor;
            DrawCoils(ctx, n, m.CoilArcDegrees);
        }

        var opacities = m.CoilOpacities;
        var hotness = m.CoilHotness;
        for (var i = 0; i < n; i++)
        {
            var opacity = hc ? Math.Max(0.45, opacities[i]) : opacities[i];
            var hot = hc ? 0 : hotness[i];
            SetOpacity(_coilBase[i], opacity * (1 - hot));
            SetOpacity(_coilHot[i], opacity * hot);
        }

        SetAngle(_rotationB, m.RotorBDegrees);
    }

    private void DrawCoils(OrbRenderContext ctx, int count, double coilDegrees)
    {
        var s = ctx.Size;
        var c = ctx.Center;
        var radius = ReactorGeometry.RotorBRadius * s;
        var basePen = _ink.Pen(_coilBaseColor.WithAlpha(1), ReactorGeometry.RotorBWidth * s);
        var hotPen = _ink.Pen(_coilHotColor.WithAlpha(1), ReactorGeometry.RotorBWidth * s);
        for (var i = 0; i < ReactorModel.MaxCoils; i++)
        {
            using var baseDc = _coilBase[i].RenderOpen();
            using var hotDc = _coilHot[i].RenderOpen();
            if (i >= count)
            {
                _coilBase[i].Opacity = 0;
                _coilHot[i].Opacity = 0;
                continue;
            }

            var arc = Arc(c, radius, 360.0 * i / count - coilDegrees / 2, coilDegrees);
            baseDc.DrawGeometry(null, basePen, arc);
            hotDc.DrawGeometry(null, hotPen, arc);
        }
    }
}
