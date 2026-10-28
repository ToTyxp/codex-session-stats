using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AgentSpeed.Localization;
using AgentSpeed.Presentation.Cost;

namespace AgentSpeed.Settings.Cost;

/// <summary>
/// The currency button and its flyout (DESIGN 9, Currency): a search box above the grouped list. The search box keeps
/// the keyboard focus; ↑ / ↓ (and Page Up / Page Down) move the highlight, Return selects, Escape closes. The list
/// itself is reachable with Tab, where Return or Space selects.
/// </summary>
internal sealed partial class CurrencyPicker : UserControl
{
    private const int PageStep = 8;

    private CurrencyPickerViewModel? _model;
    private Point? _pointer;

    public CurrencyPicker()
    {
        InitializeComponent();
        Flyout.CustomPopupPlacementCallback = Place;
        Flyout.DataContextChanged += (_, _) => Attach(Flyout.DataContext as CurrencyPickerViewModel);
    }

    private void Attach(CurrencyPickerViewModel? model)
    {
        if (_model is not null)
        {
            _model.PropertyChanged -= OnModelChanged;
        }

        _model = model;
        if (_model is not null)
        {
            _model.PropertyChanged += OnModelChanged;
        }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CurrencyPickerViewModel.IsEmpty) && Flyout.IsOpen && _model is { IsEmpty: true })
        {
            Announce(LocalizationSource.Instance["settings.currency.noResults"]);
        }
    }

    private void OnButtonClick(object sender, RoutedEventArgs e) => Flyout.IsOpen = !Flyout.IsOpen;

    /// <summary>F4 or Alt+↓ opens the list, as on a combo box.</summary>
    private void OnButtonKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.F4 || (key == Key.Down && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)))
        {
            Flyout.IsOpen = true;
            e.Handled = true;
        }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // The pointer press that closes the flyout must not reach the button and open it again.
        PickerButton.IsHitTestVisible = false;
        _pointer = null;
        if (_model is null)
        {
            Attach(Flyout.DataContext as CurrencyPickerViewModel);
        }

        _model?.Open();
        Dispatcher.BeginInvoke(() =>
        {
            SearchBox.Focus();
            CenterOnHighlighted();
        }, DispatcherPriority.Input);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _model?.Close();
        Dispatcher.BeginInvoke(() => PickerButton.IsHitTestVisible = true, DispatcherPriority.Input);
        if (Flyout.Child is UIElement { IsKeyboardFocusWithin: true } || Keyboard.FocusedElement is null)
        {
            PickerButton.Focus();
        }
    }

    private void OnFlyoutKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                MoveHighlight(1);
                break;
            case Key.Up:
                MoveHighlight(-1);
                break;
            case Key.PageDown:
                MoveHighlight(PageStep);
                break;
            case Key.PageUp:
                MoveHighlight(-PageStep);
                break;
            case Key.Enter:
                Commit(null);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space && List.SelectedItem is CurrencyPickerRow row)
        {
            Commit(row);
            e.Handled = true;
        }
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        SearchBox.Focus();
    }

    /// <summary>Only a real pointer move highlights a row, not a row scrolling under a resting pointer.</summary>
    private void OnRowMouseMove(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(List);
        if (_pointer == position)
        {
            return;
        }

        _pointer = position;
        if (sender is ListBoxItem { DataContext: CurrencyPickerRow row } && _model is not null && !Equals(_model.Highlighted, row))
        {
            _model.Highlighted = row;
        }
    }

    private void OnRowClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: CurrencyPickerRow row })
        {
            Commit(row);
            e.Handled = true;
        }
    }

    private void MoveHighlight(int step)
    {
        if (_model is null)
        {
            return;
        }

        _model.Move(step);
        if (_model.Highlighted is { } row)
        {
            List.ScrollIntoView(row);
            Announce(row.AccessibleName);
        }
    }

    private void Commit(CurrencyPickerRow? row)
    {
        if (_model?.Commit(row) == true)
        {
            Close();
        }
    }

    private void Close()
    {
        Flyout.IsOpen = false;
        PickerButton.Focus();
    }

    /// <summary>Opens with the current currency in the middle of the list (DESIGN 9).</summary>
    private void CenterOnHighlighted()
    {
        if (_model?.Highlighted is not { } row)
        {
            return;
        }

        List.UpdateLayout();
        if (List.Template.FindName("Scroller", List) is not ScrollViewer scroller || FindRow(List, row) is not { } item)
        {
            List.ScrollIntoView(row);
            return;
        }

        var top = item.TransformToAncestor(scroller).Transform(new Point(0, 0)).Y + scroller.VerticalOffset;
        scroller.ScrollToVerticalOffset(Math.Max(0, top - (scroller.ViewportHeight - item.ActualHeight) / 2));
    }

    private static ListBoxItem? FindRow(DependencyObject parent, CurrencyPickerRow row)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ListBoxItem item && Equals(item.DataContext, row))
            {
                return item;
            }

            if (FindRow(child, row) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>The search box keeps the focus, so the highlighted row is announced to screen readers explicitly.</summary>
    private void Announce(string text)
    {
        var peer = UIElementAutomationPeer.FromElement(SearchBox) ?? UIElementAutomationPeer.CreatePeerForElement(SearchBox);
        peer?.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.MostRecent, text, "AgentSpeed.CurrencyPicker");
    }

    /// <summary>Right-aligned under the button (above it when the screen has no room below); sizes arrive in device pixels.</summary>
    private CustomPopupPlacement[] Place(Size popupSize, Size targetSize, Point offset)
    {
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        var shadow = 12 * scale;
        var gap = 4 * scale;
        var x = targetSize.Width - popupSize.Width + shadow;
        return
        [
            new CustomPopupPlacement(new Point(x, targetSize.Height - shadow + gap), PopupPrimaryAxis.Horizontal),
            new CustomPopupPlacement(new Point(x, -popupSize.Height + shadow - gap), PopupPrimaryAxis.Horizontal),
        ];
    }
}
