using System.Windows;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Theme;

namespace AgentSpeed.Orb.Render;

/// <summary>Everything a renderer needs that changes only with the size, the appearance, the effects or the DPI.</summary>
/// <param name="Size">Diameter S in DIP.</param>
/// <param name="Scale">Device pixels per DIP.</param>
/// <param name="BodyOpacity">The orb's opacity setting applied to the body (0.4..1).</param>
/// <param name="SolidBody">Reduce Transparency: no translucent body.</param>
internal sealed record OrbRenderContext(
    double Size,
    double Scale,
    OrbTokens Tokens,
    EffectsPolicy Effects,
    bool HighContrast,
    bool SolidBody,
    double BodyOpacity,
    BrushCache Brushes)
{
    /// <summary>The disc center inside the 1.5 S window.</summary>
    public Point Center => new(Size * 0.75, Size * 0.75);

    public bool Dark => Tokens.Appearance == OrbAppearance.Dark;

    /// <summary>The window's square in DIP.</summary>
    public Rect Bounds => new(0, 0, Size * 1.5, Size * 1.5);
}
