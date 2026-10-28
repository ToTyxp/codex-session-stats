using System.Collections.Immutable;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Menus;
using AgentSpeed.Theme;

namespace AgentSpeed.Menus;

/// <summary>
/// An orb's source dots in a menu row's 16 × 16 icon box (<see cref="MenuDotLayout"/>). Unavailable sources are faded;
/// in a high-contrast theme the dots take the item's text color, unavailable ones as rings.
/// </summary>
internal sealed class MenuDots : FrameworkElement
{
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(MenuDots),
        new FrameworkPropertyMetadata(SystemColors.ControlTextBrush, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    private const double UnavailableAlpha = 0.35;

    public MenuDots(ImmutableArray<SourceDot> dots)
    {
        Dots = dots.IsDefault ? [] : dots;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    public ImmutableArray<SourceDot> Dots { get; }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(MenuDotLayout.Box, MenuDotLayout.Box);

    protected override void OnRender(DrawingContext dc)
    {
        var placements = MenuDotLayout.Layout(Dots.Length);
        var highContrast = SystemParameters.HighContrast;
        Pen? ring = null;
        for (var i = 0; i < placements.Count; i++)
        {
            var dot = Dots[i];
            var place = placements[i];
            var center = new Point(place.X, place.Y);
            var radius = place.Diameter / 2;
            if (!highContrast)
            {
                var color = HeatPalette.SourceColor(dot.Source).ToColor(dot.Available ? 1 : UnavailableAlpha);
                dc.DrawEllipse(MediaColors.Frozen(color), null, center, radius, radius);
            }
            else if (dot.Available)
            {
                dc.DrawEllipse(Foreground, null, center, radius, radius);
            }
            else
            {
                // Not frozen: freezing would freeze the shared theme brush with it.
                ring ??= new Pen(Foreground, 1);
                dc.DrawEllipse(null, ring, center, radius - 0.5, radius - 0.5);
            }
        }
    }
}
