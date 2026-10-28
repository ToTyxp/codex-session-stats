using System.Windows;
using System.Windows.Media;
using AgentSpeed.Core;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Theme;

namespace AgentSpeed.Orb.Render;

/// <summary>Drawings of the shared, static orb parts: body, rim, shadow, lens and the source bezel (DESIGN 2, 2b).</summary>
internal static class OrbChrome
{
    public static void DrawBody(DrawingContext dc, OrbRenderContext ctx, BodyVariant variant, IReadOnlyList<SourceKind>? sources,
        SourceSet unavailable)
    {
        var s = ctx.Size;
        var c = ctx.Center;
        var r = s / 2;
        if (ctx.HighContrast)
        {
            dc.DrawEllipse(SystemColors.WindowBrush, new Pen(SystemColors.WindowTextBrush, 1.5), c, r - 0.75, r - 0.75);
            DrawBezel(dc, ctx, sources, unavailable);
            return;
        }

        var t = ctx.Tokens;
        var shadowRadius = r + t.ShadowBlur * s;
        var shadow = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(t.ShadowColor.ToColor(), 0),
                new GradientStop(t.ShadowColor.ToColor(), (r - t.ShadowBlur * s * 0.5) / shadowRadius),
                new GradientStop(t.ShadowColor.ToColor(0.45), r / shadowRadius),
                new GradientStop(t.ShadowColor.ToColor(0.12), (r + t.ShadowBlur * s * 0.5) / shadowRadius),
                new GradientStop(Colors.Transparent, 1),
            },
        };
        shadow.Freeze();
        dc.DrawEllipse(shadow, null, new Point(c.X, c.Y + t.ShadowOffset * s), shadowRadius, shadowRadius);

        var alpha = ctx.SolidBody ? 1 : t.BodyAlpha * Math.Clamp(ctx.BodyOpacity, 0.4, 1);
        var body = new LinearGradientBrush((variant.Top ?? t.BodyTop).ToColor(alpha), (variant.Bottom ?? t.BodyBottom).ToColor(alpha), 90);
        body.Freeze();
        dc.DrawEllipse(body, null, c, r, r);

        var dark = ctx.Dark;
        var innerShadow = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.56),
            GradientOrigin = new Point(0.5, 0.62),
            GradientStops =
            {
                new GradientStop(Colors.Transparent, 0),
                new GradientStop(Colors.Transparent, 0.78),
                new GradientStop(Color.FromArgb(dark ? (byte)70 : (byte)22, 0, 0, 0), 1),
            },
        };
        innerShadow.Freeze();
        dc.DrawEllipse(innerShadow, null, c, r, r);

        if (variant.Lens)
        {
            var lens = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(dark ? (byte)16 : (byte)90, 255, 255, 255), 0),
                    new GradientStop(Colors.Transparent, 1),
                },
            };
            lens.Freeze();
            dc.DrawEllipse(lens, null, new Point(c.X - 0.17 * s, c.Y - 0.19 * s), 0.3 * s, 0.24 * s);
        }

        if (sources is null || sources.Count == 0)
        {
            var rim = new LinearGradientBrush(t.RimTop.ToColor(), t.RimTop.ToColor(0), 90);
            rim.Freeze();
            dc.DrawEllipse(null, MediaColors.FrozenPen(rim, 1), c, r - 0.5, r - 0.5);
        }

        if (t.OuterStroke is { } outer)
        {
            dc.DrawEllipse(null, MediaColors.FrozenPen(outer.ToBrush(), 1), c, r + 0.5, r + 0.5);
        }

        DrawBezel(dc, ctx, sources, unavailable);
    }

    /// <summary>
    /// The source bezel (DESIGN 2b, Identity at a glance): equal arcs clockwise from 12 o'clock in source order with 4°
    /// gaps, width max(1 pt, 0.0105 S) (1 px in thumbnails); unavailable sources dashed in the unlit token × 2.5.
    /// </summary>
    public static void DrawBezel(DrawingContext dc, OrbRenderContext ctx, IReadOnlyList<SourceKind>? sources, SourceSet unavailable)
    {
        if (sources is null || sources.Count == 0)
        {
            return;
        }

        var s = ctx.Size;
        var width = s < 64 ? 1 / Math.Max(1, ctx.Scale) : Math.Max(1, 0.0105 * s);
        var radius = s / 2 - width / 2;
        var n = sources.Count;
        var gap = n > 1 ? 4.0 : 0;
        var c = ctx.Center;
        for (var i = 0; i < n; i++)
        {
            var source = sources[i];
            var off = unavailable.Contains(source);
            var start = -90 + 360.0 / n * i + gap / 2;
            var end = -90 + 360.0 / n * (i + 1) - gap / 2;
            Rgba color;
            if (ctx.HighContrast)
            {
                color = off ? SystemColors.GrayTextColor.ToRgba() : SystemColors.HighlightColor.ToRgba();
            }
            else if (off)
            {
                color = ctx.Tokens.UnlitSegment.WithAlpha(Math.Min(1, ctx.Tokens.UnlitSegment.A * 2.5));
            }
            else
            {
                var source0 = HeatPalette.SourceColor(source);
                color = ctx.Dark ? source0.WithAlpha(0.85) : OkLab.Darken(source0, 0.1).WithAlpha(0.9);
            }

            var pen = new Pen(ctx.Brushes.Get(color), width) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
            if (off)
            {
                pen.DashStyle = new DashStyle([1.2, 1.4], 0);
            }

            pen.Freeze();
            var geometry = n == 1 && !off
                ? (Geometry)new EllipseGeometry(c, radius, radius)
                : OrbBrushes.Arc(c, radius, start, end);
            dc.DrawGeometry(null, pen, geometry);
        }
    }
}
