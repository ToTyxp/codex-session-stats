using System.Windows;

namespace AgentSpeed.Menus;

/// <summary>Attached values the aligned menu item template (MenuStyles.xaml) reads.</summary>
internal static class MenuChrome
{
    /// <summary>Reserve the leading icon column even without an icon (set on every item of a level that has icons).</summary>
    public static readonly DependencyProperty IconColumnProperty = DependencyProperty.RegisterAttached(
        "IconColumn", typeof(bool), typeof(MenuChrome), new FrameworkPropertyMetadata(false));

    /// <summary>Reserve the trailing check / chevron column (set on every item of a level with toggles or submenus).</summary>
    public static readonly DependencyProperty TrailingColumnProperty = DependencyProperty.RegisterAttached(
        "TrailingColumn", typeof(bool), typeof(MenuChrome), new FrameworkPropertyMetadata(false));

    /// <summary>Secondary text after the label (for example why a source is unavailable).</summary>
    public static readonly DependencyProperty DetailProperty = DependencyProperty.RegisterAttached(
        "Detail", typeof(string), typeof(MenuChrome), new FrameworkPropertyMetadata(null));

    /// <summary>A menu's title row: secondary semibold text, never highlighted.</summary>
    public static readonly DependencyProperty IsHeaderProperty = DependencyProperty.RegisterAttached(
        "IsHeader", typeof(bool), typeof(MenuChrome), new FrameworkPropertyMetadata(false));

    /// <summary>Bound by the template to <see cref="SystemParameters.HighContrastKey"/>.</summary>
    public static readonly DependencyProperty HighContrastProperty = DependencyProperty.RegisterAttached(
        "HighContrast", typeof(bool), typeof(MenuChrome), new FrameworkPropertyMetadata(false));

    /// <summary>Bound by the template to <see cref="SystemParameters.ClientAreaAnimationKey"/> (off with Reduce Motion).</summary>
    public static readonly DependencyProperty AnimatesProperty = DependencyProperty.RegisterAttached(
        "Animates", typeof(bool), typeof(MenuChrome), new FrameworkPropertyMetadata(false));

    public static bool GetIconColumn(DependencyObject element) => (bool)element.GetValue(IconColumnProperty);

    public static void SetIconColumn(DependencyObject element, bool value) => element.SetValue(IconColumnProperty, value);

    public static bool GetTrailingColumn(DependencyObject element) => (bool)element.GetValue(TrailingColumnProperty);

    public static void SetTrailingColumn(DependencyObject element, bool value) => element.SetValue(TrailingColumnProperty, value);

    public static string? GetDetail(DependencyObject element) => (string?)element.GetValue(DetailProperty);

    public static void SetDetail(DependencyObject element, string? value) => element.SetValue(DetailProperty, value);

    public static bool GetIsHeader(DependencyObject element) => (bool)element.GetValue(IsHeaderProperty);

    public static void SetIsHeader(DependencyObject element, bool value) => element.SetValue(IsHeaderProperty, value);

    public static bool GetHighContrast(DependencyObject element) => (bool)element.GetValue(HighContrastProperty);

    public static void SetHighContrast(DependencyObject element, bool value) => element.SetValue(HighContrastProperty, value);

    public static bool GetAnimates(DependencyObject element) => (bool)element.GetValue(AnimatesProperty);

    public static void SetAnimates(DependencyObject element, bool value) => element.SetValue(AnimatesProperty, value);
}
