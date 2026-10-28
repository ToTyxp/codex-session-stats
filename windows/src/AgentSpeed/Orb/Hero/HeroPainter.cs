using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Hero;
using AgentSpeed.Presentation.Layout;
using AgentSpeed.Presentation.Settings;

namespace AgentSpeed.Orb.Hero;

/// <summary>
/// Records one frame of the vehicle layer (DESIGN 4c) in canvas units: the crisp strokes into one drawing and the glow
/// source (each part at its role's glow share) into another. A port of the motion prototype's renderer.
/// </summary>
internal sealed partial class HeroPainter
{
    private static readonly (double Delay, double Alpha)[] Ghosts = [(0.012, 0.26), (0.026, 0.13), (0.042, 0.06)];

    /// <summary>
    /// Dark mode: how much brighter a vehicle stroke gets per unit of glow opacity. The prototype's additive glow adds
    /// light on top of each stroke (cyan turns electric, gold turns lemon); WPF cannot blend additively, so the strokes
    /// carry that light themselves.
    /// </summary>
    private const double DarkStrokeGain = 0.6;

    private readonly VehicleArt _art;
    private readonly HeroShapes _shapes;
    private readonly HeroInk _ink;
    private readonly RigPose _pose = new();
    private readonly PointD[] _contrail = new PointD[HeroShapes.ContrailPoints];

    private HeroStage _stage = null!;
    private HeroPaint _paint;
    private Rgba _color;
    private Rgba _hot;
    private Rgba _core;
    private Rgba _partColor;
    private Rgba _partHot;
    private bool _dark;
    private bool _hc;
    private bool _animated;
    private EffectsIntensity _level;
    private double _w0;
    private double _sigma;
    private bool _glowStrokes;
    private DrawingContext? _runContext;
    private Matrix _runMatrix;
    private bool _runPushed;

    public HeroPainter(VehicleArt art, HeroShapes shapes, HeroInk ink)
    {
        _art = art;
        _shapes = shapes;
        _ink = ink;
    }

    /// <param name="glow">The glow source, or null when the glow is off.</param>
    /// <param name="highContrast">The theme's highlight color in a high-contrast theme, else null.</param>
    /// <param name="glowStrokes">Approximate the glow with two wider strokes instead of a blurred copy.</param>
    public void Paint(DrawingContext crisp, DrawingContext? glow, HeroStage stage, Rgba? highContrast, bool glowStrokes)
    {
        _stage = stage;
        _paint = stage.Paint();
        _hc = highContrast is not null;
        _color = highContrast ?? _paint.Color;
        _hot = highContrast ?? _paint.Hot;
        _core = highContrast ?? _paint.Core;
        _dark = _paint.Dark;
        var gain = _dark && !_hc ? 1 + DarkStrokeGain * Math.Clamp(stage.GlowOpacity, 0, 1) : 1;
        _partColor = Brighten(_color, gain);
        _partHot = Brighten(_hot, gain);
        _animated = stage.Animated;
        _level = stage.Level;
        _w0 = _art.StrokeWidth * (stage.Options.IncreasedContrast ? 1.3 : 1);
        _sigma = stage.GlowSigma;
        _glowStrokes = glowStrokes;
        _ink.BeginFrame();

        var shake = stage.Shake;
        var shaken = shake.X != 0 || shake.Y != 0;
        if (shaken)
        {
            var offset = new Matrix(1, 0, 0, 1, shake.X, shake.Y);
            crisp.PushTransform(_ink.Transform(offset));
            glow?.PushTransform(_ink.Transform(offset));
        }

        if (!_hc)
        {
            DrawScenery(crisp);
            if (_animated)
            {
                DrawStreaks(crisp);
            }
        }

        if (stage.Previous is { } previous)
        {
            DrawActorWithGhosts(crisp, glow, previous, outgoing: true);
        }

        DrawActorWithGhosts(crisp, glow, stage.Current, outgoing: false);

        if (_animated)
        {
            DrawParticles(crisp, glow);
        }

        if (!_hc)
        {
            DrawFlash(crisp);
        }

        if (stage.Sweep is { } sweep && !_hc)
        {
            var band = _dark ? HeatPalette.WhiteHot : HeroPalette.Stroke(0, false);
            var brush = HeroInk.Band(band, (_dark ? 0.75 : 0.6) * sweep.Amount, sweep.X);
            var placement = stage.Place(stage.Current, false);
            DrawActor(crisp, null, _art.Vehicles[stage.Current.Vehicle], placement, StartupFx.None, 1, ghost: true, brush);
        }

        if (shaken)
        {
            crisp.Pop();
            glow?.Pop();
        }
    }

