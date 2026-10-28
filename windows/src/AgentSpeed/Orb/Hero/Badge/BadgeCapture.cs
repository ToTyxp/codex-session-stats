using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Hero;
using AgentSpeed.Presentation.Hero.Badge;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Settings;
using AgentSpeed.Presentation.Titles;

namespace AgentSpeed.Orb.Hero.Badge;

/// <summary>A title theme a badge capture draws: its labels as the badge draws them, idle first.</summary>
internal sealed record BadgeCaptureTheme(string Id, string Name, IReadOnlyList<string> Labels);

/// <summary>The themes of a badge capture and the language they are laid out in.</summary>
internal sealed record BadgeCaptureSource(string LanguageCode, IReadOnlyList<BadgeCaptureTheme> Themes);

/// <summary>
/// Developer captures of the hero title badge (<c>--capture-dir</c>), rendered offscreen: a sheet of every theme but
/// Vehicles at idle and levels 1–13 in a steady state, and a strip of the transitions at fixed <c>τ</c>. Each cell is
/// the badge (and the vehicle where it crossfades) over a plain orb disc, at 2×.
/// </summary>
internal static class BadgeCapture
{
    private const double Diameter = 112;
    private const double Cell = 128;
    private const double LabelWidth = 170;
    private const double CaptionHeight = 22;
    private const double Scale = 2;
    private const double Redline = HeatMath.DefaultRedline;
    private const double Fps = 60;
    private const double ChangeAt = 1.2;

    private static readonly double[] Taus = [0, 0.06, 0.12, 0.2, 0.3, 0.45, 0.7];

    /// <summary>The eight preset themes with the badge (picker order) in the app language.</summary>
    public static BadgeCaptureSource Presets(Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        var themes = new List<BadgeCaptureTheme>();
        foreach (var preset in TitlePresets.All)
        {
            if (TitlePresets.ShowsVehicleArt(preset))
            {
                continue;
            }

            var theme = TitleTheme.Of(preset);
            themes.Add(new BadgeCaptureTheme(TitlePresets.Id(preset), theme.Name(localizer), theme.Labels(localizer)));
        }

        return new BadgeCaptureSource(localizer.LanguageCode, themes);
    }

    /// <summary>Every theme (rows) at idle and levels 1–13 (columns), Standard effects.</summary>
    public static BitmapSource Sheet(BadgeCaptureSource source, bool dark)
    {
        ArgumentNullException.ThrowIfNull(source);
        var layer = new BadgeLayer();
        var width = LabelWidth + (BadgeStage.PipCount + 1) * Cell;
        var height = source.Themes.Count * Cell;
        return Compose(width, height, dark, dc =>
        {
            for (var row = 0; row < source.Themes.Count; row++)
            {
                var theme = source.Themes[row];
                Label(dc, theme.Name, new Point(12, row * Cell + Cell / 2), dark);
                for (var level = 0; level <= BadgeStage.PipCount; level++)
                {
                    var stage = Steady(theme.Labels, level, dark);
                    var cell = RenderCell(dark, layer, stage, source.LanguageCode, null, 1);
                    dc.DrawImage(cell, new Rect(LabelWidth + level * Cell, row * Cell, Cell, Cell));
                }
            }
        });
    }

