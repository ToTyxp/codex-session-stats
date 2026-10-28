using AgentSpeed.Presentation.Colors;
using AgentSpeed.Orb.Hero;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Hero;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Presentation.Settings;

namespace AgentSpeed.Orb;

/// <summary>Visual configuration of an orb instance (floating window or dashboard hero).</summary>
internal sealed record OrbConfig(
    double Diameter,
    OrbAppearance Appearance,
    double BodyOpacity,
    EffectsPolicy Effects,
    bool HighContrast,
    bool SolidBody,
    bool PulsesEnabled,
    bool PeaksEnabled,
    double Redline,
    string UnitText,
    string PeakText,
    IReadOnlyList<string> TitleLabels)
{
    public static OrbConfig Default { get; } = new(
        AppSettings.DefaultOrbSize, OrbAppearance.Dark, 1, new EffectsPolicy(EffectsIntensity.Standard), false, false,
        true, true, HeatMath.DefaultRedline, "tok/s", "NEW PEAK",
        ["IDLE", "WALKING", "BICYCLE", "E-BIKE", "MOTORCYCLE", "CAR", "SPORTS CAR", "BULLET TRAIN", "RACE CAR", "MAGLEV", "JETLINER",
            "FIGHTER JET", "HYPERSONIC", "ROCKET"]);

    /// <summary>The dashboard hero's vehicle layer (embedded style, DESIGN 4c); null for the floating orb.</summary>
    public HeroConfig? Hero { get; init; }

    /// <summary>The label for a speed title, indexed by <see cref="SpeedTitle"/>.</summary>
    public string TitleLabel(SpeedTitle title) => (int)title < TitleLabels.Count ? TitleLabels[(int)title] : string.Empty;
}

/// <summary>Vehicle layer settings: its own effects level, the vehicle parked at launch and the glow technique.</summary>
internal sealed record HeroConfig(HeroOptions Options, string? ParkedKey, HeroGlowMode Glow)
{
    /// <summary>The General title theme's art: null shows the vehicle (Vehicles and the sets based on it).</summary>
    public HeroTitles? Titles { get; init; }
}

/// <summary>
/// The hero title badge (DESIGN 4c, Hero title badge) of a title theme other than Vehicles: the label of every level as
/// the badge draws it (idle first, indexed by <see cref="SpeedTitle"/>; Latin uppercase in English) and the language
/// they are laid out in.
/// </summary>
internal sealed record HeroTitles(IReadOnlyList<string> Labels, string LanguageCode)
{
    public string Label(SpeedTitle title) => (int)title < Labels.Count ? Labels[(int)title] : string.Empty;

    public bool Equals(HeroTitles? other) =>
        ReferenceEquals(this, other) || (other is not null && LanguageCode == other.LanguageCode && Labels.SequenceEqual(other.Labels));

    public override int GetHashCode() => HashCode.Combine(LanguageCode, Labels.Count);
}