    private void DrawActorWithGhosts(DrawingContext crisp, DrawingContext? glow, HeroActor actor, bool outgoing)
    {
        var placement = _stage.Place(actor, outgoing);
        if (placement.Alpha <= 0.002)
        {
            return;
        }

        var vehicle = _art.Vehicles[actor.Vehicle];
        var fx = _stage.Startup(actor, outgoing);
        if (placement.Ghost > 40 && _animated)
        {
            foreach (var (delay, alpha) in Ghosts)
            {
                var shifted = placement with
                {
                    X = placement.X - placement.Ghost * delay * placement.HeadingX,
                    Y = placement.Y - placement.Ghost * delay * placement.HeadingY,
                };
                DrawActor(crisp, null, vehicle, shifted, fx, alpha, ghost: true, null);
            }
        }

        DrawActor(crisp, glow, vehicle, placement, fx, 1, ghost: false, null);
    }

    // ---- stroke helpers (canvas units) ----

    private void StrokeGeometry(DrawingContext dc, bool glow, Geometry geometry, Rgba color, double alpha, double width, Pen? band = null)
    {
        if (band is not null)
        {
            dc.DrawGeometry(null, band, geometry);
            return;
        }

        if (glow && _glowStrokes)
        {
            if (_ink.Pen(color, 0.2 * alpha, width + 2.6 * _sigma) is { } wide)
            {
                dc.DrawGeometry(null, wide, geometry);
            }

            if (_ink.Pen(color, 0.5 * alpha, width + 1.2 * _sigma) is { } near)
            {
                dc.DrawGeometry(null, near, geometry);
            }

            return;
        }

        if (_ink.Pen(color, alpha, width) is { } pen)
        {
            dc.DrawGeometry(null, pen, geometry);
        }
    }

    private void StrokeCircle(DrawingContext dc, bool glow, Point center, double radius, Rgba color, double alpha, double width, Pen? band = null)
    {
        if (band is not null)
        {
            dc.DrawEllipse(null, band, center, radius, radius);
            return;
        }

        if (glow && _glowStrokes)
        {
            if (_ink.Pen(color, 0.2 * alpha, width + 2.6 * _sigma) is { } wide)
            {
                dc.DrawEllipse(null, wide, center, radius, radius);
            }

            if (_ink.Pen(color, 0.5 * alpha, width + 1.2 * _sigma) is { } near)
            {
                dc.DrawEllipse(null, near, center, radius, radius);
            }

            return;
        }

        if (_ink.Pen(color, alpha, width) is { } pen)
        {
            dc.DrawEllipse(null, pen, center, radius, radius);
        }
    }

    private void StrokeLine(DrawingContext dc, bool glow, Point from, Point to, Rgba color, double alpha, double width)
    {
        if (glow && _glowStrokes)
        {
            if (_ink.Pen(color, 0.2 * alpha, width + 2.6 * _sigma) is { } wide)
            {
                dc.DrawLine(wide, from, to);
            }

            if (_ink.Pen(color, 0.5 * alpha, width + 1.2 * _sigma) is { } near)
            {
                dc.DrawLine(near, from, to);
            }

            return;
        }

        if (_ink.Pen(color, alpha, width) is { } pen)
        {
            dc.DrawLine(pen, from, to);
        }
    }

    /// <summary>Scales a color's channels (clipped), the way added light brightens and saturates it.</summary>
    private static Rgba Brighten(Rgba color, double gain) => gain <= 1
        ? color
        : new Rgba(Math.Min(1, color.R * gain), Math.Min(1, color.G * gain), Math.Min(1, color.B * gain), color.A);

    /// <summary>
    /// Draws what follows with <paramref name="matrix"/>. Consecutive strokes that share a transform share one push, so a
    /// vehicle costs a handful of transforms per frame.
    /// </summary>
    private void Use(DrawingContext dc, in Matrix matrix)
    {
        if (ReferenceEquals(_runContext, dc) && _runMatrix == matrix)
        {
            return;
        }

        EndRun();
        _runContext = dc;
        _runMatrix = matrix;
        if (!matrix.IsIdentity)
        {
            dc.PushTransform(_ink.Transform(matrix));
            _runPushed = true;
        }
    }

    private void EndRun()
    {
        if (_runPushed)
        {
            _runContext!.Pop();
            _runPushed = false;
        }

        _runContext = null;
    }

    private static T Freeze<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
