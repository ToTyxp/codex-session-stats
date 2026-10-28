using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Orb.Styles;
using AgentSpeed.Presentation.Render;
using AgentSpeed.Theme;
using static AgentSpeed.Orb.Render.Styles.LiquidReactorShapes;

namespace AgentSpeed.Orb.Render.Styles;

/// <summary>The cached layers and the particles of the liquid style (DESIGN 2a.3 layers 2, 3, 5, 7, 9).</summary>
internal sealed partial class LiquidRenderer
{
    private const double GoldBandAlpha = 0.45;

    private uint _spillKey = uint.MaxValue;
    private uint _causticKey = uint.MaxValue;
    private uint _goldKey = uint.MaxValue;
    private Brush? _goldBrush;
    private int _glowVersion = int.MinValue;

    /// <summary>Volume shading, spherical shade, scrim, gold band and glass: drawn once per size and appearance.</summary>
    private void DrawStatic(OrbRenderContext ctx)
    {
        var m = Model;
        var s = ctx.Size;
        var c = ctx.Center;
        var dark = ctx.Dark;
        var hc = ctx.HighContrast;

        using (var dc = _volume.RenderOpen())
        {
            if (!hc)
            {
                // Layer 2: clear near the light, darkening toward the edge of the disc.
                var edge = m.VolumeShadeColor;
                var brush = new RadialGradientBrush
                {
                    MappingMode = BrushMappingMode.Absolute,
                    Center = c,
                    GradientOrigin = new Point(c.X + LiquidGeometry.VolumeCenterX * s, c.Y + LiquidGeometry.VolumeCenterY * s),
                    RadiusX = 0.5 * s,
                    RadiusY = 0.5 * s,
                    GradientStops =
                    {
                        new GradientStop(edge.ToColor(0), 0.1),
                        new GradientStop(edge.WithAlpha(dark ? 0.08 : 0.03).ToColor(), 0.75),
                        new GradientStop(edge.ToColor(), 1),
                    },
                };
                brush.Freeze();
                dc.DrawEllipse(brush, null, c, 0.5 * s, 0.5 * s);
            }
        }

        _shadeBrush = null;
        if (!hc)
        {
            // Spherical shade over the liquid: filled with the liquid's own geometry, so it needs no clip.
            var shade = m.SphericalShadeColor;
            var brush = new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                Center = c,
                GradientOrigin = new Point(c.X + LiquidGeometry.ShadeCenterX * s, c.Y + LiquidGeometry.ShadeCenterY * s),
                RadiusX = R * s,
                RadiusY = R * s,
                GradientStops =
                {
                    new GradientStop(shade.ToColor(0), LiquidGeometry.ShadeInner),
                    new GradientStop(shade.ToColor(), 1),
                },
            };
            brush.Freeze();
            _shadeBrush = brush;
        }

