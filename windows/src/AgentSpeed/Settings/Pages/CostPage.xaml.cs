using System.Runtime.CompilerServices;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace AgentSpeed.Settings.Pages;

/// <summary>Settings → 費用 (DESIGN 9): currency, live value unit, subscriptions and the price table.</summary>
internal sealed partial class CostPage : UserControl
{
    private readonly ConditionalWeakTable<TextBlock, string> _announced = new();

    public CostPage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Status lines marked as polite live regions announce new text (rate failures, check results) once the layout has
    /// settled, only while they are on screen and only when the text actually changed.
    /// </summary>
    private void OnLiveTextUpdated(object? sender, DataTransferEventArgs e)
    {
        if (sender is not TextBlock block)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            var text = block.Text;
            if (_announced.TryGetValue(block, out var last) && last == text)
            {
                return;
            }

            _announced.AddOrUpdate(block, text);
            if (block.IsVisible && text.Length > 0 && AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
            {
                (UIElementAutomationPeer.FromElement(block) ?? UIElementAutomationPeer.CreatePeerForElement(block))
                    ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }
        }, DispatcherPriority.Background);
    }
}
