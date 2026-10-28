using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Titles;
using AgentSpeed.Theme;

namespace AgentSpeed.Titles;

/// <summary>
/// Title widths in the readout's title style (DESIGN 1, Title length): Segoe UI Variable Text semibold with the CJK
/// font of the language (Microsoft JhengHei UI, Microsoft YaHei UI, Yu Gothic UI), uppercase for English.
/// </summary>
internal static class TitleMeasure
{
    private static readonly Dictionary<string, Typeface> Faces = new(StringComparer.Ordinal);
    private static readonly Dictionary<(string Text, string Language), double> Cache = new();

    /// <summary><c>w7</c>: the width at the 7 pt floor with tracking 0, in DIP.</summary>
    public static double W7(string text, string languageCode)
    {
        ArgumentNullException.ThrowIfNull(text);
        var shown = TitleText.DisplayCase(text, languageCode == LanguageCodes.En);
        if (Cache.TryGetValue((shown, languageCode), out var cached))
        {
            return cached;
        }

        var width = Width(shown, TitleText.MeasureSize, languageCode);
        if (Cache.Count > 512)
        {
            Cache.Clear();
        }

        Cache[(shown, languageCode)] = width;
        return width;
    }

    /// <summary>Advance width of <paramref name="text"/> as it is, in the title style at <paramref name="size"/> DIP.</summary>
    public static double Width(string text, double size, string languageCode)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        var formatted = new FormattedText(text, LanguageResolver.CultureFor(languageCode), FlowDirection.LeftToRight, Face(languageCode),
            size, Brushes.Black, 1.0);
        return formatted.WidthIncludingTrailingWhitespace;
    }

    public static Typeface Face(string languageCode)
    {
        if (!Faces.TryGetValue(languageCode, out var face))
        {
            face = new Typeface(AppFonts.Ui(languageCode), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            Faces[languageCode] = face;
        }

        return face;
    }
}
