using System.Windows;
using AgentSpeed.Theme;

namespace AgentSpeed.Reporting;

internal sealed partial class ReportProfileWindow : Window
{
    public ReportProfileWindow(ReportProfileViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        if (AppIconSource.Frame(32) is { } icon) Icon = icon;
        MaxHeight = SystemParameters.WorkArea.Height * 0.92;
        Loaded += (_, _) => model.BeginDeviceManagement();
        Closed += (_, _) => model.EndDeviceManagement();
    }
}