    /// <summary>
    /// The transitions of <paramref name="themeId"/> at <c>τ</c> 0 … 0.7 s: level up, jump +3, level down, start-up,
    /// parking, Vehicles → theme and theme → <paramref name="otherId"/>.
    /// </summary>
    public static BitmapSource Strip(BadgeCaptureSource source, bool dark, string themeId = "wuxia", string otherId = "space")
    {
        ArgumentNullException.ThrowIfNull(source);
        var theme = Find(source, themeId);
        var other = Find(source, otherId);
        var rows = new (string Name, int From, int To, Kind Kind)[]
        {
            ("level up", 5, 6, Kind.Level),
            ("jump +3", 5, 8, Kind.Level),
            ("level down", 8, 7, Kind.Level),
            ("start-up", 0, 4, Kind.Level),
            ("parking", 2, 0, Kind.Level),
            ($"Vehicles → {theme.Name}", 5, 5, Kind.FromVehicle),
            ($"{theme.Name} → {other.Name}", 5, 5, Kind.Rename),
        };
        var layer = new BadgeLayer();
        var rowHeight = Cell + CaptionHeight;
        var width = LabelWidth + Taus.Length * Cell;
        var height = rows.Length * rowHeight;
        return Compose(width, height, dark, dc =>
        {
            for (var row = 0; row < rows.Length; row++)
            {
                var (name, from, to, kind) = rows[row];
                var top = row * rowHeight;
                Label(dc, name, new Point(12, top + Cell / 2), dark);
                for (var column = 0; column < Taus.Length; column++)
                {
                    var tau = Taus[column];
                    var cell = kind == Kind.FromVehicle
                        ? VehicleCell(dark, layer, theme.Labels, from, tau, source.LanguageCode)
                        : TransitionCell(dark, layer, theme.Labels, kind == Kind.Rename ? other.Labels : theme.Labels, from, to, tau,
                            source.LanguageCode);
                    var x = LabelWidth + column * Cell;
                    dc.DrawImage(cell, new Rect(x, top, Cell, Cell));
                    Label(dc, string.Create(CultureInfo.InvariantCulture, $"τ {tau:0.00}"), new Point(x + Cell / 2 - 16, top + Cell + 9), dark);
                }
            }
        });
    }

    private enum Kind
    {
        Level,
        FromVehicle,
        Rename,
    }

    private static BadgeCaptureTheme Find(BadgeCaptureSource source, string id)
    {
        foreach (var theme in source.Themes)
        {
            if (theme.Id == id)
            {
                return theme;
            }
        }

        return source.Themes.Count > 0 ? source.Themes[0] : throw new InvalidOperationException("No badge themes to capture.");
    }

    private static HeroOptions Options(bool dark) => new(EffectsIntensity.Standard, Dark: dark);

    /// <summary>A value inside the band of <paramref name="level"/> (idle: 0).</summary>
    private static double ValueFor(int level) => level == 0 ? 0 : SpeedTitles.Threshold((SpeedTitle)level, Redline) * 1.25;

    private static BadgeInput Input(double time, int level, double changedAt, double heat, double overdrive, double value, bool dark) =>
        new(time, level, changedAt, heat, overdrive, HeatMath.RawTier(value, Redline), Redline, Options(dark));

    /// <summary>The badge settled at a level after 1.2 s of motion (motes, streaks and embers running).</summary>
    private static BadgeStage Steady(IReadOnlyList<string> labels, int level, bool dark)
    {
        var stage = new BadgeStage(7);
        var value = ValueFor(level);
        var heat = HeatMath.Heat(value, Redline);
        var overdrive = HeatMath.Overdrive(value, Redline);
        for (var frame = 0; frame <= ChangeAt * Fps; frame++)
        {
            stage.Step(Input(frame / Fps, level, double.NegativeInfinity, heat, overdrive, value, dark), labels);
        }

        return stage;
    }

    /// <summary>
    /// The badge <paramref name="tau"/> s after the level changes from <paramref name="from"/> to <paramref name="to"/>
    /// (and the labels from <paramref name="before"/> to <paramref name="after"/>), with heat following its spring.
    /// </summary>
    private static BitmapSource TransitionCell(bool dark, BadgeLayer layer, IReadOnlyList<string> before, IReadOnlyList<string> after, int from,
        int to, double tau, string languageCode)
    {
        var stage = new BadgeStage(7);
        var heat = new SettleSpring(0.6, HeatMath.Heat(ValueFor(from), Redline));
        var overdrive = new SettleSpring(0.6, HeatMath.Overdrive(ValueFor(from), Redline));
        var end = ChangeAt + tau;
        var frames = (int)Math.Ceiling(end * Fps);
        for (var frame = 0; frame <= frames; frame++)
        {
            var time = Math.Min(end, frame / Fps);
            var changed = time >= ChangeAt;
            var level = changed ? to : from;
            var value = ValueFor(level);
            heat.Step(HeatMath.Heat(value, Redline), 1 / Fps);
            overdrive.Step(HeatMath.Overdrive(value, Redline), 1 / Fps);
            stage.Step(Input(time, level, changed ? ChangeAt : double.NegativeInfinity, heat.Value, overdrive.Value, value, dark),
                changed ? after : before);
        }

        return RenderCell(dark, layer, stage, languageCode, null, 1);
    }

