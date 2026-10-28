using System.Windows;
using System.Windows.Media.Animation;
using AgentSpeed.Presentation.Titles;

namespace AgentSpeed.Settings.TitleThemes;

/// <summary>
/// The 0.35 s settings spring (DESIGN 5, Title themes) as an easing curve; animations using it last
/// <see cref="Duration"/> so the curve has settled when they end.
/// </summary>
internal sealed class SpringEase : EasingFunctionBase
{
    public static readonly Duration Duration = new(TimeSpan.FromSeconds(TitleEditorMotion.SettleTime()));

    private static readonly double Seconds = TitleEditorMotion.SettleTime();

    public SpringEase()
    {
        EasingMode = EasingMode.EaseIn;
    }

    protected override double EaseInCore(double normalizedTime) =>
        normalizedTime >= 1 ? 1 : TitleEditorMotion.Spring(normalizedTime * Seconds);

    protected override Freezable CreateInstanceCore() => new SpringEase();
}
