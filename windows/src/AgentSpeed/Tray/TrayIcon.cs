using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Threading;
using AgentSpeed.App;
using AgentSpeed.Interop;
using AgentSpeed.Presentation.Gauge;

namespace AgentSpeed.Tray;

internal readonly record struct TrayVisualState(double Heat, bool DarkTaskbar, bool HighContrast, bool Monochrome, string Tooltip);

/// <summary>Notification-area icon (Shell_NotifyIcon, NOTIFYICON_VERSION_4) with a live gauge drawn at the taskbar DPI.</summary>
internal sealed unsafe class TrayIcon : IDisposable
{
    private const uint IconId = 1;
    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(500);

    private readonly ShellMessageWindow _window;
    private readonly DispatcherTimer _throttle;
    private readonly DispatcherTimer _retry;
    private nint _icon;
    private bool _added;
    private int _retries;
    private TrayVisualState _pending;
    private TrayVisualState? _applied;
    private int _appliedSize;
    private DateTime _lastApply = DateTime.MinValue;
    private nint _balloonIcon;
    private (string Title, string Text)? _pendingBalloon;

    public TrayIcon(ShellMessageWindow window, Dispatcher dispatcher)
    {
        _window = window;
        _window.TrayCallback += OnCallback;
        _window.TaskbarCreated += (_, _) => Recreate();
        _throttle = new DispatcherTimer(MinInterval, DispatcherPriority.Background, (_, _) => ApplyPending(), dispatcher);
        _throttle.Stop();
        _retry = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => RetryAdd(), dispatcher);
        _retry.Stop();
    }

    public event EventHandler? Invoked;

    /// <summary>Right click or keyboard context menu; coordinates are physical screen pixels.</summary>
    public event EventHandler<(int X, int Y)>? ContextMenuRequested;

    /// <summary>The user clicked the notification shown by <see cref="ShowBalloon"/>.</summary>
    public event EventHandler? BalloonClicked;

    public bool IsVisible => _added;

    /// <summary>
    /// Shows a notification from the icon (NIF_INFO; Windows presents it as a toast) with the app icon when available.
    /// If the icon is not in the notification area yet, the notification is shown once it is added.
    /// </summary>
    public void ShowBalloon(string title, string text)
    {
        if (!_added)
        {
            _pendingBalloon = (title, text);
            return;
        }

        _pendingBalloon = null;
        var data = NewData(NIF.INFO | NIF.SHOWTIP);
        SetText(data.szInfoTitle, 64, title);
        SetText(data.szInfo, 256, string.IsNullOrEmpty(text) ? " " : text);
        var icon = BalloonIcon();
        data.dwInfoFlags = NIIF.RESPECT_QUIET_TIME | (icon != 0 ? NIIF.USER | NIIF.LARGE_ICON : NIIF.INFO);
        data.hBalloonIcon = icon;
        if (!Shell32.Shell_NotifyIcon(NIM.MODIFY, ref data))
        {
            AppLog.Warn("The notification could not be shown.");
        }
    }

    public void Show(TrayVisualState state)
    {
        _pending = state;
        if (!TryAdd())
        {
            _retries = 0;
            _retry.Start();
        }
    }

    public void Update(TrayVisualState state)
    {
        if (_added && state == _pending && _applied == state && !_throttle.IsEnabled)
        {
            return;
        }

        _pending = state;
        if (!_added)
        {
            return;
        }

        var due = DateTime.UtcNow - _lastApply >= MinInterval;
        if (due && !_throttle.IsEnabled)
        {
            ApplyPending();
        }
        else if (!_throttle.IsEnabled)
        {
            _throttle.Start();
        }
    }

    /// <summary>Re-renders if the taskbar DPI changed (display or setting change).</summary>
    public void Refresh()
    {
        if (_added && IconSize() != _appliedSize)
        {
            _applied = null;
            ApplyPending();
        }
    }

    /// <summary>Screen rectangle of the icon (physical pixels), when the shell can report it.</summary>
    public RECT? GetBounds()
    {
        if (!_added)
        {
            return null;
        }

        var identifier = new NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)sizeof(NOTIFYICONIDENTIFIER),
            hWnd = _window.Handle,
            uID = IconId,
        };
        return Shell32.Shell_NotifyIconGetRect(identifier, out var rect) == 0 && !rect.IsEmpty ? rect : null;
    }

    public void Dispose()
    {
        _throttle.Stop();
        _retry.Stop();
        _window.TrayCallback -= OnCallback;
        if (_added)
        {
            var data = NewData(0);
            Shell32.Shell_NotifyIcon(NIM.DELETE, ref data);
            _added = false;
        }

        DestroyIcon();
        if (_balloonIcon != 0)
        {
            User32.DestroyIcon(_balloonIcon);
            _balloonIcon = 0;
        }
    }

    private void Recreate()
    {
        _added = false;
        _applied = null;
        if (!TryAdd())
        {
            _retries = 0;
            _retry.Start();
        }
    }

    private void RetryAdd()
    {
        if (TryAdd() || ++_retries > 30)
        {
            _retry.Stop();
            if (!_added)
            {
                AppLog.Warn("The notification area icon could not be added.");
            }
        }
    }

    private bool TryAdd()
    {
        if (_added)
        {
            return true;
        }

        RenderIcon(_pending);
        var data = NewData(NIF.MESSAGE | NIF.ICON | NIF.TIP | NIF.SHOWTIP);
        data.uCallbackMessage = ShellMessageWindow.TrayCallbackMessage;
        data.hIcon = _icon;
        SetTip(ref data, _pending.Tooltip);

        if (!Shell32.Shell_NotifyIcon(NIM.ADD, ref data))
        {
            // The icon may survive an explorer restart; replace it instead.
            Shell32.Shell_NotifyIcon(NIM.DELETE, ref data);
            if (!Shell32.Shell_NotifyIcon(NIM.ADD, ref data))
            {
                return false;
            }
        }

        var version = NewData(0);
        version.uVersion = NIN.NOTIFYICON_VERSION_4;
        Shell32.Shell_NotifyIcon(NIM.SETVERSION, ref version);
        _added = true;
        _applied = _pending;
        _lastApply = DateTime.UtcNow;
        if (_pendingBalloon is { } balloon)
        {
            ShowBalloon(balloon.Title, balloon.Text);
        }

        return true;
    }

    private void ApplyPending()
    {
        _throttle.Stop();
        if (!_added)
        {
            return;
        }

        var size = IconSize();
        var state = _pending;
        var iconChanged = _applied is not { } applied
            || size != _appliedSize
            || Quantize(applied.Heat) != Quantize(state.Heat)
            || applied.DarkTaskbar != state.DarkTaskbar
            || applied.HighContrast != state.HighContrast
            || applied.Monochrome != state.Monochrome;
        var tipChanged = _applied?.Tooltip != state.Tooltip;
        if (!iconChanged && !tipChanged)
        {
            return;
        }

        var flags = NIF.SHOWTIP;
        var previousIcon = _icon;
        if (iconChanged)
        {
            RenderIcon(state, keepPrevious: true);
            flags |= NIF.ICON;
        }

        if (tipChanged)
        {
            flags |= NIF.TIP;
        }

        var data = NewData(flags);
        data.hIcon = _icon;
        SetTip(ref data, state.Tooltip);
        if (Shell32.Shell_NotifyIcon(NIM.MODIFY, ref data))
        {
            _applied = state;
        }

        if (iconChanged && previousIcon != 0 && previousIcon != _icon)
        {
            User32.DestroyIcon(previousIcon);
        }

        _lastApply = DateTime.UtcNow;
    }

    private void RenderIcon(TrayVisualState state, bool keepPrevious = false)
    {
        var size = IconSize();
        var label = state.HighContrast
            ? System.Windows.SystemColors.WindowTextColor
            : state.DarkTaskbar ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x1A, 0x1A, 0x1A);
        nint icon;
        try
        {
            icon = TrayIconRenderer.CreateIcon(size, state.Heat, label, state.Monochrome || state.HighContrast);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ExternalException or ArgumentException)
        {
            AppLog.Warn("Tray icon rendering failed.", ex);
            icon = 0;
        }

        if (icon == 0)
        {
            return;
        }

        if (!keepPrevious)
        {
            DestroyIcon();
        }

        _icon = icon;
        _appliedSize = size;
    }

    private void DestroyIcon()
    {
        if (_icon != 0)
        {
            User32.DestroyIcon(_icon);
            _icon = 0;
        }
    }

    private void OnCallback(object? sender, TrayCallbackEventArgs e)
    {
        switch (e.Notification)
        {
            case NIN.SELECT:
            case NIN.KEYSELECT:
                Invoked?.Invoke(this, EventArgs.Empty);
                break;
            case WM.CONTEXTMENU:
                ContextMenuRequested?.Invoke(this, (e.X, e.Y));
                break;
            case NIN.BALLOONUSERCLICK:
                BalloonClicked?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private NOTIFYICONDATA NewData(uint flags) => new()
    {
        cbSize = (uint)sizeof(NOTIFYICONDATA),
        hWnd = _window.Handle,
        uID = IconId,
        uFlags = flags,
    };

    private static void SetTip(ref NOTIFYICONDATA data, string tip)
    {
        fixed (char* destination = data.szTip)
        {
            SetText(destination, 128, tip);
        }
    }

    private static void SetText(char* destination, int capacity, string? text)
    {
        text ??= string.Empty;
        var length = Math.Min(text.Length, capacity - 1);
        if (length > 0 && char.IsHighSurrogate(text[length - 1]))
        {
            length--;
        }

        for (var i = 0; i < length; i++)
        {
            destination[i] = text[i];
        }

        destination[length] = '\0';
    }

    /// <summary>The app icon at the large-icon size, extracted once from the executable; 0 when unavailable.</summary>
    private nint BalloonIcon()
    {
        if (_balloonIcon != 0 || AppPaths.ExecutablePath is not { } path)
        {
            return _balloonIcon;
        }

        var taskbar = User32.FindWindow("Shell_TrayWnd", null);
        var dpi = taskbar != 0 ? User32.GetDpiForWindow(taskbar) : 96u;
        var size = User32.GetSystemMetricsForDpi(SM.CXICON, dpi == 0 ? 96u : dpi);
        if (size <= 0)
        {
            size = 32;
        }

        nint icon = 0;
        uint id = 0;
        var count = User32.PrivateExtractIcons(path, 0, size, size, &icon, &id, 1, 0);
        _balloonIcon = count is > 0 and not uint.MaxValue ? icon : 0;
        return _balloonIcon;
    }

    private static int IconSize()
    {
        var taskbar = User32.FindWindow("Shell_TrayWnd", null);
        var dpi = taskbar != 0 ? User32.GetDpiForWindow(taskbar) : 96u;
        if (dpi == 0)
        {
            dpi = 96;
        }

        var size = User32.GetSystemMetricsForDpi(SM.CXSMICON, dpi);
        return size > 0 ? size : (int)Math.Round(16 * dpi / 96.0);
    }

    private static int Quantize(double heat) => (int)Math.Round(Math.Clamp(heat, 0, 1) * GaugeLayout.SegmentCount);
}
