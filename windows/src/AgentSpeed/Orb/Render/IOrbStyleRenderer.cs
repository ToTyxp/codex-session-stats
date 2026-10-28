using System.Windows.Media;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Orb;
using AgentSpeed.Presentation.Orb.Styles;

namespace AgentSpeed.Orb.Render;

/// <summary>Body tokens a style overrides (DESIGN 2a: liquid and reactor bodies, no lens highlight).</summary>
/// <param name="Top">Gradient top, or null for the appearance token.</param>
/// <param name="Bottom">Gradient bottom, or null for the appearance token.</param>
/// <param name="Lens">Draws the faint top-left lens highlight.</param>
internal readonly record struct BodyVariant(Rgba? Top, Rgba? Bottom, bool Lens)
{
    public static BodyVariant Standard => new(null, null, true);
}

/// <summary>
/// Draws one style's instrument between the shared body and the shared readout (DESIGN 2a). A renderer keeps retained,
/// cached visuals: it re-records a visual only when the model's change keys say so, and otherwise only moves (transforms)
/// and fades (opacity) them, and splats its particles into a <see cref="ParticleBitmap"/>.
/// </summary>
internal interface IOrbStyleRenderer
{
    OrbStyle Style { get; }

    IOrbStyleModel Model { get; }

    /// <summary>The instrument, placed above the body and below the readout.</summary>
    ContainerVisual Root { get; }

    /// <summary>Optional layer above the readout (null for most styles).</summary>
    ContainerVisual? Overlay { get; }

    OrbReadoutLayout Readout { get; }

    BodyVariant Body { get; }

    /// <summary>The shared aura, response wave and new-peak rays (all styles except <c>minimal</c>).</summary>
    bool SharedEffects { get; }

    /// <summary>
    /// The liquid style splits the readout at its surface (y in S below the center, null for none) and draws the part
    /// below it with <see cref="BelowPalette"/>.
    /// </summary>
    double? ReadoutSplit { get; }

    ReadoutPalette? BelowPalette { get; }

    /// <summary>Rebuilds cached geometry and brushes (size, appearance, effects, DPI).</summary>
    void Configure(OrbRenderContext context);

    /// <summary>Updates the visuals for this frame (after the model's Update).</summary>
    void Render(OrbScene scene, in OrbFrame frame);
}
