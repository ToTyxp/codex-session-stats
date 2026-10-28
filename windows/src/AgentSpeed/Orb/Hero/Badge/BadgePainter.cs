using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Hero;
using AgentSpeed.Presentation.Hero.Badge;

namespace AgentSpeed.Orb.Hero.Badge;

/// <summary>The theme's colors in a Windows high-contrast theme.</summary>
internal readonly record struct BadgeContrast(Rgba Highlight, Rgba GrayText);

/// <summary>
/// Records the badge layers behind the title (halo, streaks, medallion, pips, motes, embers, flash) and the level-up
/// sparks in front of it, in canvas units (DESIGN 4c, Hero title badge, Layers). WPF has no additive blending, so every
/// layer composites normally.
/// </summary>
internal sealed class BadgePainter
{
    private static readonly Point Center = new(50, 50);

    private readonly BadgeInk _ink;
    private readonly BadgePalette _palette;
    private readonly RectangleGeometry _ringHole = new();
    private readonly GeometryGroup _ringClip;

    public BadgePainter(BadgeInk ink, BadgePalette palette)
    {
        _ink = ink;
        _palette = palette;
        _ringClip = new GeometryGroup { FillRule = FillRule.EvenOdd, Children = { new RectangleGeometry(new Rect(-10, -10, 120, 120)), _ringHole } };
    }

    /// <param name="halfWidth">Half width of the title's ink box grown by the ring clearance (canvas units).</param>
    /// <param name="halfHeight">Half height of that box.</param>
    public void DrawBack(DrawingContext dc, BadgeStage stage, in BadgeColors colors, BadgeContrast? contrast, double halfWidth, double halfHeight)
    {
        var halo = stage.HaloAlpha;
        if (halo > 0.003)
        {
            var c = colors.Color;
            dc.DrawEllipse(_ink.Radial(BadgeInk.ToColor(c, halo), BadgeInk.ToColor(c, halo / 2), 0.5, BadgeInk.ToColor(c, 0)), null, Center, 34, 34);
        }

        DrawStreaks(dc, stage, colors);
        DrawMedallion(dc, stage, colors, contrast, halfWidth, halfHeight);
        DrawPips(dc, stage, colors, contrast);
        DrawMotes(dc, stage, colors);
        DrawParticles(dc, stage, colors, ember: true);

        if (stage.Flash is { } flash)
        {
            var core = stage.Dark ? HeatPalette.WhiteHot : colors.Hot;
            dc.DrawEllipse(_ink.Radial(BadgeInk.ToColor(core, flash.Alpha), BadgeInk.ToColor(Rgba.Lerp(core, colors.Hot, 0.5), flash.Alpha / 2), 0.5,
                BadgeInk.ToColor(colors.Hot, 0)), null, Center, flash.Radius, flash.Radius);
        }
    }

    public void DrawFront(DrawingContext dc, BadgeStage stage, in BadgeColors colors) => DrawParticles(dc, stage, colors, ember: false);

