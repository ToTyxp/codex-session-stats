using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AgentSpeed.Reporting;

internal sealed partial class ReportProfileCard : UserControl
{
    public static readonly DependencyProperty ManageDevicesCommandProperty = DependencyProperty.Register(nameof(ManageDevicesCommand), typeof(ICommand), typeof(ReportProfileCard));
    public ICommand? ManageDevicesCommand { get => (ICommand?)GetValue(ManageDevicesCommandProperty); set => SetValue(ManageDevicesCommandProperty, value); }
    public ReportProfileCard() => InitializeComponent();
    private void OnNameGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    { if (DataContext is ReportProfileViewModel profile) profile.NameEditing = true; }
    private void OnNameLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    { if (DataContext is ReportProfileViewModel profile) profile.NameEditing = false; }
    private void OnNameKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not ReportProfileViewModel profile) return;
        if (e.Key == Key.Enter && profile.SaveNameCommand.CanExecute(null)) { profile.SaveNameCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Escape) { profile.RestoreName(); ProfileNameBox.SelectAll(); e.Handled = true; }
    }
}
