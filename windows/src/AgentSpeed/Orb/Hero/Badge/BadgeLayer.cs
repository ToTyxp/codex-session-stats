using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Hero;
using AgentSpeed.Presentation.Hero.Badge;

namespace AgentSpeed.Orb.Hero.Badge;

/// <summary>
/// The hero title badge of the dashboard orb (DESIGN 4c, Hero title badge): the vehicle layer's canvas (100 × 100 units
/// on 0.72 S) and soft circular mask (solid to r 42, clear at r 49), holding the layers behind the title, two title
/// slots (outgoing and incoming) and the sparks in front. Decorative: plain visuals, never hit-testable or announced.
/// Drawn only while the dashboard is visible.
/// </summary>
internal sealed class BadgeLayer
{
    public const double OrbFraction = 0.72;
    public const double MaskSolid = 42;
    public const double MaskClear = 49;

    /// <summary>The two pre-blurred glow copies (σ in canvas units).</summary>
    public const double NearSigma = 1.6;

    public const double FarSigma = 4.2;

    /// <summary>BlurEffect radius per glow σ, as calibrated for the vehicle layer (HeroLayer).</summary>
    private const double BlurRadiusPerSigma = 1.5;

    /// <summary>Dark mode: the glow composites normally, a little stronger than the additive glow it stands in for.</summary>
    private const double DarkGlowGain = 1.2;

    private readonly ContainerVisual _content = new();
    private readonly TranslateTransform _shake = new();
    private readonly DrawingVisual _back = new();
    private readonly DrawingVisual _front = new();
    private readonly BadgeTitleSlot[] _slots = [new(), new()];
    private readonly BadgeTitleCache _titles;
    private readonly BadgeInk _ink = new();
    private readonly BadgePalette _palette = new();
    private readonly BadgePainter _painter;
    private HeroGlowMode _mode = HeroGlowMode.Blur;
    private bool _empty = true;
    private bool _frontEmpty = true;

