using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using AgentSpeed.App;
using AgentSpeed.Core;
using AgentSpeed.Interop;
using AgentSpeed.Presentation.Layout;
using AgentSpeed.Presentation.Orb;

namespace AgentSpeed.Taskbar;

internal abstract record TaskbarSignal
{
    public sealed record Invoked(RECT Bounds) : TaskbarSignal;

    public sealed record MenuRequested(POINT Cursor) : TaskbarSignal;

    public sealed record Hover(bool IsOver, RECT Bounds) : TaskbarSignal;

    public sealed record PresenceChanged(TaskbarPresence Presence, RECT Bounds) : TaskbarSignal;
}

/// <summary>
/// The native taskbar window. Lives on its own STA thread so the shell is never blocked by the UI thread:
/// a child of another process's window shares input state with that process.
/// </summary>
internal sealed unsafe class TaskbarHost : IDisposable
{
    private const string ClassName = "AgentSpeed.TaskbarWidget";
    private const int MaxPresentFailures = 3;
    private const long ShrinkDelayMs = 6000;

    private static TaskbarHost? s_current;
    private static bool s_classRegistered;

    private readonly Action<TaskbarSignal> _signal;
    private readonly Dispatcher _dispatcher;
    private readonly TaskbarRenderer _renderer = new();
    private readonly LayeredSurface _surface = new();
    private readonly BarScene _scene = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _animation;
    private readonly DispatcherTimer _safety;

    private TaskbarState _state = TaskbarState.Initial;
    private TaskbarPresence _presence = TaskbarPresence.Hidden;
    private nint _hwnd;
    private nint _parent;
    private RECT _screenRect;
    private double _scale = 1;
    private bool _vertical;
    private bool _embedFailed;
    private bool _hiddenForFullscreen;
    private bool _hovered;
    private bool _pressed;
    private bool _tracking;
    private int _presentFailures;
    private string _renderKey = string.Empty;
    private string _accessibleText = string.Empty;
    private long _lastAccessibleUpdate;
    private nint _foregroundHook;
    private nint _locationHook;
    private nint _watchedTaskbar;
    private nint _watchedTray;
    private bool _ensureQueued;
    private int _embedAttempts;
    private long _embedRetryAt;
    private readonly WidthHold _width = new(ShrinkDelayMs);
    private readonly DispatcherTimer _shrinkTimer;

    /// <summary>The second value's number cell (DESIGN 3): grows at once, shrinks only after the same delay.</summary>
    private readonly WidthHold _secondaryCell = new(ShrinkDelayMs);
    private readonly DispatcherTimer _secondaryTimer;
    private double _secondaryScale;

