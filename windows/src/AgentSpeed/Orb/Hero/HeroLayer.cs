using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Hero;

namespace AgentSpeed.Orb.Hero;

/// <summary>How the vehicle glow is drawn (DESIGN 4c "Color and glow").</summary>
internal enum HeroGlowMode
{
    /// <summary>A GPU-blurred copy of the strokes.</summary>
    Blur,

    /// <summary>Two wider, fainter strokes under each part (no blur effect).</summary>
    Strokes,
}

/// <summary>
/// The hero vehicle layer of the dashboard orb: a container in canvas units (100 × 100) with the soft circular mask,
/// holding the crisp strokes and the blurred glow over them. Drawn only while the flyout is visible.
/// </summary>
internal sealed class HeroLayer
{
    /// <summary>
    /// BlurEffect radius per glow σ. Calibrated on a Windows 11 machine at 200 % against the motion prototype's
    /// Gaussian (CSS <c>blur(σ)</c>) at the same scale.
    /// </summary>
    private const double BlurRadiusPerSigma = 1.5;

    /// <summary>
    /// Dark mode: WPF has no additive blending, so the glow copy (normal blending) is a little stronger to give the same
    /// luminous halo as the additive glow of DESIGN 4c; the strokes themselves carry the light it would add to them.
    /// </summary>
    private const double DarkGlowGain = 1.2;

    private readonly VehicleArt _art;
    private readonly DrawingVisual _glow = new();
    private readonly DrawingVisual _crisp = new();
    private readonly HeroPainter _painter;
    private readonly HeroInk _ink = new();
    private readonly BlurEffect _blur = new() { Radius = 4, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality };
    private HeroGlowMode _mode = HeroGlowMode.Blur;
    private bool _empty = true;

    public HeroLayer(VehicleArt art)
    {
        _art = art;
        _painter = new HeroPainter(art, new HeroShapes(art), _ink);
        // DESIGN 4c resolved detail 1: the glow is composited over the strokes.
        Root.Children.Add(_crisp);
        Root.Children.Add(_glow);

        var solid = art.MaskSolid / art.MaskClear;
        var mask = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = new Point(art.MaskCenter.X, art.MaskCenter.Y),
            GradientOrigin = new Point(art.MaskCenter.X, art.MaskCenter.Y),
            RadiusX = art.MaskClear,
            RadiusY = art.MaskClear,
            GradientStops =
            {
                new GradientStop(Colors.Black, 0),
                new GradientStop(Colors.Black, solid),
                new GradientStop(Colors.Transparent, 1),
            },
        };
        mask.Freeze();
        Root.OpacityMask = mask;
        var clip = new EllipseGeometry(new Point(art.MaskCenter.X, art.MaskCenter.Y), art.MaskClear + 0.5, art.MaskClear + 0.5);
        clip.Freeze();
        Root.Clip = clip;
        _glow.Effect = _blur;
    }

    public ContainerVisual Root { get; } = new();

    public HeroGlowMode GlowMode => _mode;

    /// <summary>Places the 100-unit canvas as a square of side <c>orbFraction × S</c> centered on the orb.</summary>
    public void Configure(Point center, double diameter, HeroGlowMode mode)
    {
        var k = _art.OrbFraction * diameter / 100;
        var transform = new MatrixTransform(k, 0, 0, k, center.X - 50 * k, center.Y - 50 * k);
        transform.Freeze();
        Root.Transform = transform;
        if (mode != _mode)
        {
            _mode = mode;
            _glow.Effect = mode == HeroGlowMode.Blur ? _blur : null;
        }

        _ink.Clear();
    }

    /// <param name="highContrast">The theme highlight color in a high-contrast theme (no glow, scenery or speed lines).</param>
    public void Render(HeroStage stage, Rgba? highContrast)
    {

        var gain = stage.Options.Dark ? DarkGlowGain : 1;
        var glowOpacity = highContrast is null ? Math.Clamp(stage.GlowOpacity * gain, 0, 1) : 0;
        var glowOn = glowOpacity > 0.003;
        using (var crisp = _crisp.RenderOpen())
        using (var glow = _glow.RenderOpen())
        {
            _painter.Paint(crisp, glowOn ? glow : null, stage, highContrast, _mode == HeroGlowMode.Strokes);
        }

        _empty = false;
        if (Math.Abs(_glow.Opacity - glowOpacity) > 0.001)
        {
            _glow.Opacity = glowOpacity;
        }

        if (_mode == HeroGlowMode.Blur && glowOn)
        {
            var radius = Math.Round(stage.GlowSigma * BlurRadiusPerSigma * 20) / 20;
            if (Math.Abs(_blur.Radius - radius) > 0.01)
            {
                _blur.Radius = radius;
            }
        }
    }

    public void Clear()
    {
        if (_empty)
        {
            return;
        }

        _empty = true;
        using (_crisp.RenderOpen())
        using (_glow.RenderOpen())
        {
        }
    }
}
