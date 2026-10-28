using System.Windows;
using System.Windows.Controls;
using AgentSpeed.Controls;

namespace AgentSpeed.History;

/// <summary>
/// The history footer's one line (DESIGN 4b): the notes in order from the left, then the trailing ones (<see
/// cref="IsTrailingProperty"/>) and the last child at the right edge. Each note is measured with the room the ones before it
/// leave, so the last child — the privacy note — gives way first; but the room of its shortest form (<see
/// cref="FirstFitPanel.MinimumWidth"/>, the lock alone) is set aside before the others are measured, so it is never cut.
/// </summary>
internal sealed class FooterLinePanel : Panel
{
    public static readonly DependencyProperty IsTrailingProperty = DependencyProperty.RegisterAttached(
        "IsTrailing", typeof(bool), typeof(FooterLinePanel), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static bool GetIsTrailing(UIElement element) => (bool)element.GetValue(IsTrailingProperty);

    public static void SetIsTrailing(UIElement element, bool value) => element.SetValue(IsTrailingProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = InternalChildren;
        if (children.Count == 0)
        {
            return default;
        }

        var width = availableSize.Width;
        var last = children[^1];
        last.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        var reserve = last.Visibility == Visibility.Collapsed ? 0 : last is FirstFitPanel yielding ? yielding.MinimumWidth : last.DesiredSize.Width;
        var used = 0.0;
        var height = 0.0;
        for (var i = 0; i < children.Count - 1; i++)
        {
            var child = children[i];
            child.Measure(new Size(Math.Max(0, width - reserve - used), availableSize.Height));
            used += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        last.Measure(new Size(Math.Max(reserve, width - used), availableSize.Height));
        height = Math.Max(height, last.DesiredSize.Height);
        return new Size(double.IsFinite(width) ? Math.Min(width, used + last.DesiredSize.Width) : used + last.DesiredSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = InternalChildren;
        if (children.Count == 0)
        {
            return finalSize;
        }

        var left = 0.0;
        for (var i = 0; i < children.Count - 1; i++)
        {
            if (!GetIsTrailing(children[i]))
            {
                left = Place(children[i], left, finalSize.Height);
            }
        }

        var right = finalSize.Width;
        for (var i = children.Count - 1; i >= 0; i--)
        {
            var child = children[i];
            if (i == children.Count - 1 || GetIsTrailing(child))
            {
                var w = Math.Min(child.DesiredSize.Width, Math.Max(0, right - left));
                right -= w;
                child.Arrange(new Rect(right, 0, w, finalSize.Height));
            }
        }

        return finalSize;
    }

    private static double Place(UIElement child, double x, double height)
    {
        child.Arrange(new Rect(x, 0, child.DesiredSize.Width, height));
        return x + child.DesiredSize.Width;
    }
}
