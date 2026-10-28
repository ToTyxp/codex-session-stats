using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AgentSpeed.Presentation.Titles;
using AgentSpeed.Theme;

namespace AgentSpeed.Settings.TitleThemes;

/// <summary>
/// Settings → General, Title themes (DESIGN 5): the theme cards with their keyboard model and menus, the undo banner
/// and the editor of the selected custom set with its live previews.
/// </summary>
internal sealed partial class TitleThemesSection : UserControl
{
    public static readonly DependencyProperty CardColumnsProperty = DependencyProperty.Register(nameof(CardColumns), typeof(int),
        typeof(TitleThemesSection), new PropertyMetadata(3));

    /// <summary>Room the sticky preview column keeps from the top of the page's viewport.</summary>
    private const double StickyInset = 12;

    private const double TwoColumnWidth = 600;

    private TitleThemesViewModel? _model;
    private TitleSetEditorViewModel? _editor;
    private ScrollViewer? _scroller;
    private Window? _window;
    private bool? _twoColumns;

    public TitleThemesSection()
    {
        InitializeComponent();
        EditorReveal.Reveal(false, animate: false, reduceMotion: false);
        DataContextChanged += (_, e) => Attach(e.NewValue as TitleThemesViewModel);
        GroupContent.SizeChanged += (_, _) => UpdateColumns();
        EditorGrid.SizeChanged += (_, _) =>
        {
            UpdateEditorLayout();
            UpdateSticky();
        };
        PreviewColumn.SizeChanged += (_, _) => UpdateSticky();
        IsVisibleChanged += (_, _) => UpdatePreviewActivity();
        EditorReveal.IsVisibleChanged += (_, _) => UpdatePreviewActivity();
        Cards.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnCardsClick));
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>Theme cards per row: three, two below 560 DIP of content width.</summary>
    public int CardColumns
    {
        get => (int)GetValue(CardColumnsProperty);
        set => SetValue(CardColumnsProperty, value);
    }

    // ---- Wiring ----

    private void Attach(TitleThemesViewModel? model)
    {
        Detach();
        _model = model;
        _editor = model?.Editor;
        if (_model is not null)
        {
            _model.PropertyChanged += OnModelChanged;
            _model.NameFocusRequested += OnNameFocusRequested;
        }

        if (_editor is not null)
        {
            _editor.PropertyChanged += OnEditorChanged;
            _editor.RowFocusRequested += OnRowFocusRequested;
            _editor.NoticeShown += OnNoticeShown;
        }

        EditorReveal.Reveal(_model?.HasEditor == true, animate: false, reduceMotion: false);
        SyncName(force: true);
        UpdatePreviewActivity();
    }

    private void Detach()
    {
        if (_model is not null)
        {
            _model.PropertyChanged -= OnModelChanged;
            _model.NameFocusRequested -= OnNameFocusRequested;
        }

        if (_editor is not null)
        {
            _editor.PropertyChanged -= OnEditorChanged;
            _editor.RowFocusRequested -= OnRowFocusRequested;
            _editor.NoticeShown -= OnNoticeShown;
        }

        _model = null;
        _editor = null;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_model is null && DataContext is TitleThemesViewModel model)
        {
            Attach(model);
        }

        _scroller = FindAncestor<ScrollViewer>(this);
        if (_scroller is not null)
        {
            _scroller.ScrollChanged += OnScrollChanged;
        }

        _window = Window.GetWindow(this);
        if (_window is not null)
        {
            _window.StateChanged += OnWindowStateChanged;
        }

        UpdateColumns();
        UpdateEditorLayout();
        UpdateSticky();
        UpdatePreviewActivity();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, RememberSelectedCard);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // The view models outlive the settings window: let go of them until loaded again.
        Detach();

        if (_scroller is not null)
        {
            _scroller.ScrollChanged -= OnScrollChanged;
            _scroller = null;
        }

        if (_window is not null)
        {
            _window.StateChanged -= OnWindowStateChanged;
            _window = null;
        }

        UpdatePreviewActivity();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TitleThemesViewModel.HasUndo) && _model is { HasUndo: true } undo)
        {
            Announce(BannerText, undo.UndoText, "AgentSpeed.TitleUndo");
        }

        if (e.PropertyName is nameof(TitleThemesViewModel.HasEditor) or null or "" && _model is { } model)
        {
            var animate = IsLoaded && IsVisible;
            EditorReveal.Reveal(model.HasEditor, animate, model.ReduceMotion);
            UpdatePreviewActivity();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, RememberSelectedCard);
        }
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TitleSetEditorViewModel.SetId))
        {
            SyncName(force: true);
        }
        else if (e.PropertyName == nameof(TitleSetEditorViewModel.NameText))
        {
            SyncName(force: false);
        }
    }

    // ---- Layout ----

    private void UpdateColumns()
    {
        var columns = TitleCardNavigation.Columns(GroupContent.ActualWidth);
        if (CardColumns != columns)
        {
            CardColumns = columns;
        }
    }

    /// <summary>Two columns (form, 200 DIP previews) from 600 DIP; below that the previews sit side by side above the form.</summary>
    private void UpdateEditorLayout()
    {
        var two = EditorGrid.ActualWidth <= 0 || EditorGrid.ActualWidth >= TwoColumnWidth;
        if (_twoColumns == two)
        {
            return;
        }

        _twoColumns = two;
        GapColumn.Width = new GridLength(two ? 24 : 0);
        PreviewColumnDefinition.Width = two ? new GridLength(200) : new GridLength(0);
        Grid.SetRow(Form, two ? 0 : 1);
        Grid.SetColumnSpan(Form, two ? 1 : 3);
        Grid.SetColumn(PreviewColumn, two ? 2 : 0);
        Grid.SetColumnSpan(PreviewColumn, two ? 1 : 3);
        PreviewColumn.Margin = two ? default : new Thickness(0, 0, 0, 12);
        PreviewCards.Columns = two ? 1 : 2;
        PreviewCards.Margin = two ? default : new Thickness(0, 0, -8, 0);
        var cardMargin = two ? new Thickness(0, 0, 0, 8) : new Thickness(0, 0, 8, 0);
        HeroCard.Margin = cardMargin;
        OrbCard.Margin = cardMargin;
        UpdateSticky();
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdateSticky();
        UpdatePreviewActivity();
    }

    /// <summary>The preview column follows the top of the viewport while the editor scrolls past (two columns only).</summary>
    private void UpdateSticky()
    {
        if (_twoColumns != true || _scroller is null || !EditorGrid.IsVisible || EditorGrid.ActualHeight <= 0)
        {
            PreviewShift.Y = 0;
            return;
        }

        var top = EditorGrid.TranslatePoint(new Point(0, 0), _scroller).Y;
        var offset = TitleEditorMotion.StickyOffset(top, PreviewColumn.ActualHeight, top + EditorGrid.ActualHeight, 0, StickyInset);
        if (Math.Abs(PreviewShift.Y - offset) > 0.1)
        {
            PreviewShift.Y = Math.Round(offset);
        }
    }

    /// <summary>The previews draw only while they can be seen: page shown, window not minimized, editor in the viewport.</summary>
    private void UpdatePreviewActivity()
    {
        var active = IsLoaded && IsVisible && EditorReveal.IsShown && EditorReveal.IsVisible
                     && _window?.WindowState != WindowState.Minimized && InViewport(PreviewColumn);
        HeroPreview.Active = active;
        OrbPreview.Active = active;
    }

    private bool InViewport(FrameworkElement element)
    {
        if (_scroller is null || !element.IsVisible)
        {
            return element.IsVisible;
        }

        var top = element.TranslatePoint(new Point(0, 0), _scroller).Y;
        return top + element.ActualHeight > 0 && top < _scroller.ViewportHeight;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e) => UpdatePreviewActivity();

    // ---- Cards ----

    private void OnCardLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TitleThemeCard element)
        {
            return;
        }

        if (element.ContextMenu is null)
        {
            var menu = new ContextMenu();
            menu.SetResourceReference(FontFamilyProperty, AppFonts.UiFontKey);
            element.ContextMenu = menu;
        }

        if (element.DataContext is TitleThemeCardViewModel { Appear: true } card)
        {
            card.Appear = false;
            Appear(element);
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, RememberSelectedCard);
    }

    /// <summary>A new set's card: a 0.35 s spring from 85 % with a fade (Reduce Motion: the fade).</summary>
    private void Appear(UIElement element)
    {
        var fade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromSeconds(0.2)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        };
        element.BeginAnimation(OpacityProperty, fade);
        if (_model?.ReduceMotion == true)
        {
            return;
        }

        var scale = new ScaleTransform(0.85, 0.85);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = scale;
        var spring = new DoubleAnimation(0.85, 1, SpringEase.Duration) { EasingFunction = new SpringEase() };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, spring);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, spring);
    }

    private void OnCardContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is TitleThemeCard { DataContext: TitleThemeCardViewModel card, ContextMenu: { } menu })
        {
            FillMenu(menu, card);
            menu.ClearValue(ContextMenu.PlacementProperty);
            menu.ClearValue(ContextMenu.PlacementTargetProperty);
        }
    }

    /// <summary>The ⋯ button opens the card's menu under itself.</summary>
    private void OnCardsClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button { Tag: TitleThemeCard { DataContext: TitleThemeCardViewModel card, ContextMenu: { } menu } } button)
        {
            return;
        }

        FillMenu(menu, card);
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>Presets: Duplicate and Edit. Custom sets: Edit, Duplicate, Delete (DESIGN 5, Title themes 2).</summary>
    private void FillMenu(ContextMenu menu, TitleThemeCardViewModel card)
    {
        menu.Items.Clear();
        if (_model is not { } model)
        {
            return;
        }

        var loc = model.Localizer;
        var id = card.Id;
        if (!card.IsCustom)
        {
            menu.Items.Add(Item(loc["titles.duplicateEdit"], model.CanAddCustom, () => model.Duplicate(id)));
            return;
        }

        menu.Items.Add(Item(loc["titles.custom.edit"], true, () => model.Edit(id)));
        menu.Items.Add(Item(loc["titles.custom.duplicate"], model.CanAddCustom, () => model.Duplicate(id)));
        var delete = Item(loc["titles.custom.delete"], true, () => DeleteAndRefocus(id));
        delete.SetResourceReference(ForegroundProperty, "SystemFillColorCriticalBrush");
        menu.Items.Add(delete);
    }

    private static MenuItem Item(string header, bool enabled, Action run)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += (_, _) => run();
        return item;
    }

    /// <summary>
    /// Arrow keys move between the cards row-major (presets, then the custom group), Space or Return selects (the New
    /// card makes a copy), Ctrl+D duplicates and edits, Delete deletes a custom set (with the undo banner).
    /// </summary>
    private void OnCardsKeyDown(object sender, KeyEventArgs e)
    {
        if (_model is not { } model || FocusedCardId() is not { } id)
        {
            return;
        }

        var order = model.FocusOrder;
        var index = IndexOf(order, id);
        if (index < 0)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        TitleCardMove? move = e.Key switch
        {
            Key.Left => FlowDirection == FlowDirection.RightToLeft ? TitleCardMove.Right : TitleCardMove.Left,
            Key.Right => FlowDirection == FlowDirection.RightToLeft ? TitleCardMove.Left : TitleCardMove.Right,
            Key.Up => TitleCardMove.Up,
            Key.Down => TitleCardMove.Down,
            _ => null,
        };
        if (move is { } direction && modifiers == ModifierKeys.None)
        {
            var next = TitleCardNavigation.Move(index, direction, model.PresetCards.Count, order.Count, CardColumns);
            FocusCard(order[next]);
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Space or Key.Enter when modifiers == ModifierKeys.None:
                if (id == TitleThemesViewModel.NewCardId)
                {
                    model.NewSet();
                }
                else
                {
                    model.Select(id);
                }

                e.Handled = true;
                break;
            case Key.D when modifiers == ModifierKeys.Control:
                model.Duplicate(id == TitleThemesViewModel.NewCardId ? model.Service.Document.General.Id : id);
                e.Handled = true;
                break;
            case Key.Delete or Key.Back when modifiers == ModifierKeys.None && model.IsCustom(id):
                DeleteAndRefocus(id);
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// The cards are one tab stop (a roving tab index): the focused card while the focus is inside, the selected theme
    /// once it leaves, so Tab moves past the group and Tab back in lands on the selection.
    /// </summary>
    private void OnCardsGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is FrameworkElement focused && focused.DataContext is TitleCardItem)
        {
            SetTabStop(focused);
        }
    }

    private void OnCardsLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is not DependencyObject next || !Cards.IsAncestorOf(next))
        {
            RememberSelectedCard();
        }
    }

    private void RememberSelectedCard()
    {
        if (_model is null || Cards.IsKeyboardFocusWithin)
        {
            return;
        }

        var selected = _model.PresetCards.Concat(_model.CustomCards.OfType<TitleThemeCardViewModel>()).FirstOrDefault(c => c.IsSelected);
        if (selected is not null && CardElement(selected) is { } element)
        {
            SetTabStop(element);
        }
    }

    private void SetTabStop(FrameworkElement stop)
    {
        if (_model is null)
        {
            return;
        }

        foreach (var item in _model.PresetCards.Cast<object>().Concat(_model.CustomCards))
        {
            if (CardElement(item) is { } element)
            {
                KeyboardNavigation.SetIsTabStop(element, ReferenceEquals(element, stop));
            }
        }
    }

    private void DeleteAndRefocus(string id)
    {
        if (_model is not { } model)
        {
            return;
        }

        var index = IndexOf(model.FocusOrder, id);
        model.Delete(id);
        RefocusAt(Math.Max(index, 0));
    }

    /// <summary>
    /// Undo in the banner and Delete in the editor hide the button that has the focus: the focus goes to the selected
    /// theme's card instead of the window.
    /// </summary>
    private void OnFocusReturnClick(object sender, RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (_model is { } model && (Keyboard.FocusedElement is null or Window || !IsKeyboardFocusWithin))
            {
                FocusCard(model.Service.Document.General.Id);
            }
        });

    /// <summary>After a delete the focus stays at the same place in the order (the next card, or the last one).</summary>
    private void RefocusAt(int index) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (_model is { } model && model.FocusOrder is { Count: > 0 } order)
            {
                FocusCard(order[Math.Clamp(index, 0, order.Count - 1)]);
            }
        });

    private string? FocusedCardId() => Keyboard.FocusedElement switch
    {
        TitleThemeCard { DataContext: TitleThemeCardViewModel card } => card.Id,
        Button { DataContext: NewTitleCardViewModel } => TitleThemesViewModel.NewCardId,
        _ => null,
    };

    private void FocusCard(string id)
    {
        if (_model is not { } model)
        {
            return;
        }

        object? item = id == TitleThemesViewModel.NewCardId
            ? model.NewCard
            : model.PresetCards.FirstOrDefault(c => c.Id == id) ?? (object?)model.CustomCards.OfType<TitleThemeCardViewModel>().FirstOrDefault(c => c.Id == id);
        if (item is not null && CardElement(item) is { } element)
        {
            element.Focus();
            element.BringIntoView();
        }
    }

    private FrameworkElement? CardElement(object item)
    {
        var host = item is TitleThemeCardViewModel { IsCustom: false } ? PresetItems : CustomItems;
        if (host.ItemContainerGenerator.ContainerFromItem(item) is not ContentPresenter presenter)
        {
            return null;
        }

        presenter.ApplyTemplate();
        return VisualTreeHelper.GetChildrenCount(presenter) > 0 ? VisualTreeHelper.GetChild(presenter, 0) as FrameworkElement : null;
    }

    private static int IndexOf(IReadOnlyList<string> order, string id)
    {
        for (var i = 0; i < order.Count; i++)
        {
            if (order[i] == id)
            {
                return i;
            }
        }

        return -1;
    }

    // ---- Editor ----

    private void SyncName(bool force)
    {
        if (_editor is null || (!force && NameBox.IsKeyboardFocused))
        {
            return;
        }

        if (NameBox.Text != _editor.NameText)
        {
            NameBox.Text = _editor.NameText;
        }
    }

    private void OnNameKeyDown(object sender, KeyEventArgs e)
    {
        if (_editor is null)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            CommitName();
            NameBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && NameBox.Text != _editor.NameText)
        {
            NameBox.Text = _editor.NameText;
            NameBox.CaretIndex = NameBox.Text.Length;
            e.Handled = true;
        }
    }

    private void OnNameLostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitName();

    /// <summary>Commits the name; an empty name keeps the previous one.</summary>
    private void CommitName()
    {
        if (_editor?.SetId is null)
        {
            return;
        }

        var stored = _editor.CommitName(NameBox.Text);
        if (NameBox.Text != stored)
        {
            NameBox.Text = stored;
            NameBox.CaretIndex = stored.Length;
        }
    }

    private void OnNameFocusRequested(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            SyncName(force: true);
            if (NameBox.Focus())
            {
                NameBox.SelectAll();
                NameBox.BringIntoView();
            }
        });

    private void OnRowFocusRequested(object? sender, int level)
    {
        if (RowItems.ItemContainerGenerator.ContainerFromIndex(level) is not DependencyObject container
            || FindDescendant<TextBox>(container) is not { } field)
        {
            return;
        }

        if (field.Focus())
        {
            field.SelectAll();
            field.BringIntoView();
        }
    }

    /// <summary>A click on ↺ keeps the focus in the field, so the previews stay on its level.</summary>
    private void OnResetMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button { Tag: TextBox field, IsEnabled: true })
        {
            field.Focus();
            TitleField.Reset(field);
            e.Handled = true;
        }
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TextBox field })
        {
            // The button fades out with the text, so the field keeps the focus (and Ctrl+Z brings the text back).
            field.Focus();
            TitleField.Reset(field);
        }
    }

    /// <summary><c>titles.custom.tooLong</c> and <c>titles.custom.noEmoji</c> are announced politely.</summary>
    private void OnNoticeShown(object? sender, string text) => Announce(NoticeText, text, "AgentSpeed.TitleNotice");

    private static void Announce(UIElement source, string text, string activity)
    {
        if (text.Length > 0 && UIElementAutomationPeer.CreatePeerForElement(source) is { } peer)
        {
            peer.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.MostRecent, text, activity);
        }
    }

    // ---- Tree helpers ----

    private static T? FindAncestor<T>(DependencyObject element)
        where T : DependencyObject
    {
        for (var node = VisualTreeHelper.GetParent(element); node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is T match)
            {
                return match;
            }
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject element)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
