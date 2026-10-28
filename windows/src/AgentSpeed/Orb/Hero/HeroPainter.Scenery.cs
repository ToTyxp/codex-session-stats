using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Hero;

namespace AgentSpeed.Orb.Hero;

internal sealed partial class HeroPainter
{
    private const int MixSteps = 16;

    private readonly Rgba[] _hotToColor = new Rgba[MixSteps + 1];
    private readonly Rgba[] _coreToColor = new Rgba[MixSteps + 1];
    private (Rgba Hot, Rgba Core, Rgba Color) _mixKey;
    private (double X, double Y, double Depth)[]? _stars;

    /// <summary>
    /// The backdrops of the vehicles on stage: each crossfades with its vehicle; a backdrop both share is drawn once at
    /// full strength and its ground line glides.
    /// </summary>
    private void DrawScenery(DrawingContext dc)
    {
        Span<double> alpha = stackalloc double[6];
        Span<double> ground = stackalloc double[6];
        ground.Fill(double.NaN);
        var current = _stage.Current;
        var currentVehicle = _art.Vehicles[current.Vehicle];
        if (_stage.Previous is { } previous)
        {
            var vehicle = _art.Vehicles[previous.Vehicle];
            var kind = (int)vehicle.Scenery;
            alpha[kind] = Math.Max(alpha[kind], _stage.Place(previous, true).Alpha);
            ground[kind] = vehicle.GroundY ?? double.NaN;
            if (vehicle.Scenery == currentVehicle.Scenery && vehicle.GroundY is { } from && currentVehicle.GroundY is { } to)
            {
                var u = Math.Clamp((_stage.Time - current.Start) / 0.45, 0, 1);
                ground[kind] = from + (to - from) * (0.5 - 0.5 * Math.Cos(Math.PI * u));
            }
            else
            {
                ground[(int)currentVehicle.Scenery] = currentVehicle.GroundY ?? double.NaN;
            }
        }
        else
        {
            ground[(int)currentVehicle.Scenery] = currentVehicle.GroundY ?? double.NaN;
        }

        var currentKind = (int)currentVehicle.Scenery;
        alpha[currentKind] = _stage.Previous is { } shared && _art.Vehicles[shared.Vehicle].Scenery == currentVehicle.Scenery
            ? 1
            : Math.Max(alpha[currentKind], _stage.Place(current, false).Alpha);
        for (var kind = 0; kind < alpha.Length; kind++)
        {
            var a = alpha[kind] * _paint.Presence;
            if (a > 0.002)
            {
                DrawScene(dc, (SceneryKind)kind, a, ground[kind]);
            }
        }
    }