        using (var dc = _scrim.RenderOpen())
        {
            if (dark && !hc)
            {
                var brush = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(89, 0, 0, 0), 0),
                        new GradientStop(Color.FromArgb(77, 0, 0, 0), 0.55),
                        new GradientStop(Color.FromArgb(0, 0, 0, 0), 1),
                    },
                };
                brush.Freeze();
                dc.DrawEllipse(brush, null, new Point(c.X, c.Y + LiquidGeometry.ScrimCenterY * s), LiquidGeometry.ScrimRadiusX * s,
                    LiquidGeometry.ScrimRadiusY * s);
            }
        }

        using (var dc = _glass.RenderOpen())
        {
            var wall = hc ? SystemColors.GrayTextColor.ToRgba() : m.GlassWallColor;
            dc.DrawEllipse(null, _ink.Pen(wall, Math.Max(1, LiquidGeometry.WallWidth * s)), c, R * s, R * s);
            if (!hc)
            {
                var spot = new Point(c.X + LiquidGeometry.SpecularX * s, c.Y + LiquidGeometry.SpecularY * s);
                var specular = m.SpecularColor;
                var brush = new RadialGradientBrush
                {
                    MappingMode = BrushMappingMode.Absolute,
                    Center = spot,
                    GradientOrigin = spot,
                    RadiusX = 0.24 * s,
                    RadiusY = 0.24 * s,
                    GradientStops = { new GradientStop(specular.ToColor(), 0), new GradientStop(specular.ToColor(0), 1) },
                };
                brush.Freeze();
                var tilt = new RotateTransform(LiquidGeometry.SpecularRotationDegrees, spot.X, spot.Y);
                tilt.Freeze();
                dc.PushTransform(tilt);
                dc.DrawEllipse(brush, null, spot, LiquidGeometry.SpecularRadiusX * s, LiquidGeometry.SpecularRadiusY * s);
                dc.Pop();

                var glint = Arc(c, LiquidGeometry.GlintRadius * s, LiquidGeometry.GlintStartDegrees,
                    LiquidGeometry.GlintEndDegrees - LiquidGeometry.GlintStartDegrees);
                dc.DrawGeometry(null, _ink.Pen(m.GlintColor, Math.Max(1, LiquidGeometry.GlintWidth * s), PenLineCap.Round), glint);
            }
        }
    }

    private double _spillY = double.NaN;
    private double _goldY = double.NaN;
    private Brush? _spillBrush;

    /// <summary>Layer 3: a band above the mean surface inside the glass; re-recorded only when it moves or recolors.</summary>
    private void RenderSpill(OrbRenderContext ctx, bool effects)
    {
        var color = Model.SpillColor;
        if (!effects || color.A < 0.004)
        {
            SetOpacity(_spill, 0);
            return;
        }

        var s = ctx.Size;
        color = Model.LitOver(color, 0, Model.MeanSurfaceY - LiquidGeometry.SpillHeight - 0.001);
        var key = LiquidReactorInk.PackRgb(color);
        var y = Math.Round(Model.MeanSurfaceY * s * 2) / 2;
        if (key != _spillKey || y != _spillY)
        {
            if (key != _spillKey || _spillBrush is null)
            {
                _spillBrush = FrozenBand(color, [(0, 0), (1, 1)]);
            }

            _spillKey = key;
            _spillY = y;
            using var dc = _spill.RenderOpen();
            var c = ctx.Center;
            if (GlassBand(c, R * s, c.Y + y - LiquidGeometry.SpillHeight * s, c.Y + y) is { } band)
            {
                dc.DrawGeometry(_spillBrush, null, band);
            }
        }

        SetOpacity(_spill, color.A);
    }

    /// <summary>The new-peak gold band rising through the liquid (re-recorded while it moves).</summary>
    private void RenderGoldBand(OrbRenderContext ctx, bool effects)
    {
        var alpha = Model.GoldBandAlpha;
        if (!effects || alpha < 0.004)
        {
            SetOpacity(_gold, 0);
            return;
        }

        var s = ctx.Size;
        var y = Math.Round(Model.GoldBandY * s * 2) / 2;
        // The band adds gold onto whatever it passes (dark): its color follows the liquid under it as it rises.
        var gold = Model.LitOver(HeatPalette.Gold.WithAlpha(GoldBandAlpha), 0, Model.GoldBandY);
        var key = LiquidReactorInk.PackRgb(gold);
        if (y != _goldY || key != _goldKey || _goldBrush is null)
        {
            _goldY = y;
            if (key != _goldKey || _goldBrush is null)
            {
                _goldKey = key;
                _goldBrush = FrozenBand(gold, [(0, 0), (0.5, Math.Min(1, gold.A)), (1, 0)]);
            }

            var c = ctx.Center;
            var half = LiquidGeometry.GoldBandHalfHeight * s;
            using var dc = _gold.RenderOpen();
            if (GlassBand(c, R * s, c.Y + y - half, c.Y + y + half) is { } band)
            {
                dc.DrawGeometry(_goldBrush, null, band);
            }
        }

        SetOpacity(_gold, alpha / GoldBandAlpha);
    }

    /// <summary>A vertical gradient over the filled shape's bounds (top = offset 0).</summary>
    private static Brush FrozenBand(Rgba color, (double Offset, double Alpha)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach (var (offset, alpha) in stops)
        {
            brush.GradientStops.Add(new GradientStop(color.WithAlpha(1).ToColor(alpha), offset));
        }

        brush.Freeze();
        return brush;
    }

    /// <summary>Layer 9 bottom caustic: a blurred arc outside the glass; its alpha is the visual's opacity.</summary>
    private void RenderCaustic(OrbRenderContext ctx, bool effects)
    {
        var color = Model.CausticColor;
        if (!effects || color.A < 0.004)
        {
            SetOpacity(_caustic, 0);
            return;
        }

        var key = LiquidReactorInk.PackRgb(color);
        if (key != _causticKey)
        {
            _causticKey = key;
            var s = ctx.Size;
            var arc = Arc(ctx.Center, LiquidGeometry.CausticRadius * s, LiquidGeometry.CausticStartDegrees,
                LiquidGeometry.CausticEndDegrees - LiquidGeometry.CausticStartDegrees);
            using var dc = _caustic.RenderOpen();
            BlurredStroke(dc, _ink, arc, color.WithAlpha(1), LiquidGeometry.CausticWidth * s, LiquidGeometry.CausticBlur * s);
        }

        SetOpacity(_caustic, color.A);
    }

    /// <summary>Layer 7: motes and bubbles, then pops, splashes and steam, splatted into the particle bitmap (inside the glass).</summary>
    private void RenderParticles(OrbRenderContext ctx, bool effects)
    {
        var sprites = Model.Sprites;
        var any = effects && sprites.Length > Model.Droplets.Length;
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
            Splat(raster, ctx, sprites, surface: false);
            Splat(raster, ctx, sprites, surface: true);
        }

        _particles.End();
    }

    /// <summary>
    /// Layer 8's glow: the meniscus stroke's blurred shadow (σ <see cref="LiquidModel.MeniscusGlowSigma"/>, the stroke's
    /// alpha) in its own half-resolution bitmap, redrawn only when the surface or its color moved.
    /// </summary>
    private void RenderGlow(bool effects, double dt)
    {
        var glow = effects && _meniscusGlow.A > 0.004 && _surfaceVersion != int.MinValue;
        if (!glow)
        {
            if (_glowLayer.HasContent)
            {
                _glowLayer.Clear();
            }

            _glowVersion = int.MinValue;
            return;
        }

        if (_glowVersion == _surfaceVersion || _glowLayer.SkipThisFrame(dt) || _glowLayer.Begin() is not { } raster)
        {
            return;
        }

        var px = _glowLayer.Scale;
        raster.SurfaceGlow(_glowXs, _glowYs, _meniscusWidth * px, _meniscusSigma * px, _meniscusGlow, Model.MeniscusColor.A);
        _glowLayer.End();
        _glowVersion = _surfaceVersion;
    }

    /// <summary>The bubbles and motes (<paramref name="surface"/> false) or the pops, splashes and steam (true).</summary>
    private void Splat(SpriteRaster raster, OrbRenderContext ctx, ReadOnlySpan<LiquidSprite> sprites, bool surface)
    {
        var m = Model;
        var s = ctx.Size;
        var c = ctx.Center;
        var px = _particles.Scale;
        var dark = ctx.Dark;
        var moteShape = dark ? DotShape.Mote : DotShape.MoteLight;
        var ring = m.BubbleRingColor;
        var highlight = m.BubbleHighlightColor;
        var moteAlpha = dark ? 0.95 : 0.9;
        foreach (ref readonly var p in sprites)
        {
            var mote = p.Kind is LiquidSpriteKind.Mote or LiquidSpriteKind.GoldMote;
            if (p.Kind == LiquidSpriteKind.Droplet || mote == surface)
            {
                continue;
            }

            var reach = R + p.Radius * (p.Kind is LiquidSpriteKind.Mote or LiquidSpriteKind.GoldMote ? 2 : 1);
            if (p.X * p.X + p.Y * p.Y > reach * reach)
            {
                continue;
            }

            var (x, y) = _particles.ToPixels(c.X + p.X * s, c.Y + p.Y * s);
            var r = p.Radius * s * px;
            switch (p.Kind)
            {
                case LiquidSpriteKind.Mote:
                    if (p.Hot < 1)
                    {
                        raster.Ring(x, y, r, Math.Max(0.6 * px, 0.28 * r), ring, p.Alpha * (1 - p.Hot), SpriteBlend.Over);
                        raster.Dot(x - 0.35 * r, y - 0.35 * r, Math.Max(0.4 * px, 0.28 * r), highlight, 1 - p.Hot, DotShape.Solid,
                            SpriteBlend.Over);
                    }

                    if (p.Hot > 0)
                    {
                        raster.Dot(x, y, 2 * r, m.LitOver(p.Color, p.X, p.Y), moteAlpha * p.Hot * p.Alpha, moteShape, SpriteBlend.Over);
                    }

                    break;
                case LiquidSpriteKind.GoldMote:
                    raster.Dot(x, y, 2 * r, m.LitOver(p.Color, p.X, p.Y), moteAlpha * p.Alpha, moteShape, SpriteBlend.Over);
                    break;
                case LiquidSpriteKind.Pop:
                    raster.Ring(x, y, r, 0.8 * px, p.Color, p.Alpha, SpriteBlend.Over);
                    break;
                case LiquidSpriteKind.Splash:
                    raster.Dot(x, y, r, m.LitOver(p.Color, p.X, p.Y), p.Alpha, DotShape.Solid, SpriteBlend.Over);
                    break;
                case LiquidSpriteKind.Steam:
                    raster.Dot(x, y, r, m.LitOver(p.Color, p.X, p.Y), p.Alpha, DotShape.Glow, SpriteBlend.Over);
                    break;
            }
        }
    }
}
