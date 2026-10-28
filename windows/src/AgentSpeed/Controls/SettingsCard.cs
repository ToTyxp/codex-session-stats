using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace AgentSpeed.Controls;

/// <summary>Windows 11 settings row: icon, header, description and a control on the right.</summary>
internal sealed class SettingsCard : ContentControl
{
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingsCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingsCard), new PropertyMetadata(null));

    /// <summary>Places the control on its own full-width line below the text, leaving descriptions the whole row.</summary>
    public static readonly DependencyProperty ContentBelowProperty =
        DependencyProperty.Register(nameof(ContentBelow), typeof(bool), typeof(SettingsCard), new PropertyMetadata(false));

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(SettingsCard), new PropertyMetadata(null));

    static SettingsCard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SettingsCard), new FrameworkPropertyMetadata(typeof(SettingsCard)));
    }

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string? Description
    {
        get => (string?)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public bool ContentBelow
    {
        get => (bool)GetValue(ContentBelowProperty);
        set => SetValue(ContentBelowProperty, value);
    }

    public string? Icon
    {
        get => (string?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SettingsCardPeer(this);

    private sealed class SettingsCardPeer(SettingsCard owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetNameCore() => ((SettingsCard)Owner).Header;

        protected override string GetHelpTextCore() => ((SettingsCard)Owner).Description ?? string.Empty;

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
    }
}