    private void DrawScene(DrawingContext dc, SceneryKind kind, double alpha, double groundY)
    {
        var scenery = _art.Scenery;
        var w = _art.StrokeWidth;
        var g = _stage.Ground;
        var d = _animated ? _stage.Distance : 0;
        var baseAlpha = (_dark ? 0.3 : 0.34) * alpha;
        switch (kind)
        {
            case SceneryKind.Road when double.IsFinite(groundY):
            {
                var period = scenery.RoadDash + scenery.RoadGap;
                var smear = Math.Clamp(g / 60 / period / 0.45, 0, 1);
                var length = scenery.RoadDash + smear * scenery.RoadGap * 0.85;
                if (_ink.Pen(_color, baseAlpha * (1 - 0.35 * smear), w * 0.55) is not { } pen)
                {
                    break;
                }

                var offset = d * scenery.RoadParallax % period;
                for (var x = -offset - period; x < 104; x += period)
                {
                    dc.DrawLine(pen, new Point(x, groundY), new Point(x + length, groundY));
                }

                break;
            }

            case SceneryKind.Rail when double.IsFinite(groundY):
            {
                if (_ink.Pen(_color, baseAlpha * 1.3, w * 0.55) is { } rail)
                {
                    dc.DrawLine(rail, new Point(-2, groundY), new Point(102, groundY));
                }

                var spacing = scenery.SleeperSpacing;
                var smear = Math.Clamp(g / 60 / spacing / 0.4, 0, 1);
                if (_ink.Pen(_color, baseAlpha * (1 - 0.55 * smear), w * 0.5) is not { } sleeper)
                {
                    break;
                }

                var offset = d * scenery.RailParallax % spacing;
                for (var x = -offset; x < 106; x += spacing)
                {
                    dc.DrawLine(sleeper, new Point(x, groundY + 1.4), new Point(x - 1.2 + smear * 4, groundY + 1.4 + scenery.SleeperDepth));
                }

                break;
            }

            case SceneryKind.Guideway when double.IsFinite(groundY):
            {
                var depth = scenery.GuidewayDepth;
                if (_ink.Pen(_color, baseAlpha * 1.2, w * 0.55) is { } beam)
                {
                    dc.DrawLine(beam, new Point(-2, groundY), new Point(102, groundY));
                    dc.DrawLine(beam, new Point(-2, groundY + depth), new Point(102, groundY + depth));
                }

                if (_ink.Pen(_color, baseAlpha, w * 0.55) is { } pylon)
                {
                    var offset = d * scenery.GuidewayParallax % scenery.PylonSpacing;
                    for (var x = -offset; x < 110; x += scenery.PylonSpacing)
                    {
                        dc.DrawLine(pylon, new Point(x, groundY + depth), new Point(x, groundY + depth + scenery.PylonDepth));
                    }
                }

                break;
            }

            case SceneryKind.Sky:
                for (var i = 0; i < scenery.SkyLayers.Count; i++)
                {
                    var layer = scenery.SkyLayers[i];
                    const double spacing = 72;
                    var travel = d * layer.Parallax;
                    var offset = (travel + i * 31) % spacing;
                    if (_ink.Pen(_color, layer.Opacity * alpha * (_dark ? 1 : 1.2), w * 0.5 / layer.Scale) is not { } pen)
                    {
                        continue;
                    }

                    for (var x = 104 - offset; x > -30; x -= spacing)
                    {
                        var cloud = _shapes.Clouds[(HeroMath.RoundHalfUp((x + travel) / spacing) + i) & 1];
                        Use(dc, new Matrix(layer.Scale, 0, 0, layer.Scale, x - 8, layer.Y));
                        dc.DrawGeometry(null, pen, cloud);
                    }
                }

                EndRun();
                break;
            case SceneryKind.Stratosphere:
                if (_ink.Pen(_color, baseAlpha * 0.35 * 0.3, w * 4) is { } wide)
                {
                    dc.DrawGeometry(null, wide, _shapes.HorizonGlow);
                }

                if (_ink.Pen(_color, baseAlpha * 0.35 * 0.7, w * 2.2) is { } soft)
                {
                    dc.DrawGeometry(null, soft, _shapes.HorizonGlow);
                }

                if (_ink.Pen(_color, baseAlpha, w * 0.55) is { } limb)
                {
                    dc.DrawGeometry(null, limb, _shapes.Horizon);
                }

                break;
            case SceneryKind.Space:
                DrawStars(dc, baseAlpha, w, g, d);
                break;
        }
    }

    private void DrawStars(DrawingContext dc, double baseAlpha, double w, double ground, double distance)
    {
        var scenery = _art.Scenery;
        _stars ??= Stars(scenery.Stars);
        var heading = (_art.Vehicles[^1].Heading ?? 0) * Math.PI / 180;
        var ux = Math.Cos(heading);
        var uy = Math.Sin(heading);
        var length = Math.Max(0.3, ground * scenery.SpaceParallax / 60 * 1.3);
        foreach (var (x0, y0, depth) in _stars)
        {
            var travel = distance * scenery.SpaceParallax * depth % 140;
            var x = Wrap(x0 - ux * travel);
            var y = Wrap(y0 - uy * travel);
            if (_ink.Pen(_color, baseAlpha * depth * 1.2, w * 0.4) is { } pen)
            {
                dc.DrawLine(pen, new Point(x, y), new Point(x + ux * length * depth, y + uy * length * depth));
            }
        }

        static double Wrap(double v) => (v % 120 + 120) % 120 - 10;
    }

    private static (double X, double Y, double Depth)[] Stars(int count)
    {
        long seed = 11;
        double Next()
        {
            seed = seed * 16807 % 2147483647;
            return seed / 2147483647.0;
        }

        var stars = new (double, double, double)[count];
        for (var i = 0; i < count; i++)
        {
            stars[i] = (Next() * 100, Next() * 100, 0.5 + Next() * 0.7);
        }

        return stars;
    }

    /// <summary>Ambient speed lines fading from head to tail (accent color at redline in dark mode).</summary>
    private void DrawStreaks(DrawingContext dc)
    {
        var color = _dark && _stage.Tier >= SpeedTier.Redline ? _hot : _color;
        var extra = _stage.Ground / 60 * 1.5;
        var width = _art.StrokeWidth * 0.5;
        foreach (ref readonly var s in _stage.Streaks.Items)
        {
            if (!s.Alive)
            {
                continue;
            }

            var length = s.Length + extra;
            var fade = Math.Clamp(s.Age / 0.12, 0, 1);
            var head = new Point(s.X, s.Y);
            var tail = new Point(s.X - s.DirectionX * length, s.Y - s.DirectionY * length);
            dc.DrawLine(_ink.Fade(color, s.Alpha * fade, width, head, tail), head, tail);
        }
    }

