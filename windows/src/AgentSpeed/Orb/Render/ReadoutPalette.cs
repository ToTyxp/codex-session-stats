using System.Windows;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Theme;

namespace AgentSpeed.Orb.Render;

/// <summary>Readout colors (DESIGN 2 tokens); the liquid style uses a second palette under its surface.</summary>
internal sealed record ReadoutPalette(
    Rgba Value,
    Rgba IdleValue,
    Rgba Secondary,
    Rgba? TitleOverride,
    Rgba? Shadow,
    double ShadowRadius,
    bool Glow,
    Rgba DotHalo)
{
    public static ReadoutPalette For(OrbTokens tokens, bool highContrast)
    {
        if (highContrast)
        {
            var text = SystemColors.WindowTextColor.ToRgba();
            return new ReadoutPalette(text, text, text, text, null, 0, false, Rgba.Transparent);
        }

        return new ReadoutPalette(tokens.ValueText, tokens.IdleValueText, tokens.SecondaryText, null, null, 0, true,
            tokens.Appearance == OrbAppearance.Dark ? Rgba.Black.WithAlpha(0.55) : Rgba.White.WithAlpha(0.75));
    }
}
