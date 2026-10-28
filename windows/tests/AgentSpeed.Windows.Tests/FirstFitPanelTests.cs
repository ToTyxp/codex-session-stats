using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using AgentSpeed.Controls;

namespace AgentSpeed.Windows.Tests;

/// <summary>DESIGN 4b: the panel shows one form of a text, and the others are neither drawn, hit nor seen by accessibility.</summary>
public class FirstFitPanelTests
{
    [Fact]
    public void ItShowsTheFirstFormThatFits()
    {
        var (index, width) = OnStaThread(() =>
        {
            var panel = Panel(420, 130, 17);
            Layout(panel, 200);
            return (panel.ShownIndex, panel.DesiredSize.Width);
        });
        Assert.Equal(1, index);
        Assert.Equal(130, width);
    }

    [Fact]
    public void ItShowsTheLastFormMeasuredToTheRoomWhenNoneFits()
    {
        var (index, width) = OnStaThread(() =>
        {
            var panel = Panel(420, 130, 17);
            Layout(panel, 10);
            return (panel.ShownIndex, panel.DesiredSize.Width);
        });
        Assert.Equal(2, index);
        Assert.Equal(10, width);
    }

    [Fact]
    public void ItKeepsTheHeightOfItsTallestForm()
    {
        var height = OnStaThread(() =>
        {
            var panel = Panel(420, 130, 17);
            ((FrameworkElement)panel.Children[0]).Height = 20;
            Layout(panel, 200);
            return panel.DesiredSize.Height;
        });
        Assert.Equal(20, height);
    }

    [Fact]
    public void ACollapsedFormIsNotOffered()
    {
        var index = OnStaThread(() =>
        {
            var panel = Panel(420, 130, 17);
            panel.Children[1].Visibility = Visibility.Collapsed;
            Layout(panel, 200);
            return panel.ShownIndex;
        });
        Assert.Equal(2, index);
    }

    [Fact]
    public void HiddenFormsAreClippedAwayAndNeverHit()
    {
        var (clips, hits) = OnStaThread(() =>
        {
            var panel = Panel(420, 130, 17);
            Layout(panel, 200);
            var clipped = panel.Children.Cast<UIElement>().Select(c => VisualTreeHelper.GetClip(c)?.Bounds.Size ?? Size.Empty).ToArray();
            var hit = new List<int>();
            foreach (var x in new[] { 1.0, 16, 60, 129 })
            {
                VisualTreeHelper.HitTest(panel, null, result =>
                {
                    if (result.VisualHit is UIElement element && panel.Children.IndexOf(element) is var i and >= 0)
                    {
                        hit.Add(i);
                    }

                    return HitTestResultBehavior.Continue;
                }, new PointHitTestParameters(new Point(x, 8)));
            }

            return (clipped, hit.Distinct().ToArray());
        });
        Assert.Equal(new Size(0, 0), clips[0]);
        Assert.Equal(new Size(0, 0), clips[2]);
        Assert.Equal([1], hits);
    }

    [Fact]
    public void AccessibilitySeesOneTextNamedWithTheFullForm()
    {
        var (type, name, children, fallback) = OnStaThread(() =>
        {
            var panel = Panel(420, 130, 17);
            AutomationProperties.SetName(panel, "Only daily totals are kept");
            Layout(panel, 10);
            var peer = UIElementAutomationPeer.CreatePeerForElement(panel);
            var unnamed = new FirstFitPanel { Children = { new TextBlock { Text = "Full" }, new TextBlock { Text = "Short" } } };
            Layout(unnamed, 1);
            return (peer.GetAutomationControlType(), peer.GetName(), peer.GetChildren()?.Count ?? 0,
                UIElementAutomationPeer.CreatePeerForElement(unnamed).GetName());
        });
        Assert.Equal(AutomationControlType.Text, type);
        Assert.Equal("Only daily totals are kept", name);
        Assert.Equal(0, children);
        Assert.Equal("Full", fallback);
    }

    private static FirstFitPanel Panel(params double[] widths)
    {
        var panel = new FirstFitPanel();
        foreach (var width in widths)
        {
            panel.Children.Add(new Border { Width = width, Height = 16, Background = Brushes.Black });
        }

        return panel;
    }

    private static void Layout(FrameworkElement element, double width)
    {
        element.Measure(new Size(width, double.PositiveInfinity));
        element.Arrange(new Rect(new Point(), element.DesiredSize));
    }

    private static T OnStaThread<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return error is null ? result : throw new InvalidOperationException("Layout failed.", error);
    }
}
