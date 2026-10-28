using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using AgentSpeed.Core;

namespace AgentSpeed.Dashboard;

/// <summary>Agent phase indicator: shimmering dots while generating, a wrench for tools, a grey dot when idle.</summary>
internal sealed class PhaseBadge : Grid
{
    public static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(
        nameof(Phase), typeof(AgentPhase), typeof(PhaseBadge),
        new PropertyMetadata(AgentPhase.Idle, (d, _) => ((PhaseBadge)d).Rebuild()));

    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(Color), typeof(PhaseBadge),
        new PropertyMetadata(Colors.Gray, (d, _) => ((PhaseBadge)d).Rebuild()));

    public static readonly DependencyProperty ReduceMotionProperty = DependencyProperty.Register(
        nameof(ReduceMotion), typeof(bool), typeof(PhaseBadge),
        new PropertyMetadata(false, (d, _) => ((PhaseBadge)d).Rebuild()));

    public PhaseBadge()
    {
        Width = 26;
        Height = 16;
        // A repeating animation keeps WPF rendering at 60 fps even in a hidden window, so it runs only while visible.
        IsVisibleChanged += (_, _) => Rebuild();
        Rebuild();
    }

    public AgentPhase Phase
    {
        get => (AgentPhase)GetValue(PhaseProperty);
        set => SetValue(PhaseProperty, value);
    }

    public Color Color
    {
        get => (Color)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public bool ReduceMotion
    {
        get => (bool)GetValue(ReduceMotionProperty);
        set => SetValue(ReduceMotionProperty, value);
    }

    private void Rebuild()
    {
        foreach (var panel in Children.OfType<StackPanel>())
        {
            foreach (var dot in panel.Children.OfType<UIElement>())
            {
                dot.BeginAnimation(OpacityProperty, null);
            }
        }

        Children.Clear();
        switch (Phase)
        {
            case AgentPhase.Generating:
                var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var brush = new SolidColorBrush(Color);
                brush.Freeze();
                for (var i = 0; i < 3; i++)
                {
                    var dot = new Ellipse { Width = 4, Height = 4, Fill = brush, Margin = new Thickness(1.5, 0, 1.5, 0) };
                    if (!ReduceMotion && IsVisible)
                    {
                        var animation = new DoubleAnimationUsingKeyFrames
                        {
                            RepeatBehavior = RepeatBehavior.Forever,
                            Duration = TimeSpan.FromSeconds(1.2),
                            BeginTime = TimeSpan.FromSeconds(i * 0.18),
                        };
                        animation.KeyFrames.Add(new EasingDoubleKeyFrame(0.3, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                        animation.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.35)), new SineEase()));
                        animation.KeyFrames.Add(new EasingDoubleKeyFrame(0.3, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.8)), new SineEase()));
                        animation.KeyFrames.Add(new EasingDoubleKeyFrame(0.3, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.2))));
                        dot.BeginAnimation(OpacityProperty, animation);
                    }

                    panel.Children.Add(dot);
                }

                Children.Add(panel);
                break;
            case AgentPhase.Tools:
                var wrench = new TextBlock
                {
                    Text = "",
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                wrench.SetResourceReference(TextBlock.FontFamilyProperty, Theme.AppFonts.SymbolFontKey);
                wrench.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
                Children.Add(wrench);
                break;
            default:
                var idle = new Ellipse { Width = 6, Height = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                idle.SetResourceReference(Shape.FillProperty, "TextFillColorTertiaryBrush");
                Children.Add(idle);
                break;
        }
    }
}