    public TaskbarHost(Action<TaskbarSignal> signal)
    {
        _signal = signal;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _animation = new DispatcherTimer(TimeSpan.FromMilliseconds(1000.0 / BarScene.AnimatedFps), DispatcherPriority.Render,
            (_, _) => Animate(), _dispatcher);
        _animation.Stop();
        _safety = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => SafetyCheck(), _dispatcher);
        _safety.Stop();
        _shrinkTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(ShrinkDelayMs + 50), DispatcherPriority.Background, (_, _) =>
        {
            _shrinkTimer!.Stop();
            Ensure();
        }, _dispatcher);
        _shrinkTimer.Stop();
        _secondaryTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(ShrinkDelayMs + 50), DispatcherPriority.Background, (_, _) =>
        {
            _secondaryTimer!.Stop();
            Ensure();
        }, _dispatcher);
        _secondaryTimer.Stop();
        s_current = this;
    }

    public void Apply(TaskbarState state)
    {
        var layoutChanged = state.LayoutDiffers(_state) || state.TaskbarDark != _state.TaskbarDark;
        _state = state;
        UpdateSecondaryCell(_scale > 0 ? _scale : 1);
        _scene.Redline = state.Redline;
        _scene.ReduceMotion = state.ReduceMotion;
        _scene.SetTarget(state.Value, state.Tier);

        if (!state.Enabled)
        {
            Teardown();
            UnwatchTaskbar();
            SetPresence(TaskbarPresence.Hidden);
            _safety.Stop();
            return;
        }

        _safety.Start();
        if (_hwnd == 0 || layoutChanged)
        {
            Ensure(forceLength: true);
        }
        else
        {
            var desired = _renderer.MeasureLength(_state, _scale, _vertical);
            if (desired > _width.Applied)
            {
                Ensure();
            }
            else
            {
                ResolveLength(desired, force: false);
                Render();
            }
        }

        UpdateAnimation();
        UpdateAccessibleName(force: false);
    }

    /// <summary>Explorer restarted: drop the old window (it died with its parent) and embed again.</summary>
    public void Reembed()
    {
        _embedFailed = false;
        _embedAttempts = 0;
        _presentFailures = 0;
        Teardown();
        Ensure(forceLength: true);
    }

    public void QueueEnsure()
    {
        if (_ensureQueued)
        {
            return;
        }

        _ensureQueued = true;
        _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _ensureQueued = false;
            Ensure();
        });
    }

    public void Dispose()
    {
        _animation.Stop();
        _safety.Stop();
        _shrinkTimer.Stop();
        _secondaryTimer.Stop();
        Teardown();
        UnwatchTaskbar();
        _surface.Dispose();
        if (s_classRegistered)
        {
            User32.UnregisterClass(ClassName, Kernel32.GetModuleHandle(null));
            s_classRegistered = false;
        }

        if (s_current == this)
        {
            s_current = null;
        }
    }

    private void Ensure(bool forceLength = false)
    {
        if (!_state.Enabled)
        {
            return;
        }

        var geometry = TaskbarLocator.Find();
        if (geometry is not { } geo)
        {
            Teardown();
            SetPresence(TaskbarPresence.Unavailable);
            return;
        }

        var scaleChanged = Math.Abs(geo.Scale - _scale) > 0.001 || geo.Vertical != _vertical;
        UpdateSecondaryCell(geo.Scale);
        var length = ResolveLength(_renderer.MeasureLength(_state, geo.Scale, geo.Vertical), forceLength || scaleChanged || _hwnd == 0);
        if (TaskbarLocator.WidgetRect(geo, length) is not { } rect)
        {
            Teardown();
            SetPresence(TaskbarPresence.Unavailable);
            return;
        }

        _scale = geo.Scale;
        _vertical = geo.Vertical;
        WatchTaskbar(geo);

        if (_hwnd != 0 && (!User32.IsWindow(_hwnd) || (_presence == TaskbarPresence.Embedded && _parent != geo.Taskbar)))
        {
            Teardown();
        }

        if (!_embedFailed && (_hwnd == 0 || _presence != TaskbarPresence.Embedded))
        {
            Teardown();
            if (!CreateEmbedded(geo.Taskbar, rect))
            {
                _embedFailed = true;
                _embedAttempts++;
                _embedRetryAt = Environment.TickCount64 + 30_000L * _embedAttempts;
            }
            else
            {
                _embedAttempts = 0;
            }
        }

        if (_embedFailed && (_hwnd == 0 || _presence != TaskbarPresence.Floating))
        {
            Teardown();
            if (!CreateFloating(rect))
            {
                SetPresence(TaskbarPresence.Unavailable);
                return;
            }
        }

        if (_presence == TaskbarPresence.Embedded)
        {
            PlaceEmbedded(geo.Taskbar, rect);
        }
        else
        {
            PlaceFloating(geo, rect);
        }
    }

    private bool CreateEmbedded(nint taskbar, RECT screen)
    {
        if (!EnsureClass())
        {
            return false;
        }

        var client = ToClient(taskbar, screen);
        var hwnd = User32.CreateWindowEx(
            WS_EX.LAYERED | WS_EX.TOOLWINDOW | WS_EX.NOACTIVATE | WS_EX.NOPARENTNOTIFY,
            ClassName, "Agent Speed",
            WS.CHILD | WS.VISIBLE | WS.CLIPSIBLINGS,
            client.Left, client.Top, screen.Width, screen.Height,
            taskbar, 0, Kernel32.GetModuleHandle(null), 0);
        if (hwnd == 0)
        {
            AppLog.Warn($"Taskbar embedding failed: CreateWindowEx error {Marshal.GetLastPInvokeError()}.");
            return false;
        }

        _hwnd = hwnd;
        _parent = taskbar;
        _screenRect = screen;
        _renderKey = string.Empty;
        if (!RenderAndPresent(force: true))
        {
            AppLog.Warn("Taskbar embedding failed: the layered child window could not be updated.");
            Teardown();
            return false;
        }

        User32.SetWindowPos(hwnd, HWND.TOP, 0, 0, 0, 0, SWP.NOMOVE | SWP.NOSIZE | SWP.NOACTIVATE);
        if (!User32.IsWindowVisible(hwnd) && User32.IsWindowVisible(taskbar))
        {
            AppLog.Warn("Taskbar embedding failed: the child window is not visible.");
            Teardown();
            return false;
        }

        SetPresence(TaskbarPresence.Embedded);
        UpdateAccessibleName(force: true);
        AppLog.Info("Taskbar widget embedded.");
        return true;
    }

    private bool CreateFloating(RECT screen)
    {
        if (!EnsureClass())
        {
            return false;
        }

        var hwnd = User32.CreateWindowEx(
            WS_EX.LAYERED | WS_EX.TOOLWINDOW | WS_EX.NOACTIVATE | WS_EX.TOPMOST,
            ClassName, "Agent Speed",
            WS.POPUP,
            screen.Left, screen.Top, screen.Width, screen.Height,
            0, 0, Kernel32.GetModuleHandle(null), 0);
        if (hwnd == 0)
        {
            AppLog.Warn($"Floating taskbar widget failed: CreateWindowEx error {Marshal.GetLastPInvokeError()}.");
            return false;
        }

        _hwnd = hwnd;
        _parent = 0;
        _screenRect = screen;
        _renderKey = string.Empty;
        if (!RenderAndPresent(force: true))
        {
            Teardown();
            return false;
        }

        _foregroundHook = User32.SetWinEventHook(WinEvent.EVENT_SYSTEM_FOREGROUND, WinEvent.EVENT_SYSTEM_FOREGROUND, 0,
            &OnForegroundChanged, 0, 0, WinEvent.WINEVENT_OUTOFCONTEXT | WinEvent.WINEVENT_SKIPOWNPROCESS);
        SetPresence(TaskbarPresence.Floating);
        UpdateAccessibleName(force: true);
        AppLog.Info("Taskbar widget running as a floating window over the taskbar.");
        return true;
    }

    private void PlaceEmbedded(nint taskbar, RECT screen)
    {
        var client = ToClient(taskbar, screen);
        var sizeChanged = screen.Width != _screenRect.Width || screen.Height != _screenRect.Height;
        _screenRect = screen;
        void Move() => User32.SetWindowPos(_hwnd, HWND.TOP, client.Left, client.Top, screen.Width, screen.Height,
            SWP.NOACTIVATE | SWP.SHOWWINDOW | (sizeChanged ? SWP.NOREDRAW : 0));

        if (sizeChanged)
        {
            // New content is ready before the window moves, so the resize and the new pixels land together.
            RenderAndPresent(force: true, beforePresent: Move);
        }
        else
        {
            Move();
            RenderAndPresent(force: false);
        }

        SignalBounds();
    }

    private void PlaceFloating(TaskbarGeometry geometry, RECT screen)
    {
        var hide = TaskbarLocator.IsFullscreenActive(geometry) || TaskbarLocator.IsAutoHidden(geometry)
            || !User32.IsWindowVisible(geometry.Taskbar);
        var sizeChanged = screen.Width != _screenRect.Width || screen.Height != _screenRect.Height;
        _screenRect = screen;
        if (hide)
        {
            if (!_hiddenForFullscreen)
            {
                User32.ShowWindow(_hwnd, SW.HIDE);
                _hiddenForFullscreen = true;
            }

            return;
        }

        _hiddenForFullscreen = false;
        void Move() => User32.SetWindowPos(_hwnd, HWND.TOPMOST, screen.Left, screen.Top, screen.Width, screen.Height,
            SWP.NOACTIVATE | SWP.SHOWWINDOW | (sizeChanged ? SWP.NOREDRAW : 0));
        if (sizeChanged)
        {
            RenderAndPresent(force: true, beforePresent: Move);
        }
        else
        {
            Move();
            RenderAndPresent(force: false);
        }

        SignalBounds();
    }

    private void SafetyCheck()
    {
        try
        {
            if (!_state.Enabled)
            {
                return;
            }

            if (_hwnd == 0 || !User32.IsWindow(_hwnd))
            {
                _hwnd = 0;
                Ensure();
                return;
            }

            if (_presence == TaskbarPresence.Embedded)
            {
                var geometry = TaskbarLocator.Find();
                if (geometry is null || geometry.Value.Taskbar != _parent)
                {
                    Reembed();
                    return;
                }

                var desired = _renderer.MeasureLength(_state, geometry.Value.Scale, geometry.Value.Vertical);
                var length = Math.Abs(geometry.Value.Scale - _scale) > 0.001 ? desired : _width.Peek(desired, Environment.TickCount64);
                var rect = TaskbarLocator.WidgetRect(geometry.Value, length);
                if (rect is not { } r || !RectEquals(r, _screenRect) || Math.Abs(geometry.Value.Scale - _scale) > 0.001)
                {
                    Ensure();
                    return;
                }

                if (User32.GetWindow(_hwnd, GW.HWNDPREV) != 0)
                {
                    User32.SetWindowPos(_hwnd, HWND.TOP, 0, 0, 0, 0, SWP.NOMOVE | SWP.NOSIZE | SWP.NOACTIVATE);
                }
            }
            else
            {
                // A failed embedding may have been transient (for example, the shell was still starting).
                if (_embedFailed && _embedAttempts < 4 && Environment.TickCount64 >= _embedRetryAt)
                {
                    _embedFailed = false;
                }

                Ensure();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Taskbar safety check failed.", ex);
        }
    }

    private void Animate()
    {
        _scene.Update(_clock.Elapsed.TotalSeconds);
        Render();
        UpdateAnimation();
    }

    private void UpdateAnimation()
    {
        var animate = _hwnd != 0 && _scene.IsAnimating && !_hiddenForFullscreen;
        if (animate && !_animation.IsEnabled)
        {
            _scene.Update(_clock.Elapsed.TotalSeconds);
            _animation.Start();
        }
        else if (!animate && _animation.IsEnabled)
        {
            _animation.Stop();
        }
    }

    private void Render()
    {
        if (_hwnd == 0 || _hiddenForFullscreen)
        {
            return;
        }

        RenderAndPresent(force: false);
    }

    /// <summary>
    /// Sizes the second value's number cell (DESIGN 3): the number is right-aligned in a cell that grows at once and
    /// shrinks only after a narrower number has held for 6 s, so the widget never jitters while the numbers change.
    /// </summary>
    private void UpdateSecondaryCell(double scale)
    {
        var needed = _renderer.SecondaryNumberWidth(_state, scale);
        if (needed <= 0)
        {
            _secondaryCell.Reset();
            _renderer.SecondaryCell = 0;
            _secondaryTimer.Stop();
            return;
        }

        var force = Math.Abs(scale - _secondaryScale) > 0.001;
        _secondaryScale = scale;
        var applied = _secondaryCell.Resolve((int)Math.Ceiling(needed * scale), Environment.TickCount64, force);
        _renderer.SecondaryCell = applied / scale;
        if (_secondaryCell.PendingSince is null)
        {
            _secondaryTimer.Stop();
        }
        else if (!_secondaryTimer.IsEnabled)
        {
            var wait = Math.Max(50, (_secondaryCell.DueAt ?? 0) - Environment.TickCount64 + 50);
            _secondaryTimer.Interval = TimeSpan.FromMilliseconds(wait);
            _secondaryTimer.Start();
        }
    }

    /// <summary>The widget grows at once but shrinks only after the shorter width has held for 6 s.</summary>
    private int ResolveLength(int desired, bool force)
    {
        var length = _width.Resolve(desired, Environment.TickCount64, force);
        if (_width.PendingSince is null)
        {
            _shrinkTimer.Stop();
        }
        else if (!_shrinkTimer.IsEnabled)
        {
            var wait = Math.Max(50, (_width.DueAt ?? 0) - Environment.TickCount64 + 50);
            _shrinkTimer.Interval = TimeSpan.FromMilliseconds(wait);
            _shrinkTimer.Start();
        }

        return length;
    }

    private bool RenderAndPresent(bool force, Action? beforePresent = null)
    {
        var width = Math.Max(1, _screenRect.Width);
        var height = Math.Max(1, _screenRect.Height);
        var key = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{width}x{height}|{_scale:F3}|{_state.ValueText ?? SpeedFormat.Speed(_state.Value)}|{_state.ValueMuted}|{_state.Secondary?.Live.Number}|{_state.Secondary?.Today.Number}|{_state.Secondary?.Live.Dim}|{_state.Secondary?.Today.Dim}|{_renderer.SecondaryCell:F2}|{_state.Tier}|{Math.Round(_scene.Heat * 720)}|{Math.Round(_scene.FillOpacity * 40)}|{Math.Round(_scene.Glow * 40)}|{_hovered}|{_pressed}|{_state.TaskbarDark}|{_state.HighContrast}|{_state.ColoredGlyph}|{_state.Mode}|{_state.ShowUnit}|{_state.Traffic?.Rx?.Text}|{_state.Traffic?.Tx?.Text}|{_state.Traffic?.Bits}");
        if (!force && key == _renderKey)
        {
            beforePresent?.Invoke();
            return true;
        }

        if (!_surface.Ensure(width, height))
        {
            AppLog.Warn("Taskbar surface allocation failed.");
            return false;
        }

        try
        {
            _renderer.Render(_state, _scene, width, height, _scale, _vertical, _hovered, _pressed, _surface.Bits, _surface.ByteCount);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OutOfMemoryException)
        {
            AppLog.Warn("Taskbar rendering failed.", ex);
            return false;
        }

        beforePresent?.Invoke();
        if (!_surface.Present(_hwnd, out var error))
        {
            AppLog.Warn($"UpdateLayeredWindow failed with error {error}.");
            if (_presence == TaskbarPresence.Embedded && ++_presentFailures >= MaxPresentFailures)
            {
                _embedFailed = true;
                _embedAttempts++;
                _embedRetryAt = Environment.TickCount64 + 30_000L * _embedAttempts;
                QueueEnsure();
            }

            return false;
        }

        _presentFailures = 0;
        _renderKey = key;
        return true;
    }

    /// <summary>Follows the taskbar and notification area as the shell moves or resizes them (icons added, auto-hide).</summary>
    private void WatchTaskbar(TaskbarGeometry geometry)
    {
        _watchedTray = geometry.Tray;
        if (_locationHook != 0 && _watchedTaskbar == geometry.Taskbar)
        {
            return;
        }

        UnwatchTaskbar();
        var thread = User32.GetWindowThreadProcessId(geometry.Taskbar, out var process);
        if (thread == 0)
        {
            return;
        }

        _watchedTaskbar = geometry.Taskbar;
        _locationHook = User32.SetWinEventHook(WinEvent.EVENT_OBJECT_LOCATIONCHANGE, WinEvent.EVENT_OBJECT_LOCATIONCHANGE, 0,
            &OnLocationChanged, process, thread, WinEvent.WINEVENT_OUTOFCONTEXT);
    }

    private void UnwatchTaskbar()
    {
        if (_locationHook != 0)
        {
            User32.UnhookWinEvent(_locationHook);
            _locationHook = 0;
        }

        _watchedTaskbar = 0;
        _watchedTray = 0;
    }

    private void Teardown()
    {
        _animation.Stop();
        if (_foregroundHook != 0)
        {
            User32.UnhookWinEvent(_foregroundHook);
            _foregroundHook = 0;
        }

        if (_hwnd != 0)
        {
            var hwnd = _hwnd;
            _hwnd = 0;
            if (User32.IsWindow(hwnd))
            {
                User32.DestroyWindow(hwnd);
            }
        }

        _parent = 0;
        _hovered = false;
        _pressed = false;
        _tracking = false;
        _hiddenForFullscreen = false;
        _renderKey = string.Empty;
    }

    private void SetPresence(TaskbarPresence presence)
    {
        if (_presence == presence)
        {
            return;
        }

        _presence = presence;
        _signal(new TaskbarSignal.PresenceChanged(presence, _screenRect));
    }

    private void SignalBounds()
    {
        if (_hwnd != 0 && User32.GetWindowRect(_hwnd, out var rect))
        {
            _screenRect = new RECT(rect.Left, rect.Top, rect.Left + _screenRect.Width, rect.Top + _screenRect.Height);
        }
    }

    private void UpdateAccessibleName(bool force)
    {
        if (_hwnd == 0)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (!force && (now - _lastAccessibleUpdate < 1000 || _state.AccessibleText == _accessibleText))
        {
            return;
        }

        _lastAccessibleUpdate = now;
        _accessibleText = _state.AccessibleText;
        User32.SetWindowText(_hwnd, _accessibleText);
    }

    private nint OnMessage(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case WM.MOUSEACTIVATE:
                return MA.NOACTIVATE;
            case WM.ERASEBKGND:
                return 1;
            case WM.MOUSEMOVE:
                if (!_tracking)
                {
                    var track = new TRACKMOUSEEVENT
                    {
                        cbSize = (uint)sizeof(TRACKMOUSEEVENT),
                        dwFlags = TME.LEAVE | TME.HOVER,
                        hwndTrack = hwnd,
                        dwHoverTime = TME.HOVER_DEFAULT,
                    };
                    _tracking = User32.TrackMouseEvent(ref track);
                    _hovered = true;
                    Render();
                }

                return 0;
            case WM.MOUSEHOVER:
                if (!_pressed)
                {
                    _signal(new TaskbarSignal.Hover(true, CurrentBounds()));
                }

                return 0;
            case WM.MOUSELEAVE:
                _tracking = false;
                _hovered = false;
                _pressed = false;
                Render();
                _signal(new TaskbarSignal.Hover(false, CurrentBounds()));
                return 0;
            case WM.LBUTTONDOWN:
                _pressed = true;
                Render();
                _signal(new TaskbarSignal.Hover(false, CurrentBounds()));
                return 0;
            case WM.LBUTTONUP:
                if (_pressed)
                {
                    _pressed = false;
                    Render();
                    _signal(new TaskbarSignal.Invoked(CurrentBounds()));
                }

                return 0;
            case WM.RBUTTONDOWN:
                return 0;
            case WM.RBUTTONUP:
                User32.GetCursorPos(out var cursor);
                _signal(new TaskbarSignal.Hover(false, CurrentBounds()));
                _signal(new TaskbarSignal.MenuRequested(cursor));
                return 0;
            case WM.CONTEXTMENU:
                // Never forward to the parent: the shell would show its own taskbar menu.
                return 0;
            case WM.DPICHANGED:
            case WM.DPICHANGED_AFTERPARENT:
            case WM.DISPLAYCHANGE:
            case WM.SETTINGCHANGE:
                QueueEnsure();
                break;
            case WM.NCDESTROY:
                if (hwnd == _hwnd)
                {
                    _hwnd = 0;
                    _renderKey = string.Empty;
                    QueueEnsure();
                }

                break;
        }

        return User32.DefWindowProc(hwnd, message, wParam, lParam);
    }

    /// <summary>Screen to taskbar client coordinates (the shell taskbar has no non-client area).</summary>
    private static RECT ToClient(nint taskbar, RECT screen)
    {
        var client = screen;
        if (User32.MapWindowPoints(0, taskbar, ref client, 2) != 0 || Marshal.GetLastPInvokeError() == 0)
        {
            return client;
        }

        return User32.GetWindowRect(taskbar, out var bar)
            ? new RECT(screen.Left - bar.Left, screen.Top - bar.Top, screen.Right - bar.Left, screen.Bottom - bar.Top)
            : screen;
    }

    private RECT CurrentBounds() =>
        _hwnd != 0 && User32.GetWindowRect(_hwnd, out var rect) ? rect : _screenRect;

    private static bool RectEquals(RECT a, RECT b) =>
        a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;

    private static bool EnsureClass()
    {
        if (s_classRegistered)
        {
            return true;
        }

        fixed (char* name = ClassName)
        {
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)sizeof(WNDCLASSEX),
                style = 0,
                lpfnWndProc = &WindowProc,
                hInstance = Kernel32.GetModuleHandle(null),
                hCursor = User32.LoadCursor(0, IDC.ARROW),
                lpszClassName = name,
            };

            if (User32.RegisterClassEx(wc) == 0)
            {
                var error = Marshal.GetLastPInvokeError();
                if (error != Errors.ERROR_CLASS_ALREADY_EXISTS)
                {
                    AppLog.Warn($"RegisterClassEx failed with error {error}.");
                    return false;
                }
            }
        }

        s_classRegistered = true;
        return true;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProc(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        try
        {
            if (s_current is { } host)
            {
                return host.OnMessage(hwnd, message, wParam, lParam);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"Taskbar window message 0x{message:X4} failed.", ex);
        }

        return User32.DefWindowProc(hwnd, message, wParam, lParam);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnLocationChanged(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            if (idObject == WinEvent.OBJID_WINDOW && s_current is { } host && hwnd != 0
                && (hwnd == host._watchedTray || hwnd == host._watchedTaskbar))
            {
                host.QueueEnsure();
            }
        }
        catch
        {
            // Never let an exception cross the native boundary.
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnForegroundChanged(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            s_current?.QueueEnsure();
        }
        catch
        {
            // Never let an exception cross the native boundary.
        }
    }
}
