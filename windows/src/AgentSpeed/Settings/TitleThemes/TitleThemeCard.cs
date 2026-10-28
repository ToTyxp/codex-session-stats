using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace AgentSpeed.Settings.TitleThemes;

/// <summary>
/// A theme card (DESIGN 5, Title themes): a radio button whose checked state always comes from the view model, so a
/// click, Space or a screen reader's Select only asks for the selection (<see cref="System.Windows.Controls.Primitives.ButtonBase.Command"/>)
/// and the card follows the stored setting.
/// </summary>
internal sealed class TitleThemeCard : RadioButton
{
    public static readonly DependencyProperty IsCustomProperty = DependencyProperty.Register(nameof(IsCustom), typeof(bool),
        typeof(TitleThemeCard), new PropertyMetadata(false, (d, _) => ((TitleThemeCard)d).UpdateShowsMore()));

    public static readonly DependencyProperty MoreTextProperty = DependencyProperty.Register(nameof(MoreText), typeof(string),
        typeof(TitleThemeCard), new PropertyMetadata(string.Empty));

    private static readonly DependencyPropertyKey ShowsMorePropertyKey = DependencyProperty.RegisterReadOnly(nameof(ShowsMore), typeof(bool),
        typeof(TitleThemeCard), new PropertyMetadata(false));

    public static readonly DependencyProperty ShowsMoreProperty = ShowsMorePropertyKey.DependencyProperty;

    public TitleThemeCard()
    {
        MouseEnter += (_, _) => UpdateShowsMore();
        MouseLeave += (_, _) => UpdateShowsMore();
        IsKeyboardFocusWithinChanged += (_, _) => UpdateShowsMore();
    }

    /// <summary>A custom set: the card offers the <c>⋯</c> button.</summary>
    public bool IsCustom
    {
        get => (bool)GetValue(IsCustomProperty);
        set => SetValue(IsCustomProperty, value);
    }

    /// <summary>The <c>⋯</c> button's name (<c>titles.custom.more.a11y</c>).</summary>
    public string MoreText
    {
        get => (string)GetValue(MoreTextProperty);
        set => SetValue(MoreTextProperty, value);
    }

    /// <summary>The <c>⋯</c> button shows: a custom card under the pointer, focused or selected.</summary>
    public bool ShowsMore => (bool)GetValue(ShowsMoreProperty);

    protected override void OnToggle()
    {
        // The view model decides; the binding then checks this card.
    }

    protected override void OnChecked(RoutedEventArgs e)
    {
        base.OnChecked(e);
        UpdateShowsMore();
    }

    protected override void OnUnchecked(RoutedEventArgs e)
    {
        base.OnUnchecked(e);
        UpdateShowsMore();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new CardPeer(this);

    /// <summary>Selects through the command, as a click does (UI Automation SelectionItem.Select).</summary>
    internal void RequestSelect()
    {
        if (Command is { } command && command.CanExecute(CommandParameter))
        {
            command.Execute(CommandParameter);
        }
    }

    private void UpdateShowsMore() =>
        SetValue(ShowsMorePropertyKey, IsCustom && (IsMouseOver || IsKeyboardFocusWithin || IsChecked == true));

    private sealed class CardPeer(TitleThemeCard owner) : RadioButtonAutomationPeer(owner), ISelectionItemProvider
    {
        private TitleThemeCard Card => (TitleThemeCard)Owner;

        bool ISelectionItemProvider.IsSelected => Card.IsChecked == true;

        IRawElementProviderSimple? ISelectionItemProvider.SelectionContainer => null;

        void ISelectionItemProvider.Select()
        {
            if (!IsEnabled())
            {
                throw new ElementNotEnabledException();
            }

            Card.RequestSelect();
        }

        void ISelectionItemProvider.AddToSelection()
        {
            if (Card.IsChecked != true)
            {
                throw new InvalidOperationException();
            }
        }

        void ISelectionItemProvider.RemoveFromSelection()
        {
            if (Card.IsChecked == true)
            {
                throw new InvalidOperationException();
            }
        }

        public override object GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.SelectionItem ? this : base.GetPattern(patternInterface);
    }
}

/// <summary>The theme cards as one radio group for UI Automation, labelled <c>titles.theme</c>.</summary>
internal sealed class TitleCardsGroup : StackPanel
{
    protected override AutomationPeer OnCreateAutomationPeer() => new GroupPeer(this);

    private sealed class GroupPeer(TitleCardsGroup owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

        protected override string GetClassNameCore() => nameof(TitleCardsGroup);
    }
}

/// <summary>An items control that leaves no element of its own in the UI Automation control view: its items are the group's.</summary>
internal sealed class PlainItemsControl : ItemsControl
{
    protected override AutomationPeer OnCreateAutomationPeer() => new PassThroughPeer(this);

    private sealed class PassThroughPeer(PlainItemsControl owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override bool IsControlElementCore() => false;

        protected override bool IsContentElementCore() => false;
    }
}