    public BadgeLayer(BadgeTitleCache? titles = null)
    {
        _titles = titles ?? new BadgeTitleCache();
        _painter = new BadgePainter(_ink, _palette);
        _content.Transform = _shake;
        _content.Children.Add(_back);
        foreach (var slot in _slots)
        {
            _content.Children.Add(slot.GlowFar);
            _content.Children.Add(slot.GlowNear);
            _content.Children.Add(slot.Crisp);
        }

        _content.Children.Add(_front);
        Root.Children.Add(_content);
        ApplyGlowMode();

        var mask = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = new Point(50, 50),
            GradientOrigin = new Point(50, 50),
            RadiusX = MaskClear,
            RadiusY = MaskClear,
            GradientStops =
            {
                new GradientStop(Colors.Black, 0),
                new GradientStop(Colors.Black, MaskSolid / MaskClear),
                new GradientStop(Colors.Transparent, 1),
            },
        };
        mask.Freeze();
        Root.OpacityMask = mask;
        var clip = new EllipseGeometry(new Point(50, 50), MaskClear + 0.5, MaskClear + 0.5);
        clip.Freeze();
        Root.Clip = clip;
    }

    /// <summary>The layer; the shake moves its content, never the mask.</summary>
    public ContainerVisual Root { get; } = new();

    /// <summary>Places the 100-unit canvas as a square of side <c>0.72 S</c> centered on the orb.</summary>
    public void Configure(Point center, double diameter, HeroGlowMode mode)
    {
        var k = OrbFraction * diameter / 100;
        var transform = new MatrixTransform(k, 0, 0, k, center.X - 50 * k, center.Y - 50 * k);
        transform.Freeze();
        Root.Transform = transform;
        if (mode != _mode)
        {
            _mode = mode;
            ApplyGlowMode();
        }
    }

    /// <param name="languageCode">The language the labels are laid out in.</param>
    /// <param name="contrast">The theme's colors in a Windows high-contrast theme, else null.</param>
    public void Render(BadgeStage stage, string languageCode, BadgeContrast? contrast)
    {
        _empty = false;
        _ink.BeginFrame();
        var options = stage.Input.Options;
        var colors = _palette.Resolve(stage.Input.Heat, stage.Input.Overdrive, stage.Wake, options.Dark, options.IncreasedContrast);

        var shake = stage.Shake;
        if (_shake.X != shake.X)
        {
            _shake.X = shake.X;
        }

        if (_shake.Y != shake.Y)
        {
            _shake.Y = shake.Y;
        }

        var items = stage.Titles;
        var top = items.Top;
        var topGlyphs = _titles.Get(top.Text, languageCode);
        var halfWidth = topGlyphs.InkWidth * top.Scale / 2 + BadgeStage.RingClearance;
        var halfHeight = topGlyphs.InkHeight * top.Scale / 2 + BadgeStage.RingClearance;
        using (var dc = _back.RenderOpen())
        {
            _painter.DrawBack(dc, stage, colors, contrast, halfWidth, halfHeight);
        }

        ShowTitle(_slots[0], items.First, stage, colors, contrast, languageCode);
        if (items.Second is { } second)
        {
            ShowTitle(_slots[1], second, stage, colors, contrast, languageCode);
        }
        else
        {
            _slots[1].Hide();
        }

        var sparks = BadgePainter.HasSparks(stage);
        if (sparks || !_frontEmpty)
        {
            _frontEmpty = !sparks;
            using var dc = _front.RenderOpen();
            _painter.DrawFront(dc, stage, colors);
        }
    }

    public void Clear()
    {
        if (_empty)
        {
            return;
        }

        _empty = true;
        _frontEmpty = true;
        using (_back.RenderOpen())
        {
        }

        using (_front.RenderOpen())
        {
        }

        foreach (var slot in _slots)
        {
            slot.Hide();
        }
    }

    private void ShowTitle(BadgeTitleSlot slot, in BadgeTitleItem item, BadgeStage stage, in BadgeColors colors, BadgeContrast? contrast,
        string languageCode)
    {
        if (item.Alpha <= 0.005)
        {
            slot.Hide();
            return;
        }

        var glyphs = _titles.Get(item.Text, languageCode);
        var options = stage.Input.Options;
        var dark = options.Dark;
        var idle = item.IsIdle;
        BadgeFill fill;
        if (contrast is { } hc)
        {
            fill = new BadgeFill(idle ? hc.GrayText : hc.Highlight, default, false);
        }
        else if (idle)
        {
            fill = new BadgeFill(colors.IdleFill, default, false);
        }
        else
        {
            var gradient = dark && !options.IncreasedContrast;
            fill = new BadgeFill(gradient ? colors.TitleTop : colors.Color, colors.TitleBottom, gradient);
        }

        (BadgeBand, Rgba)? band = null;
        if (contrast is null)
        {
            if (idle && stage.IdleBand is { } light)
            {
                band = (light, dark ? HeatPalette.WhiteHot : HeatPalette.Sample(0));
            }
            else if (!idle && stage.Sheen(glyphs.InkWidth) is { } sheen)
            {
                band = (sheen, dark ? HeatPalette.WhiteHot : Rgba.White);
            }
        }

        var paint = default(BadgeGlowPaint);
        if (!idle && contrast is null && stage.Wake > 0.01)
        {
            var glow = stage.Glow(item.Boost);
            var strength = glow.Strength * (dark ? DarkGlowGain : 1);
            if (_mode == HeroGlowMode.Strokes)
            {
                paint = new BadgeGlowPaint(colors.Glow, 0.5 * strength, 0.2 * strength, 1.2 * glow.Sigma, 2.6 * glow.Sigma);
            }
            else
            {
                var (near, far) = Split(glow.Sigma, strength);
                paint = new BadgeGlowPaint(colors.Glow, near, far, 0, 0);
            }
        }

        slot.Show(glyphs, item, fill, band, paint, _mode == HeroGlowMode.Strokes);
    }

    /// <summary>
    /// Blends the two pre-blurred copies so their mix follows σ (1.6 → 4.2 units); a strength above 1 (a level-up boost)
    /// spills into the other copy.
    /// </summary>
    public static (double Near, double Far) Split(double sigma, double strength)
    {
        var t = HeroMath.Clamp01((sigma - NearSigma) / (FarSigma - NearSigma));
        var total = Math.Clamp(strength, 0, 2);
        var near = total * (1 - t);
        var far = total * t;
        if (near > 1)
        {
            far = Math.Min(1, far + near - 1);
            near = 1;
        }

        if (far > 1)
        {
            near = Math.Min(1, near + far - 1);
            far = 1;
        }

        return (near, far);
    }

    private void ApplyGlowMode()
    {
        foreach (var slot in _slots)
        {
            if (_mode == HeroGlowMode.Blur)
            {
                slot.SetEffects(Blur(NearSigma), Blur(FarSigma));
            }
            else
            {
                slot.SetEffects(null, null);
            }
        }
    }

    private static BlurEffect Blur(double sigma)
    {
        var blur = new BlurEffect { Radius = sigma * BlurRadiusPerSigma, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality };
        blur.Freeze();
        return blur;
    }
}
