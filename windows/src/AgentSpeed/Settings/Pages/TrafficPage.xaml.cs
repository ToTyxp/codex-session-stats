using AgentSpeed.Controls;

namespace AgentSpeed.Settings.Pages;

internal sealed partial class TrafficPage
{
    public TrafficPage()
    {
        InitializeComponent();
        if (ShieldIcon.Small() is { } shield)
        {
            Shield.Source = shield;
        }
        else
        {
            Shield.Visibility = System.Windows.Visibility.Collapsed;
        }
    }
}
