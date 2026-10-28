using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using AgentSpeed.Presentation.Orb.Names;
using AgentSpeed.Theme;

namespace AgentSpeed.Orb.Names;

/// <summary>
/// The plate's type (DESIGN 2b, Name plate, Type): the system font at semibold (600) with the language's CJK fallback,
/// letter-spaced by placing each grapheme at its shaped position (kerning kept) plus the tracking, since WPF text has no
/// letter spacing. Widths are measured in Ideal mode, so they do not depend on the display's DPI.
/// </summary>
internal sealed class PlateTypography
{
    private const int MaxCached = 512;
    private const double LatinCapHeight = 0.7;

    /// <summary>The center of the ideographic em box above the baseline (CJK glyphs are centered on it).</summary>
    private const double IdeographicCenter = 0.38;

    private readonly Dictionary<(string Text, double Size), double> _widths = new();
    private readonly CultureInfo _culture;

    public PlateTypography(string languageCode)
    {
        LanguageCode = languageCode;
        Typeface = new Typeface(AppFonts.Ui(languageCode), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        Language = AppFonts.XmlLanguageFor(languageCode);
        _culture = CultureInfo.GetCultureInfo(Language.IetfLanguageTag);
        CapHeight = Typeface.TryGetGlyphTypeface(out var glyphs) && glyphs.CapsHeight is > 0.3 and < 1 ? glyphs.CapsHeight : LatinCapHeight;
    }

    public string LanguageCode { get; }

    public Typeface Typeface { get; }

    public XmlLanguage Language { get; }

    /// <summary>Cap height of the primary face, as a fraction of the em.</summary>
    public double CapHeight { get; }

    /// <summary>The natural (untracked) width of a text at a size (<see cref="NamePlateMeasure"/>).</summary>
    public double Measure(string text, double size)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        if (_widths.TryGetValue((text, size), out var width))
        {
            return width;
        }

        width = Format(text, size, Brushes.Black, 1).WidthIncludingTrailingWhitespace;
        if (_widths.Count >= MaxCached)
        {
            _widths.Clear();
        }

        _widths[(text, size)] = width;
        return width;
    }

    /// <summary>The baseline for text centered on <paramref name="centerY"/>: cap height for Latin, the ideographic center for CJK.</summary>
    public double Baseline(double centerY, double size, bool cjk) => centerY + size * (cjk ? IdeographicCenter : CapHeight / 2);

    /// <summary>
    /// Draws a run with its tracking: the left edge at <paramref name="x"/>, the baseline at <paramref name="baseline"/>.
    /// Scripts that join or reorder are drawn as one run without tracking.
    /// </summary>
    public void Draw(DrawingContext dc, string text, double size, double tracking, double x, double baseline, Brush brush, double pixelsPerDip)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var whole = Format(text, size, brush, pixelsPerDip);
        var starts = NamePlateScript.GraphemeStarts(text);
        if (!NamePlateScript.AllowsTracking(text) || starts.Length < 2 || tracking == 0)
        {
            dc.DrawText(whole, new Point(x, baseline - whole.Baseline));
            return;
        }

        for (var i = 0; i < starts.Length; i++)
        {
            var start = starts[i];
            var length = (i + 1 < starts.Length ? starts[i + 1] : text.Length) - start;
            var left = Offset(whole, text, start, length, size);
            var grapheme = Format(text.Substring(start, length), size, brush, pixelsPerDip);
            dc.DrawText(grapheme, new Point(x + left + i * tracking, baseline - grapheme.Baseline));
        }
    }

    /// <summary>The shaped left edge of a grapheme in the whole run (kerning included); the prefix width as a fallback.</summary>
    private double Offset(FormattedText whole, string text, int start, int length, double size)
    {
        if (start == 0)
        {
            return 0;
        }

        var bounds = whole.BuildHighlightGeometry(new Point(0, 0), start, length)?.Bounds;
        return bounds is { IsEmpty: false } b && double.IsFinite(b.Left) ? b.Left : Measure(text[..start], size);
    }

    private FormattedText Format(string text, double size, Brush brush, double pixelsPerDip) =>
        new(text, _culture, FlowDirection.LeftToRight, Typeface, size, brush, null, TextFormattingMode.Ideal, pixelsPerDip);
}
