using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Titles;
using AgentSpeed.Theme;

namespace AgentSpeed.Orb.Hero.Badge;

/// <summary>
/// A title set for the badge (DESIGN 4c, Hero title badge, Title layout): its glyph outlines in canvas units, each line
/// centered on x = 50 by its advance and the ink box of all lines centered on y = 50.
/// </summary>
internal sealed class BadgeGlyphs
{
    public BadgeGlyphs(TitleBadgeLayout layout, Geometry geometry, double inkTop, double inkBottom, double inkWidth)
    {
        Layout = layout;
        Geometry = geometry;
        InkTop = inkTop;
        InkBottom = inkBottom;
        InkWidth = inkWidth;
    }

    public TitleBadgeLayout Layout { get; }

    /// <summary>Frozen outlines of every line.</summary>
    public Geometry Geometry { get; }

    public double InkTop { get; }

    public double InkBottom { get; }

    /// <summary>The widest line's advance (without trailing tracking).</summary>
    public double InkWidth { get; }

    public double InkHeight => InkBottom - InkTop;
}

/// <summary>
/// Glyph outlines of badge titles, built once per text and language and then cached (sizes are canvas units, so the
/// orb size never invalidates them). Segoe UI Variable Display Bold with the language's CJK face in bold.
/// </summary>
internal sealed class BadgeTitleCache
{
    private const int Capacity = 96;
    private const double ReferenceSize = 100;

    private readonly Dictionary<(string Text, string Language), BadgeGlyphs> _entries = new();
    private readonly Dictionary<string, (Typeface Face, CultureInfo Culture)> _faces = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _widths = new(StringComparer.Ordinal);

    public BadgeGlyphs Get(string text, string languageCode)
    {
        if (_entries.TryGetValue((text, languageCode), out var cached))
        {
            return cached;
        }

        if (_entries.Count >= Capacity)
        {
            _entries.Clear();
        }

        var glyphs = Build(text, languageCode);
        _entries[(text, languageCode)] = glyphs;
        return glyphs;
    }

    private (Typeface Face, CultureInfo Culture) Face(string languageCode)
    {
        if (!_faces.TryGetValue(languageCode, out var face))
        {
            face = (new Typeface(AppFonts.Display(languageCode), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                LanguageResolver.CultureFor(languageCode));
            _faces[languageCode] = face;
        }

        return face;
    }

    private static FormattedText Text(string text, (Typeface Face, CultureInfo Culture) face, double size) =>
        new(text, face.Culture, FlowDirection.LeftToRight, face.Face, size, Brushes.Black, 1.0);

    /// <summary>Advance width at <see cref="ReferenceSize"/> (glyph advances scale linearly with the size).</summary>
    private double ReferenceWidth(string text, (Typeface Face, CultureInfo Culture) face)
    {
        if (!_widths.TryGetValue(text, out var width))
        {
            width = text.Length == 0 ? 0 : Text(text, face, ReferenceSize).WidthIncludingTrailingWhitespace;
            _widths[text] = width;
        }

        return width;
    }

    /// <summary>Advance width of a line without its trailing tracking, in canvas units.</summary>
    private double LineWidth(IReadOnlyList<string> graphemes, string text, double size, double trackingEm,
        (Typeface Face, CultureInfo Culture) face) =>
        ReferenceWidth(text, face) * size / ReferenceSize + trackingEm * size * Math.Max(0, graphemes.Count - 1);

    private BadgeGlyphs Build(string text, string languageCode)
    {
        var face = Face(languageCode);
        _widths.Clear();
        var layout = TitleBadgeLayout.Layout(text, languageCode,
            (line, size, tracking) => LineWidth(TitleBadgeLayout.Graphemes(line), line, size, tracking, face));
        var size = layout.Size;
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        var inkWidth = 0.0;
        for (var index = 0; index < layout.Lines.Count; index++)
        {
            var line = layout.Lines[index];
            var graphemes = TitleBadgeLayout.Graphemes(line);
            var width = LineWidth(graphemes, line, size, layout.Tracking, face);
            inkWidth = Math.Max(inkWidth, width);
            var left = 50 - width / 2;
            var baseline = index * layout.LineAdvance * size;
            if (layout.Tracking == 0 || graphemes.Count < 2)
            {
                var formatted = Text(line, face, size);
                group.Children.Add(formatted.BuildGeometry(new Point(left, baseline - formatted.Baseline)));
                continue;
            }

            // Tracking: each grapheme sits at the advance of the text before it (kerning kept) plus the tracking.
            var prefix = string.Empty;
            for (var i = 0; i < graphemes.Count; i++)
            {
                var x = left + ReferenceWidth(prefix, face) * size / ReferenceSize + layout.Tracking * size * i;
                prefix += graphemes[i];
                if (graphemes[i] == " ")
                {
                    continue;
                }

                var formatted = Text(graphemes[i], face, size);
                group.Children.Add(formatted.BuildGeometry(new Point(x, baseline - formatted.Baseline)));
            }
        }

        var bounds = group.Bounds;
        double top, bottom;
        if (bounds.IsEmpty || bounds.Height <= 0)
        {
            top = -0.72 * size;
            bottom = 0;
        }
        else
        {
            top = bounds.Top;
            bottom = bounds.Bottom;
        }

        var shift = 50 - (top + bottom) / 2;
        group.Transform = new TranslateTransform(0, shift);
        group.Freeze();
        return new BadgeGlyphs(layout, group, top + shift, bottom + shift, inkWidth);
    }
}
