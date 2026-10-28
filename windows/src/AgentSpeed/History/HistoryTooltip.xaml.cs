using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AgentSpeed.History;

/// <summary>
/// A label/value line of the tooltip card; source lines carry a color dot and are indented; a note line is small
/// explanatory text under the line before it (its label is the whole text).
/// </summary>
internal sealed record TooltipLine(string Label, string Value, Brush? Dot = null, bool Strong = false, bool Indented = false,
    bool IsNote = false)
{
    public bool HasDot => Dot is not null;

    public bool IsValue => !IsNote;

    public FontWeight Weight => Strong ? FontWeights.SemiBold : FontWeights.Normal;

    public Thickness Margin => IsNote ? new(0, 0, 0, 3) : new(Indented ? 2 : 0, Indented ? 1 : 2, 0, Indented ? 1 : 2);
}

/// <summary>One of a day's top models: name, average speed (empty when not shown), share of the output and cost.</summary>
internal sealed record TooltipShare(string Name, double Share, string Percent, string Speed = "", string Cost = "")
{
    public bool HasSpeed => !string.IsNullOrEmpty(Speed);

    public bool HasCost => !string.IsNullOrEmpty(Cost);
}

internal sealed record TooltipModel(
    string Title,
    string Badge,
    IReadOnlyList<TooltipLine> Lines,
    string Note,
    string ModelsTitle,
    IReadOnlyList<TooltipShare> Models)
{
    public bool HasBadge => !string.IsNullOrEmpty(Badge);

    public bool HasNote => !string.IsNullOrEmpty(Note);

    public bool HasModels => Models.Count > 0;
}

/// <summary>
/// The chart's hover card (DESIGN 4b, item 3). It fades in, then glides between bars; with Reduce Motion it only
/// fades. It never takes the pointer.
/// </summary>
internal sealed partial class HistoryTooltip : UserControl
{
    private static readonly Duration Fade = TimeSpan.FromMilliseconds(120);
    private static readonly Duration Glide = TimeSpan.FromMilliseconds(120);
    private bool _shown;

    public HistoryTooltip()
    {
        InitializeComponent();
    }

    public bool IsShown => _shown;

    /// <summary>Shows <paramref name="model"/> with its card's top-left corner at <paramref name="x"/>, <paramref name="y"/>.</summary>
    public void Show(TooltipModel model, double x, double y, bool reduceMotion)
    {
        DataContext = model;
        Visibility = Visibility.Visible;
        if (!_shown || reduceMotion)
        {
            BeginAnimation(Canvas.LeftProperty, null);
            BeginAnimation(Canvas.TopProperty, null);
            Canvas.SetLeft(this, x);
            Canvas.SetTop(this, y);
        }
        else
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(x, Glide) { EasingFunction = ease });
            BeginAnimation(Canvas.TopProperty, new DoubleAnimation(y, Glide) { EasingFunction = ease });
        }

        if (!_shown)
        {
            _shown = true;
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, Fade) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        }
    }

    public void Hide()
    {
        if (!_shown)
        {
            return;
        }

        _shown = false;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(100));
        fade.Completed += (_, _) =>
        {
            if (!_shown)
            {
                Visibility = Visibility.Collapsed;
            }
        };
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>The card's size for <paramref name="model"/>, measured before it is placed.</summary>
    public Size MeasureCard(TooltipModel model)
    {
        DataContext = model;
        var visibility = Visibility;
        Visibility = Visibility.Visible;
        Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = DesiredSize;
        if (!_shown)
        {
            Visibility = visibility;
        }

        return size;
    }
}