    /// <summary>Vehicles → theme on the same level: the car crossfades into the badge (0.3 s).</summary>
    private static BitmapSource VehicleCell(bool dark, BadgeLayer layer, IReadOnlyList<string> labels, int level, double tau, string languageCode)
    {
        var art = VehicleArt.Shared;
        var mix = new HeroArtMix();
        mix.Update(false, 0, false);
        var vehicle = art is null ? null : new HeroStage(art, VehicleArt.KeyFor((SpeedTitle)level), 7);
        var stage = new BadgeStage(7);
        var value = ValueFor(level);
        var heat = HeatMath.Heat(value, Redline);
        var overdrive = HeatMath.Overdrive(value, Redline);
        var tier = HeatMath.RawTier(value, Redline);
        var end = ChangeAt + tau;
        var frames = (int)Math.Ceiling(end * Fps);
        for (var frame = 0; frame <= frames; frame++)
        {
            var time = Math.Min(end, frame / Fps);
            vehicle?.Update(time, value, Redline, tier, (SpeedTitle)level, Options(dark), snap: frame == 0);
            if (time >= ChangeAt)
            {
                if (mix.Update(true, ChangeAt, false))
                {
                    stage.Reset();
                }

                stage.Step(Input(time, level, double.NegativeInfinity, heat, overdrive, value, dark), labels);
            }
        }

        HeroLayer? hero = null;
        if (vehicle is not null && art is not null)
        {
            hero = new HeroLayer(art);
            hero.Configure(new Point(Cell / 2, Cell / 2), Diameter, HeroGlowMode.Blur);
            hero.Render(vehicle, null);
        }

        var badgeMix = mix.BadgeMix(end);
        return RenderCell(dark, layer, badgeMix > 0.001 ? stage : null, languageCode, hero, badgeMix);
    }

    private static BitmapSource RenderCell(bool dark, BadgeLayer layer, BadgeStage? stage, string languageCode, HeroLayer? vehicle, double badgeMix)
    {
        var root = new ContainerVisual();
        var body = new DrawingVisual();
        using (var dc = body.RenderOpen())
        {
            var center = new Point(Cell / 2, Cell / 2);
            var fill = new RadialGradientBrush(dark ? Color.FromRgb(0x24, 0x28, 0x31) : Color.FromRgb(0xFF, 0xFF, 0xFF),
                dark ? Color.FromRgb(0x12, 0x14, 0x19) : Color.FromRgb(0xE4, 0xE8, 0xEE));
            var rim = new Pen(new SolidColorBrush(dark ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x24, 0, 0, 0)), 1);
            dc.DrawEllipse(fill, rim, center, Diameter / 2, Diameter / 2);
        }

        root.Children.Add(body);
        if (vehicle is not null)
        {
            vehicle.Root.Opacity = 1 - badgeMix;
            root.Children.Add(vehicle.Root);
        }

        if (stage is not null)
        {
            layer.Configure(new Point(Cell / 2, Cell / 2), Diameter, HeroGlowMode.Blur);
            layer.Render(stage, languageCode, null);
            layer.Root.Opacity = badgeMix;
            root.Children.Add(layer.Root);
        }

        var pixels = (int)Math.Ceiling(Cell * Scale);
        var bitmap = new RenderTargetBitmap(pixels, pixels, 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        bitmap.Render(root);
        root.Children.Clear();
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource Compose(double width, double height, bool dark, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(dark ? Color.FromRgb(0x1C, 0x1D, 0x21) : Color.FromRgb(0xF3, 0xF4, 0xF6)), null,
                new Rect(0, 0, width, height));
            draw(dc);
        }

        var target = new RenderTargetBitmap((int)Math.Ceiling(width * Scale), (int)Math.Ceiling(height * Scale), 96 * Scale, 96 * Scale,
            PixelFormats.Pbgra32);
        target.Render(visual);
        return target;
    }

    private static void Label(DrawingContext dc, string text, Point left, bool dark)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft JhengHei UI, Microsoft YaHei UI, Yu Gothic UI"),
                FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            12, new SolidColorBrush(dark ? Color.FromRgb(0xE6, 0xE6, 0xE6) : Color.FromRgb(0x30, 0x30, 0x30)), Scale);
        dc.DrawText(formatted, new Point(left.X, left.Y - formatted.Height / 2));
    }
}
