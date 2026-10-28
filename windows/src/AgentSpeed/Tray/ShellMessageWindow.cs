using System.Runtime.InteropServices;
using System.Windows.Interop;
using AgentSpeed.App;
using AgentSpeed.Interop;

namespace AgentSpeed.Tray;

/// <summary>
/// Hidden top-level window on the UI thread. Message-only windows do not receive broadcasts, so this is a
/// zero-size, never-shown popup: it gets TaskbarCreated, display and setting changes, and tray callbacks.
/// </summary>
internal sealed class ShellMessageWindow : IDisposable
{
    public const uint TrayCallbackMessage = WM.APP + 1;

    private readonly HwndSource _source;

    public ShellMessageWindow()
    {
        TaskbarCreatedMessage = User32.RegisterWindowMessage("TaskbarCreated");
        var parameters = new HwndSourceParameters("AgentSpeed.Shell")
        {
            WindowStyle = WS.POPUP,
            ExtendedWindowStyle = WS_EX.TOOLWINDOW | WS_EX.NOACTIVATE,
            Width = 0,
            Height = 0,
            PositionX = -32000,
            PositionY = -32000,
            ParentWindow = 0,
        };

        _source = new HwndSource(parameters);
        _source.AddHook(Hook);

        if (TaskbarCreatedMessage != 0
            && !User32.ChangeWindowMessageFilterEx(Handle, TaskbarCreatedMessage, MSGFLT.ALLOW, 0))
        {
            AppLog.Warn($"ChangeWindowMessageFilterEx(TaskbarCreated) failed: {Marshal.GetLastPInvokeError()}");
        }
    }

    public event EventHandler? TaskbarCreated;

    public event EventHandler? DisplayChanged;

    /// <summary>WM_SETTINGCHANGE; the string is the lParam section name when present (for example "ImmersiveColorSet").</summary>
    public event EventHandler<string?>? SettingChanged;

    public event EventHandler<TrayCallbackEventArgs>? TrayCallback;

    public event EventHandler? SessionEnding;

    public nint Handle => _source.Handle;

    public uint TaskbarCreatedMessage { get; }

    public void Dispose()
    {
        _source.RemoveHook(Hook);
        _source.Dispose();
    }

    private nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        var message = (uint)msg;
        try
        {
            if (message == TrayCallbackMessage)
            {
                var low = (uint)(lParam.ToInt64() & 0xFFFF);
                var x = unchecked((short)(wParam.ToInt64() & 0xFFFF));
                var y = unchecked((short)((wParam.ToInt64() >> 16) & 0xFFFF));
                TrayCallback?.Invoke(this, new TrayCallbackEventArgs(low, x, y));
                handled = true;
                return 0;
            }

            if (TaskbarCreatedMessage != 0 && message == TaskbarCreatedMessage)
            {
                TaskbarCreated?.Invoke(this, EventArgs.Empty);
                return 0;
            }

            switch (message)
            {
                case WM.DISPLAYCHANGE:
                    DisplayChanged?.Invoke(this, EventArgs.Empty);
                    break;
                case WM.SETTINGCHANGE:
                    SettingChanged?.Invoke(this, lParam != 0 ? Marshal.PtrToStringUni(lParam) : null);
                    break;
                case WM.THEMECHANGED:
                case WM.DWMCOMPOSITIONCHANGED:
                    SettingChanged?.Invoke(this, null);
                    break;
                case WM.ENDSESSION when wParam != 0:
                    SessionEnding?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"Shell message 0x{message:X4} handler failed.", ex);
        }

        return 0;
    }
}

internal sealed class TrayCallbackEventArgs(uint notification, int x, int y) : EventArgs
{
    public uint Notification { get; } = notification;

    /// <summary>Anchor in physical screen coordinates (NOTIFYICON_VERSION_4).</summary>
    public int X { get; } = x;

    public int Y { get; } = y;
}
