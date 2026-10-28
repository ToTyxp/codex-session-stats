using System.Windows.Media;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Theme;

namespace AgentSpeed.Settings.TitleThemes;

/// <summary>
/// The color of a title's threshold in Settings: the heat color on dark backgrounds, the darker label color of that
/// heat on light ones (contrast), as the redline title list draws it.
/// </summary>
internal static class TitleHeatBrushes
{
    private static readonly Dictionary<(int Heat, bool Dark), SolidColorBrush> Cache = new();

    public static SolidColorBrush For(double heat, bool dark)
    {
        var key = ((int)Math.Round(Math.Clamp(heat, 0, 1) * 1000), dark);
        if (!Cache.TryGetValue(key, out var brush))
        {
            var rgba = dark ? HeatPalette.Sample(heat) : OrbTokens.Light.TierLabel(heat);
            brush = MediaColors.Frozen(rgba.ToColor());
            Cache[key] = brush;
        }

        return brush;
    }

    /// <summary>The color of level 1–13's threshold at <paramref name="redline"/>.</summary>
    public static SolidColorBrush ForLevel(int level, double redline, bool dark) =>
        For(SpeedTitles.HeatAt(Presentation.Titles.TitleSettingsText.LevelTitle(level), redline), dark);
}
