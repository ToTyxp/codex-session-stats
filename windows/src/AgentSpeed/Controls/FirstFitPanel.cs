using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using AgentSpeed.Presentation.Layout;

namespace AgentSpeed.Controls;

/// <summary>
/// Shows one of its children, the forms of one text from longest to shortest: the first whose natural width fits, else
/// the last, measured to the room (DESIGN 4b: shortened, never truncated). The others are arranged into an empty slot,
/// whose layout clip keeps them from being drawn or hit; accessibility sees one text element named by
/// <see cref="AutomationProperties.NameProperty"/>, whichever form is shown. The panel is as tall as its tallest form,
/// so the line keeps its height. A collapsed child is not offered.
/// </summary>
internal sealed class FirstFitPanel : Panel
{
    private static readonly Rect Hidden = new(0, 0, 0, 0);

    /// <summary>Index of the child on screen; -1 when there is none to show.</summary>
    public int ShownIndex { get; private set; } = -1;

    /// <summary>The natural width of the shortest form (the last offered one), as of the latest measure.</summary>
    public double MinimumWidth { get; private set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = InternalChildren;
        var widths = new double[children.Count];
        var height = 0.0;
        var natural = new Size(double.PositiveInfinity, availableSize.Height);
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            child.Measure(natural);
            widths[i] = child.Visibility == Visibility.Collapsed ? double.NaN : child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        var shown = FirstFit.Index(widths, availableSize.Width);
        var shortest = FirstFit.Index(widths, 0);
        MinimumWidth = shortest < 0 ? 0 : widths[shortest];
        if (shown != ShownIndex)
        {
            ShownIndex = shown;
            InvalidateArrange();
        }

        if (shown < 0)
        {
            return default;
        }

        var form = children[shown];
        if (!FirstFit.Fits(widths[shown], availableSize.Width))
        {
            form.Measure(availableSize);
        }

        return new Size(form.DesiredSize.Width, Math.Max(height, form.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = InternalChildren;
        for (var i = 0; i < children.Count; i++)
        {
            children[i].Arrange(i == ShownIndex ? new Rect(finalSize) : Hidden);
        }

        return finalSize;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new FirstFitPeer(this);

    private sealed class FirstFitPeer(FirstFitPanel owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override string GetClassNameCore() => nameof(FirstFitPanel);

        protected override string GetNameCore()
        {
            var name = AutomationProperties.GetName(Owner);
            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }

            var panel = (FirstFitPanel)Owner;
            return panel.InternalChildren.Count > 0 && CreatePeerForElement(panel.InternalChildren[0]) is { } full
                ? full.GetName()
                : string.Empty;
        }

        protected override List<AutomationPeer>? GetChildrenCore() => null;
    }
}