    /// <summary>Whether a level-up spark is on screen (the front layer is left alone otherwise).</summary>
    public static bool HasSparks(BadgeStage stage)
    {
        foreach (ref readonly var p in stage.Particles)
        {
            if (p.Alive && !p.Ember && p.Age >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private void DrawStreaks(DrawingContext dc, BadgeStage stage, in BadgeColors colors)
    {
        var color = stage.StreakAccent ? colors.Hot : colors.Color;
        foreach (ref readonly var streak in stage.Streaks)
        {
            if (!streak.Alive)
            {
                continue;
            }

            var alpha = streak.Alpha * Math.Min(1, streak.Age / 0.12);
            if (alpha <= 0.003)
            {
                continue;
            }

            var head = new Point(streak.X, streak.Y);
            var tail = new Point(streak.X + streak.Length, streak.Y);
            dc.DrawLine(_ink.Fade(color, alpha, 0.5, head, tail), head, tail);
        }
    }

    private void DrawMedallion(DrawingContext dc, BadgeStage stage, in BadgeColors colors, BadgeContrast? contrast, double halfWidth,
        double halfHeight)
    {
        var hole = new Rect(50 - halfWidth, 50 - halfHeight, 2 * halfWidth, 2 * halfHeight);
        if (_ringHole.Rect != hole)
        {
            _ringHole.Rect = hole;
        }

        var increased = stage.Input.Options.IncreasedContrast;
        var (outer, inner) = contrast is not null ? (1.0, 1.0) : increased ? (0.6, 0.3) : stage.Dark ? (0.34, 0.16) : (0.42, 0.22);
        var presence = contrast is null ? stage.RingPresence : 1;
        var color = contrast?.Highlight ?? colors.Color;
        dc.PushClip(_ringClip);
        var sweep = stage.OuterRingSweep;
        var pen = _ink.Pen(color, outer * presence, 0.8);
        if (sweep >= 359.9)
        {
            dc.DrawEllipse(null, pen, Center, BadgeStage.RingRadius, BadgeStage.RingRadius);
        }
        else if (sweep > 0.5)
        {
            dc.DrawGeometry(null, pen, _ink.Arc(Center, BadgeStage.RingRadius, -90, sweep));
        }

        dc.DrawEllipse(null, _ink.Pen(color, inner * presence, 0.5), Center, BadgeStage.InnerRingRadius, BadgeStage.InnerRingRadius);
        if (stage.Shockwave is { } ring && ring.Width > 0.01 && ring.Alpha > 0.003)
        {
            dc.DrawEllipse(null, _ink.Pen(contrast?.Highlight ?? colors.Hot, ring.Alpha, ring.Width), Center, ring.Radius, ring.Radius);
        }

        dc.Pop();
    }

    private void DrawPips(DrawingContext dc, BadgeStage stage, in BadgeColors colors, BadgeContrast? contrast)
    {
        var unlitBrush = contrast is null ? _ink.Brush(colors.Unlit, 1) : null;
        var unlitPen = contrast is { } hc ? _ink.Pen(hc.GrayText, 1, 0.35) : null;
        var redline = stage.Input.Redline;
        var dark = stage.Dark;
        var glows = dark && contrast is null && !stage.Input.Options.IncreasedContrast;
        for (var k = 1; k <= BadgeStage.PipCount; k++)
        {
            var p = BadgeStage.PipCenter(k);
            var center = new Point(p.X, p.Y);
            var unlitRadius = unlitPen is null ? BadgeStage.PipRadius : BadgeStage.PipRadius - 0.175;
            dc.DrawEllipse(unlitBrush, unlitPen, center, unlitRadius, unlitRadius);
            var lit = stage.PipLit(k);
            if (lit <= 0.01)
            {
                continue;
            }

            var radius = stage.PipRadiusOf(k);
            if (radius <= 0.01)
            {
                continue;
            }

            var color = contrast?.Highlight ?? _palette.Pip(k, redline, dark);
            if (glows && k == stage.Level)
            {
                // Current pip glow σ 1.2 (dark): a soft disc to about 3 σ.
                var outer = radius + 3.6;
                dc.DrawEllipse(_ink.Radial(BadgeInk.ToColor(color, 0.35 * lit), BadgeInk.ToColor(color, 0.35 * lit), radius / outer,
                    BadgeInk.ToColor(color, 0)), null, center, outer, outer);
            }

            dc.DrawEllipse(_ink.Brush(color, lit), null, center, radius, radius);
        }
    }

    private void DrawMotes(DrawingContext dc, BadgeStage stage, in BadgeColors colors)
    {
        if (stage.MoteCount <= 0)
        {
            return;
        }

        const int segments = 5;
        var trail = stage.MoteTrailDegrees;
        for (var slot = 0; slot < BadgeStage.MoteSlots; slot++)
        {
            var visible = stage.MoteVisibility(slot);
            if (visible <= 0.01)
            {
                continue;
            }

            var head = stage.MoteHeadDegrees(slot);
            for (var j = 0; j < segments; j++)
            {
                var f = j / (double)segments;
                var start = head - trail * (j + 1) / segments;
                dc.DrawGeometry(null, _ink.FlatPen(colors.Color, visible * (1 - f), HeroMath.Lerp(1.2, 0.35, f)),
                    _ink.Arc(Center, BadgeStage.RingRadius, start, trail / segments + 0.05));
            }

            var radians = head * Math.PI / 180;
            var dot = new Point(50 + BadgeStage.RingRadius * Math.Cos(radians), 50 + BadgeStage.RingRadius * Math.Sin(radians));
            dc.DrawEllipse(_ink.Brush(colors.Hot, visible), null, dot, 1.0, 1.0);
        }
    }

    private void DrawParticles(DrawingContext dc, BadgeStage stage, in BadgeColors colors, bool ember)
    {
        foreach (ref readonly var p in stage.Particles)
        {
            if (!p.Alive || p.Ember != ember || p.Age < 0)
            {
                continue;
            }

            var life = HeroMath.Clamp01(p.Age / p.Life);
            var alpha = (1 - life) * (ember ? 0.8 : 1);
            if (alpha <= 0.003)
            {
                continue;
            }

            var color = Rgba.Lerp(colors.Core, colors.Color, life);
            var head = new Point(p.X, p.Y);
            var tail = new Point(p.X - p.VelocityX * 0.022, p.Y - p.VelocityY * 0.022);
            dc.DrawLine(_ink.Pen(color, alpha, ember ? 0.7 : 0.6), tail, head);
        }
    }
}
