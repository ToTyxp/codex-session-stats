using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace AgentSpeed.Dashboard;

/// <summary>What the trailing live-speed slot shows (DESIGN section 4, items 3–4).</summary>
internal enum LiveValueKind
{
    /// <summary>The speed as digits.</summary>
    Number,

    /// <summary>Nothing measurable (idle, or generating before a value exists): a tertiary en dash.</summary>
    Dash,

    /// <summary>Waiting on tools: a slowly breathing hourglass.</summary>
    Waiting,
}

/// <summary>
/// The right-aligned live value of an agent or source row. Below 0.05 tok/s it never shows a number: the digits
/// crossfade (0.2 s) to a dash or an hourglass that sits in the same slot and on the same line, so the row never changes
/// width or height. The hourglass breathes (2 s, 55–100 %) only while visible and while motion is allowed.
/// </summary>
internal sealed class LiveValue : Grid
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(LiveValue),
        new PropertyMetadata(string.Empty, (d, e) => ((LiveValue)d)._number.Text = (string)e.NewValue ?? string.Empty));

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(LiveValueKind), typeof(LiveValue),
        new PropertyMetadata(LiveValueKind.Number, (d, _) => ((LiveValue)d).ShowKind(animate: true)));

    public static readonly DependencyProperty ReduceMotionProperty = DependencyProperty.Register(
        nameof(ReduceMotion), typeof(bool), typeof(LiveValue),
        new PropertyMetadata(false, (d, _) => ((LiveValue)d).UpdateBreathing()));

    private static readonly Duration Crossfade = new(TimeSpan.FromMilliseconds(200));

    /// <summary>A Fluent-weight hourglass on an 11 × 11 grid: two bulbs between end caps, sand settled at the bottom.</summary>
    private static readonly Geometry HourglassOutline = Freeze(Geometry.Parse(
        "M1.6,0.7 L9.4,0.7 M1.6,10.3 L9.4,10.3 " +
        "M2.6,0.7 C2.6,3.6 5.5,4.3 5.5,5.5 C5.5,6.7 2.6,7.4 2.6,10.3 " +
        "M8.4,0.7 C8.4,3.6 5.5,4.3 5.5,5.5 C5.5,6.7 8.4,7.4 8.4,10.3"));

    private static readonly Geometry HourglassSand = Freeze(Geometry.Parse("M3.7,9.7 L7.3,9.7 L5.5,7.7 Z"));

    private readonly TextBlock _number = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _dash = new() { Text = "–", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _hourglassSlot = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly Canvas _hourglass = new() { Width = 11, Height = 11 };
    private bool _breathing;

    public LiveValue()
    {
        Typography.SetNumeralAlignment(_number, FontNumeralAlignment.Tabular);
        _dash.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");

        var outline = new Path { Data = HourglassOutline, StrokeThickness = 1.1, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        outline.SetResourceReference(Shape.StrokeProperty, "TextFillColorSecondaryBrush");
        var sand = new Path { Data = HourglassSand, Opacity = 0.75 };
        sand.SetResourceReference(Shape.FillProperty, "TextFillColorSecondaryBrush");
        _hourglass.Children.Add(sand);
        _hourglass.Children.Add(outline);
        // Optically on the digits' line: the glyph's center sits where a digit's middle is, not at the line box center.
        _hourglassSlot.Margin = new Thickness(0, 0, 1, 1);
        _hourglassSlot.Children.Add(_hourglass);

        Children.Add(_number);
        Children.Add(_dash);
        Children.Add(_hourglassSlot);
        ShowKind(animate: false);

        // A repeating animation keeps WPF rendering even in a hidden window, so the hourglass breathes only while shown.
        IsVisibleChanged += (_, _) => UpdateBreathing();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public LiveValueKind Kind
    {
        get => (LiveValueKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public bool ReduceMotion
    {
        get => (bool)GetValue(ReduceMotionProperty);
        set => SetValue(ReduceMotionProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new LiveValuePeer(this);

    private void ShowKind(bool animate)
    {
        var kind = Kind;
        animate &= IsLoaded && IsVisible;
        Fade(_number, kind == LiveValueKind.Number, animate);
        Fade(_dash, kind == LiveValueKind.Dash, animate);
        Fade(_hourglassSlot, kind == LiveValueKind.Waiting, animate);
        UpdateBreathing();
    }

    private static void Fade(UIElement element, bool shown, bool animate)
    {
        var to = shown ? 1.0 : 0.0;
        if (!animate)
        {
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = to;
            return;
        }

        var from = element.Opacity;
        if (Math.Abs(from - to) < 0.001)
        {
            return;
        }

        var fade = new DoubleAnimation(from, to, Crossfade)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        };
        element.Opacity = to;
        element.BeginAnimation(OpacityProperty, fade);
    }

    private void UpdateBreathing()
    {
        var breathe = Kind == LiveValueKind.Waiting && IsVisible && !ReduceMotion;
        if (breathe == _breathing)
        {
            return;
        }

        _breathing = breathe;
        if (!breathe)
        {
            _hourglass.BeginAnimation(OpacityProperty, null);
            _hourglass.Opacity = 1;
            return;
        }

        var breath = new DoubleAnimation(1, 0.55, new Duration(TimeSpan.FromSeconds(1)))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        _hourglass.BeginAnimation(OpacityProperty, breath);
    }

    private static Geometry Freeze(Geometry geometry)
    {
        geometry.Freeze();
        return geometry;
    }

    /// <summary>One text element named by <c>AutomationProperties.Name</c> (the speed, or the phase name).</summary>
    private sealed class LiveValuePeer(LiveValue owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override string GetClassNameCore() => "LiveValue";

        protected override string GetNameCore()
        {
            var name = AutomationProperties.GetName(Owner);
            return string.IsNullOrEmpty(name) ? ((LiveValue)Owner).Text : name;
        }

        protected override List<AutomationPeer>? GetChildrenCore() => null;
    }
}
