using AgentSpeed.Core;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Presentation.Orb;

namespace AgentSpeed.Orb.Render;

/// <summary>The visual configuration of one floating orb (or a thumbnail / style preview).</summary>
/// <param name="Size">Diameter S in DIP.</param>
/// <param name="BodyOpacity">The orb's opacity setting (0.4..1) applied to the body.</param>
/// <param name="Redline">The orb's effective redline for the selected metric.</param>
/// <param name="Sources">Tracked sources for the source bezel; null for all sources (plain rim).</param>
/// <param name="Unavailable">Tracked sources that are disabled or not detected (dashed bezel arcs).</param>
internal sealed record OrbSurfaceConfig(
    OrbStyle Style,
    double Size,
    OrbAppearance Appearance,
    EffectsPolicy Effects,
    bool HighContrast,
    bool SolidBody,
    double BodyOpacity,
    bool PulsesEnabled,
    bool PeaksEnabled,
    double Redline,
    ReadoutTexts Texts,
    IReadOnlyList<SourceKind>? Sources,
    SourceSet Unavailable);
