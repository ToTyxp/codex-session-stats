using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using AgentSpeed.Interop;

namespace AgentSpeed.Menus;

/// <summary>
/// Shows a Fluent context menu at the cursor, or at a given point, from places that have no focusable window (tray,
/// taskbar, orb). A tiny invisible host window takes foreground so the menu gets keyboard input and closes on outside
/// clicks.
/// </summary>
internal sealed class MenuHost
{
    private readonly Window _host;
    private ContextMenu? _open;

    public MenuHost()
    {
        _host = new Window
        {
            Width = 1,
            Height = 1,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = true,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
            Left = -10000,
            Top = -10000,
            Style = new Style(typeof(Window)),
            Title = "Agent Speed",
        };
        _host.SourceInitialized += (_, _) =>
            WindowEffects.AddExtendedStyle(new WindowInteropHelper(_host).Handle, WS_EX.TOOLWINDOW);
    }

    public bool IsOpen => _open?.IsOpen == true;

    /// <summary>Opens the menu at the cursor, as a right click does.</summary>
    public void Show(ContextMenu menu) => Show(menu, null);

    /// <summary>
    /// Opens the menu with its top-left corner at <paramref name="at"/> (screen point in physical pixels; near a screen
    /// edge it flips above the point or shifts left) and focuses its first enabled item, for a menu opened by the
    /// keyboard or UI Automation. Null opens it at the cursor.
    /// </summary>
    public void Show(ContextMenu menu, POINT? at)
    {
        ArgumentNullException.ThrowIfNull(menu);
        _open?.SetCurrentValue(ContextMenu.IsOpenProperty, false);

        POINT? point = at ?? (User32.GetCursorPos(out var cursor) ? cursor : null);
        if (point is { } p)
        {
            var helper = new WindowInteropHelper(_host);
            helper.EnsureHandle();
            User32.SetWindowPos(helper.Handle, HWND.TOPMOST, p.X, p.Y, 1, 1, SWP.NOACTIVATE);
        }

        if (!_host.IsVisible)
        {
            _host.Show();
        }

        var handle = new WindowInteropHelper(_host).Handle;
        User32.SetForegroundWindow(handle);
        _host.Activate();

        menu.PlacementTarget = _host;
        if (at is null)
        {
            menu.Placement = PlacementMode.MousePoint;
            menu.PlacementRectangle = Rect.Empty;
        }
        else
        {
            // The host's top-left corner is the point; Bottom flips the menu above it when there is no room below.
            menu.Placement = PlacementMode.Bottom;
            menu.PlacementRectangle = new Rect(0, 0, 0, 0);
            menu.Opened += FocusFirstItem;
        }

        menu.Closed += OnClosed;
        _open = menu;
        menu.IsOpen = true;
    }

    public void Close()
    {
        if (_open is { } menu)
        {
            menu.IsOpen = false;
        }
    }

    private static void FocusFirstItem(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
        {
            return;
        }

        menu.Opened -= FocusFirstItem;
        menu.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (menu.IsOpen && menu.Items.OfType<MenuItem>().FirstOrDefault(i => i.IsEnabled && i.Focusable) is { } first)
            {
                first.Focus();
            }
        });
    }

    private void OnClosed(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
        {
            menu.Closed -= OnClosed;
            menu.Opened -= FocusFirstItem;
            if (ReferenceEquals(menu, _open))
            {
                _open = null;
                _host.Hide();
            }
        }
    }
}