    private void DrawParticles(DrawingContext crisp, DrawingContext? glow)
    {
        var particles = _stage.Particles.Items;
        var any = false;
        foreach (ref readonly var p in particles)
        {
            any |= p.Alive;
        }

        if (!any)
        {
            return;
        }

        PrepareMixes();
        DrawParticleLayer(crisp, false, particles);
        if (glow is not null)
        {
            DrawParticleLayer(glow, true, particles);
        }
    }

    private void DrawParticleLayer(DrawingContext dc, bool glow, ReadOnlySpan<HeroParticle> particles)
    {
        var w0 = _art.StrokeWidth;
        foreach (ref readonly var p in particles)
        {
            if (!p.Alive)
            {
                continue;
            }

            var u = p.Progress;
            if (u < 0)
            {
                continue;
            }

            var step = (int)Math.Round(u * MixSteps);
            switch (p.Kind)
            {
                case HeroParticleKind.Puff or HeroParticleKind.Ring:
                {
                    var ring = p.Kind == HeroParticleKind.Ring;
                    var radius = p.Size + p.Grow * (1 - (1 - u) * (1 - u));
                    var alpha = (ring ? 0.7 : 0.5) * (1 - u) * (glow ? 0.6 : 1);
                    var color = ring ? _hotToColor[step] : _partColor;
                    StrokeCircle(dc, glow, new Point(p.X, p.Y), radius, color, alpha, w0 * 0.45 * (1 - 0.5 * u));
                    break;
                }

                case HeroParticleKind.Spark:
                    StrokeLine(dc, glow, new Point(p.X, p.Y), new Point(p.X - p.VelocityX * 0.022, p.Y - p.VelocityY * 0.022),
                        _dark ? _coreToColor[step] : _hotToColor[step], (1 - u) * (glow ? 1 : 0.95), w0 * 0.45);
                    break;
                case HeroParticleKind.Arc:
                {
                    var color = _dark ? _core : _partHot;
                    var alpha = (1 - u) * (glow ? 1 : 0.95);
                    const double r0 = 3.2;
                    var r1 = r0 + p.Size;
                    var previous = new Point(p.X + Math.Cos(p.Angle) * r0, p.Y + Math.Sin(p.Angle) * r0);
                    for (var i = 1; i <= 3; i++)
                    {
                        var rr = r0 + (r1 - r0) * i / 3;
                        var jitter = (i % 2 == 1 ? 1 : -1) * 0.35 * (0.5 + p.Seed);
                        var next = new Point(p.X + Math.Cos(p.Angle + jitter) * rr, p.Y + Math.Sin(p.Angle + jitter) * rr);
                        StrokeLine(dc, glow, previous, next, color, alpha, w0 * 0.4);
                        previous = next;
                    }

                    break;
                }

                case HeroParticleKind.Pop:
                {
                    var length = 3.4 * Math.Sin(Math.PI * u);
                    if (length < 0.05)
                    {
                        break;
                    }

                    var matrix = Matrix.Identity;
                    matrix.Scale(length, 1);
                    matrix.Rotate(p.Angle * 180 / Math.PI);
                    matrix.Translate(p.X, p.Y);
                    Use(dc, matrix);
                    dc.DrawGeometry(_ink.Brush(_core, 0.95), null, _shapes.Pop);
                    break;
                }
            }
        }

        EndRun();
    }

    private void PrepareMixes()
    {
        var key = (_partHot, _core, _partColor);
        if (key == _mixKey)
        {
            return;
        }

        _mixKey = key;
        for (var i = 0; i <= MixSteps; i++)
        {
            var t = i / (double)MixSteps;
            _hotToColor[i] = _hc ? _color : OkLab.Mix(_partHot, _partColor, t);
            _coreToColor[i] = _hc ? _color : OkLab.Mix(_core, _partColor, t);
        }
    }

    /// <summary>The upgrade flash: a radial glow on the incoming vehicle, 8 → 36 units.</summary>
    private void DrawFlash(DrawingContext dc)
    {
        if (_stage.Flash is not { } flash)
        {
            return;
        }

        var brush = _ink.Flash(_dark ? HeatPalette.WhiteHot : _hot, _hot);
        dc.PushOpacity(Math.Clamp(flash.Alpha, 0, 1));
        dc.DrawEllipse(brush, null, new Point(flash.Center.X, flash.Center.Y), flash.Radius, flash.Radius);
        dc.Pop();
    }
}
